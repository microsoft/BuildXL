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
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public record GlobalCacheServiceConfiguration
    {
        public CheckpointManagerConfiguration Checkpoint { get; init; }

        public int MaxEventParallelism { get; init; }

        public TimeSpan MasterLeaseStaleThreshold { get; init; } = Timeout.InfiniteTimeSpan;

        public CentralStoreConfiguration CentralStorage { get; init; }

        public ContentMetadataEventStreamConfiguration EventStream { get; init; }

        public RedisVolatileEventStorageConfiguration VolatileEventStorage { get; init; }

        public BlobEventStorageConfiguration PersistentEventStorage { get; init; }

        public ClusterManagementConfiguration ClusterManagement { get; init; }
    }

    public record ClusterManagementConfiguration
    {
        public TimeSpan MachineExpiryInterval { get; init; } = TimeSpan.FromDays(1);
    }

    /// <summary>
    /// Interface that represents a content metadata service backed by a <see cref="IGlobalCacheStore"/>
    /// </summary>
    public partial class ResilientGlobalCacheService : GlobalCacheService, IRoleObserver
    {
        private const string LogCursorKey = "ResilientContentMetadataService.LogCursor";

        private readonly ContentMetadataEventStream _eventStream;
        private readonly GlobalCacheServiceConfiguration _configuration;
        private readonly CheckpointManager _checkpointManager;
        private readonly RocksDbContentMetadataStore _store;

        private readonly SemaphoreSlim _restoreCheckpointGate = TaskUtilities.CreateMutex();
        private readonly SemaphoreSlim _createCheckpointGate = TaskUtilities.CreateMutex();

        private readonly IClock _clock;
        protected override Tracer Tracer { get; } = new Tracer(nameof(ResilientGlobalCacheService));

        internal bool ForceClientRetries(out string reason)
        {
            if (_role == Role.Worker)
            {
                reason = "Service is in worker mode";
                return true;
            }

            var lastHeartbeat = _lastSuccessfulHeartbeat;
            var now = _clock.UtcNow;
            if (!lastHeartbeat.IsRecent(now, _configuration.MasterLeaseStaleThreshold))
            {
                reason = $"Service's last successful heartbeat was at `{lastHeartbeat}`, currently `{now}` is beyond staleness threshold `{_configuration.MasterLeaseStaleThreshold}`";
                return true;
            }

            if (!_hasRestoredCheckpoint)
            {
                reason = "Service has yet to restore a checkpoint";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        private DateTime _lastSuccessfulHeartbeat;
        private Role _role = Role.Worker;
        private bool _hasRestoredCheckpoint;
        private Task _createCheckpointLoopTask = Task.CompletedTask;

        public ResilientGlobalCacheService(
            GlobalCacheServiceConfiguration configuration,
            CheckpointManager checkpointManager,
            RocksDbContentMetadataStore store,
            ContentMetadataEventStream eventStream,
            IStreamStorage streamStorage,
            IClock clock = null)
            : base(store, new ClusterManagementStore(configuration.ClusterManagement, streamStorage, clock))
        {
            _configuration = configuration;
            _store = store;
            _checkpointManager = checkpointManager;
            _eventStream = eventStream;
            _clock = clock ?? SystemClock.Instance;

            LinkLifetime(streamStorage);
            LinkLifetime(_eventStream);
            LinkLifetime(_checkpointManager.Storage);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await base.StartupCoreAsync(context).ThrowIfFailureAsync();

            _createCheckpointLoopTask = CreateCheckpointLoopAsync(context)
                .FireAndForgetErrorsAsync(context, operation: nameof(CreateCheckpointLoopAsync));

            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _createCheckpointLoopTask;

            if (!ForceClientRetries(out _))
            {
                // Stop logging
                _eventStream.SetIsLogging(false);

                // Seal the log
                await _eventStream.CompleteOrChangeLogAsync(context);
            }

            return await base.ShutdownCoreAsync(context);
        }

        public async Task OnRoleUpdatedAsync(OperationContext context, Role role)
        {
            if (!StartupCompleted || ShutdownStarted)
            {
                return;
            }

            _lastSuccessfulHeartbeat = _clock.UtcNow;
            if (_role != role)
            {
                _eventStream.SetIsLogging(false);
                _hasRestoredCheckpoint = false;
                _role = role;
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
            if (!request.Replaying && ForceClientRetries(out var reason))
            {
                return new TResponse()
                       {
                           ShouldRetry = true,
                           ErrorMessage = reason
                       };
            }

            var result = await base.ExecuteCoreAsync(context, request, executeAsync);

            if (!request.Replaying)
            {
                if (result.TryGetValue(out var response))
                {
                    if (ForceClientRetries(out reason))
                    {
                        response.ShouldRetry = true;
                        response.ErrorMessage = reason;
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
                else if (ForceClientRetries(out reason))
                {
                    return new TResponse()
                           {
                               ShouldRetry = true,
                               ErrorMessage = reason,
                               Diagnostics = !response.Succeeded ? response.Diagnostics : null,
                           };
                }
            }

            return result;
        }

        private async Task<BoolResult> RestoreCheckpointAsync(OperationContext context)
        {
            return await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var checkpointState = await _checkpointManager.CheckpointRegistry.GetCheckpointStateAsync(context)
                        .ThrowIfFailureAsync();

                    CheckpointLogId logId = default;

                    using (await _createCheckpointGate.AcquireAsync())
                    {
                        await _checkpointManager.RestoreCheckpointAsync(context, checkpointState).ThrowIfFailureAsync();
                    }

                    await ClusterManagementStore.RestoreClusterCheckpointAsync(context).ThrowIfFailureAsync();

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
                    case RpcMethodId.PutBlob:
                    {
                        await PutBlobAsync((PutBlobRequest)request);
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
                    case RpcMethodId.GetBlob:
                    {
                        await GetBlobAsync((GetBlobRequest)request);
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
                    case RpcMethodId.Heartbeat:
                    {
                        await HeartbeatAsync((HeartbeatMachineRequest)request);
                        break;
                    }
                    default:
                        throw Contract.AssertFailure($"Unhandled method id: {request.MethodId}");
                }
            }
        }

        private async Task CreateCheckpointLoopAsync(OperationContext context)
        {
            try
            {
                while (!context.Token.IsCancellationRequested)
                {
                    await Task.Delay(_configuration.Checkpoint.CreateCheckpointInterval, context.Token);

                    if (ForceClientRetries(out _))
                    {
                        continue;
                    }

                    // TODO: Timeout. Long create checkpoint could lose master while checkpointing.
                    // TODO: Checkpoints started later should take precedence. Might require a compare
                    // exchange in Redis.
                    await CreateCheckpointAsync(context).FireAndForgetErrorsAsync(context);
                }
            }
            catch (TaskCanceledException)
            {
                // Do nothing
            }
        }

        public Task<BoolResult> CreateCheckpointAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    using (await _createCheckpointGate.AcquireAsync())
                    {
                        var logId = await _eventStream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                        _store.Database.SetGlobalEntry(LogCursorKey, logId.Serialize());

                        await ClusterManagementStore.CreateClusterCheckpointAsync(context).ThrowIfFailureAsync();

                        await _checkpointManager.CreateCheckpointAsync(context, new EventSequencePoint(logId.Value)).ThrowIfFailureAsync();

                        await _eventStream.AfterCheckpointAsync(context, logId).ThrowIfFailureAsync();

                        return BoolResult.Success;
                    }
                });
        }
    }
}
