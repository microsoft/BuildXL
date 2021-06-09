// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
    public record ContentMetadataServiceConfiguration
    {
        public CheckpointManagerConfiguration Checkpoint { get; init; }

        public int MaxEventParallelism { get; init; }

        public TimeSpan MasterLeaseStaleThreshold { get; init; } = Timeout.InfiniteTimeSpan;

        public CentralStoreConfiguration CentralStorage { get; init; }

        public ContentMetadataEventStreamConfiguration EventStream { get; init; }

        public RedisVolatileEventStorageConfiguration VolatileEventStorage { get; init; }

        public BlobEventStorageConfiguration PersistentEventStorage { get; init; }
    }

    /// <summary>
    /// Interface that represents a content metadata service backed by a <see cref="IContentMetadataStore"/>
    /// </summary>
    public class ResilientContentMetadataService : ContentMetadataService, IHeartbeatObserver
    {
        private const string LogCursorKey = "ResilientContentMetadataService.LogCursor";

        private readonly ContentMetadataEventStream _eventStream;
        private readonly ContentMetadataServiceConfiguration _configuration;
        private readonly CheckpointManager _checkpointManager;
        private readonly RocksDbContentMetadataStore _store;

        private readonly SemaphoreSlim _restoreCheckpointGate = TaskUtilities.CreateMutex();
        private readonly SemaphoreSlim _createCheckpointGate = TaskUtilities.CreateMutex();

        private readonly IClock _clock;
        protected override Tracer Tracer { get; } = new Tracer(nameof(ResilientContentMetadataService));

        private readonly TaskSourceSlim<bool> _startupCompletion = TaskSourceSlim.Create<bool>();

        public bool ForceClientRetries
        {
            get
            {
                return _role == Role.Worker
                        || !_lastSuccessfulHeartbeat.IsRecent(_clock.UtcNow, _configuration.MasterLeaseStaleThreshold)
                        || !_hasRestoredCheckpoint;
            }
        }

        private DateTime _lastSuccessfulHeartbeat;
        private Role _role = Role.Worker;
        private bool _hasRestoredCheckpoint;
        private Task _createCheckpointLoopTask = Task.CompletedTask;

        public ResilientContentMetadataService(
            ContentMetadataServiceConfiguration configuration,
            CheckpointManager checkpointManager,
            RocksDbContentMetadataStore store,
            ContentMetadataEventStream eventStream,
            IClock clock = null)
            : base(store)
        {
            _configuration = configuration;
            _store = store;
            _checkpointManager = checkpointManager;
            _eventStream = eventStream;
            _clock = clock ?? SystemClock.Instance;

            LinkLifetime(_eventStream);
            LinkLifetime(_checkpointManager.Storage);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await base.StartupCoreAsync(context).ThrowIfFailureAsync();

            _createCheckpointLoopTask = CreateCheckpointLoopAsync(context)
                .FireAndForgetErrorsAsync(context, operation: nameof(CreateCheckpointLoopAsync));

            _startupCompletion.SetResult(true);

            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _createCheckpointLoopTask;

            if (!ForceClientRetries)
            {
                // Stop logging
                _eventStream.SetIsLogging(false);

                // Seal the log
                await _eventStream.CompleteOrChangeLogAsync(context);
            }

            return await base.ShutdownCoreAsync(context);
        }

        public async Task OnSuccessfulHeartbeatAsync(OperationContext context, Role role)
        {
            if (!StartupCompleted)
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

        protected override async Task<TResponse> ExecuteCoreAsync<TRequest, TResponse>(
            OperationContext context,
            TRequest request,
            Func<OperationContext, Task<Result<TResponse>>> executeAsync,
            string caller = null)
        {
            if (!request.Replaying && ForceClientRetries)
            {
                return new TResponse()
                {
                    ShouldRetry = true
                };
            }

            var response = await base.ExecuteCoreAsync(context, request, executeAsync, caller);
            if (ForceClientRetries)
            {
                response.ShouldRetry = true;
            }
            else if (!request.Replaying && response.PersistRequest)
            {
                var success = await _eventStream.WriteEventAsync(context, request);
                if (!success)
                {
                    response.ShouldRetry = true;
                }
            }

            return response;
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

                    await _checkpointManager.RestoreCheckpointAsync(context, checkpointState).ThrowIfFailureAsync();

                    logId = CheckpointLogId.InitialLogId;
                    if (_store.Database.TryGetGlobalEntry(LogCursorKey, out var cursor))
                    {
                        logId = CheckpointLogId.Deserialize(cursor);
                    }

                    var requestChannel = Channel.CreateBounded<ServiceRequestBase>(1000);
                    var dispatchTasks = Enumerable.Range(0, _configuration.MaxEventParallelism).Select(_ => DispatchAsync(context, requestChannel.Reader)).ToArray();

                    var startLogId = await _eventStream.ReadEventsAsync(
                        context,
                        logId,
                        request => requestChannel.Writer.WriteAsync(request, context.Token)).ThrowIfFailureAsync();

                    requestChannel.Writer.Complete();

                    await Task.WhenAll(dispatchTasks);

                    await _eventStream.CompleteOrChangeLogAsync(context, startLogId);

                    return Result.Success((checkpointId: checkpointState.CheckpointId, logId, startLogId: startLogId.Value));
                },
                extraEndMessage: r => $"CheckpointId=[{r.GetValueOrDefault().checkpointId}] LogId=[{r.GetValueOrDefault().logId}] StartLogId=[{r.GetValueOrDefault().startLogId}]");
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
                        var typedRequest = (RegisterContentLocationsRequest)request;
                        await RegisterContentLocationsAsync(typedRequest);
                        break;
                    }
                    case RpcMethodId.GetContentLocations:
                    case RpcMethodId.PutBlob:
                    case RpcMethodId.GetBlob:
                    default:
                        // Unhandled
                        break;
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

                    if (ForceClientRetries)
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

                        await _checkpointManager.CreateCheckpointAsync(context, new EventSequencePoint(logId.Value)).ThrowIfFailureAsync();

                        await _eventStream.AfterCheckpointAsync(context, logId).ThrowIfFailureAsync();

                        return BoolResult.Success;
                    }
                });
        }
    }
}
