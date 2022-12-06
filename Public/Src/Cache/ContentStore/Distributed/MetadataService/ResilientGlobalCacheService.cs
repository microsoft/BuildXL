// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public record GlobalCacheServiceConfiguration
    {
        public CheckpointManagerConfiguration Checkpoint { get; init; }

        public bool EnableBackgroundRestoreCheckpoint { get; init; }

        public int MaxEventParallelism { get; init; }

        public TimeSpan MasterLeaseStaleThreshold { get; init; } = Timeout.InfiniteTimeSpan;

        public CentralStoreConfiguration CentralStorage { get; init; }

        public ContentMetadataEventStreamConfiguration EventStream { get; init; }

        public BlobEventStorageConfiguration PersistentEventStorage { get; init; }

        /// <summary>
        /// If set, all the operations for the global cache service will go through a queue for limiting the number of simultaneous operations.
        /// </summary>
        public int? MaxOperationConcurrency { get; init; }

        /// <summary>
        /// If <see cref="MaxOperationConcurrency"/> is set, then this property will represent a length of the queue that will be used for managing concurrency of the global cache service.
        /// </summary>
        public int? MaxOperationQueueLength { get; init; }

        /// <summary>
        /// Maximum age of the newest checkpoint that we're willing to tolerate. If the latest checkpoint is older than
        /// the age, we'll wipe out all of the data in the system and start from a clean slate.
        /// </summary>
        public TimeSpan? CheckpointMaxAge { get; set; }
    }

    /// <summary>
    /// Interface that represents a content metadata service backed by a <see cref="IGlobalCacheStore"/>
    /// </summary>
    public class ResilientGlobalCacheService : GlobalCacheService, IRoleObserver
    {
        private class CancellableOperation : IDisposable
        {
            private readonly AsyncLazy<bool> _lazyOperation;
            private bool _isDisposedOrCanceled = false;
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public CancellableOperation(Func<CancellationToken, Task<bool>> operation)
            {
                _lazyOperation = new AsyncLazy<bool>(() =>
                {
                    if (_isDisposedOrCanceled)
                    {
                        return BoolTask.False;
                    }

                    return operation(_cts.Token);
                });
            }

            public Task<bool> EnsureStartedAndAwaitCompletionAsync()
            {
                return _lazyOperation.GetValueAsync();
            }

            public void Dispose()
            {
                lock (this)
                {
                    _isDisposedOrCanceled = true;
                    _cts.Dispose();
                }
            }

            public void Cancel()
            {
                if (!_isDisposedOrCanceled)
                {
                    lock (this)
                    {
                        if (!_isDisposedOrCanceled)
                        {
                            _cts.Cancel();
                            _isDisposedOrCanceled = true;
                        }
                    }
                }
            }

        }

        private const string LogCursorKey = "ResilientContentMetadataService.LogCursor";

        private readonly ContentMetadataEventStream _eventStream;
        private readonly GlobalCacheServiceConfiguration _configuration;
        private readonly CheckpointManager _checkpointManager;
        private readonly RocksDbContentMetadataStore _store;

        private CancellableOperation _pendingBackgroundRestore = new(token => BoolTask.False);
        private readonly SemaphoreSlim _createBackgroundRestoreOperationGate = TaskUtilities.CreateMutex();

        private readonly SemaphoreSlim _restoreCheckpointGate = TaskUtilities.CreateMutex();
        private readonly SemaphoreSlim _createCheckpointGate = TaskUtilities.CreateMutex();

        private ActionQueue _concurrencyLimitingQueue;

        private readonly IClock _clock;
        private readonly BlobContentLocationRegistry _registry;

        protected override Tracer Tracer { get; } = new Tracer(nameof(ResilientGlobalCacheService));

        internal ContentMetadataEventStream EventStream => _eventStream;

        internal CheckpointManager CheckpointManager => _checkpointManager;

        /// <summary>
        /// Returns true if the client should retry an operation.
        /// </summary>
        /// <remarks>
        /// If the method returns <code>true</code> then <paramref name="retryReason"/> contains a reason why to do that
        /// and <paramref name="errorMessage"/> contains a non-null (and not empty) error message.
        /// </remarks>
        internal bool ShouldRetry(out RetryReason retryReason, out string errorMessage, bool isShutdown = false)
        {
            errorMessage = null;
            retryReason = RetryReason.Invalid;

            try
            {
                if (ShutdownStarted && !isShutdown)
                {
                    retryReason = RetryReason.ShutdownStarted;
                    return true;
                }

                if (_role == Role.Worker)
                {
                    retryReason = RetryReason.WorkerMode;
                    return true;
                }

                var lastHeartbeat = _lastSuccessfulHeartbeat;
                var now = _clock.UtcNow;
                if (!lastHeartbeat.IsRecent(now, _configuration.MasterLeaseStaleThreshold))
                {
                    errorMessage = $"Service's last successful heartbeat was at `{lastHeartbeat}`, currently `{now}` is beyond staleness threshold `{_configuration.MasterLeaseStaleThreshold}`";
                    retryReason = RetryReason.StaleHeartbeat;
                    return true;
                }

                if (!_hasRestoredCheckpoint)
                {
                    retryReason = RetryReason.MissingCheckpoint;
                    return true;
                }

                return false;
            }
            finally
            {
                if (retryReason != RetryReason.Invalid && errorMessage == null)
                {
                    // If the method returns true, then the error message should not be null.
                    // Using a text representation of the reason.
                    errorMessage = retryReason.ToString();
                }
            }
        }

        private DateTime _lastSuccessfulHeartbeat;
        private Role _role = Role.Worker;
        private bool _hasRestoredCheckpoint;

        public ResilientGlobalCacheService(
            GlobalCacheServiceConfiguration configuration,
            CheckpointManager checkpointManager,
            RocksDbContentMetadataStore store,
            ContentMetadataEventStream eventStream,
            IClock clock = null,
            BlobContentLocationRegistry registry = null)
            : base(store)
        {
            _configuration = configuration;
            _store = store;
            _checkpointManager = checkpointManager;
            _eventStream = eventStream;
            _clock = clock ?? SystemClock.Instance;
            _registry = registry;

            LinkLifetime(_eventStream);
            LinkLifetime(_checkpointManager);
            LinkLifetime(registry);

            RunInBackground(nameof(CreateCheckpointLoopAsync), CreateCheckpointLoopAsync, fireAndForget: true);

            store.Database.DatabaseInvalidated = OnDatabaseInvalidated;
        }

        private void OnDatabaseInvalidated(OperationContext context, Failure<Exception> exception)
        {
            // In the GCS case, we don't want to release the role when we encounter corruption. The reason for that is
            // that, as master, we may have acknowledged a number of requests and written them down to the log. In that
            // case, it is best for us to shutdown gracefully so those requests get properly persisted.
            LifetimeManager.RequestTeardown(context, "GCS database has been invalidated");
        }

        protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            if (_configuration?.MaxOperationConcurrency != null)
            {
                var maxConcurrency = _configuration.MaxOperationConcurrency.Value;
                var maxQueueLength = _configuration.MaxOperationQueueLength;
                Tracer.Debug(context, $"Using concurrency limiting queue. MaxConcurrency={maxConcurrency}, MaxQueueLength={maxQueueLength}");
                _concurrencyLimitingQueue = new ActionQueue(maxConcurrency, maxQueueLength);
            }

            return BoolResult.SuccessTask;
        }

        protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            if (!ShouldRetry(out var retryReason, out var errorMessage, isShutdown: true))
            {
                // Stop database updates
                _registry?.SetDatabaseUpdateLeaseExpiry(null);

                // Stop logging
                _eventStream.SetIsLogging(false);

                // Seal the log
                await _eventStream.CompleteOrChangeLogAsync(context);
            }
            else
            {
                Tracer.Debug(context, $"Could not seal log: Reason=[{retryReason}] Error=[{errorMessage}]", "ShutdownSealLogFailure");
            }

            return BoolResult.Success;
        }

        public async Task OnRoleUpdatedAsync(OperationContext context, MasterElectionState electionState)
        {
            var role = electionState.Role;
            if (!StartupCompleted || ShutdownStarted)
            {
                return;
            }

            // This function gets called by the role observer, so we can potentially hang master election operations if we don't yield and wind up waiting
            await Task.Yield();

            _lastSuccessfulHeartbeat = _clock.UtcNow;
            if (_role != role)
            {
                // Stop database updates
                _registry?.SetDatabaseUpdateLeaseExpiry(null);

                _eventStream.SetIsLogging(false);
                _hasRestoredCheckpoint = false;
                _role = role;
            }

            if (!ShouldRetry(out _, out _))
            {
                // Notify registry that master lease is still held to ensure database is updated.
                _registry?.SetDatabaseUpdateLeaseExpiry(electionState.MasterLeaseExpiryUtc);
            }

            if (_role == Role.Master)
            {
                // Acquire mutex to ensure we cancel the actual outstanding background restore
                // (i.e. no new _pendingBackgroundRestore gets created after calling Cancel)
                using (_createBackgroundRestoreOperationGate.AcquireSemaphore())
                {
                    _pendingBackgroundRestore.Cancel();
                }

                // Need to wait for restore to complete to not corrupt state
                await _pendingBackgroundRestore.EnsureStartedAndAwaitCompletionAsync();
            }

            if (_role == Role.Master && !_hasRestoredCheckpoint)
            {
                if (await _restoreCheckpointGate.WaitAsync(0))
                {
                    try
                    {
                        var result = await RestoreCheckpointAsync(context);
                        if (result.Succeeded)
                        {
                            _hasRestoredCheckpoint = true;
                            _eventStream.SetIsLogging(true);

                            // Resume database updates
                            _registry?.SetDatabaseUpdateLeaseExpiry(electionState.MasterLeaseExpiryUtc);
                        }
                    }
                    finally
                    {
                        _restoreCheckpointGate.Release();
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override async Task<Result<TResponse>> ExecuteCoreAsync<TRequest, TResponse>(
            OperationContext context,
            TRequest request,
            Func<OperationContext, Task<Result<TResponse>>> executeAsync)
        {
            if (!request.Replaying && ShouldRetry(out var retryReason, out var errorMessage))
            {
                return new TResponse()
                {
                    ShouldRetry = true,
                    ErrorMessage = errorMessage,
                    RetryReason = retryReason,
                };
            }

            Result<TResponse> result;

            if (!request.Replaying)
            {
                CacheActivityTracker.Increment(CaSaaSActivityTrackingCounters.ProcessedMetadataRequests);
            }

            if (_concurrencyLimitingQueue == null || request.Replaying)
            {
                // Not using concurrency limiter when the request is replayed.
                result = await base.ExecuteCoreAsync(context, request, executeAsync);
            }
            else
            {
                try
                {
                    result = await _concurrencyLimitingQueue.RunAsync(
                        () =>
                        {
                            return base.ExecuteCoreAsync(context, request, executeAsync);
                        });
                }
                catch (ActionBlockIsFullException e)
                {
                    return new TResponse()
                    {
                        // TODO (1888943): support different retry kinds to notify the clients that the retry should happen after a longer period of time, for instance.
                        ShouldRetry = true,
                        ErrorMessage = $"Too many simultaneous operations. Limit={e.ConcurrencyLimit}, CurrentCount={e.CurrentCount}",
                    };
                }
            }

            if (!request.Replaying)
            {
                if (result.TryGetValue(out var response))
                {
                    if (ShouldRetry(out retryReason, out errorMessage))
                    {
                        response.ShouldRetry = true;
                        response.ErrorMessage = errorMessage;
                        response.RetryReason = retryReason;
                    }
                    else if (response.PersistRequest)
                    {
                        var success = await _eventStream.WriteEventAsync(context, request);
                        if (!success)
                        {
                            response.ShouldRetry = true;
                        }
                    }
                }
                else if (ShouldRetry(out retryReason, out errorMessage))
                {
                    return new TResponse()
                    {
                        ShouldRetry = true,
                        ErrorMessage = errorMessage,
                        RetryReason = retryReason,
                        Diagnostics = !response.Succeeded ? response.Diagnostics : null,
                    };
                }
            }

            return result;
        }

        internal async Task<BoolResult> RestoreCheckpointAsync(OperationContext context)
        {
            return await context.PerformOperationAsync<Result<(string checkpointId, CheckpointLogId logId, CheckpointLogId startReadLogId, int startWriteLogId)>>(
                Tracer,
                async () =>
                {
                    var checkpointState = await RestoreCheckpointDatabaseAsync(context);

                    // It is possible for a system failure (in creating checkpoints, for example) to create a huge
                    // backlog of events to be processed. If that happens, the system can enter a state where it needs
                    // to process a lot of events in order to get back to normal operation, and for those events to be
                    // very old (and hence irrelevant). This flag allows us to wipe the slate and start from scratch.
                    var now = _clock.UtcNow;
                    if (_configuration.CheckpointMaxAge is not null && !checkpointState.CheckpointTime.IsRecent(now, _configuration.CheckpointMaxAge.Value))
                    {
                        // If there was a failure, it was reported in the log message. The fact of the matter is that
                        // if this fails, there's really not much we can do except pretending everything's OK.
                        await DiscardStaleCheckpointsAsync(context, checkpointState).IgnoreFailure();
                        return Result.Success((checkpointId: string.Empty, CheckpointLogId.InitialLogId, CheckpointLogId.InitialLogId, startWriteLogId: CheckpointLogId.InitialLogId.Value));
                    }

                    CheckpointLogId logId = default;

                    using (await _createCheckpointGate.AcquireAsync())
                    {
                        await _checkpointManager.RestoreCheckpointAsync(context, checkpointState).ThrowIfFailureAsync();
                    }

                    logId = CheckpointLogId.InitialLogId;
                    var startReadLogId = logId;
                    if (_store.Database.TryGetGlobalEntry(LogCursorKey, out var cursor))
                    {
                        logId = CheckpointLogId.Deserialize(cursor);

                        // We start reading from the next log id because database contains
                        // all events up to AND INCLUDING the stored log id
                        startReadLogId = logId.Next();
                    }

                    var requestChannel = Channel.CreateBounded<ServiceRequestBase>(1000);
                    var dispatchTasks = Enumerable.Range(0, _configuration.MaxEventParallelism).Select(_ => DispatchAsync(context, requestChannel.Reader)).ToArray();

                    var startWriteLogId = await _eventStream.ReadEventsAsync(
                        context,
                        startReadLogId,
                        request => requestChannel.Writer.WriteAsync(request, context.Token)).ThrowIfFailureAsync();

                    requestChannel.Writer.Complete();

                    await Task.WhenAll(dispatchTasks);

                    await _eventStream.CompleteOrChangeLogAsync(context, startWriteLogId);

                    return Result.Success((checkpointId: checkpointState.CheckpointId, logId, startReadLogId, startWriteLogId: startWriteLogId.Value));
                },
                extraEndMessage: r => $"CheckpointId=[{r.GetValueOrDefault().checkpointId}] DbLogId=[{r.GetValueOrDefault().logId}] StartReadLogId=[{r.GetValueOrDefault().startReadLogId}] StartWriteLogId=[{r.GetValueOrDefault().startWriteLogId}]");
        }

        private async Task<BoolResult> BackgroundRestoreCheckpointAsync(OperationContext context)
        {
            return await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var checkpointState = await RestoreCheckpointDatabaseAsync(context);
                    return Result.Success(checkpointState);
                },
                extraEndMessage: r => $"CheckpointId=[{r.GetValueOrDefault()?.CheckpointId}]");
        }

        private async Task<CheckpointState> RestoreCheckpointDatabaseAsync(OperationContext context)
        {
            var checkpointState = await _checkpointManager.CheckpointRegistry.GetCheckpointStateAsync(context)
                                    .ThrowIfFailureAsync();

            using (await _createCheckpointGate.AcquireAsync())
            {
                await _checkpointManager.RestoreCheckpointAsync(context, checkpointState).ThrowIfFailureAsync();
            }

            return checkpointState;
        }

        private Task<BoolResult> DiscardStaleCheckpointsAsync(OperationContext context, CheckpointState checkpointState)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var t1 = _checkpointManager.CheckpointRegistry.ClearCheckpointsAsync(context);
                var t2 = _eventStream.ClearAsync(context);

                // These may take some time, so we do them in parallel
                await Task.WhenAll(t1, t2);

                var r1 = await t1;
                var r2 = await t2;
                if (!r1 || !r2)
                {
                    return new BoolResult(r1 & r2, $"Last checkpoint creation time is `{checkpointState.CheckpointTime}` which is beyond the allowed staleness (`{_configuration.CheckpointMaxAge}`), and we failed to clear the checkpoint registry and event streams");
                }

                return BoolResult.Success;
            });
        }

        private static IAsyncEnumerable<ServiceRequestBase> ReadAsync(ChannelReader<ServiceRequestBase> reader, CancellationToken cancellationToken)
        {
            return readWorkaround();

            async IAsyncEnumerable<ServiceRequestBase> readWorkaround()
            {
                while (await reader.WaitToReadAsync(cancellationToken))
                {
                    while (reader.TryRead(out var item))
                    {
                        yield return item;
                    }
                }
            }
        }

        private async Task DispatchAsync(OperationContext context, ChannelReader<ServiceRequestBase> requestReader)
        {
            await Task.Yield();

            await foreach (var request in ReadAsync(requestReader, context.Token))
            {
                request.Replaying = true;

                switch (request.MethodId)
                {
                    case RpcMethodId.RegisterContentLocations:
                    {
                        // This is a hot path, so instead of calling 'RegisterContentLocations' that does
                        // all the tracing we use a special way more optimized version
                        // that most likely will complete synchronously.
                        await RegisterContentLocationsFastAsync((RegisterContentLocationsRequest)request);
                        break;
                    }
                    case RpcMethodId.CompareExchange:
                    {
                        await CompareExchangeAsync((CompareExchangeRequest)request);
                        break;
                    }
                    case RpcMethodId.GetContentLocations:
                    {
                        await GetContentLocationsAsync((GetContentLocationsRequest)request);
                        break;
                    }
                    case RpcMethodId.GetContentHashList:
                    {
                        await GetContentHashListAsync((GetContentHashListRequest)request);
                        break;
                    }
                    case RpcMethodId.GetLevelSelectors:
                    {
                        await GetLevelSelectorsAsync((GetLevelSelectorsRequest)request);
                        break;
                    }
                    default:
                        throw Contract.AssertFailure($"Unhandled method id: {request.MethodId}");
                }
            }
        }

        private async Task<BoolResult> CreateCheckpointLoopAsync(OperationContext context)
        {
            try
            {
                while (!context.Token.IsCancellationRequested)
                {
                    await Task.Delay(_configuration.Checkpoint.CreateCheckpointInterval, context.Token);

                    if (_configuration.EnableBackgroundRestoreCheckpoint && _role != Role.Master)
                    {
                        using (_createBackgroundRestoreOperationGate.AcquireSemaphore())
                        {
                            if (_role != Role.Master)
                            {
                                _pendingBackgroundRestore = new CancellableOperation(async token =>
                                {
                                    if (token.IsCancellationRequested)
                                    {
                                        return false;
                                    }

                                    using (var innerContext = context.WithCancellationToken(token))
                                    {
                                        await BackgroundRestoreCheckpointAsync(innerContext).FireAndForgetErrorsAsync(context);
                                        return true;
                                    }
                                });
                            }
                        }

                        using (_pendingBackgroundRestore)
                        {
                            await _pendingBackgroundRestore.EnsureStartedAndAwaitCompletionAsync();
                        }
                    }

                    if (ShouldRetry(out _, out _))
                    {
                        continue;
                    }

                    // TODO: Timeout. Long create checkpoint could lose master while checkpointing.
                    // TODO: Checkpoints started later should take precedence. Might require a compare exchange in global store.
                    await CreateCheckpointAsync(context).FireAndForgetErrorsAsync(context);
                }
            }
            catch (TaskCanceledException)
            {
                // Do nothing
            }

            return BoolResult.Success;
        }

        public Task<Result<CheckpointLogId>> CreateCheckpointAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    using (await _createCheckpointGate.AcquireAsync())
                    {
                        var logId = await _eventStream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                        _store.Database.SetGlobalEntry(LogCursorKey, logId.Serialize());

                        await _checkpointManager.CreateCheckpointAsync(context, new EventSequencePoint(logId.Value), maxEventProcessingDelay: null).ThrowIfFailureAsync();

                        await _eventStream.AfterCheckpointAsync(context, logId).ThrowIfFailureAsync();

                        return Result.Success(logId);
                    }
                });
        }
    }
}
