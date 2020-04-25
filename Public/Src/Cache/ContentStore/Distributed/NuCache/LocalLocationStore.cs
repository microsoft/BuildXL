// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.Tracing.TracingStructuredExtensions;
using static BuildXL.Cache.ContentStore.UtilitiesCore.Internal.CollectionUtilities;
using DateTimeUtilities = BuildXL.Cache.ContentStore.Utils.DateTimeUtilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Top level class that represents a local location store.
    /// </summary>
    /// <remarks>
    /// Local location store is a mediator between a content location database and a central store.
    /// </remarks>
    public sealed class LocalLocationStore : StartupShutdownBase
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalLocationStore));

        // LLS is 'managed' by multiple 'TransitioningContentLocationStore' instances. Allow multiple
        // startups and shutdowns to account for this.
        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <nodoc />
        public CounterCollection<ContentLocationStoreCounters> Counters { get; } = new CounterCollection<ContentLocationStoreCounters>();

        /// <nodoc />
        public Role? CurrentRole { get; private set; }

        /// <nodoc />
        public ContentLocationDatabase Database { get; }

        /// <nodoc />
        public IGlobalLocationStore GlobalStore { get; }

        /// <nodoc />
        public LocalLocationStoreConfiguration Configuration => _configuration;

        /// <nodoc />
        public MachineReputationTracker MachineReputationTracker { get; private set; }

        /// <nodoc />
        public ContentLocationEventStore EventStore { get; private set; }


        internal ClusterState ClusterState => GlobalStore.ClusterState;

        private readonly IClock _clock;

        private readonly LocalLocationStoreConfiguration _configuration;

        internal CheckpointManager CheckpointManager { get; private set; }

        /// <nodoc />
        public CentralStorage CentralStorage { get; }

        private readonly CentralStorage _innerCentralStorage;

        // The (optional) distributed central storage which wraps the inner central storage
        internal DistributedCentralStorage DistributedCentralStorage { get; }

        // Fields that are initialized in StartupCoreAsync method.
        private Timer _heartbeatTimer;

        // Local volatile caches for preventing resending the same events over and over again.
        private readonly VolatileSet<ContentHash> _recentlyAddedHashes;
        private readonly VolatileSet<ContentHash> _recentlyTouchedHashes;

        // Normally content registered with the machine already will be skipped or re-registered lazily. If content was recently removed (evicted),
        // we need to eagerly update the content because other machines may have already received updated db with content unregistered for the this machine.
        // This tracks recent removals so the corresponding content can be registered eagerly with global store.
        private readonly VolatileSet<ContentHash> _recentlyRemovedHashes;

        private DateTime _lastCheckpointTime;
        private Task<BoolResult> _pendingProcessCheckpointTask;
        private readonly object _pendingProcessCheckpointTaskLock = new object();

        private DateTime _lastRestoreTime;
        private string _lastCheckpointId;

        private readonly SemaphoreSlim _databaseInvalidationGate = new SemaphoreSlim(1);

        private readonly SemaphoreSlim _heartbeatGate = new SemaphoreSlim(1);

        /// <summary>
        /// Initialization for local location store may take too long if we restore the first checkpoint in there.
        /// So the initialization process is split into two pieces: core initialization and post-initialization that should be checked in every public method.
        /// </summary>
        private Task<BoolResult> _postInitializationTask;

        private const string BinManagerKey = "LocalLocationStore.BinManager";

        /// <summary>
        /// This is the machine state reported during heartbeat. Initialized as <see cref="MachineState.Unknown"/> in
        /// order to avoid updates at first.
        /// </summary>
        private MachineState _heartbeatMachineState = MachineState.Unknown;

        /// <nodoc />
        public LocalLocationStore(
            IClock clock,
            IGlobalLocationStore globalStore,
            LocalLocationStoreConfiguration configuration,
            IDistributedContentCopier copier)
        {
            Contract.RequiresNotNull(clock);
            Contract.RequiresNotNull(globalStore);
            Contract.RequiresNotNull(configuration);

            _clock = clock;
            _configuration = configuration;
            GlobalStore = globalStore;

            _recentlyAddedHashes = new VolatileSet<ContentHash>(clock);
            _recentlyTouchedHashes = new VolatileSet<ContentHash>(clock);
            _recentlyRemovedHashes = new VolatileSet<ContentHash>(clock);

            ValidateConfiguration(configuration);

            _innerCentralStorage = CreateCentralStorage(_configuration.CentralStore);

            if (_configuration.DistributedCentralStore != null)
            {
                DistributedCentralStorage = new DistributedCentralStorage(
                    _configuration.DistributedCentralStore,
                    new DistributedCentralStorageLocationStoreAdapter(this),
                    copier,
                    fallbackStorage: _innerCentralStorage);
                CentralStorage = DistributedCentralStorage;
            }
            else
            {
                CentralStorage = _innerCentralStorage;
            }

            configuration.Database.TouchFrequency = configuration.TouchFrequency;
            Database = ContentLocationDatabase.Create(clock, configuration.Database, () => ClusterState.InactiveMachines);
        }

        /// <summary>
        /// Checks whether the reconciliation for the store is up to date
        /// </summary>
        public bool IsReconcileUpToDate(MachineId machineId)
        {
            if (!File.Exists(GetReconcileFilePath(machineId)))
            {
                return false;
            }

            var contents = File.ReadAllText(GetReconcileFilePath(machineId));
            var parts = contents.Split('|');
            if (parts.Length != 2 || parts[0] != _configuration.GetCheckpointPrefix())
            {
                return false;
            }

            var reconcileTime = DateTimeUtilities.FromReadableTimestamp(parts[1]);
            if (reconcileTime == null)
            {
                return false;
            }

            return reconcileTime.Value.IsRecent(_clock.UtcNow, _configuration.LocationEntryExpiry.Multiply(0.75));
        }

        /// <summary>
        /// Marks reconciliation for the store is up to date with the current time stamp
        /// </summary>
        public void MarkReconciled(MachineId machineId, bool reconciled = true)
        {
            if (reconciled)
            {
                File.WriteAllText(GetReconcileFilePath(machineId), $"{_configuration.GetCheckpointPrefix()}|{_clock.UtcNow.ToReadableString()}");
            }
            else
            {
                FileUtilities.DeleteFile(GetReconcileFilePath(machineId));
            }
        }

        private string GetReconcileFilePath(MachineId machineId)
        {
            return (_configuration.Checkpoint.WorkingDirectory / $"reconcileMarker.{machineId.Index}.txt").Path;
        }

        /// <summary>
        /// Gets a value indicating whether the store supports storing and retrieving blobs.
        /// </summary>
        public bool AreBlobsSupported => GlobalStore.AreBlobsSupported;

        private ContentLocationEventStore CreateEventStore(LocalLocationStoreConfiguration configuration, string subfolder)
        {
            return ContentLocationEventStore.Create(
                configuration.EventStore,
                new ContentLocationDatabaseAdapter(Database, ClusterState),
                configuration.PrimaryMachineLocation.ToString(),
                CentralStorage,
                configuration.Checkpoint.WorkingDirectory / "reconciles" / subfolder,
                _clock
                );
        }

        private CentralStorage CreateCentralStorage(CentralStoreConfiguration configuration)
        {
            // TODO: Validate configuration before construction (bug 1365340)
            Contract.Requires(configuration != null);

            switch (configuration)
            {
                case LocalDiskCentralStoreConfiguration localDiskConfig:
                    return new LocalDiskCentralStorage(localDiskConfig);
                case BlobCentralStoreConfiguration blobStoreConfig:
                    return new BlobCentralStorage(blobStoreConfig);
                default:
                    throw new NotSupportedException();
            }
        }

        private static void ValidateConfiguration(LocalLocationStoreConfiguration configuration)
        {
            Contract.Assert(configuration.Database != null, "Database configuration must be provided.");
            Contract.Assert(configuration.EventStore != null, "Event store configuration must be provided.");
            Contract.Assert(configuration.Checkpoint != null, "Checkpointing configuration must be provided.");
            Contract.Assert(configuration.CentralStore != null, "Central store configuration must be provided.");
        }

        /// <summary>
        /// Gets the counters with high level statistics associated with a current instance.
        /// </summary>
        public CounterSet GetCounters(Context context)
        {
            var counters = Counters.ToCounterSet();
            counters.Merge(Database.Counters.ToCounterSet(), "LocalDatabase.");
            counters.Merge(CentralStorage.Counters.ToCounterSet(), "CentralStorage.");

            if (EventStore != null)
            {
                counters.Merge(EventStore.GetCounters(), "EventStore.");
            }

            counters.Merge(GlobalStore.GetCounters(new OperationContext(context)), "GlobalStore.");
            return counters;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _innerCentralStorage.StartupAsync(context).ThrowIfFailure();

            if (DistributedCentralStorage != null)
            {
                await DistributedCentralStorage.StartupAsync(context).ThrowIfFailure();
            }

            CheckpointManager = new CheckpointManager(Database, GlobalStore, CentralStorage, _configuration.Checkpoint, Counters);

            await GlobalStore.StartupAsync(context).ThrowIfFailure();

            EventStore = CreateEventStore(_configuration, subfolder: "main");

            await Database.StartupAsync(context).ThrowIfFailure();

            await EventStore.StartupAsync(context).ThrowIfFailure();

            MachineReputationTracker = new MachineReputationTracker(context, _clock, _configuration.ReputationTrackerConfiguration, ResolveMachineLocation, ClusterState);

            // We need to detect what our previous exit state was in order to choose the appropriate recovery strategy.
            var fetchLastMachineStateResult = await GlobalStore.SetLocalMachineStateAsync(context, MachineState.Unknown);
            var lastMachineState = MachineState.Unknown;
            if (fetchLastMachineStateResult.Succeeded)
            {
                lastMachineState = fetchLastMachineStateResult.Value;
            }

            _heartbeatMachineState = lastMachineState switch
            {
                // Here, when we set a Closed state, it means we will wait until the next heartbeat after
                // reconciliation finishes before announcing ourselves as open.
                MachineState.Unknown => MachineState.Open,
                MachineState.Open => MachineState.Open,
                MachineState.DeadUnavailable => MachineState.Closed,
                MachineState.DeadExpired => MachineState.Closed,
                MachineState.Closed => MachineState.Open,
                _ => throw new NotImplementedException($"Unknown machine state: {lastMachineState}"),
            };

            // Configuring a heartbeat timer. The timer is used differently by a master and by a worker.
            _heartbeatTimer = new Timer(
                _ =>
                {
                    var nestedContext = context.CreateNested(nameof(LocalLocationStore));
                    HeartbeatAsync(nestedContext).FireAndForget(nestedContext);
                }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            _postInitializationTask = Task.Run(() => HeartbeatAsync(context, inline: true)
                .ThenAsync(r => r.Succeeded ? r : new BoolResult(r, "Failed initializing Local Location Store")))
                // Run continuations asynchronously because many callers may be queued waiting for initialization to complete
                .RunContinuationsAsync();

            await _postInitializationTask.FireAndForgetOrInlineAsync(context, _configuration.InlinePostInitialization);

            Database.DatabaseInvalidated = OnContentLocationDatabaseInvalidation;

            return BoolResult.Success;
        }

        private MachineLocation ResolveMachineLocation(MachineId machineId)
        {
            if (ClusterState.TryResolve(machineId, out var result))
            {
                return result;
            }

            throw new InvalidOperationException($"Unable to resolve machine location for machine id '{machineId}'.");
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            Tracer.Info(context, "Shutting down local location store.");

            BoolResult result = BoolResult.Success;
            if (_postInitializationTask != null)
            {
                var postInitializationResult = await EnsureInitializedAsync();
                if (!postInitializationResult && !postInitializationResult.IsCancelled)
                {
                    result &= postInitializationResult;
                }
            }

            _heartbeatMachineState = MachineState.Closed;

#pragma warning disable AsyncFixer02
            _heartbeatTimer?.Dispose();
#pragma warning restore AsyncFixer02

            await GlobalStore.SetLocalMachineStateAsync(context, MachineState.Closed).IgnoreFailure();

            if (EventStore != null)
            {
                result &= await EventStore.ShutdownAsync(context);
            }

            result &= await Database.ShutdownAsync(context);

            CurrentRole = null;

            result &= await GlobalStore.ShutdownAsync(context);

            if (DistributedCentralStorage != null)
            {
                result &= await DistributedCentralStorage.ShutdownAsync(context);
            }

            result &= await _innerCentralStorage.ShutdownAsync(context);

            return result;
        }

        /// <summary>
        /// For testing purposes only.
        /// Releases current master role (other workers now can pick it up) and changes the current role to a newly acquired one.
        /// </summary>
        internal async Task ReleaseRoleIfNecessaryAsync(OperationContext operationContext)
        {
            CurrentRole = await GlobalStore.ReleaseRoleIfNecessaryAsync(operationContext);
        }

        /// <summary>
        /// Restore checkpoint.
        /// </summary>
        internal async Task<BoolResult> ProcessStateAsync(OperationContext context, CheckpointState checkpointState, bool inline, bool forceRestore = false)
        {
            var operationResult = await RunOutOfBandAsync(
                _configuration.InlinePostInitialization || inline,
                ref _pendingProcessCheckpointTask,
                _pendingProcessCheckpointTaskLock,
                context.CreateOperation(Tracer, async () =>
                {
                    var oldRole = CurrentRole;
                    var newRole = checkpointState.Role;
                    var switchedRoles = oldRole != newRole;

                    if (switchedRoles)
                    {
                        Tracer.Debug(context, $"Switching Roles: New={newRole}, Old={oldRole}.");

                        // Saving a global information about the new role of a current service.
                        context.TracingContext.ChangeRole(newRole.ToString());

                        // Local database should be immutable on workers and only master is responsible for collecting stale records
                        Database.SetDatabaseMode(isDatabaseWriteable: newRole == Role.Master);
                        ClusterState.EnableBinManagerUpdates = newRole == Role.Master;
                    }

                    // Set the current role to the newly acquired role
                    CurrentRole = newRole;

                    // Always restore when switching roles
                    bool shouldRestore = switchedRoles;

                    // Restore if this is a worker and the last restore time is past the restore interval
                    shouldRestore |= (newRole == Role.Worker && ShouldSchedule(
                                          _configuration.Checkpoint.RestoreCheckpointInterval,
                                          _lastRestoreTime));
                    BoolResult result;

                    if (shouldRestore || forceRestore)
                    {
                        result = await RestoreCheckpointStateAsync(context, checkpointState);
                        if (!result)
                        {
                            return result;
                        }

                        _lastRestoreTime = _clock.UtcNow;

                        // Update the checkpoint time to avoid uploading a checkpoint immediately after restoring on the master
                        _lastCheckpointTime = _lastRestoreTime;
                    }

                    var updateResult = await UpdateClusterStateAsync(context, machineState: _heartbeatMachineState);

                    if (!updateResult)
                    {
                        return updateResult;
                    }

                    if (newRole == Role.Master)
                    {
                        // Start receiving events from the given checkpoint
                        result = EventStore.StartProcessing(context, checkpointState.StartSequencePoint);
                    }
                    else
                    {
                        // Stop receiving events.
                        result = EventStore.SuspendProcessing(context);
                    }

                    if (!result)
                    {
                        return result;
                    }

                    if (newRole == Role.Master)
                    {
                        // Only create a checkpoint if the machine is currently a master machine and was a master machine
                        if (ShouldSchedule(_configuration.Checkpoint.CreateCheckpointInterval, _lastCheckpointTime))
                        {
                            result = await CreateCheckpointAsync(context);
                            if (!result)
                            {
                                return result;
                            }

                            _lastCheckpointTime = _clock.UtcNow;
                        }
                    }

                    return result;
                }),
                out var factoryWasCalled);

            if (!factoryWasCalled)
            {
                Tracer.Debug(context, "ProcessStateAsync operation was skipped because _pendingProcessCheckpointTask is still running.");
            }

            return operationResult;
        }

        internal async Task<BoolResult> HeartbeatAsync(OperationContext context, bool inline = false, bool forceRestore = false)
        {
            BoolResult result = await context.PerformOperationAsync(Tracer,
                () => processStateCoreAsync(),
                extraEndMessage: result => $"Skipped=[{(result.Succeeded ? result.Value : false)}]");

            if (result.Succeeded)
            {
                // A post initialization process may fail due to a transient issue, like a storage failure or an inconsistent checkpoint's state.
                // The transient error can go away and the system may recover itself by calling this method again.

                // In this case we need to reset _postInitializationTask and move its state from "failure" to "success"
                // and unblock all the public operations that will fail if post-initialization task is unsuccessful.

                _postInitializationTask = BoolResult.SuccessTask;
            }

            return result;

            async Task<Result<bool>> processStateCoreAsync()
            {
                try
                {
                    using (SemaphoreSlimToken.TryWait(_heartbeatGate, 0, out var acquired))
                    {
                        // This makes sure that the heartbeat is only run once at the time for each call to this
                        // function. It is a non-blocking check.
                        if (!acquired)
                        {
                            return true;
                        }

                        var checkpointState = await GlobalStore.GetCheckpointStateAsync(context);
                        if (!checkpointState)
                        {
                            // The error is already logged. We need to specify cast because of the implicit bool cast.
                            return new Result<bool>((ResultBase)checkpointState);
                        }

                        var processStateResult = await ProcessStateAsync(context, checkpointState.Value, inline, forceRestore);
                        if (!processStateResult)
                        {
                            // The error is already logged. We need to specify cast because of the implicit bool cast.
                            return new Result<bool>((ResultBase)processStateResult);
                        }

                        return false;
                    }
                }
                finally
                {
                    if (!ShutdownStarted)
                    {
                        // Reseting the timer at the end to avoid multiple calls if it at the same time.
                        _heartbeatTimer.Change(_configuration.Checkpoint.HeartbeatInterval, Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        private Task<BoolResult> UpdateClusterStateAsync(OperationContext context, MachineState machineState)
        {
            var startMaxMachineId = ClusterState.MaxMachineId;

            int postDbMaxMachineId = startMaxMachineId;
            int postGlobalMaxMachineId = startMaxMachineId;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    if (_configuration.Database.StoreClusterState)
                    {
                        Database.UpdateClusterState(context, ClusterState, write: false);
                        postDbMaxMachineId = ClusterState.MaxMachineId;
                    }

                    var updateResult = await GlobalStore.UpdateClusterStateAsync(context, ClusterState, machineState);
                    postGlobalMaxMachineId = ClusterState.MaxMachineId;

                    // Update the local database with new machines if the cluster state was updated from the global store
                    if (updateResult && (CurrentRole == Role.Master) && _configuration.Database.StoreClusterState)
                    {
                        Database.UpdateClusterState(context, ClusterState, write: true);
                    }

                    if (CurrentRole == Role.Master && _configuration.UseBinManager)
                    {
                        ClusterState.InitializeBinManagerIfNeeded(locationsPerBin: _configuration.ProactiveCopyLocationsThreshold, _clock, expiryTime: _configuration.PreferredLocationsExpiryTime);
                    }

                    return updateResult;
                },
                extraEndMessage: result => $"[MaxMachineId=({startMaxMachineId} -> (Db={postDbMaxMachineId}, Global={postGlobalMaxMachineId}))]");
        }

        private bool ShouldSchedule(TimeSpan interval, DateTime lastTime)
        {
            return (_clock.UtcNow - lastTime) >= interval;
        }

        /// <summary>
        /// Run a given operation out of band but only if <paramref name="pendingTask"/> is not completed.
        /// </summary>
        public static Task<BoolResult> RunOutOfBandAsync(bool inline, ref Task<BoolResult> pendingTask, object locker, PerformAsyncOperationBuilder<BoolResult> operation, out bool factoryWasCalled, [CallerMemberName] string caller = null)
        {
            factoryWasCalled = false;
            if (inline)
            {
                operation.AppendStartMessage(extraStartMessage: "inlined=true");
                return operation.RunAsync(caller);
            }

            // Using a separate method to avoid a race condition.
            if (pendingTaskIsNullOrCompleted(pendingTask))
            {
                lock (locker)
                {
                    if (pendingTaskIsNullOrCompleted(pendingTask))
                    {
                        factoryWasCalled = true;
                        pendingTask = Task.Run(() => operation.RunAsync(caller));
                    }
                }
            }

            return BoolResult.SuccessTask;

            static bool pendingTaskIsNullOrCompleted(Task task)
            {
                return task == null || task.IsCompleted;
            }
        }

        internal async Task<BoolResult> CreateCheckpointAsync(OperationContext context)
        {
            // Need to obtain the sequence point first to avoid race between the sequence point and the database's state.
            EventSequencePoint currentSequencePoint = EventStore.GetLastProcessedSequencePoint();
            if (currentSequencePoint == null || currentSequencePoint.SequenceNumber == null)
            {
                Tracer.Debug(context.TracingContext, "Could not create a checkpoint because the sequence point is missing. Apparently, no events were processed at this time.");
                return BoolResult.Success;
            }

            var manager = ClusterState.BinManager;
            if (manager != null)
            {
                var serializeResult = manager.Serialize();
                if (serializeResult)
                {
                    var bytes = serializeResult.Value!;
                    var serializedString = Convert.ToBase64String(bytes);
                    Database.SetGlobalEntry(BinManagerKey, serializedString);
                }
                else
                {
                    serializeResult.TraceIfFailure(context);
                }
            }

            return await CheckpointManager.CreateCheckpointAsync(context, currentSequencePoint);
        }

        private async Task<BoolResult> RestoreCheckpointStateAsync(OperationContext context, CheckpointState checkpointState)
        {
            var latestCheckpoint = CheckpointManager.GetLatestCheckpointInfo(context);
            var latestCheckpointAge = _clock.UtcNow - latestCheckpoint?.checkpointTime;

            // Only skip if this is the first restore and it is sufficiently recent
            // NOTE: _lastRestoreTime will be set since skipping this operation will return successful result.
            var shouldSkipRestore = _lastRestoreTime == default
                && latestCheckpoint != null
                && _configuration.Checkpoint.RestoreCheckpointAgeThreshold != default
                && latestCheckpoint.Value.checkpointTime.IsRecent(_clock.UtcNow, _configuration.Checkpoint.RestoreCheckpointAgeThreshold);

            if (latestCheckpointAge > _configuration.LocationEntryExpiry)
            {
                Tracer.Debug(context, $"Checkpoint {latestCheckpoint.Value.checkpointId} age is {latestCheckpointAge}, which is larger than location expiry {_configuration.LocationEntryExpiry}");
            }

            var latestCheckpointId = latestCheckpoint?.checkpointId ?? "null";
            if (shouldSkipRestore)
            {
                Tracer.Debug(context, $"First checkpoint {checkpointState} will be skipped. LatestCheckpointId={latestCheckpointId}, LatestCheckpointAge={latestCheckpointAge}, Threshold=[{_configuration.Checkpoint.RestoreCheckpointAgeThreshold}]");
                Counters[ContentLocationStoreCounters.RestoreCheckpointsSkipped].Increment();
                return BoolResult.Success;
            }
            else if (_lastRestoreTime == default)
            {
                Tracer.Debug(context, $"First checkpoint {checkpointState} will not be skipped. LatestCheckpointId={latestCheckpointId}, LatestCheckpointAge={latestCheckpointAge}, Threshold=[{_configuration.Checkpoint.RestoreCheckpointAgeThreshold}]");
            }

            if (checkpointState.CheckpointAvailable)
            {
                if (_lastCheckpointId != checkpointState.CheckpointId)
                {
                    Tracer.Debug(context, $"Restoring the checkpoint '{checkpointState}'.");
                    var possibleCheckpointResult = await CheckpointManager.RestoreCheckpointAsync(context, checkpointState);
                    if (!possibleCheckpointResult)
                    {
                        return possibleCheckpointResult;
                    }

                    Counters[ContentLocationStoreCounters.RestoreCheckpointsSucceeded].Increment();
                    _lastCheckpointId = checkpointState.CheckpointId;
                }
                else
                {
                    Tracer.Debug(context, $"Checkpoint '{checkpointState}' already restored.");
                }

                // Update bin manager in cluster state.
                if (_configuration.UseBinManager && Database.TryGetGlobalEntry(BinManagerKey, out var serializedString))
                {
                    var bytes = Convert.FromBase64String(serializedString);
                    var binManagerResult = BinManager.CreateFromSerialized(
                        bytes,
                        _configuration.ProactiveCopyLocationsThreshold,
                        _clock,
                        _configuration.PreferredLocationsExpiryTime);
                    binManagerResult.TraceIfFailure(context);

                    ClusterState.BinManager = binManagerResult.Succeeded ? binManagerResult.Value : null;
                }
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// Gets the list of <see cref="ContentLocationEntry"/> for every hash specified by <paramref name="contentHashes"/> from a given <paramref name="origin"/>.
        /// </summary>
        public async Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, MachineId requestingMachineId, IReadOnlyList<ContentHash> contentHashes, GetBulkOrigin origin)
        {
            Contract.Requires(contentHashes != null);

            var postInitializationResult = await EnsureInitializedAsync();
            if (!postInitializationResult)
            {
                return new GetBulkLocationsResult(postInitializationResult);
            }

            if (contentHashes.Count == 0)
            {
                return new GetBulkLocationsResult(CollectionUtilities.EmptyArray<ContentHashWithSizeAndLocations>(), origin);
            }

            var result = await context.PerformOperationAsync(
                Tracer,
                () => GetBulkCoreAsync(context, requestingMachineId, contentHashes, origin),
                traceOperationStarted: false,
                extraEndMessage: r => $"GetBulk({origin}) => [{r.GetShortHashesTraceString()}]");

            return result;
        }

        private Task<GetBulkLocationsResult> GetBulkCoreAsync(OperationContext context, MachineId requestingMachineId, IReadOnlyList<ContentHash> contentHashes, GetBulkOrigin origin)
        {
            if (origin == GetBulkOrigin.Local)
            {
                // Query local db
                return GetBulkFromLocalAsync(context, requestingMachineId, contentHashes);
            }
            else
            {
                // Query global store
                return GetBulkFromGlobalAsync(context, requestingMachineId, contentHashes);
            }
        }

        internal Task<GetBulkLocationsResult> GetBulkFromGlobalAsync(OperationContext context, MachineId requestingMachineId, IReadOnlyList<ContentHash> contentHashes)
        {
            Counters[ContentLocationStoreCounters.GetBulkGlobalHashes].Add(contentHashes.Count);

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var entries = await GlobalStore.GetBulkAsync(context, contentHashes);
                    if (!entries)
                    {
                        return new GetBulkLocationsResult(entries);
                    }

                    return await ResolveLocationsAsync(context, requestingMachineId, entries.Value, contentHashes, GetBulkOrigin.Global);
                },
                Counters[ContentLocationStoreCounters.GetBulkGlobal],
                traceErrorsOnly: true); // Intentionally tracing errors only.
        }

        private Task<GetBulkLocationsResult> GetBulkFromLocalAsync(OperationContext context, MachineId requestingMachineId, IReadOnlyList<ContentHash> contentHashes)
        {
            Counters[ContentLocationStoreCounters.GetBulkLocalHashes].Add(contentHashes.Count);

            return context.PerformOperationAsync(
                Tracer,
                () =>
                {
                    var entries = new List<ContentLocationEntry>(contentHashes.Count);
                    var touchEventHashes = new List<ContentHash>();
                    var now = _clock.UtcNow;

                    foreach (var hash in contentHashes)
                    {
                        if (TryGetContentLocations(context, hash, out var entry))
                        {
                            if ((entry.LastAccessTimeUtc.ToDateTime() + _configuration.TouchFrequency < now)
                                && _recentlyTouchedHashes.Add(hash, _configuration.TouchFrequency))
                            {
                                // Entry was not touched recently so no need to send a touch event
                                touchEventHashes.Add(hash);
                            }
                        }
                        else
                        {
                            // NOTE: Entries missing from the local db are not touched. They referring to content which is no longer
                            // in the system or content which is new and has not been propagated through local db
                            entry = ContentLocationEntry.Missing;
                        }

                        entries.Add(entry);
                    }

                    if (touchEventHashes.Count != 0)
                    {
                        EventStore.Touch(context, requestingMachineId, touchEventHashes, _clock.UtcNow).ThrowIfFailure();
                    }

                    return ResolveLocationsAsync(context, requestingMachineId, entries, contentHashes, GetBulkOrigin.Local);
                },
                traceErrorsOnly: true, // Intentionally tracing errors only.
                counter: Counters[ContentLocationStoreCounters.GetBulkLocal]);
        }

        private async Task<GetBulkLocationsResult> ResolveLocationsAsync(OperationContext context, MachineId requestingMachineId, IReadOnlyList<ContentLocationEntry> entries, IReadOnlyList<ContentHash> contentHashes, GetBulkOrigin origin)
        {
            Contract.Requires(entries.Count == contentHashes.Count);

            var results = new List<ContentHashWithSizeAndLocations>(entries.Count);
            bool hasUnknownLocations = false;

            for (int i = 0; i < entries.Count; i++)
            {
                // TODO: Its probably possible to do this by getting the max machine id in the locations set rather than enumerating all of them (bug 1365340)
                var entry = entries[i];
                foreach (var machineId in entry.Locations.EnumerateMachineIds())
                {
                    if (!ClusterState.TryResolve(machineId, out _))
                    {
                        hasUnknownLocations = true;
                    }
                }

                var contentHash = contentHashes[i];
                results.Add(new ContentHashWithSizeAndLocations(contentHash, entry.ContentSize, GetMachineList(contentHash, entry), entry));
            }

            // Machine locations are resolved lazily by MachineList.
            // If we faced at least one unknown machine location we're forcing an update of a cluster state to make the resolution successful.
            if (hasUnknownLocations)
            {
                // Update cluster. Query global to ensure that we have all machines ids (even those which may not be added
                // to local db yet.)
                var result = await UpdateClusterStateAsync(context, machineState: MachineState.Unknown);
                if (!result)
                {
                    return new GetBulkLocationsResult(result);
                }
            }

            return new GetBulkLocationsResult(results, origin);
        }

        private IReadOnlyList<MachineLocation> GetMachineList(ContentHash hash, ContentLocationEntry entry)
        {
            if (entry.IsMissing)
            {
                return CollectionUtilities.EmptyArray<MachineLocation>();
            }

            return new MachineList(
                entry.Locations,
                machineId =>
                {
                    return ResolveMachineLocation(machineId);
                },
                MachineReputationTracker,
                randomize: true);
        }

        /// <summary>
        /// Gets content locations for a given <paramref name="hash"/> from a local database.
        /// </summary>
        private bool TryGetContentLocations(OperationContext context, ContentHash hash, out ContentLocationEntry entry)
        {
            using (Counters[ContentLocationStoreCounters.DatabaseGet].Start())
            {
                if (Database.TryGetEntry(context, hash, out entry))
                {
                    return true;
                }

                return false;
            }
        }

        private enum RegisterAction
        {
            EagerGlobal,
            RecentInactiveEagerGlobal,
            RecentRemoveEagerGlobal,
            LazyEventOnly,
            LazyTouchEventOnly,
            SkippedDueToRecentAdd,
            SkippedDueToRedundantAdd,
        }

        private enum RegisterCoreAction
        {
            Skip,
            Events,
            Global,
        }

        private static RegisterCoreAction ToCoreAction(RegisterAction action)
        {
            switch (action)
            {
                case RegisterAction.EagerGlobal:
                case RegisterAction.RecentInactiveEagerGlobal:
                case RegisterAction.RecentRemoveEagerGlobal:
                    return RegisterCoreAction.Global;
                case RegisterAction.LazyEventOnly:
                case RegisterAction.LazyTouchEventOnly:
                    return RegisterCoreAction.Events;
                case RegisterAction.SkippedDueToRecentAdd:
                case RegisterAction.SkippedDueToRedundantAdd:
                    return RegisterCoreAction.Skip;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, $"Unexpected action '{action}'.");
            }
        }

        private RegisterAction GetRegisterAction(OperationContext context, MachineId machineId, ContentHash hash, DateTime now)
        {
            if (_configuration.SkipRedundantContentLocationAdd && _recentlyRemovedHashes.Contains(hash))
            {
                // Content was recently removed. Eagerly register with global store.
                Counters[ContentLocationStoreCounters.LocationAddRecentRemoveEager].Increment();
                return RegisterAction.RecentRemoveEagerGlobal;
            }

            if (ClusterState.LastInactiveTime.IsRecent(now, _configuration.MachineStateRecomputeInterval.Multiply(5)))
            {
                // The machine was recently inactive. We should eagerly register content for some amount of time (a few heartbeats) because content may be currently filtered from other machines
                // local db results due to inactive machines filter.
                Counters[ContentLocationStoreCounters.LocationAddRecentInactiveEager].Increment();
                return RegisterAction.RecentInactiveEagerGlobal;
            }

            if (_configuration.SkipRedundantContentLocationAdd && _recentlyAddedHashes.Contains(hash))
            {
                // Content was recently added for the machine by a prior operation
                Counters[ContentLocationStoreCounters.RedundantRecentLocationAddSkipped].Increment();
                return RegisterAction.SkippedDueToRecentAdd;
            }

            // Query local db and only eagerly update global store if replica count is below threshold
            if (TryGetContentLocations(context, hash, out var entry))
            {
                // TODO[LLS]: Is it ok to ignore a hash already registered to the machine. There is a subtle race condition (bug 1365340)
                // here if the hash is deleted and immediately added to the machine
                if (_configuration.SkipRedundantContentLocationAdd
                    && entry.Locations[machineId.Index]) // content is registered for this machine
                {
                    // If content was touched recently, we can skip. Otherwise, we touch via event
                    if (entry.LastAccessTimeUtc.ToDateTime().IsRecent(now, _configuration.TouchFrequency))
                    {
                        Counters[ContentLocationStoreCounters.RedundantLocationAddSkipped].Increment();
                        return RegisterAction.SkippedDueToRedundantAdd;
                    }
                    else
                    {
                        Counters[ContentLocationStoreCounters.LazyTouchEventOnly].Increment();
                        return RegisterAction.LazyTouchEventOnly;
                    }
                }

                // The entry is not on the machine, we definitely need to register the content location via event stream
                if (entry.Locations.Count >= _configuration.SafeToLazilyUpdateMachineCountThreshold)
                {
                    Counters[ContentLocationStoreCounters.LocationAddQueued].Increment();
                    return RegisterAction.LazyEventOnly;
                }
            }

            Counters[ContentLocationStoreCounters.LocationAddEager].Increment();
            return RegisterAction.EagerGlobal;
        }

        internal Task<BoolResult> EnsureInitializedAsync()
        {
            Contract.Assert(_postInitializationTask != null);
            return _postInitializationTask;
        }

        /// <summary>
        /// Notifies a central store that content represented by <paramref name="contentHashes"/> is available on a current machine.
        /// </summary>
        public async Task<BoolResult> RegisterLocalLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ContentHashWithSize> contentHashes, bool touch)
        {
            Contract.Requires(contentHashes != null);

            var postInitializationResult = await EnsureInitializedAsync();
            if (!postInitializationResult)
            {
                return postInitializationResult;
            }

            if (contentHashes.Count == 0)
            {
                return BoolResult.Success;
            }

            string extraMessage = string.Empty;
            return await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var eventContentHashes = new List<ContentHashWithSize>(contentHashes.Count);
                    var eagerContentHashes = new List<ContentHashWithSize>(contentHashes.Count);
                    var actions = new List<RegisterAction>(contentHashes.Count);
                    var now = _clock.UtcNow;

                    // Select which hashes are not already registered for the local machine and those which must eagerly go to the global store
                    foreach (var contentHash in contentHashes)
                    {
                        var registerAction = GetRegisterAction(context, machineId, contentHash.Hash, now);
                        actions.Add(registerAction);

                        var coreAction = ToCoreAction(registerAction);
                        if (coreAction == RegisterCoreAction.Skip)
                        {
                            continue;
                        }

                        // In both cases (RegisterCoreAction.Events and RegisterCoreAction.Global)
                        // we need to send an events over event hub.
                        eventContentHashes.Add(contentHash);

                        if (coreAction == RegisterCoreAction.Global)
                        {
                            eagerContentHashes.Add(contentHash);
                        }
                    }

                    var registerActionsMessage = string.Join(", ", contentHashes.Select((c, i) => $"{new ShortHash(c.Hash)}={actions[i]}"));
                    extraMessage = $"Register actions(Eager={eagerContentHashes.Count.ToString()}, Event={eventContentHashes.Count.ToString()}): [{registerActionsMessage}]";

                    if (eagerContentHashes.Count != 0)
                    {
                        // Update global store
                        await GlobalStore.RegisterLocationAsync(context, machineId, eagerContentHashes).ThrowIfFailure();
                    }

                    if (eventContentHashes.Count != 0)
                    {
                        // Send add events
                        EventStore.AddLocations(context, machineId, eventContentHashes, touch).ThrowIfFailure();
                    }

                    // Register all recently added hashes so subsequent operations do not attempt to re-add
                    if (_configuration.SkipRedundantContentLocationAdd)
                    {
                        foreach (var hash in eventContentHashes)
                        {
                            _recentlyAddedHashes.Add(hash.Hash, _configuration.TouchFrequency);
                        }

                        // Only eagerly added hashes should invalidate recently removed hashes.
                        foreach (var hash in eagerContentHashes)
                        {
                            _recentlyRemovedHashes.Invalidate(hash.Hash);
                        }
                    }

                    return BoolResult.Success;
                },
                Counters[ContentLocationStoreCounters.RegisterLocalLocation],
                traceOperationStarted: false,
                extraEndMessage: _ => extraMessage);
        }

        /// <nodoc />
        public async Task<BoolResult> TouchBulkAsync(OperationContext context, MachineId machineId, IReadOnlyList<ContentHash> contentHashes)
        {
            Contract.Requires(contentHashes != null);

            var postInitializationResult = await EnsureInitializedAsync();
            if (!postInitializationResult)
            {
                return postInitializationResult;
            }

            if (contentHashes.Count == 0)
            {
                return BoolResult.Success;
            }

            return context.PerformOperation(
                Tracer,
                () =>
                {
                    var touchEventHashes = new List<ContentHash>();
                    var now = _clock.UtcNow;

                    foreach (var hash in contentHashes)
                    {
                        if (_recentlyAddedHashes.Contains(hash) || !_recentlyTouchedHashes.Add(hash, _configuration.TouchFrequency))
                        {
                            continue;
                        }

                        if (TryGetContentLocations(context, hash, out var entry) && (entry.LastAccessTimeUtc.ToDateTime() + _configuration.TouchFrequency) > now)
                        {
                            continue;
                        }

                        // Entry was not touched recently so no need to send a touch event
                        touchEventHashes.Add(hash);
                    }

                    if (touchEventHashes.Count != 0)
                    {
                        return EventStore.Touch(context, machineId, touchEventHashes, now);
                    }

                    return BoolResult.Success;
                },
                Counters[ContentLocationStoreCounters.BackgroundTouchBulk]);
        }

        /// <nodoc />
        public async Task<BoolResult> TrimBulkAsync(OperationContext context, MachineId machineId, IReadOnlyList<ContentHash> contentHashes)
        {
            Contract.Requires(contentHashes != null);

            var postInitializationResult = await EnsureInitializedAsync();
            if (!postInitializationResult)
            {
                return postInitializationResult;
            }

            if (contentHashes.Count == 0)
            {
                return BoolResult.Success;
            }

            foreach (var contentHashesPage in contentHashes.GetPages(100))
            {
                context.TraceDebug($"LocalLocationStore.TrimBulk({contentHashesPage.GetShortHashesTraceString()})");
            }

            return context.PerformOperation(
                Tracer,
                () =>
                {
                    if (_configuration.SkipRedundantContentLocationAdd)
                    {
                        foreach (var hash in contentHashes)
                        {
                            // Content has been removed. Ensure that subsequent additions will not be skipped
                            _recentlyAddedHashes.Invalidate(hash);
                            _recentlyRemovedHashes.Add(hash, _configuration.TouchFrequency);
                        }
                    }

                    // Send remove event for hashes
                    return EventStore.RemoveLocations(context, machineId, contentHashes);
                },
                Counters[ContentLocationStoreCounters.TrimBulkLocal]);
        }

        /// <summary>
        /// Computes content hashes with effective last access time sorted in LRU manner.
        /// </summary>
        public IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrder(
            Context context,
            IDistributedMachineInfo machineInfo,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo,
            bool reverse)
        {
            // contentHashesWithInfo is literally all data inside the content directory. The Purger wants to remove
            // content until we are within quota. Here we return batches of content to be removed.

            // contentHashesWithInfo is sorted by (local) LastAccessTime in descending order (Least Recently Used).
            if (contentHashesWithInfo.Count != 0)
            {
                var first = contentHashesWithInfo[0];
                var last = contentHashesWithInfo[contentHashesWithInfo.Count - 1];

                context.Debug($"{nameof(GetHashesInEvictionOrder)} start with contentHashesWithInfo.Count={contentHashesWithInfo.Count}, firstAge={first.Age(_clock)}, lastAge={last.Age(_clock)}");
            }

            var operationContext = new OperationContext(context);
            var effectiveLastAccessTimeProvider = new EffectiveLastAccessTimeProvider(_configuration, _clock, new ContentResolver(this, machineInfo));

            // Ideally, we want to remove content we know won't be used again for quite a while. We don't have that
            // information, so we use an evictability metric. Here we obtain and sort by that evictability metric.

            var comparer = reverse
                ? ContentEvictionInfo.AgeBucketingPrecedenceComparer.ReverseInstance
                : ContentEvictionInfo.AgeBucketingPrecedenceComparer.Instance;
            var contentHashesWithLastAccessTimes = contentHashesWithInfo.SelectList(v => new ContentHashWithLastAccessTime(v.ContentHash, v.LastAccessTime));


            if (_configuration.UseFullEvictionSort)
            {
                return GetHashesInEvictionOrderUsingFullSort(
                    operationContext,
                    effectiveLastAccessTimeProvider,
                    comparer,
                    contentHashesWithLastAccessTimes,
                    reverse);
            }
            else
            {
                return GetHashesInEvictionOrderUsingApproximateSort(
                    operationContext,
                    effectiveLastAccessTimeProvider,
                    comparer,
                    contentHashesWithLastAccessTimes);
            }
        }

        private IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrderUsingApproximateSort(
            OperationContext operationContext,
            EffectiveLastAccessTimeProvider effectiveLastAccessTimeProvider,
            IComparer<ContentEvictionInfo> comparer,
            IReadOnlyList<ContentHashWithLastAccessTime> contentHashesWithInfo)
        {
            // Counter for successful eviction candidates. Different than total number of eviction candidates, because this only increments when candidate is above minimum eviction age
            int evictionCount = 0;
            var now = _clock.UtcNow;

            IEnumerable<ContentEvictionInfo> getContentEvictionInfos(IReadOnlyList<ContentHashWithLastAccessTime> page) =>
                GetEffectiveLastAccessTimes(
                        operationContext,
                        effectiveLastAccessTimeProvider,
                        page)
                    .ThrowIfFailure();

            // We make sure that we select a set of the newer content, to ensure that we at least look at newer
            // content to see if it should be evicted first due to having a high number of replicas. We do this by
            // looking at the start as well as at middle of the list.

            // NOTE(jubayard, 12/13/2019): observe that the comparer is the one that decides whether we sort in
            // ascending or descending order by evictability. The oldest and newest elude to the content's actual age,
            // not the evictability metric.
            var oldestContentSortedByEvictability = contentHashesWithInfo
                .Take(contentHashesWithInfo.Count / 2)
                .ApproximateSort(comparer, getContentEvictionInfos, _configuration.EvictionPoolSize, _configuration.EvictionWindowSize, _configuration.EvictionRemovalFraction, _configuration.EvictionDiscardFraction);

            var newestContentSortedByEvictability = contentHashesWithInfo
                .SkipOptimized(contentHashesWithInfo.Count / 2)
                .ApproximateSort(comparer, getContentEvictionInfos, _configuration.EvictionPoolSize, _configuration.EvictionWindowSize, _configuration.EvictionRemovalFraction, _configuration.EvictionDiscardFraction);

            return NuCacheCollectionUtilities.MergeOrdered(oldestContentSortedByEvictability, newestContentSortedByEvictability, comparer)
                .Where((candidate, index) => IsPassEvictionAge(operationContext, candidate, _configuration.EvictionMinAge, index, ref evictionCount));
        }

        private IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrderUsingFullSort(
            OperationContext operationContext,
            EffectiveLastAccessTimeProvider effectiveLastAccessTimeProvider,
            IComparer<ContentEvictionInfo> comparer,
            IReadOnlyList<ContentHashWithLastAccessTime> contentHashesWithInfo,
            bool reverse)
        {
            var candidateQueue = new PriorityQueue<ContentEvictionInfo>(_configuration.EvictionPoolSize, comparer);

            foreach (var candidate in GetFullSortedContentWithEffectiveLastAccessTimes(operationContext, effectiveLastAccessTimeProvider, contentHashesWithInfo, reverse))
            {
                candidateQueue.Push(candidate);

                // Only consider content when the eviction pool size is reached.
                if (candidateQueue.Count > _configuration.EvictionPoolSize)
                {
                    yield return candidateQueue.Top;
                    candidateQueue.Pop();
                }
            }

            while (candidateQueue.Count != 0)
            {
                yield return candidateQueue.Top;
                candidateQueue.Pop();
            }
        }

        private IEnumerable<ContentEvictionInfo> GetFullSortedContentWithEffectiveLastAccessTimes(
            OperationContext operationContext,
            EffectiveLastAccessTimeProvider effectiveLastAccessTimeProvider,
            IReadOnlyList<ContentHashWithLastAccessTime> contentHashesWithInfo,
            bool reverse)
        {
            var ageOnlyComparer = reverse
                ? ContentEvictionInfo.ReverseFullSortAgeOnlyComparer
                : ContentEvictionInfo.FullSortAgeOnlyComparer;

            var ageSortingQueue = new PriorityQueue<ContentEvictionInfo>(_configuration.EvictionPoolSize, ageOnlyComparer);

            foreach (var page in contentHashesWithInfo.GetPages(_configuration.EvictionWindowSize))
            {
                foreach (var item in GetEffectiveLastAccessTimes(operationContext, effectiveLastAccessTimeProvider, page).ThrowIfFailure())
                {
                    while (ageSortingQueue.Count != 0 && ContentEvictionInfo.OrderAges(ageSortingQueue.Top.FullSortAge, item.LocalAge, reverse) != OrderResult.PreferSecond)
                    {
                        // NOTE: Optimization to ensure we return elements as soon as possible rather than having to put all elements in the queue
                        // before a single element can be returned.
                        // The top item's distributed age from the queue is older than the current item's local age. This means the top item in the
                        // queue should be preferred to any other item that will be encountered because we that subsequent items will always be newer.

                        // For all items, i: i.LocalAge >= i.FullSortAge (distributed age or effective age). In other words, distributed age is always equal or newer than local age.
                        // Suppose the current item is i_n.
                        // Since we are traversing the items in oldest local age first order. For x > 0, i_n.LocalAge >= i_(n+x).LocalAge
                        // We know from the condition that: ageSortingQueue.Top.FullSortAge >= i_n.LocalAge >= i_(n+x).LocalAge >= i_(n+x).Age
                        yield return ageSortingQueue.Top;
                        ageSortingQueue.Pop();
                    }

                    ageSortingQueue.Push(item);
                }
            }

            while (ageSortingQueue.Count != 0)
            {
                yield return ageSortingQueue.Top;
                ageSortingQueue.Pop();
            }
        }

        private bool IsPassEvictionAge(Context context, ContentEvictionInfo candidate, TimeSpan evictionMinAge, int index, ref int evictionCount)
        {
            if (candidate.Age >= evictionMinAge)
            {
                evictionCount++;
                return true;
            }
            Counters[ContentLocationStoreCounters.EvictionMinAge].Increment();
            context.Debug($"Previous successful eviction attempts = {evictionCount}, Total eviction attempts previously = {index}, minimum eviction age = {evictionMinAge.ToString()}, pool size = {_configuration.EvictionPoolSize}." +
                $" Candidate replica count = {candidate.ReplicaCount}, effective age = {candidate.EffectiveAge}, age = {candidate.Age}.");
            return false;
        }

        /// <summary>
        /// Returns effective last access time for all the <paramref name="contentHashes"/>.
        /// </summary>
        /// <remarks>
        /// Effective last access time is computed based on entries last access time considering content's size and replica count.
        /// This method is used in distributed eviction.
        /// </remarks>
        public Result<IReadOnlyList<ContentEvictionInfo>> GetEffectiveLastAccessTimes(
            OperationContext context,
            IDistributedMachineInfo machineInfo,
            IReadOnlyList<ContentHashWithLastAccessTime> contentHashes)
        {
            Contract.Requires(contentHashes != null);
            Contract.Requires(contentHashes.Count > 0);

            var effectiveLastAccessTimeProvider = new EffectiveLastAccessTimeProvider(_configuration, _clock, new ContentResolver(this, machineInfo));

            return GetEffectiveLastAccessTimes(context, effectiveLastAccessTimeProvider, contentHashes);
        }

        /// <summary>
        /// Returns effective last access time for all the <paramref name="contentHashes"/>.
        /// </summary>
        /// <remarks>
        /// Effective last access time is computed based on entries last access time considering content's size and replica count.
        /// This method is used in distributed eviction.
        /// </remarks>
        private Result<IReadOnlyList<ContentEvictionInfo>> GetEffectiveLastAccessTimes(
            OperationContext context,
            EffectiveLastAccessTimeProvider effectiveLastAccessTimeProvider,
            IReadOnlyList<ContentHashWithLastAccessTime> contentHashes)
        {
            Contract.Requires(contentHashes != null);
            Contract.Requires(contentHashes.Count > 0);

            var postInitializationResult = EnsureInitializedAsync().GetAwaiter().GetResult();
            if (!postInitializationResult)
            {
                return new Result<IReadOnlyList<ContentEvictionInfo>>(postInitializationResult);
            }

            return context.PerformOperation(
                    Tracer,
                    () => effectiveLastAccessTimeProvider.GetEffectiveLastAccessTimes(context, contentHashes),
                    Counters[ContentLocationStoreCounters.GetEffectiveLastAccessTimes],
                    traceOperationStarted: false,
                    traceErrorsOnly: true);
        }

        private enum ReconciliationCycleCounters
        {
            Limit,
            Adds,
            Deletes,
        }

        /// <summary>
        /// Forces reconciliation process between local content store and LLS.
        /// </summary>
        public async Task<ReconciliationResult> ReconcileAsync(OperationContext context, MachineId machineId, ILocalContentStore localContentStore)
        {
            var token = context.Token;
            var postInitializationResult = await EnsureInitializedAsync();
            if (!postInitializationResult)
            {
                return new ReconciliationResult(postInitializationResult);
            }

            var result = await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    if (localContentStore == null)
                    {
                        return new ReconciliationResult(new ErrorResult("Local content store is not provided"));
                    }

                    if (_configuration.AllowSkipReconciliation && IsReconcileUpToDate(machineId))
                    {
                        return new ReconciliationResult(addedCount: 0, removedCount: 0, totalLocalContentCount: -1);
                    }

                    token.ThrowIfCancellationRequested();

                    var totalAddedContent = 0;
                    var totalRemovedContent = 0;
                    var allLocalStoreContentCount = 0;
                    ShortHash? lastProcessedHash = null;
                    var isFinished = false;

                    while (!isFinished)
                    {
                        var delayTask = Task.Delay(_configuration.ReconciliationCycleFrequency, context.Token);

                        await context.PerformOperationAsync(
                            Tracer,
                            operation: performReconciliationCycleAsync,
                            caller: "PerformReconciliationCycleAsync",
                            counter: Counters[ContentLocationStoreCounters.ReconciliationCycles],
                            extraEndMessage: r =>
                            {
                                if (!r.Succeeded)
                                {
                                    return string.Empty;
                                }

                                var c = r.Value;
                                return $"Limit=[{c[ReconciliationCycleCounters.Limit].Value}] Adds=[{c[ReconciliationCycleCounters.Adds].Value}] Deletes=[{c[ReconciliationCycleCounters.Deletes].Value}]";
                            }).ThrowIfFailure();

                        if (!isFinished)
                        {
                            await delayTask;
                        }
                    }

                    MarkReconciled(machineId);

                    return new ReconciliationResult(addedCount: totalAddedContent, removedCount: totalRemovedContent, totalLocalContentCount: allLocalStoreContentCount);

                    async Task<Result<CounterCollection<ReconciliationCycleCounters>>> performReconciliationCycleAsync()
                    {
                        // Pause events in main event store while sending reconciliation events via temporary event store
                        // to ensure reconciliation does cause some content to be lost due to apply reconciliation changes
                        // in the wrong order. For instance, if a machine has content [A] and [A] is removed during reconciliation.
                        // It is possible that remove event could be sent before reconciliation event and the final state
                        // in the database would still have missing content [A].
                        using (EventStore.PauseSendingEvents())
                        {
                            var allLocalStoreContentInfos = await localContentStore.GetContentInfoAsync(token);
                            token.ThrowIfCancellationRequested();

                            var allLocalStoreContent = allLocalStoreContentInfos
                                .Select(c => (hash: new ShortHash(c.ContentHash), size: c.Size))
                                .OrderBy(c => c.hash)
                                .SkipWhile(hashWithSize => lastProcessedHash.HasValue && hashWithSize.hash < lastProcessedHash.Value)
                                .ToList();

                            allLocalStoreContentCount = allLocalStoreContent.Count;

                            var dbContent = Database.EnumerateSortedHashesWithContentSizeForMachineId(context, machineId, startingPoint: lastProcessedHash);
                            token.ThrowIfCancellationRequested();

                            // Diff the two views of the local machines content (left = local store, right = content location db)
                            // Then send changes as events
                            var diffedContent = NuCacheCollectionUtilities.DistinctDiffSorted(leftItems: allLocalStoreContent, rightItems: dbContent, t => t.hash);

                            var addedContent = new List<ShortHashWithSize>();
                            var removedContent = new List<ShortHash>();

                            var removalsOnlyLimit = _configuration.ReconciliationMaxRemoveHashesCycleSize ?? _configuration.ReconciliationMaxCycleSize;
                            var maximumAddsOnRemoveBatch = removalsOnlyLimit * (_configuration.ReconciliationMaxRemoveHashesAddPercentage ?? 0);
                            var limit = _configuration.ReconciliationMaxCycleSize;
                            var limitThreshold = Math.Min(limit, removalsOnlyLimit);
                            foreach (var diffItem in diffedContent)
                            {
                                if (addedContent.Count + removedContent.Count >= limit)
                                {
                                    break;
                                }

                                if (diffItem.mode == MergeMode.LeftOnly)
                                {
                                    // Content is not in DB but is in the local store need to send add event
                                    addedContent.Add(new ShortHashWithSize(diffItem.item.hash, diffItem.item.size));
                                }
                                else
                                {
                                    // Content is in DB but is not local store need to send remove event
                                    removedContent.Add(diffItem.item.hash);
                                }

                                // We have the most information about the batch size at the limit, so that's when we
                                // compute exactly what size we need.
                                if (addedContent.Count + removedContent.Count == limitThreshold)
                                {
                                    if (addedContent.Count > 0)
                                    {
                                        if (removedContent.Count > 0 && addedContent.Count <= maximumAddsOnRemoveBatch)
                                        {
                                            limit = removalsOnlyLimit;
                                        }
                                    }
                                    else
                                    {
                                        limit = removalsOnlyLimit;
                                    }
                                }

                                lastProcessedHash = diffItem.item.hash;
                            }

                            Counters[ContentLocationStoreCounters.Reconcile_AddedContent].Add(addedContent.Count);
                            Counters[ContentLocationStoreCounters.Reconcile_RemovedContent].Add(removedContent.Count);
                            totalAddedContent += addedContent.Count;
                            totalRemovedContent += removedContent.Count;

                            // Only call reconcile if content needs to be updated for machine
                            if (addedContent.Count != 0 || removedContent.Count != 0)
                            {
                                // Create separate event store for reconciliation events so they are dispatched first before
                                // events in normal event store which may be queued during reconciliation operation.
                                var reconciliationEventStore = CreateEventStore(_configuration, subfolder: "reconcile");

                                try
                                {
                                    await reconciliationEventStore.StartupAsync(context).ThrowIfFailure();

                                    await reconciliationEventStore.ReconcileAsync(context, machineId, addedContent, removedContent).ThrowIfFailure();

                                    if (Configuration.LogReconciliationHashes)
                                    {
                                        LogContentLocationOperations(
                                            context,
                                            $"{Tracer.Name}.ReconcileAsync",
                                            addedContent.Select(s => (s.Hash, EntryOperation.AddMachine, OperationReason.Reconcile))
                                                .Concat(removedContent.Select(s => (s, EntryOperation.RemoveMachine, OperationReason.Reconcile))));
                                    }
                                }
                                finally
                                {
                                    await reconciliationEventStore.ShutdownAsync(context).ThrowIfFailure();
                                }
                            }

                            // Corner case where they are equal and we have finished should be very unlikely.
                            isFinished = (addedContent.Count + removedContent.Count) < limit;

                            var counters = new CounterCollection<ReconciliationCycleCounters>();
                            counters[ReconciliationCycleCounters.Limit].Add(limit);
                            counters[ReconciliationCycleCounters.Adds].Add(addedContent.Count);
                            counters[ReconciliationCycleCounters.Deletes].Add(removedContent.Count);
                            return counters;
                        }
                    }
                },
                Counters[ContentLocationStoreCounters.Reconcile]);

            if (result.Succeeded)
            {
                _heartbeatMachineState = MachineState.Open;
            }

            return result;
        }

        /// <nodoc />
        public async Task<BoolResult> InvalidateLocalMachineAsync(OperationContext operationContext, MachineId machineId)
        {
            // Unmark reconcile since the machine is invalidated
            MarkReconciled(machineId, reconciled: false);

            _heartbeatMachineState = MachineState.DeadUnavailable;
            return await GlobalStore.SetLocalMachineStateAsync(operationContext, MachineState.DeadUnavailable);
        }

        /// <nodoc />
        public async Task<BoolResult> PutBlobAsync(OperationContext context, ContentHash hash, byte[] blob)
        {
            return await GlobalStore.PutBlobAsync(context, hash, blob);
        }

        /// <nodoc />
        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ContentHash hash)
        {
            return GlobalStore.GetBlobAsync(context, hash);
        }

        private void OnContentLocationDatabaseInvalidation(OperationContext context, Failure<Exception> failure)
        {
            OnContentLocationDatabaseInvalidationAsync(context, failure).FireAndForget(context);
        }

        private async Task OnContentLocationDatabaseInvalidationAsync(OperationContext context, Failure<Exception> failure)
        {
            Contract.Requires(failure != null);

            using (SemaphoreSlimToken.TryWait(_databaseInvalidationGate, 0, out var acquired))
            {
                // If multiple threads fail at the same time (i.e. corruption error), this cheaply deduplicates the
                // restores, avoids redundant logging. This is a non-blocking check.
                if (!acquired)
                {
                    return;
                }

                Tracer.Error(context, $"Content location database has been invalidated. Forcing a restore from the last checkpoint. Error: {failure.DescribeIncludingInnerFailures()}");

                // We can safely ignore errors, because there is nothing more we can do here.
                await HeartbeatAsync(context, forceRestore: true).IgnoreFailure();
            }
        }

        /// <summary>
        /// Adapts <see cref="LocalLocationStore"/> to interface needed for content locations (<see cref="DistributedCentralStorage.ILocationStore"/>) by
        /// <see cref="NuCache.DistributedCentralStorage"/>
        /// </summary>
        private class DistributedCentralStorageLocationStoreAdapter : DistributedCentralStorage.ILocationStore
        {
            public ClusterState ClusterState => _store.ClusterState;

            private readonly LocalLocationStore _store;

            public DistributedCentralStorageLocationStoreAdapter(LocalLocationStore store)
            {
                _store = store;
            }

            public Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes)
            {
                return _store.GetBulkFromGlobalAsync(context, ClusterState.PrimaryMachineId, contentHashes);
            }

            public Task<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentInfo)
            {
                return _store.GlobalStore.RegisterLocationAsync(context, ClusterState.PrimaryMachineId, contentInfo);
            }
        }

        private class ContentLocationDatabaseAdapter : IContentLocationEventHandler
        {
            private readonly ContentLocationDatabase _database;
            private readonly ClusterState _clusterState;

            /// <nodoc />
            public ContentLocationDatabaseAdapter(ContentLocationDatabase database, ClusterState clusterState)
            {
                _database = database;
                _clusterState = clusterState;
            }

            /// <inheritdoc />
            public void ContentTouched(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, UnixTime accessTime)
            {
                _clusterState.MarkMachineActive(sender).TraceIfFailure(context);

                foreach (var hash in hashes.AsStructEnumerable())
                {
                    _database.ContentTouched(context, hash, accessTime);
                }
            }

            /// <inheritdoc />
            public void LocationAdded(OperationContext context, MachineId sender, IReadOnlyList<ShortHashWithSize> hashes, bool reconciling, bool updateLastAccessTime)
            {
                _clusterState.MarkMachineActive(sender).TraceIfFailure(context);

                foreach (var hashWithSize in hashes.AsStructEnumerable())
                {
                    _database.LocationAdded(context, hashWithSize.Hash, sender, hashWithSize.Size, reconciling, updateLastAccessTime);
                }
            }

            /// <inheritdoc />
            public void LocationRemoved(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, bool reconciling)
            {
                _clusterState.MarkMachineActive(sender).TraceIfFailure(context);
                foreach (var hash in hashes.AsStructEnumerable())
                {
                    _database.LocationRemoved(context, hash, sender, reconciling);
                }
            }

            /// <inheritdoc />
            public void MetadataUpdated(OperationContext context, StrongFingerprint strongFingerprint, MetadataEntry entry)
            {
                Analysis.IgnoreResult(_database.TryUpsert(
                    context,
                    strongFingerprint,
                    entry.ContentHashListWithDeterminism,

                    // Update the entry if the current entry is newer
                    // TODO: Use real versioning scheme for updates to resolve possible race conditions and
                    // issues with time comparison due to clock skew
                    shouldReplace: oldEntry => oldEntry.LastAccessTimeUtc <= entry.LastAccessTimeUtc));
            }
        }

        private sealed class ContentResolver : IContentResolver
        {
            private readonly LocalLocationStore _localLocationStore;
            private readonly IDistributedMachineInfo _machineInfo;

            public MachineId LocalMachineId => _machineInfo.LocalMachineId;

            public ContentResolver(LocalLocationStore localLocationStore, IDistributedMachineInfo machineInfo)
            {
                _localLocationStore = localLocationStore;
                _machineInfo = machineInfo;
            }

            /// <inheritdoc />
            public (ContentInfo localInfo, ContentLocationEntry distributedEntry, bool isDesignatedLocation) GetContentInfo(OperationContext context, ContentHash hash)
            {
                ContentInfo localInfo = default;
                bool foundLocalInfo = _machineInfo.LocalContentStore?.TryGetContentInfo(hash, out localInfo) ?? false;

                bool foundDistributedEntry = _localLocationStore.TryGetContentLocations(context, hash, out var distributedEntry);

                if (_localLocationStore._configuration.UpdateStaleLocalLastAccessTimes
                    && foundLocalInfo
                    && foundDistributedEntry
                    && distributedEntry.LastAccessTimeUtc.ToDateTime() > localInfo.LastAccessTimeUtc)
                {
                    // Update the local content store with distributed last access time if it is newer (within some margin of error specified by RedisContentLocationStoreConstants.TargetRange)
                    _machineInfo.LocalContentStore.UpdateLastAccessTimeIfNewer(hash, distributedEntry.LastAccessTimeUtc.ToDateTime());
                    _localLocationStore.Counters[ContentLocationStoreCounters.StaleLastAccessTimeUpdates].Increment();
                }

                bool isDesignatedLocation = _localLocationStore.ClusterState.IsDesignatedLocation(LocalMachineId, hash, includeExpired: true);

                return (localInfo, distributedEntry, isDesignatedLocation);
            }
        }
    }
}
