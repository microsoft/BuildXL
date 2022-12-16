// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
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
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Serialization;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.Tracing.TracingStructuredExtensions;
using static BuildXL.Cache.ContentStore.UtilitiesCore.Internal.CollectionUtilities;

#nullable enable annotations

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Top level class that represents a local location store.
    /// </summary>
    /// <remarks>
    /// Local location store is a mediator between a content location database and a central store.
    /// </remarks>
    public sealed class LocalLocationStore : StartupShutdownComponentBase
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalLocationStore));

        // LLS is 'managed' by multiple 'TransitioningContentLocationStore' instances. Allow multiple
        // startups and shutdowns to account for this.
        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <nodoc />
        public CounterCollection<ContentLocationStoreCounters> Counters { get; }

        /// <nodoc />
        public Role? CurrentRole { get; private set; }

        /// <nodoc />
        public ContentLocationDatabase Database { get; }

        /// <nodoc />
        public IGlobalCacheStore GlobalCacheStore { get; }

        /// <nodoc />
        public LocalLocationStoreConfiguration Configuration { get; }

        /// <nodoc />
        public MachineReputationTracker MachineReputationTracker { get; private set; }

        /// <nodoc />
        public ContentLocationEventStore EventStore { get; }

        private readonly IClock _clock;

        internal CheckpointManager CheckpointManager { get; }

        /// <nodoc />
        public CentralStorage CentralStorage { get; }

        // The (optional) distributed central storage which wraps the inner central storage
        internal DistributedCentralStorage DistributedCentralStorage => CentralStorage as DistributedCentralStorage;

        private readonly ICheckpointRegistry _checkpointRegistry;

        public IMasterElectionMechanism MasterElectionMechanism { get; }

        // Fields that are initialized in StartupCoreAsync method.
        private Timer _heartbeatTimer;

        // Local volatile caches for preventing resending the same events over and over again.
        private readonly VolatileSet<ShortHash> _recentlyAddedHashes;
        private readonly VolatileSet<ShortHash> _recentlyTouchedHashes;

        // Normally content registered with the machine already will be skipped or re-registered lazily. If content was recently removed (evicted),
        // we need to eagerly update the content because other machines may have already received updated db with content unregistered for the this machine.
        // This tracks recent removals so the corresponding content can be registered eagerly with global store.
        private readonly VolatileSet<ShortHash> _recentlyRemovedHashes;

        private DateTime _lastCheckpointTime;

        // The time when the checkpoint was produced by the master.
        private DateTime _llsCheckpointCreationTime;
        private Task<BoolResult> _pendingProcessCheckpointTask = BoolResult.SuccessTask;
        private readonly object _pendingProcessCheckpointTaskLock = new object();

        private DateTime _lastRestoreTime;
        private string? _lastCheckpointId;

        private int _isReconcileCheckpointRunning;
        private ShortHash? _lastProcessedAddHash;
        private ShortHash? _lastProcessedRemoveHash;
        private readonly VolatileSet<ShortHash> _reconcileAddRecents;
        private readonly VolatileSet<ShortHash> _reconcileRemoveRecents;
        private Task<ReconciliationPerCheckpointResult> _pendingReconciliationTask = Task.FromResult<ReconciliationPerCheckpointResult>(null);
        private CancellationTokenSource _reconciliationTokenSource = new CancellationTokenSource();

        private MachineId? _localMachineId;
        private ILocalContentStore? _localContentStore;
        private bool _forceRestoreOnNextProcessState;

        public ClusterStateManager ClusterStateManager { get; private set; }
        internal ClusterState ClusterState => ClusterStateManager.ClusterState;

        private readonly SemaphoreSlim _databaseInvalidationGate = new SemaphoreSlim(1);

        private readonly SemaphoreSlim _heartbeatGate = new SemaphoreSlim(1);

        /// <summary>
        /// Initialization for local location store may take too long if we restore the first checkpoint in there.
        /// So the initialization process is split into two phases: core initialization and post-initialization that should be checked in every public method.
        /// This field has two parts: TaskSourceSlim (TaskCompletionSource under the hood) and a boolean flag that is true when the source is linked to
        /// an actual post-initialization task (the flag is true when the postinialization has started).
        /// This is done to make sure the underlying "post initialization task" is never null and we won't get any errors if the client will wait on
        /// it by calling EnsureInitialized even before StartupAsync method is done.
        /// But in some cases we need to know that the post-initialization has not started yet, for instance, in shutdown logic.
        /// </summary>
        private (TaskSourceSlim<BoolResult> tcs, bool postInitializationStarted) _postInitialization = (TaskSourceSlim.Create<BoolResult>(), false);

        private const string BinManagerKey = "LocalLocationStore.BinManager";

        private const string EventProcessingDelayKey = "LocalLocationStore.EventProcessingDelay";

        private ResultNagleQueue<IReadOnlyList<ShortHashWithSize>, (BoolResult Result, string TraceId)>? _registerNagleQueue;

        private readonly MachineLocationResolver.Settings _machineListSettings;

        private readonly ColdStorage? _coldStorage;

        private readonly ReadOnlyArray<PartitionId> _evictionPartitions;

        private readonly byte[] _machineHash;
        private readonly uint _evictionPartitionOffset;

        /// <nodoc />
        public LocalLocationStore(
            IClock clock,
            IGlobalCacheStore globalCacheStore,
            LocalLocationStoreConfiguration configuration,
            CheckpointManager checkpointManager,
            IMasterElectionMechanism masterElectionMechanism,
            ClusterStateManager clusterStateManager,
            ColdStorage? coldStorage)
        {
            Contract.RequiresNotNull(clock);
            Contract.RequiresNotNull(configuration);

            _clock = clock;
            Counters = checkpointManager.Counters;
            Configuration = configuration;
            GlobalCacheStore = globalCacheStore;
            MasterElectionMechanism = masterElectionMechanism;
            ClusterStateManager = clusterStateManager;
            _checkpointRegistry = checkpointManager.CheckpointRegistry;
            _coldStorage = coldStorage;
            _evictionPartitions = PartitionId.GetPartitions(Configuration.Settings.EvictionPartitionCount);

            _machineHash = MurmurHash3.Create(Encoding.UTF8.GetBytes(Configuration.PrimaryMachineLocation.Path ?? String.Empty)).ToByteArray();

            var reader = new SpanReader(_machineHash.AsSpan());
            _evictionPartitionOffset = reader.Read<uint>();

            _recentlyAddedHashes = new VolatileSet<ShortHash>(clock);
            _recentlyTouchedHashes = new VolatileSet<ShortHash>(clock);
            _recentlyRemovedHashes = new VolatileSet<ShortHash>(clock);
            _reconcileAddRecents = new VolatileSet<ShortHash>(clock);
            _reconcileRemoveRecents = new VolatileSet<ShortHash>(clock);

            Contract.Assert(Configuration.IsValidForLls());

            CentralStorage = checkpointManager.Storage;

            Configuration.Database.TouchFrequency = configuration.TouchFrequency;
            Database = checkpointManager.Database;

            _machineListSettings = new MachineLocationResolver.Settings
            {
                PrioritizeDesignatedLocations = Configuration.MachineListPrioritizeDesignatedLocations,
            };

            CheckpointManager = checkpointManager;
            EventStore = CreateEventStore(Configuration, subfolder: "main");

            LinkLifetime(CentralStorage);
            LinkLifetime(CheckpointManager);
            LinkLifetime(ClusterStateManager);
            LinkLifetime(MasterElectionMechanism);
            LinkLifetime(GlobalCacheStore);
            LinkLifetime(Database);
            LinkLifetime(EventStore);
        }

        internal void PostInitialization(MachineId machineId, ILocalContentStore localContentStore)
        {
            _localMachineId = machineId;
            _localContentStore = localContentStore;
        }

        private ContentLocationEventStore CreateEventStore(LocalLocationStoreConfiguration configuration, string subfolder)
        {
            return ContentLocationEventStore.Create(
                configuration.EventStore,
                new ContentLocationDatabaseAdapter(Database),
                configuration.PrimaryMachineLocation.ToString(),
                CentralStorage,
                configuration.Checkpoint.WorkingDirectory / "reconciles" / subfolder,
                _clock);
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

            return counters;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            MachineReputationTracker = new MachineReputationTracker(context, _clock, ClusterState, Configuration.ReputationTrackerConfiguration);

            // We need to detect what our previous exit state was in order to choose the appropriate recovery strategy.
            var fetchLastMachineStateResult = await SetOrGetMachineStateAsync(context, MachineState.Unknown);
            var lastMachineState = MachineState.Unknown;
            if (fetchLastMachineStateResult.Succeeded)
            {
                lastMachineState = fetchLastMachineStateResult.Value;
            }

            await SetOrGetMachineStateAsync(context, lastMachineState switch
            {
                // Here, when we set a Closed state, it means we will wait until the next heartbeat after
                // reconciliation finishes before announcing ourselves as open.

                // The machine is new to the content tracking mechanism
                MachineState.Unknown => MachineState.Open,
                // This is an abnormal state: we likely crashed earlier, or the shutdown had some sort of failure. The
                // assumption here is that we got restarted briefly afterwards.
                MachineState.Open => MachineState.Open,
                // Machine was shutdown for maintenance (reimage, OS update, etc). It may have had all of its content
                // removed from the drives and content tracking may be extremely inaccurate, so we wait for
                // reconciliation to complete.
                MachineState.DeadUnavailable => MachineState.Closed,
                // Machine didn't heartbeat for long enough that it was considered dead. Many content entries may be
                // missing from content tracking, so we wait for reconciliation to complete.
                MachineState.DeadExpired => MachineState.Closed,
                // Machine shut down correctly and normally.
                MachineState.Closed => MachineState.Open,

                _ => throw new NotImplementedException($"Unknown machine state: {lastMachineState}"),
            }).IgnoreFailure();

            // Configuring a heartbeat timer. The timer is used differently by a master and by a worker.
            _heartbeatTimer = new Timer(
                _ =>
                {
                    var nestedContext = context.CreateNested(nameof(LocalLocationStore));
                    HeartbeatAsync(nestedContext).FireAndForget(nestedContext);
                }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            var postInitializationTask = Task.Run(() => HeartbeatAsync(context, inline: true)
                .ThenAsync(r => r.Succeeded ? r : new BoolResult(r, "Failed initializing Local Location Store")));
            _postInitialization.tcs.LinkToTask(postInitializationTask);
            _postInitialization.postInitializationStarted = true;

            await postInitializationTask.FireAndForgetOrInlineAsync(context, Configuration.InlinePostInitialization);

            Analysis.IgnoreResult(
                postInitializationTask.ContinueWith(
                    _ =>
                    {
                        // It is very important to explicitly trace when the post initialization is done,
                        // because only after that the service can process the requests.
                        LifetimeTracker.ServiceReadyToProcessRequests(context);
                    }));

            Database.DatabaseInvalidated = OnContentLocationDatabaseInvalidation;

            if (Configuration.Settings.GlobalRegisterNagleInterval?.Value is TimeSpan nagleInterval)
            {
                var batchSize = Configuration.Settings.GlobalRegisterNagleBatchSize;
                _registerNagleQueue = ResultNagleQueue<IReadOnlyList<ShortHashWithSize>, (BoolResult Result, string TraceId)>.CreateAndStart(
                    async batch =>
                    {
                        var results = new (BoolResult, string)[batch.Count];
                        var hashes = new List<ShortHashWithSize>();
                        int resultCount = 0;
                        var resultsMemory = results.AsMemory();
                        for (int i = 0; i < batch.Count; i++)
                        {
                            hashes.AddRange(batch[i]);
                            resultCount++;

                            if (i == (batch.Count - 1) || hashes.Count >= batchSize)
                            {
                                var registerContext = context.CreateNested(Tracer.Name, nameof(RegisterLocalLocationAsync));
                                var result = await GlobalRegisterAsync(registerContext, _localMachineId.Value, hashes, touch: true);
                                resultsMemory.Span.Slice(0, resultCount).Fill((result, registerContext.TracingContext.TraceId));
                                resultsMemory = resultsMemory.Slice(resultCount);
                                resultCount = 0;
                                hashes.Clear();
                            }
                        }

                        return results;
                    },
                    Configuration.Settings.GlobalRegisterNagleParallelism,
                    nagleInterval,
                    batchSize);
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            Tracer.Info(context, "Shutting down local location store.");

            _registerNagleQueue?.Dispose();

            BoolResult result = BoolResult.Success;
            if (_postInitialization.postInitializationStarted)
            {
                // If startup procedure reached the point when the post initialization started, then waiting for its completion.
                var postInitializationResult = await EnsureInitializedAsync();
                if (!postInitializationResult && !postInitializationResult.IsCancelled)
                {
                    result &= postInitializationResult;
                }
            }

            // Cancel any ongoing reconciliation cycles, and has to be before we set the machine state as closed, because reconciliation completion can set the state as open.
            await CancelCurrentReconciliationAsync(context);

            await SetOrGetMachineStateAsync(context, MachineState.Closed).IgnoreFailure();

            _heartbeatTimer?.Dispose();

            CurrentRole = null;

            var state = ClusterState.CurrentState;
            if (state == MachineState.DeadUnavailable || state == MachineState.DeadExpired)
            {
                // This only happens when we're shutting down for repairs. If we're the master, it's important that we
                // release the role so as to allow another machine to pick up the role while this machine gets repaired
                await MasterElectionMechanism.ReleaseRoleIfNecessaryAsync(context).IgnoreFailure();
            }

            return result;
        }

        /// <summary>
        /// For testing purposes only.
        /// Releases current master role (other workers now can pick it up) and changes the current role to a newly acquired one.
        /// </summary>
        internal async Task ReleaseRoleIfNecessaryAsync(OperationContext operationContext)
        {
            CurrentRole = await MasterElectionMechanism.ReleaseRoleIfNecessaryAsync(operationContext).ThrowIfFailureAsync();
        }

        /// <summary>
        /// Restore checkpoint.
        /// </summary>
        internal async Task<BoolResult> ProcessStateAsync(OperationContext context, CheckpointState checkpointState, MasterElectionState masterElectionState, bool inline, bool forceRestore = false)
        {
            var operationResult = await RunOutOfBandAsync(
                Configuration.InlinePostInitialization || inline,
                ref _pendingProcessCheckpointTask,
                _pendingProcessCheckpointTaskLock,
                context.CreateOperation(Tracer, async () =>
                {
                    if (_forceRestoreOnNextProcessState)
                    {
                        forceRestore = true;
                        _forceRestoreOnNextProcessState = false;
                    }

                    var oldRole = CurrentRole;
                    var newRole = masterElectionState.Role;
                    var switchedRoles = oldRole != newRole;

                    if (switchedRoles)
                    {
                        Tracer.Debug(context, $"Switching Roles: New={newRole}, Old={oldRole}.");

                        // Saving a global information about the new role of a current service.
                        LoggerExtensions.ChangeRole(newRole.ToString());

                        if (newRole == Role.Master)
                        {
                            // Local database should be immutable on workers and only master is responsible for collecting stale records
                            await Database.SetDatabaseModeAsync(isDatabaseWriteable: true);
                            ClusterState.EnableBinManagerUpdates = true;
                            if (Configuration.UseBinManager)
                            {
                                Tracer.Info(context, $"Initializing bin manager");
                                ClusterState.InitializeBinManagerIfNeeded(
                                    locationsPerBin: Configuration.ProactiveCopyLocationsThreshold,
                                    _clock,
                                    expiryTime: Configuration.PreferredLocationsExpiryTime);
                            }
                        }
                    }

                    // Set the current role to the newly acquired role
                    CurrentRole = newRole;

                    // Always restore when switching roles
                    bool shouldRestore = switchedRoles;

                    // Restore if this is a worker and we should restore
                    shouldRestore |= newRole == Role.Worker && ShouldRestoreCheckpoint(context, checkpointState.CheckpointTime).ThrowIfFailure();

                    BoolResult result;

                    if (shouldRestore || forceRestore)
                    {
                        // Stop receiving events when restoring checkpoint
                        EventStore.SuspendProcessing(context).ThrowIfFailure();

                        result = await RestoreCheckpointStateAsync(context, checkpointState);
                        if (!result)
                        {
                            return result;
                        }

                        _lastRestoreTime = _clock.UtcNow;

                        // Update the checkpoint time to avoid uploading a checkpoint immediately after restoring on the master
                        _lastCheckpointTime = _lastRestoreTime;
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
                        if (ShouldSchedule(Configuration.Checkpoint.CreateCheckpointInterval, _lastCheckpointTime))
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

        public Task<Result<MachineState>> SetOrGetMachineStateAsync(OperationContext context, MachineState proposedState)
        {
            if (proposedState != MachineState.Unknown)
            {
                var currentState = ClusterState.CurrentState;
                switch (currentState)
                {
                    // The in-memory state can only ever be one of these two if the machine is set up for reimaging. In
                    // these cases, we don't want to actually change state of the currently running process no matter what
                    case MachineState.DeadExpired:
                    case MachineState.DeadUnavailable:
                        Tracer.Warning(context, $"Attempt to transition from state `{currentState}` to `{proposedState}`, which is invalid. Transition will not happen.");
                        proposedState = MachineState.Unknown;
                        break;
                    default:
                        break;
                }
            }

            return ClusterStateManager.HeartbeatAsync(context, proposedState);
        }

        private Result<bool> ShouldRestoreCheckpoint(OperationContext context, DateTime checkpointCreationTime)
        {
            var now = _clock.UtcNow;

            // If we haven't restored for too long, force a restore.
            if (ShouldSchedule(Configuration.Checkpoint.RestoreCheckpointInterval, _lastRestoreTime, now))
            {
                return true;
            }

            // At this point, we know we don't need to restore a checkpoint in this heartbeat, however, we can do so
            // anyways if the bucketing allows.
            var result = context.PerformOperation(Tracer, () =>
            {
                var openMachines = ClusterState.OpenMachines.Count;
                Contract.Assert(openMachines >= 1);

                return Result.Success(RestoreCheckpointPacemaker.ShouldRestoreCheckpoint(
                    _machineHash,
                    openMachines,
                    checkpointCreationTime,
                    Configuration.Checkpoint.CreateCheckpointInterval));
            }, messageFactory: r =>
            {
                if (!r.Succeeded)
                {
                    return string.Empty;
                }

                return $"CheckpointCreationTime=[{checkpointCreationTime}] Now=[{now}] Buckets=[{r.Value.Buckets}] Bucket=[{r.Value.Bucket}] RestoreTime=[{r.Value.RestoreTime}]";
            }, traceOperationStarted: false);

            if (!result.Succeeded)
            {
                return false;
            }

            return ShouldSchedule(result.Value.RestoreTime, checkpointCreationTime, now);
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

                // In this case we need to reset the post-initialization task and move its state from "failure" to "success"
                // and unblock all the public operations that will fail if post-initialization task is unsuccessful.
                var postInitialization = _postInitialization.tcs.Task;

                // If the task already in one of the failure states, and the current call is successful, then it means that the transient issue is gone and
                // the component is back to a working state.
                if (postInitialization.IsCompleted)
                {
                    // The task is completed already. If the task is not completed yet, it will be in a moment, because Heartbeat is almost done.
                    if (postInitialization.Status != TaskStatus.RanToCompletion || !postInitialization.GetAwaiter().GetResult().Succeeded)
                    {
                        // The task either failed (with an exception or was canceled), or the result was not successful.
                        var tcs = TaskSourceSlim.Create<BoolResult>();
                        tcs.SetResult(BoolResult.Success);
                        _postInitialization.tcs = tcs;
                    }
                }
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

                        // It is very important that GetRoleAsync is called on every heartbeat to update the master information
                        // and not inside 'ProcessStateAsync' methodthat might take a reasonable time to complete (20+ minutes in some cases).
                        var checkpointState = await _checkpointRegistry.GetCheckpointStateAsync(context).ThrowIfFailureAsync();

                        var leadershipState = await MasterElectionMechanism.GetRoleAsync(context).ThrowIfFailureAsync();

                        if (_coldStorage != null)
                        {
                            // We update the ColdStorage consistent-hashing ring on every heartbeat in case the cluster state has changed 
                            _coldStorage.UpdateRingAsync(context, ClusterState).FireAndForget(context);
                        }

                        await ProcessStateAsync(context, checkpointState, leadershipState, inline, forceRestore).ThrowIfFailureAsync();

                        return false;
                    }
                }
                finally
                {
                    if (!ShutdownStarted)
                    {
                        // Reseting the timer at the end to avoid multiple calls if it at the same time.
                        _heartbeatTimer.Change(Configuration.Checkpoint.HeartbeatInterval, Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        private bool ShouldSchedule(TimeSpan interval, DateTime lastTime, DateTime? now = null)
        {
            if (interval == Timeout.InfiniteTimeSpan)
            {
                return false;
            }

            now ??= _clock.UtcNow;
            return (now - lastTime) >= interval;
        }

        /// <summary>
        /// Run a given operation out of band but only if <paramref name="pendingTask"/> is not completed.
        /// </summary>
        public static Task<BoolResult> RunOutOfBandAsync(bool inline, ref Task<BoolResult> pendingTask, object locker, PerformAsyncOperationBuilder<BoolResult> operation, out bool factoryWasCalled, [CallerMemberName] string caller = null)
        {
            Contract.Requires(pendingTask != null);

            factoryWasCalled = false;
            if (inline)
            {
                var priorPendingTask = pendingTask;
                async Task<BoolResult> runInlineAsync()
                {
                    await priorPendingTask.IgnoreErrorsAndReturnCompletion();

                    operation.AppendStartMessage(extraStartMessage: "inlined=true");
                    return await operation.RunAsync(caller);
                }

                factoryWasCalled = true;
                pendingTask = runInlineAsync();
                return pendingTask;
            }

            // Using a separate method to avoid a race condition.
            if (pendingTask.IsCompleted)
            {
                lock (locker)
                {
                    if (pendingTask.IsCompleted)
                    {
                        factoryWasCalled = true;
                        pendingTask = Task.Run(() => operation.RunAsync(caller));
                    }
                }
            }

            return BoolResult.SuccessTask;
        }

        internal async Task<BoolResult> CreateCheckpointAsync(OperationContext context)
        {
            // Need to obtain the sequence point first to avoid race between the sequence point and the database's state.
            EventSequencePoint currentSequencePoint = EventStore.GetLastProcessedSequencePoint();
            TimeSpan? maxProcessingDelay = EventStore.GetMaxProcessingDelay();
            if (currentSequencePoint == null || currentSequencePoint.SequenceNumber == null)
            {
                Tracer.Debug(context.TracingContext, "Could not create a checkpoint because the sequence point is missing. Apparently, no events were processed at this time.");
                return BoolResult.Success;
            }

            UpdateSerializableClusterStateValues(context, maxProcessingDelay);

            var result = await CheckpointManager.CreateCheckpointAsync(context, currentSequencePoint, maxProcessingDelay);

            if (result.Succeeded)
            {
                // Updating last checkpoint creation time used for determining how stale LLS data is only when the checkpoint was successfully saved.
                _llsCheckpointCreationTime = _clock.UtcNow;
            }

            return result;
        }

        private void UpdateSerializableClusterStateValues(OperationContext context, TimeSpan? maxProcessingDelay)
        {
            var manager = ClusterState.BinManager;
            if (manager != null)
            {
                var serializeResult = manager.Serialize();
                if (serializeResult.Succeeded)
                {
                    var bytes = serializeResult.Value;
                    var serializedString = Convert.ToBase64String(bytes);
                    Database.SetGlobalEntry(BinManagerKey, serializedString);
                }
                else
                {
                    serializeResult.TraceIfFailure(context);
                }
            }

            Database.SetGlobalEntry(EventProcessingDelayKey, maxProcessingDelay.ToString());
        }

        private async Task<BoolResult> RestoreCheckpointStateAsync(OperationContext context, CheckpointState checkpointState)
        {
            // NOTE: latestCheckpoint's checkpointId is only the Guid part of the checkpoint id. Do NOT use it for
            // anything other than reporting.
            var latestCheckpoint = CheckpointManager.DatabaseGetLatestCheckpointInfo(context);
            var latestCheckpointAge = _clock.UtcNow - latestCheckpoint?.checkpointTime;

            if (latestCheckpoint?.checkpointTime != null)
            {
                // Updating the last checkpoint creation time based on the current state of the database.
                _llsCheckpointCreationTime = latestCheckpoint.Value.checkpointTime;
            }

            // Only skip if this is the first restore and it is sufficiently recent
            // NOTE: _lastRestoreTime will be set since skipping this operation will return successful result.
            var shouldSkipRestore = _lastRestoreTime == default
                // Master should always get the latest checkpoint
                && CurrentRole != Role.Master
                && latestCheckpoint != null
                && Configuration.Checkpoint.RestoreCheckpointAgeThreshold != default
                && latestCheckpoint.Value.checkpointTime.IsRecent(_clock.UtcNow, Configuration.Checkpoint.RestoreCheckpointAgeThreshold);

            if (latestCheckpointAge > Configuration.LocationEntryExpiry)
            {
                Tracer.Debug(context, $"Checkpoint {latestCheckpoint.Value.checkpointId} age is {latestCheckpointAge}, which is larger than location expiry {Configuration.LocationEntryExpiry}");
            }

            var latestCheckpointId = latestCheckpoint?.checkpointId ?? "null";
            if (shouldSkipRestore)
            {
                Tracer.Debug(context, $"First checkpoint {checkpointState} will be skipped. LatestCheckpointId={latestCheckpointId}, LatestCheckpointAge={latestCheckpointAge}, Threshold=[{Configuration.Checkpoint.RestoreCheckpointAgeThreshold}]");
                Counters[ContentLocationStoreCounters.RestoreCheckpointsSkipped].Increment();
                return BoolResult.Success;
            }
            else if (_lastRestoreTime == default)
            {
                Tracer.Debug(context, $"First checkpoint {checkpointState} will not be skipped. LatestCheckpointId={latestCheckpointId}, LatestCheckpointAge={latestCheckpointAge}, Threshold=[{Configuration.Checkpoint.RestoreCheckpointAgeThreshold}]");
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

                    TimeSpan? eventProcessingDelay = null;
                    if (Configuration.MaxProcessingDelayToReconcile.HasValue && Database.TryGetGlobalEntry(EventProcessingDelayKey, out var processingDelayString))
                    {
                        // ProcessingDelayString should only be empty if no events have been processed, so we want to reconcile.
                        // This case shouldn't happen, but just a check to prevent exception thrown from parsing empty string to timespan.
                        if (!string.IsNullOrEmpty(processingDelayString))
                        {
                            eventProcessingDelay = TimeSpan.Parse(processingDelayString);
                        }
                    }

                    Counters[ContentLocationStoreCounters.RestoreCheckpointsSucceeded].Increment();
                    _lastCheckpointId = checkpointState.CheckpointId;
                    _llsCheckpointCreationTime = checkpointState.CheckpointTime;
                    if (Configuration.ReconcileMode == ReconciliationMode.Checkpoint)
                    {
                        await CancelCurrentReconciliationAsync(context);

                        // If MaxProcessingDelayToReconcile is not set in the config we reconcile regardless
                        // If no events were processed, meaning no delay, we reconcile as well. Should never really hapen.
                        if (eventProcessingDelay >= Configuration.MaxProcessingDelayToReconcile)
                        {
                            Tracer.Debug(context, $"Processing delay={eventProcessingDelay} is greater than limit {Configuration.MaxProcessingDelayToReconcile}, skipping reconciliation");
                        }
                        else
                        {
                            if (Configuration.InlinePostInitialization)
                            {
                                await ReconcilePerCheckpointAsync(context, _reconciliationTokenSource.Token).ThrowIfFailure();
                            }
                            else
                            {
                                _pendingReconciliationTask = ReconcilePerCheckpointAsync(context, _reconciliationTokenSource.Token).FireAndForgetAndReturnTask(context);
                            }
                        }
                    }
                }
                else
                {
                    Tracer.Debug(context, $"Checkpoint '{checkpointState}' already restored.");
                }

                // Update bin manager in cluster state.
                if (Configuration.UseBinManager && Database.TryGetGlobalEntry(BinManagerKey, out var serializedString))
                {
                    var bytes = Convert.FromBase64String(serializedString);
                    var binManagerResult = BinManager.CreateFromSerialized(
                        bytes,
                        Configuration.ProactiveCopyLocationsThreshold,
                        _clock,
                        Configuration.PreferredLocationsExpiryTime);
                    binManagerResult.TraceIfFailure(context);

                    ClusterState.BinManager = binManagerResult.Succeeded ? binManagerResult.Value : null;
                }
            }

            return BoolResult.Success;
        }

        // We acknowledge this function is not thread safe, but do not anticipate this being a problem, since checkpoints should not be restored in multiple occurrences.
        private Task CancelCurrentReconciliationAsync(OperationContext context)
        {
            // Using PerformOperationAsync for tracing purposes.
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // Tracing the completeness of the task we're about to cancel.
                    var pendingProcessCheckpointTaskCompleted = _pendingReconciliationTask.IsCompleted;

                    _reconciliationTokenSource.Cancel();

                    // We are aware that if <see cref="_pendingReconciliationTask"/> fails this could throw an exception.
                    // The task shouldn't fail though, a failure would most likely indicate a bug.
                    _ = await _pendingReconciliationTask;
                    Volatile.Write(ref _reconciliationTokenSource, new CancellationTokenSource());

                    return Result.Success(pendingProcessCheckpointTaskCompleted);
                },
                extraEndMessage: r => $"_pendingReconciliationTask.IsCompleted={r.GetValueOrDefault()}").ThrowIfFailure();
        }

        /// <summary>
        /// Gets the list of <see cref="ContentLocationEntry"/> for every hash specified by <paramref name="contentHashes"/> from a given <paramref name="origin"/>.
        /// </summary>
        public async Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, MachineId requestingMachineId, IReadOnlyList<ContentHash> contentHashes, GetBulkOrigin origin)
        {
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
                extraEndMessage: r => getExtraEndMessage(r));

            return result;

            string getExtraEndMessage(GetBulkLocationsResult r)
            {
                // Print the resulting hashes with locations if succeeded, but still print the set of hashes to simplify diagnostics in case of a failure as well.
                // Printing filtered out locations if available for successful results.
                string? filteredOutLocations = r.Succeeded ? r.GetShortHashesTraceStringForInactiveMachines() : null;
                filteredOutLocations = filteredOutLocations != null ? $", Inactive: {filteredOutLocations}" : null;

                DateTime lastCheckpointCreationTime = _llsCheckpointCreationTime;
                if (lastCheckpointCreationTime == default)
                {
                    // If the checkpoint was not restored, consider the start time as the time of the last restore.
                    // This is not 100% correct, but good enough for diagnostics purposes.
                    // Need to get UTC time because 'StartTime' returns the local time.
                    lastCheckpointCreationTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
                }

                var timeSinceLastRestore = _clock.UtcNow - lastCheckpointCreationTime;
                // Checking if the checkpoint is stale (only valid for the local case).
                string isCheckpointStaleMessage =
                    r.Succeeded && origin == GetBulkOrigin.Local && timeSinceLastRestore > Configuration.LocationEntryExpiry
                    ? $", StaleCheckpointAge={timeSinceLastRestore}"
                    : string.Empty;

                return r.Succeeded
                    ? $"GetBulk({origin}) => [{r.GetShortHashesTraceString()}]{filteredOutLocations}{isCheckpointStaleMessage}"
                    : $"GetBulk({origin}) => [{contentHashes.GetShortHashesTraceString()}]";
            }
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
                    if (Configuration.Settings.GlobalGetBulkLocationDelay != null)
                    {
                        await _clock.Delay(Configuration.Settings.GlobalGetBulkLocationDelay.Value, context.Token);
                    }

                    var entries = await GlobalCacheStore.GetBulkAsync(context, contentHashes.SelectList(c => new ShortHash(c)));
                    if (!entries)
                    {
                        return new GetBulkLocationsResult(entries);
                    }

                    return await ResolveLocationsAsync(context, entries.Value, contentHashes, GetBulkOrigin.Global);
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
                            if ((entry.LastAccessTimeUtc.ToDateTime() + Configuration.TouchFrequency < now)
                                && _recentlyTouchedHashes.Add(hash, Configuration.TouchFrequency))
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

                    return ResolveLocationsAsync(context, entries, contentHashes, GetBulkOrigin.Local);
                },
                traceErrorsOnly: true, // Intentionally tracing errors only.
                counter: Counters[ContentLocationStoreCounters.GetBulkLocal]);
        }

        private async Task<GetBulkLocationsResult> ResolveLocationsAsync(OperationContext context, IReadOnlyList<ContentLocationEntry> entries, IReadOnlyList<ContentHash> contentHashes, GetBulkOrigin origin)
        {
            Contract.Requires(entries.Count == contentHashes.Count);

            var results = new List<ContentHashWithSizeAndLocations>(entries.Count);
            bool hasUnknownLocations = false;
            // TODO (WI - 2003955): we should have only one place where the filtering is happening - here, and not on the database level.

            // Inactive machines can be filtered on this level (and not on the database level) for tracing purposes.
            // But the filtering only applies to local locations, because we explicitly want to try out
            // the global locations (unless FilterInactiveMachinesForGlobalLocations flag is set).
            // When the machines switches from inactive state we forcing the locations to be registered in the global store.
            // It means that filtering out the global store will not allow us to see those locations until the
            // machine state will be propagated to all the clients.
            bool shouldFilter = Configuration.ShouldFilterInactiveMachinesInLocalLocationStore && (origin == GetBulkOrigin.Local ||
                                                                                                   Configuration.FilterInactiveMachinesForGlobalLocations ||
                                                                                                   Configuration.TraceInactiveMachinesForGlobalLocations);
            var inactiveMachineSet = shouldFilter ? ClusterState.InactiveMachines : null;
            var inactiveMachineList = shouldFilter ? ClusterState.InactiveMachineList : null;

            for (int i = 0; i < entries.Count; i++)
            {
                var filteredOutMachines = Configuration.ShouldFilterInactiveMachinesInLocalLocationStore ? new List<MachineId>() : null;

                // TODO: Its probably possible to do this by getting the max machine id in the locations set rather than enumerating all of them (bug 1365340)
                var entry = entries[i];
                Contract.AssertNotNull(entry);

                foreach (var machineId in entry.Locations.EnumerateMachineIds())
                {
                    // Filtering out inactive machines if configured.
                    if (inactiveMachineSet?.Contains(machineId) == true)
                    {
                        filteredOutMachines!.Add(machineId);
                        continue;
                    }

                    if (!ClusterState.TryResolve(machineId, out _))
                    {
                        hasUnknownLocations = true;
                    }
                }

                if (Configuration.ShouldFilterInactiveMachinesInLocalLocationStore && (origin == GetBulkOrigin.Local || Configuration.FilterInactiveMachinesForGlobalLocations))
                {
                    entry = entry.SetMachineExistence(MachineIdCollection.Create(inactiveMachineList!), exists: false);
                }

                var contentHash = contentHashes[i];
                results.Add(
                    new ContentHashWithSizeAndLocations(
                        contentHash,
                        entry.ContentSize,
                        GetMachineList(context.TracingContext, contentHash, entry),
                        entry,
                        GetFilteredOutMachineList(context.TracingContext, contentHash, filteredOutMachines)));
            }

            // If we faced at least one unknown machine location we're forcing an update of a cluster state to make the resolution successful.
            if (hasUnknownLocations)
            {
                // Update cluster. Query global to ensure that we have all machines ids (even those which may not be added
                // to local db yet.)
                var result = await SetOrGetMachineStateAsync(context, proposedState: MachineState.Unknown);
                if (!result)
                {
                    return new GetBulkLocationsResult(result);
                }
            }

            return new GetBulkLocationsResult(results, origin);
        }

        private IReadOnlyList<MachineLocation> GetMachineList(Context context, ContentHash hash, ContentLocationEntry entry)
        {
            if (entry.IsMissing)
            {
                return CollectionUtilities.EmptyArray<MachineLocation>();
            }

            return MachineLocationResolver.Resolve(
                context,
                entry.Locations,
                MachineReputationTracker,
                ClusterState,
                hash,
                _machineListSettings,
                MasterElectionMechanism);
        }

        private IReadOnlyList<MachineLocation>? GetFilteredOutMachineList(Context context, ContentHash hash, List<MachineId>? machineIds)
        {
            if (machineIds == null)
            {
                return null;
            }

            return MachineLocationResolver.Resolve(
                context,
                MachineIdSet.Empty.SetExistence(MachineIdCollection.Create(machineIds), exists: true),
                MachineReputationTracker,
                ClusterState,
                hash,
                _machineListSettings,
                MasterElectionMechanism);
        }

        /// <summary>
        /// Gets content locations for a given <paramref name="hash"/> from a local database.
        /// </summary>
        private bool TryGetContentLocations(OperationContext context, ShortHash hash, [NotNullWhen(true)] out ContentLocationEntry entry)
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
            EagerGlobalOnPut,
            RecentInactiveEagerGlobal,
            RecentRemoveEagerGlobal,
            LazyEventOnly,
            LazyTouchEventOnly,
            SkippedDueToRecentAdd,
            SkippedDueToRedundantAdd,
            SkippedDueToMissingLocalContent,
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
                case RegisterAction.EagerGlobalOnPut:
                case RegisterAction.RecentInactiveEagerGlobal:
                case RegisterAction.RecentRemoveEagerGlobal:
                    return RegisterCoreAction.Global;
                case RegisterAction.LazyEventOnly:
                case RegisterAction.LazyTouchEventOnly:
                    return RegisterCoreAction.Events;
                case RegisterAction.SkippedDueToRecentAdd:
                case RegisterAction.SkippedDueToRedundantAdd:
                case RegisterAction.SkippedDueToMissingLocalContent:
                    return RegisterCoreAction.Skip;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, $"Unexpected action '{action}'.");
            }
        }

        private RegisterAction GetRegisterAction(OperationContext context, MachineId machineId, ShortHash hash, DateTime now)
        {
            if (_recentlyRemovedHashes.Contains(hash))
            {
                // Content was recently removed. Eagerly register with global store.
                Counters[ContentLocationStoreCounters.LocationAddRecentRemoveEager].Increment();
                return RegisterAction.RecentRemoveEagerGlobal;
            }

            if (ClusterState.LastInactiveTime.IsRecent(now, Configuration.MachineStateRecomputeInterval.Multiply(5)))
            {
                // The machine was recently inactive. We should eagerly register content for some amount of time (a few heartbeats) because content may be currently filtered from other machines
                // local db results due to inactive machines filter.
                Counters[ContentLocationStoreCounters.LocationAddRecentInactiveEager].Increment();
                return RegisterAction.RecentInactiveEagerGlobal;
            }

            if (_recentlyAddedHashes.Contains(hash))
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
                if (entry.Locations[machineId.Index]) // content is registered for this machine
                {
                    // If content was touched recently, we can skip. Otherwise, we touch via event
                    if (entry.LastAccessTimeUtc.ToDateTime().IsRecent(now, Configuration.TouchFrequency))
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
                if (entry.Locations.Count >= Configuration.SafeToLazilyUpdateMachineCountThreshold)
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
            return _postInitialization.tcs.Task;
        }

        private async Task<BoolResult?> PrepareForWriteAsync<T>(IReadOnlyCollection<T> hashes)
        {
            var postInitializationResult = await EnsureInitializedAsync();
            if (!postInitializationResult)
            {
                return postInitializationResult;
            }

            if (Configuration.DistributedContentConsumerOnly)
            {
                return BoolResult.Success;
            }

            if (hashes.Count == 0)
            {
                return BoolResult.Success;
            }

            return null;
        }

        private bool HasResult(BoolResult? possibleResult, [NotNullWhen(true)] out BoolResult? result)
        {
            result = possibleResult;
            return result != null;
        }

        /// <summary>
        /// Notifies a central store that content represented by <paramref name="contentHashes"/> is available on a current machine.
        /// </summary>
        public async Task<BoolResult> RegisterLocalContentAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, bool touch)
        {
            if (HasResult(await PrepareForWriteAsync(contentHashes), out var result))
            {
                return result;
            }

            Contract.AssertNotNull(_localMachineId);

            var hashes = contentHashes.SelectArray(hash => GetContentHashAndSize(context, hash));
            return await RegisterLocalLocationAsync(context, _localMachineId.Value, hashes, touch, isRegisterLocalContent: true);
        }

        private ContentHashWithSize GetContentHashAndSize(OperationContext context, ContentHash hash)
        {
            long size = -1;
            if (_localContentStore?.TryGetContentInfo(hash, out var info) == true)
            {
                size = info.Size;
            }

            return new ContentHashWithSize(hash, size);
        }

        /// <summary>
        /// Notifies a central store that content represented by <paramref name="contentHashes"/> is available on a current machine.
        /// </summary>
        public async Task<BoolResult> RegisterLocalLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ContentHashWithSize> contentHashes, bool touch, bool isRegisterLocalContent = false, UrgencyHint urgencyHint = UrgencyHint.Minimum)
        {
            Contract.Requires(contentHashes != null);

            if (HasResult(await PrepareForWriteAsync(contentHashes), out var result))
            {
                return result;
            }

            string extraMessage = string.Empty;
            return await context.PerformOperationAsync<BoolResult>(
                Tracer,
                async () =>
                {
                    if (Configuration.Settings.RegisterLocationDelay != null)
                    {
                        await _clock.Delay(Configuration.Settings.RegisterLocationDelay.Value, context.Token);
                    }

                    var eventContentHashes = new List<ShortHashWithSize>(contentHashes.Count);
                    var eagerContentHashes = new List<ShortHashWithSize>(contentHashes.Count);
                    var actions = new List<RegisterAction>(contentHashes.Count);
                    var now = _clock.UtcNow;

                    // Select which hashes are not already registered for the local machine and those which must eagerly go to the global store
                    foreach (var contentHash in contentHashes)
                    {
                        RegisterAction registerAction;
                        if (urgencyHint == UrgencyHint.RegisterEagerly)
                        {
                            registerAction = RegisterAction.EagerGlobalOnPut;
                        }
                        else
                        {
                            registerAction = (isRegisterLocalContent && contentHash.Size < 0)
                                ? RegisterAction.SkippedDueToMissingLocalContent // this is a local content register where content was not found. Skip.
                                : GetRegisterAction(context, machineId, contentHash.Hash, now);
                        }

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
                    extraMessage = $"Register actions(Eager={eagerContentHashes.Count}, Event={eventContentHashes.Count}, Total={contentHashes.Count}): [{registerActionsMessage}]";

                    if (eventContentHashes.Count != 0)
                    {
                        // Send add events
                        Tracer.TrackMetric(context, "RegisterEventCalls", 1);
                        Tracer.TrackMetric(context, "RegisterEventHashes", eventContentHashes.Count);
                        EventStore.AddLocations(context, machineId, eventContentHashes, touch).ThrowIfFailure();
                    }

                    if (eagerContentHashes.Count != 0)
                    {
                        // Update global store
                        Tracer.TrackMetric(context, "RegisterEagerCalls", 1);
                        Tracer.TrackMetric(context, "RegisterEagerHashes", eagerContentHashes.Count);
                        if (_registerNagleQueue != null && _localMachineId.Value == machineId)
                        {
                            var (result, traceId) = await _registerNagleQueue.EnqueueAsync(eagerContentHashes);
                            extraMessage = $"TraceId=[{traceId}] {extraMessage}";
                            result.ThrowIfFailure();

                        }
                        else
                        {
                            await GlobalRegisterAsync(context, machineId, eagerContentHashes, touch).ThrowIfFailure();
                        }
                    }

                    // Register all recently added hashes so subsequent operations do not attempt to re-add
                    foreach (var hash in eventContentHashes)
                    {
                        _recentlyAddedHashes.Add(hash.Hash, Configuration.TouchFrequency);
                        AddToRecentReconcileAdds(hash.Hash, Configuration.ReconcileCacheLifetime);
                    }

                    // Only eagerly added hashes should invalidate recently removed hashes.
                    foreach (var hash in eagerContentHashes)
                    {
                        _recentlyRemovedHashes.Invalidate(hash.Hash);
                    }

                    return BoolResult.Success;
                },
                Counters[ContentLocationStoreCounters.RegisterLocalLocation],
                traceOperationStarted: false,
                extraEndMessage: _ => extraMessage,
                caller: isRegisterLocalContent ? nameof(RegisterLocalContentAsync) : nameof(RegisterLocalLocationAsync));
        }

        private ValueTask<BoolResult> GlobalRegisterAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> eagerContentHashes, bool touch)
        {
            Tracer.TrackMetric(context, "RegisterEagerGlobalCalls", 1);
            Tracer.TrackMetric(context, "RegisterEagerGlobalHashes", eagerContentHashes.Count);
            return GlobalCacheStore.RegisterLocationAsync(context, machineId, eagerContentHashes, touch);
        }

        /// <nodoc />
        public async Task<BoolResult> TouchBulkAsync(OperationContext context, MachineId machineId, IReadOnlyList<ContentHash> contentHashes)
        {
            Contract.Requires(contentHashes != null);

            if (HasResult(await PrepareForWriteAsync(contentHashes), out var result))
            {
                return result;
            }

            return context.PerformOperation(
                Tracer,
                () =>
                {
                    var touchEventHashes = new List<ContentHash>();
                    var now = _clock.UtcNow;

                    foreach (var hash in contentHashes)
                    {
                        if (_recentlyAddedHashes.Contains(hash) || !_recentlyTouchedHashes.Add(hash, Configuration.TouchFrequency))
                        {
                            continue;
                        }

                        if (TryGetContentLocations(context, hash, out var entry) && (entry.LastAccessTimeUtc.ToDateTime() + Configuration.TouchFrequency) > now)
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
            if (HasResult(await PrepareForWriteAsync(contentHashes), out var result))
            {
                return result;
            }

            var shortHashes = contentHashes.SelectList(c => c.AsShortHash());
            foreach (var shortHashesPage in shortHashes.GetPages(100))
            {
                context.TracingContext.Debug($"LocalLocationStore.TrimBulk({shortHashesPage.GetShortHashesTraceString()})", component: nameof(LocalLocationStore));
            }

            return await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    foreach (var hash in contentHashes)
                    {
                        // Content has been removed. Ensure that subsequent additions will not be skipped
                        _recentlyAddedHashes.Invalidate(hash);
                        _recentlyRemovedHashes.Add(hash, Configuration.TouchFrequency);
                        AddToRecentReconcileRemoves(hash, Configuration.ReconcileCacheLifetime);
                    }

                    // Send remove event for hashes
                    var result = EventStore.RemoveLocations(context, machineId, shortHashes);

                    if (Configuration.OnEvictionDeleteLocationFromGCS)
                    {
                        result &= await GlobalCacheStore.DeleteLocationAsync(context, machineId, shortHashes);
                    }
                    
                    return result;
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
            PartitionId? evictionPartition = null;

            if (contentHashesWithInfo.Count != 0)
            {
                var first = contentHashesWithInfo[0];
                var last = contentHashesWithInfo[contentHashesWithInfo.Count - 1];

                if (!reverse && Configuration.Settings.EvictionPartitionInterval > TimeSpan.Zero && _evictionPartitions.Length != 0
                    && Configuration.Settings.EvictionPartitionFraction > 0)
                {
                    var partitionIndex = ((_clock.UtcNow.Ticks / Configuration.Settings.EvictionPartitionInterval.Value.Ticks) + _evictionPartitionOffset) % _evictionPartitions.Length;
                    evictionPartition = _evictionPartitions[(int)partitionIndex];
                }

                Tracer.Debug(context, $"{nameof(GetHashesInEvictionOrder)} start with contentHashesWithInfo.Count={contentHashesWithInfo.Count}, firstAge={first.Age(_clock)}, lastAge={last.Age(_clock)}, evictionPartition={evictionPartition}");
            }

            var operationContext = new OperationContext(context);
            var effectiveLastAccessTimeProvider = new EffectiveLastAccessTimeProvider(Configuration, _clock, new ContentResolver(this, machineInfo));

            // Ideally, we want to remove content we know won't be used again for quite a while. We don't have that
            // information, so we use an evictability metric. Here we obtain and sort by that evictability metric.

            var comparer = reverse
                ? ContentEvictionInfo.AgeBucketingPrecedenceComparer.ReverseInstance
                : ContentEvictionInfo.AgeBucketingPrecedenceComparer.Instance;
            var contentHashesWithLastAccessTimes = contentHashesWithInfo.SelectList(v => new ContentHashWithLastAccessTime(v.ContentHash, v.LastAccessTime));


            if (Configuration.UseFullEvictionSort)
            {
                return GetHashesInEvictionOrderUsingFullSort(
                    operationContext,
                    effectiveLastAccessTimeProvider,
                    comparer,
                    contentHashesWithLastAccessTimes,
                    reverse,
                    evictionPartition);
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
                .ApproximateSort(comparer, getContentEvictionInfos, Configuration.EvictionPoolSize, Configuration.EvictionWindowSize, Configuration.EvictionRemovalFraction, Configuration.EvictionDiscardFraction);

            var newestContentSortedByEvictability = contentHashesWithInfo
                .SkipOptimized(contentHashesWithInfo.Count / 2)
                .ApproximateSort(comparer, getContentEvictionInfos, Configuration.EvictionPoolSize, Configuration.EvictionWindowSize, Configuration.EvictionRemovalFraction, Configuration.EvictionDiscardFraction);

            return NuCacheCollectionUtilities.MergeOrdered(oldestContentSortedByEvictability, newestContentSortedByEvictability, comparer)
                .Where((candidate, index) => IsPassEvictionAge(operationContext, candidate, Configuration.EvictionMinAge, index, ref evictionCount));
        }

        private IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrderUsingFullSort(
            OperationContext operationContext,
            EffectiveLastAccessTimeProvider effectiveLastAccessTimeProvider,
            IComparer<ContentEvictionInfo> comparer,
            IReadOnlyList<ContentHashWithLastAccessTime> contentHashesWithInfo,
            bool reverse,
            PartitionId? evictionPartition)
        {
            var candidateQueue = new PriorityQueue<ContentEvictionInfo>(Configuration.EvictionPoolSize, comparer);
            int preferredEvictionCount = (int)Math.Max(0, Math.Min(contentHashesWithInfo.Count, contentHashesWithInfo.Count * Configuration.Settings.EvictionPartitionFraction));

            IEnumerable<ContentEvictionInfo> enumerate(Func<ContentHashWithLastAccessTime, int, bool> include, bool preferred)
            {
                foreach (var candidate in GetFullSortedContentWithEffectiveLastAccessTimes(
                    operationContext,
                    effectiveLastAccessTimeProvider,
                    contentHashesWithInfo.Take(preferred ? preferredEvictionCount : contentHashesWithInfo.Count).Where(include),
                    reverse))
                {
                    candidateQueue.Push(candidate with { EvictionPreferred = preferred });

                    // Only consider content when the eviction pool size is reached.
                    if (candidateQueue.Count > Configuration.EvictionPoolSize)
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

            if (evictionPartition != null)
            {
                foreach (var item in enumerate(include: (e, _) => evictionPartition.Value.Contains(e.Hash), preferred: true))
                {
                    yield return item;
                }

                foreach (var item in enumerate(include: (e, index) => index >= preferredEvictionCount || !evictionPartition.Value.Contains(e.Hash), preferred: false))
                {
                    yield return item;
                }
            }
            else
            {
                foreach (var item in enumerate(include: (_, _) => true, preferred: false))
                {
                    yield return item;
                }
            }
        }

        private IEnumerable<ContentEvictionInfo> GetFullSortedContentWithEffectiveLastAccessTimes(
            OperationContext operationContext,
            EffectiveLastAccessTimeProvider effectiveLastAccessTimeProvider,
            IEnumerable<ContentHashWithLastAccessTime> contentHashesWithInfo,
            bool reverse)
        {
            var ageOnlyComparer = reverse
                ? ContentEvictionInfo.ReverseFullSortAgeOnlyComparer
                : ContentEvictionInfo.FullSortAgeOnlyComparer;

            var ageSortingQueue = new PriorityQueue<ContentEvictionInfo>(Configuration.EvictionPoolSize, ageOnlyComparer);

            foreach (var page in contentHashesWithInfo.GetPages(Configuration.EvictionWindowSize))
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
            Tracer.Debug(context, $"Previous successful eviction attempts = {evictionCount}, Total eviction attempts previously = {index}, minimum eviction age = {evictionMinAge.ToString()}, pool size = {Configuration.EvictionPoolSize}." +
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

            var effectiveLastAccessTimeProvider = new EffectiveLastAccessTimeProvider(Configuration, _clock, new ContentResolver(this, machineInfo));

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

        #region Reconciliation

        /// <summary>
        /// Forces reconciliation process between local content store and LLS.
        /// </summary>
        public async Task<ReconciliationPerCheckpointResult> ReconcilePerCheckpointAsync(OperationContext context, CancellationToken reconcileToken)
        {
            Contract.RequiresNotNull(_localMachineId);
            Contract.RequiresNotNull(_localContentStore);

            var result = await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // Making this operation fully asynchronous because the reconciliation should be performed in a non-blocking manner.
                    // I.e. the method should return the resulting task without doing any work that potentially may block the caller (getting the data from the content directory during reconciliation is actually synchronous).
                    // Without Yield() here this method is effectively inlined as part as ProcessStateAsync call.
                    await Task.Yield();

                    // Only one iteration of reconciliation should run at a time. Config should be set where two iterations colliding is a rare case
                    // In the rare case we do collide because the old iteration takes longer to run than our restore checkpoint interval, we continue with the previous iteration, and do not call a new one.
                    return await ConcurrencyHelper.RunOnceIfNeeded(ref _isReconcileCheckpointRunning,
                        func: () => ReconcilePerCheckpointCoreAsync(context, reconcileToken, _localMachineId.Value, _localContentStore, _lastProcessedAddHash, _lastProcessedRemoveHash),
                        funcIsRunningResultProvider: () => Task.FromResult(ReconciliationPerCheckpointResult.Error(new BoolResult("Previous reconciliation cycle is still running, new cycle not queued"))));
                },
                Counters[ContentLocationStoreCounters.Reconcile],
                // We know that this is a long running operation. Stop tracing when its running.
                pendingOperationTracingInterval: TimeSpan.MaxValue);

            // We only change state for lastProcessedAddHash or remove if the result succeeded, in case of cancellation, skips, and errors
            if (result.IsSuccessCode)
            {
                var data = result.SuccessData;

                // After reconciliation ends, or if the machine is clearly undergoing an epoch change, we set the
                // machine state to open. Since we don't explicitly track the machine state, it is impossible to know
                // when we're undergoing an epoch change. This is a heuristic to guess if that's happening.
                var epochChange = _lastProcessedAddHash == null // It's the first run of reconciliation
                    && data.AddsSent == Configuration.ReconciliationAddLimit; // All sent events were adds
                if (data.ReachedEnd || epochChange)
                {
                    Tracer.Info(context, $"Setting machine state to Open. ReachedEnd=[{data.ReachedEnd}] EpochChange=[{epochChange}]");
                    await SetOrGetMachineStateAsync(context, MachineState.Open).IgnoreFailure();
                }

                // We do not call MarkReconciled, because we do not check whether reconciliation is up to date to skip reconciliation
                if (data.ReachedEnd)
                {
                    _lastProcessedAddHash = null;
                    _lastProcessedRemoveHash = null;
                }
                else
                {
                    _lastProcessedAddHash = data.LastProcessedAddHash;
                    _lastProcessedRemoveHash = data.LastProcessedRemoveHash;
                }
            }

            return result;
        }

        private async Task<ReconciliationPerCheckpointResult> ReconcilePerCheckpointCoreAsync(OperationContext context, CancellationToken reconcileToken, MachineId machineId, ILocalContentStore localContentStore, ShortHash? lastProcessedAddHash, ShortHash? lastProcessedRemoveHash)
        {
            Contract.RequiresNotNull(_localContentStore);

            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.Token, reconcileToken);
            var token = tokenSource.Token;

            using var cancellationRegistration = token.Register(() => { Tracer.Debug(context, $"Received cancellation request for {nameof(ReconcilePerCheckpointAsync)}."); });

            if (Configuration.DistributedContentConsumerOnly)
            {
                return ReconciliationPerCheckpointResult.Skipped();
            }

            int totalAddedContent = 0, totalRemovedContent = 0, skippedRecentAdds = 0, skippedRecentRemoves = 0;
            var addedContent = new List<ShortHashWithSize>();
            var removedContent = new List<ShortHash>();
            var reachedEnd = false;

            ShortHash? startingPoint = null;
            if (lastProcessedAddHash.HasValue && lastProcessedRemoveHash.HasValue)
            {
                startingPoint = lastProcessedAddHash < lastProcessedRemoveHash ? lastProcessedAddHash : lastProcessedRemoveHash;
            }
            else if (lastProcessedAddHash.HasValue)
            {
                startingPoint = lastProcessedAddHash;
            }
            else if (lastProcessedRemoveHash.HasValue)
            {
                startingPoint = lastProcessedRemoveHash;
            }

            try
            {
                token.ThrowIfCancellationRequested();

                // Pause events in main event store while sending reconciliation events via temporary event store
                // to ensure reconciliation does cause some content to be lost due to apply reconciliation changes
                // in the wrong order. For instance, if a machine has content [A] and [A] is removed during reconciliation.
                // It is possible that remove event could be sent before reconciliation event and the final state
                // in the database would still have missing content [A].
                using (EventStore.PauseSendingEvents(context))
                {
                    var originalStartingPoint = startingPoint;
                    var allLocalStoreContentInfos = await localContentStore.GetContentInfoAsync(token);
                    token.ThrowIfCancellationRequested();

                    var allLocalStoreContent = allLocalStoreContentInfos
                        .Select(c => (hash: new ShortHash(c.ContentHash), size: c.Size))
                        .OrderBy(c => c.hash)
                        // Linq expressions are lazy, need to capture the original value instead of referencing 'startingPoint' here
                        // that can be changed during enumeration.
                        .SkipWhile(hashWithSize => originalStartingPoint.HasValue && hashWithSize.hash < originalStartingPoint.Value);

                    // Creating a new OperationContext with the combined token to cancel the database operation if needed.
                    var allDbContent = Database.EnumerateSortedHashesWithContentSizeForMachineId(new OperationContext(context.TracingContext, token), machineId, startingPoint);
                    token.ThrowIfCancellationRequested();

                    // Diff the two views of the local machines content (left = local store, right = content location db)
                    // Then send changes as events
                    var allDiffContent = NuCacheCollectionUtilities.DistinctDiffSorted(leftItems: allLocalStoreContent, rightItems: allDbContent, t => t.hash);

                    foreach (var diffItem in allDiffContent)
                    {
                        token.ThrowIfCancellationRequested();

                        var inAddRange = false;
                        var inRemoveRange = false;
                        var curHash = diffItem.item.hash;

                        // If we have no previous last reconciled hashes, set it as the first hash
                        startingPoint ??= curHash;

                        if (addedContent.Count >= Configuration.ReconciliationAddLimit && removedContent.Count >= Configuration.ReconciliationRemoveLimit)
                        {
                            break;
                        }

                        if (addedContent.Count < Configuration.ReconciliationAddLimit)
                        {
                            lastProcessedAddHash = curHash;
                            inAddRange = true;
                        }

                        if (removedContent.Count < Configuration.ReconciliationRemoveLimit)
                        {
                            lastProcessedRemoveHash = curHash;
                            inRemoveRange = true;
                        }

                        if (diffItem.mode == MergeMode.LeftOnly)
                        {
                            totalAddedContent += 1;
                            if (inAddRange)
                            {
                                if (!_reconcileAddRecents.Contains(curHash))
                                {
                                    addedContent.Add(new ShortHashWithSize(curHash, diffItem.item.size));
                                }
                                else
                                {
                                    skippedRecentAdds++;
                                }
                            }
                        }
                        else if (diffItem.mode == MergeMode.RightOnly)
                        {
                            totalRemovedContent += 1;
                            if (inRemoveRange)
                            {
                                if (!_reconcileRemoveRecents.Contains(curHash))
                                {
                                    removedContent.Add(curHash);
                                }
                                else
                                {
                                    skippedRecentRemoves++;
                                }
                            }
                        }
                    }

                    var (addHashRange, removeHashRange) = computeHashRanges(startingPoint, lastProcessedAddHash, lastProcessedRemoveHash);

                    // If the add/remove count is below the limits, that means we went through the list looking for add/remove events
                    // Restart from the start of the list for next iteration of reconciliation
                    if (addedContent.Count < Configuration.ReconciliationAddLimit)
                    {
                        lastProcessedAddHash = null;
                    }

                    if (removedContent.Count < Configuration.ReconciliationRemoveLimit)
                    {
                        lastProcessedRemoveHash = null;
                    }

                    // If we reached the end we will set machine state as available, because we completed add and remove events
                    if (lastProcessedAddHash == null && lastProcessedRemoveHash == null)
                    {
                        reachedEnd = true;
                    }

                    return await SendReconcileEventsAfterEnumerationAsync(context, machineId, addHashRange, removeHashRange, lastProcessedAddHash, lastProcessedRemoveHash,
                        totalAddedContent, totalRemovedContent, skippedRecentAdds, skippedRecentRemoves, addedContent, removedContent, reachedEnd);
                }
            }
            catch (OperationCanceledException) when (reconcileToken.IsCancellationRequested)
            {
                var (addHashRange, removeHashRange) = computeHashRanges(startingPoint, lastProcessedAddHash, lastProcessedRemoveHash);

                return await SendReconcileEventsAfterEnumerationAsync(context, machineId, addHashRange, removeHashRange, lastProcessedAddHash, lastProcessedRemoveHash,
                    totalAddedContent, totalRemovedContent, skippedRecentAdds, skippedRecentRemoves, addedContent, removedContent, reachedEnd, reconcileCancelled: true);
            }

            static (string addHashRange, string removeHashRange) computeHashRanges(ShortHash? startingPoint, ShortHash? lastProcessedAddHash, ShortHash? lastProcessedRemoveHash)
            {
                var addHashRange = $"{startingPoint} - {lastProcessedAddHash}";
                var removeHashRange = $"{startingPoint} - {lastProcessedRemoveHash}";
                return (addHashRange, removeHashRange);
            }
        }

        private async Task<ReconciliationPerCheckpointResult> SendReconcileEventsAfterEnumerationAsync(OperationContext context, MachineId machineId, string addHashRange, string removeHashRange, ShortHash? lastProcessedAddHash, ShortHash? lastProcessedRemoveHash,
            int totalAddedContent, int totalRemovedContent, int skippedRecentAdds, int skippedRecentRemoves, List<ShortHashWithSize> addedContent, List<ShortHash> removedContent, bool reachedEnd, bool reconcileCancelled = false)
        {
            Counters[ContentLocationStoreCounters.Reconcile_AddedContent].Add(addedContent.Count);
            Counters[ContentLocationStoreCounters.Reconcile_RemovedContent].Add(removedContent.Count);

            // Only call reconcile if content needs to be updated for machine
            if (addedContent.Count != 0 || removedContent.Count != 0)
            {
                await SendReconciliationEventsAsync(
                    context,
                    suffix: $".{_clock.UtcNow:yyyyMMdd.HHmm}.{Guid.NewGuid()}",
                    machineId: machineId,
                    addedContent: addedContent,
                    removedContent: removedContent);
            }
            else
            {
                Tracer.Debug(context, "Skipping SendReconciliationEventsAsync because there is no added or removed content.");
            }

            var sampleHashes = new List<(ShortHash hash, bool add)>(Configuration.ReconcileHashesLogLimit * 2);

            // After sending reconcile events, we do not want to repeat add/remove events for hashes
            // We need to invalidate after adding to the opposing volatile set, so we don't prevent the hash from being added/removed.
            foreach (var addItem in addedContent)
            {
                AddToRecentReconcileAdds(addItem.Hash, Configuration.ReconcileCacheLifetime);
                if (sampleHashes.Count < Configuration.ReconcileHashesLogLimit)
                {
                    sampleHashes.Add((addItem.Hash, add: true));
                }
            }

            var removeSamples = 0;
            foreach (var removeItem in removedContent)
            {
                AddToRecentReconcileRemoves(removeItem, Configuration.ReconcileCacheLifetime);
                if (removeSamples < Configuration.ReconcileHashesLogLimit)
                {
                    sampleHashes.Add((removeItem, add: false));
                    removeSamples++;
                }
            }

            var successData = new ReconciliationPerCheckpointData()
            {
                AddsSent = addedContent.Count,
                RemovesSent = removedContent.Count,
                TotalMissingRecord = totalAddedContent,
                TotalMissingContent = totalRemovedContent,
                AddHashRange = addHashRange,
                RemoveHashRange = removeHashRange,
                LastProcessedAddHash = lastProcessedAddHash,
                LastProcessedRemoveHash = lastProcessedRemoveHash,
                ReachedEnd = reachedEnd,
                SkippedRecentAdds = skippedRecentAdds,
                SkippedRecentRemoves = skippedRecentRemoves,
                RecentAddCount = _reconcileAddRecents.Count,
                RecentRemoveCount = _reconcileRemoveRecents.Count,
                Cancelled = reconcileCancelled,
            };

            if (sampleHashes.Count != 0)
            {
                Tracer.Debug(context, "SampleHashes: " + sampleHashesToString());
            }

            return ReconciliationPerCheckpointResult.Success(successData);

            string sampleHashesToString()
            {
                // This method prints sample hashes in the following way:
                // SampleHashes: Adds = [hash1, hash2], Removes = [hash3, hash4]
                var sb = new StringBuilder();
                sb.Append($"Adds = [{string.Join(", ", sampleHashes.Where(tpl => tpl.add).Select(tpl => tpl.hash.ToString()))}], ");
                sb.Append($"Removes = [{string.Join(", ", sampleHashes.Where(tpl => !tpl.add).Select(tpl => tpl.hash.ToString()))}], ");

                return sb.ToString();
            }
        }

        private async Task SendReconciliationEventsAsync(
            OperationContext context,
            string suffix,
            MachineId machineId,
            List<ShortHashWithSize> addedContent,
            List<ShortHash> removedContent)
        {
            // Create separate event store for reconciliation events so they are dispatched first before
            // events in normal event store which may be queued during reconciliation operation.

            // Setting 'FailWhenSendEventsFails' to true, to fail reconciliation if we fail to send the events to event hub.
            // In this case 'ShutdownEventQueueAndWaitForCompletionAsync' will propagate an exception from a failed SendEventsAsync method.
            var reconciliationStoreConfiguration = Configuration with { EventStore = Configuration.EventStore with { FailWhenSendEventsFails = true } };
            var reconciliationEventStore = CreateEventStore(reconciliationStoreConfiguration, subfolder: "reconcile");

            try
            {
                await reconciliationEventStore.StartupAsync(context).ThrowIfFailure();

                await reconciliationEventStore.ReconcileAsync(
                    context,
                    machineId,
                    addedContent,
                    removedContent,
                    suffix).ThrowIfFailure();

                if (Configuration.LogReconciliationHashes)
                {
                    LogContentLocationOperations(
                        context,
                        Tracer.Name,
                        addedContent.Select(s => (s.Hash, EntryOperation.AddMachine, OperationReason.Reconcile))
                            .Concat(removedContent.Select(s => (s, EntryOperation.RemoveMachine, OperationReason.Reconcile))));
                }

                // It is very important to wait till all the messages are sent before shutting down the store!
                await reconciliationEventStore.ShutdownEventQueueAndWaitForCompletionAsync();
            }
            finally
            {
                await reconciliationEventStore.ShutdownAsync(context).ThrowIfFailure();
            }
        }

        private bool AddToRecentReconcileAdds(ShortHash hash, TimeSpan timeToLive)
        {
            _reconcileRemoveRecents.Invalidate(hash);
            return _reconcileAddRecents.Add(hash, timeToLive);
        }

        private bool AddToRecentReconcileRemoves(ShortHash hash, TimeSpan timeToLive)
        {
            _reconcileAddRecents.Invalidate(hash);
            return _reconcileRemoveRecents.Add(hash, timeToLive);
        }

        #endregion Reconciliation

        /// <nodoc />
        public Task<BoolResult> InvalidateLocalMachineAsync(OperationContext context, MachineId machineId)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // TODO: Setting machine state to DeadUnavailable is too aggressive as the machine may not shutdown immediately.
                    // Instead we need to mark the machine as untrusted.
                    await SetOrGetMachineStateAsync(context, MachineState.DeadUnavailable).IgnoreFailure();

                    return BoolResult.Success;
                });
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

                if (CurrentRole == Role.Master)
                {
                    // All of these log, and we really want to make sure we don't fail
                    await SetOrGetMachineStateAsync(context, MachineState.DeadUnavailable).IgnoreFailure();
                    await MasterElectionMechanism.ReleaseRoleIfNecessaryAsync(context).IgnoreFailure();
                    LifetimeManager.RequestTeardown(context, "Content location database has been invalidated");
                }
                else
                {
                    Tracer.Error(context, $"Content location database has been invalidated. Forcing a restore from the last checkpoint. Error: {failure.DescribeIncludingInnerFailures()}");

                    // Ensure restore is forced even if there is an outstanding heartbeat ongoing and the requested heartbeat on the next
                    // line is skipped
                    _forceRestoreOnNextProcessState = true;

                    // We can safely ignore errors, because there is nothing more we can do here. Any requests that are
                    // coming in are likely to be failing
                    await HeartbeatAsync(context, forceRestore: true).IgnoreFailure();
                }
            }
        }

        private class ContentLocationDatabaseAdapter : IContentLocationEventHandler
        {
            private readonly ContentLocationDatabase _database;

            /// <nodoc />
            public ContentLocationDatabaseAdapter(ContentLocationDatabase database)
            {
                _database = database;
            }

            /// <inheritdoc />
            public long ContentTouched(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, UnixTime accessTime)
            {
                long changes = 0;
                foreach (var hash in hashes.AsStructEnumerable())
                {
                    changes += _database.ContentTouched(context, hash, accessTime).ToLong();
                }

                return changes;
            }

            /// <inheritdoc />
            public long LocationAdded(OperationContext context, MachineId sender, IReadOnlyList<ShortHashWithSize> hashes, bool reconciling, bool updateLastAccessTime)
            {
                long changes = 0;
                foreach (var hashWithSize in hashes.AsStructEnumerable())
                {
                    changes += _database.LocationAdded(context, hashWithSize.Hash, sender, hashWithSize.Size, reconciling, updateLastAccessTime).ToLong();
                }

                return changes;
            }

            /// <inheritdoc />
            public long LocationRemoved(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, bool reconciling)
            {
                long changes = 0;
                foreach (var hash in hashes.AsStructEnumerable())
                {
                    changes += _database.LocationRemoved(context, hash, sender, reconciling).ToLong();
                }

                return changes;
            }

            /// <inheritdoc />
            public long MetadataUpdated(OperationContext context, StrongFingerprint strongFingerprint, MetadataEntry entry)
            {
                return _database.TryUpsert(
                    context,
                    strongFingerprint,
                    entry.ContentHashListWithDeterminism,

                    // Update the entry if the current entry is newer
                    // TODO: Use real versioning scheme for updates to resolve possible race conditions and
                    // issues with time comparison due to clock skew
                    shouldReplace: oldEntry =>
                        entry.ContentHashListWithDeterminism.ContentHashList != null
                        && oldEntry.LastAccessTimeUtc <= entry.LastAccessTimeUtc,
                    lastAccessTimeUtc: entry.LastAccessTimeUtc).ToLong();
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

                if (_localLocationStore.Configuration.UpdateStaleLocalLastAccessTimes
                    && foundLocalInfo
                    && foundDistributedEntry
                    && distributedEntry.LastAccessTimeUtc.ToDateTime() > localInfo.LastAccessTimeUtc)
                {
                    // Update the local content store with distributed last access time if it is newer (within some margin of error specified by TargetRange)
                    _machineInfo.LocalContentStore.UpdateLastAccessTimeIfNewer(hash, distributedEntry.LastAccessTimeUtc.ToDateTime());
                    _localLocationStore.Counters[ContentLocationStoreCounters.StaleLastAccessTimeUpdates].Increment();
                }

                bool isDesignatedLocation = _localLocationStore.ClusterState.IsDesignatedLocation(LocalMachineId, hash, includeExpired: true);

                return (localInfo, distributedEntry, isDesignatedLocation);
            }
        }
    }

    internal static class BooleanExtensions
    {
        /// <nodoc />
        public static long ToLong(this bool boolValue) => boolValue ? 1 : 0;

        /// <nodoc />
        public static long ToLong(this Possible<bool> possibleBoolValue) => possibleBoolValue.Succeeded && possibleBoolValue.Result ? 1 : 0;
    }

    /// <summary>
    /// Temporarily keeping this as a separate class to avoid referencing 'System.Threading.Tasks.Extensions' in too many places.
    /// </summary>
    internal static class ValueTaskExtensions
    {
        /// <summary>
        /// Awaits the task and throws <see cref="ResultPropagationException"/> if the result is not successful.
        /// </summary>
        public static ValueTask<T> ThrowIfFailure<T>(this ValueTask<T> task)
            where T : ResultBase
        {
            if (task.IsCompletedSuccessfully)
            {
                return task;
            }

            return ThrowIfFailureSlow(task);
        }

        /// <summary>
        /// Awaits the task and throws <see cref="ResultPropagationException"/> if the result is not successful.
        /// </summary>
        private static async ValueTask<T> ThrowIfFailureSlow<T>(this ValueTask<T> task)
            where T : ResultBase
        {
            var result = await task;
            result.ThrowIfFailure();
            return result;
        }
    }
}
