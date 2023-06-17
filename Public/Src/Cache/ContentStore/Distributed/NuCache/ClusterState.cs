// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// State of all known machines in the stamp
    /// </summary>
    public sealed class ClusterState
    {
        private Tracer Tracer { get; } = new(nameof(ClusterState));

        private readonly object _clusterUpdateLock = new();

        /// <summary>
        /// This is the current state of the cluster as known by this machine. This immutable class is what actually
        /// backs the methods in here. The value of this instance is updated atomically in the <see cref="Update"/>
        /// method by <see cref="ClusterStateManager"/>.
        /// </summary>
        public QueryableClusterState QueryableClusterState { get; private set; }

        #region Local Machine

        /// <summary>
        /// Most recent time the ClusterState was updated with information from the remote storage
        /// </summary>
        public DateTime LastUpdateTimeUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// The self-determined state of the machine as of the current time.
        /// </summary>
        /// <remarks>
        /// This can be out of sync with what other machines believe the state is.
        /// </remarks>
        public MachineState CurrentState { get; set; } = MachineState.Unknown;

        /// <summary>
        /// The machine id representing the primary CAS instance on the machine. 
        /// In multi-drive scenarios where one CAS instance is on SSD and others are on HDD
        /// this should correspond to the SSD CAS instance.
        /// </summary>
        public MachineId PrimaryMachineId { get; private set; }

        /// <summary>
        /// Gets a list of machine ids representing unique CAS instances on the current machine
        /// </summary>
        public IReadOnlyList<MachineMapping> LocalMachineMappings { get; private set; }

        #endregion

        #region Read-Only Machine Management

        public MachineIdSet OpenMachines => QueryableClusterState.OpenMachinesSet;

        /// <summary>
        /// Returns a set of inactive machines.
        /// </summary>
        public MachineIdSet InactiveMachines => QueryableClusterState.InactiveMachinesSet;

        /// <summary>
        /// Returns a list of inactive machines.
        /// </summary>
        public IReadOnlyList<MachineId> InactiveMachineList => QueryableClusterState.InactiveMachinesList;

        /// <summary>
        /// Returns a list of closed machines.
        /// </summary>
        /// <remarks>
        /// Used only for tests.
        /// </remarks>
        public IReadOnlyCollection<MachineId> ClosedMachines => QueryableClusterState.ClosedMachines;

        /// <nodoc />
        public IEnumerable<MachineLocation> Locations => QueryableClusterState.RecordsByMachineLocation.Keys;

        /// <nodoc />
        public IEnumerable<MachineRecord> Records => QueryableClusterState.RecordsByMachineLocation.Values;

        /// <summary>
        /// Gets whether a machine is marked inactive
        /// </summary>
        public bool IsMachineMarkedInactive(MachineId machineId)
        {
            return QueryableClusterState.InactiveMachinesSet[machineId.Index];
        }

        /// <summary>
        /// Gets whether a machine is marked closed
        /// </summary>
        public bool IsMachineMarkedClosed(MachineId machineId)
        {
            return QueryableClusterState.ClosedMachinesSet[machineId.Index];
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineLocation"/> by a machine id (<paramref name="machine"/>).
        /// </summary>
        public bool TryResolve(MachineId machine, out MachineLocation machineLocation)
        {
            if (QueryableClusterState.RecordsByMachineId.TryGetValue(machine, out var record))
            {
                machineLocation = record.Location;
                return true;
            }
            else
            {
                machineLocation = default;
                return false;
            }
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineId"/> by <paramref name="machineLocation"/>.
        /// </summary>
        public bool TryResolveMachineId(MachineLocation machineLocation, out MachineId machineId)
        {
            if (QueryableClusterState.RecordsByMachineLocation.TryGetValue(machineLocation, out var record))
            {
                machineId = record.Id;
                return true;
            }
            else
            {
                machineId = default;
                return false;
            }
        }

        #endregion

        #region Bin Manager

        /// <nodoc />
        public bool EnableBinManagerUpdates { get; set; }

        /// <summary>
        /// Getting or setting an instance of <see cref="BinManager"/>.
        /// </summary>
        /// <remarks>
        /// The setter is called by the worker machines.
        /// </remarks>
        public BinManager? BinManager { get; set; }

        /// <summary>
        /// Initializes the BinManager if it is required.
        /// </summary>
        /// <remarks>This operation is used only by the master. The worker still may set BinManager via <see cref="BinManager"/> property.</remarks>
        internal void InitializeBinManagerIfNeeded(int locationsPerBin, IClock clock, TimeSpan expiryTime)
        {
            BinManager = new BinManager(locationsPerBin, QueryableClusterState.OpenMachines, clock, expiryTime);
        }

        /// <summary>
        /// Gets a random locations excluding the specified location. Returns default if operation is not possible.
        /// </summary>
        public Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except)
        {
            var candidates = QueryableClusterState.RecordsByMachineLocation
                .Where(kvp => kvp.Key.IsValid && kvp.Value.IsOpen())
                .Select(kvp => kvp.Key)
                .Except(except)
                .ToList();
            if (candidates.Any())
            {
                var index = ThreadSafeRandom.Generator.Next(candidates.Count);
                return candidates[index];
            }

            return new Result<MachineLocation>("Could not select a machine location.");
        }

        /// <summary>
        /// Gets the set of locations designated for a hash. This is relevant for proactive copies and eviction.
        /// </summary>
        internal Result<MachineLocation[]> GetDesignatedLocations(ContentHash hash, bool includeExpired)
        {
            if (BinManager == null)
            {
                return new Result<MachineLocation[]>("Could not get designated locations because BinManager is null");
            }

            var locationsResult = BinManager.GetDesignatedLocations(hash, includeExpired);
            if (!locationsResult.Succeeded)
            {
                return new Result<MachineLocation[]>(locationsResult);
            }

            return locationsResult.Value
                .Where(machineId => !QueryableClusterState.InactiveMachinesSet[machineId] && !QueryableClusterState.ClosedMachinesSet[machineId])
                .Select(id => QueryableClusterState.RecordsByMachineId[id].Location)
                .ToArray();
        }

        /// <summary>
        /// Gets whether the given machine is a designated location for the hash
        /// </summary>
        internal bool IsDesignatedLocation(MachineId machineId, ContentHash hash, bool includeExpired)
        {
            var locations = BinManager?.GetDesignatedLocations(hash, includeExpired);
            if (locations == null || !locations.Succeeded)
            {
                return false;
            }

            return locations.Value.Contains(machineId);
        }

        #endregion

        /// <nodoc />
        public ClusterState(MachineId primaryMachineId, IReadOnlyList<MachineMapping> localMachineMappings)
        {
            PrimaryMachineId = primaryMachineId;
            LocalMachineMappings = localMachineMappings;
            QueryableClusterState = new QueryableClusterState(new ClusterStateMachine(), PrimaryMachineId);
        }

        /// <summary>
        /// Update primary machine id and the local machine mappings.
        /// </summary>
        public void UpdateMachineMappings(MachineId primaryMachineId, IReadOnlyList<MachineMapping> localMachineMappings)
        {
            PrimaryMachineId = primaryMachineId;
            LocalMachineMappings = localMachineMappings;
            QueryableClusterState = new QueryableClusterState(new ClusterStateMachine(), PrimaryMachineId);
        }

        public delegate void ClusterStateUpdateHandler(OperationContext context, QueryableClusterState clusterState);

        public event ClusterStateUpdateHandler? OnClusterStateUpdate;

        public BoolResult Update(
            OperationContext context,
            ClusterStateMachine stateMachine,
            ClusterStateRecomputeConfiguration? configuration = null,
            DateTime? nowUtc = null)
        {
            // This is an operation just for performance tracking purposes (i.e., if this takes too long, it'd be
            // interesting to know).
            return context.PerformOperation(Tracer, () =>
            {
                QueryableClusterState prevCache, nextCache;

                if (configuration is not null)
                {
                    // The first thing we do is to transition the inactive machines. This is because the inactive
                    // machines aren't stored by the ClusterStateManager to prevent clock skew issues from causing
                    // incidents; therefore, inactive states are basically only representable in-memory.
                    stateMachine = stateMachine.TransitionInactiveMachines(configuration, nowUtc!.Value);
                }

                // It is not impossible for this function to be called concurrently, hence this lock. It's very
                // unlikely that it will be called concurrently in practice, though.
                lock (_clusterUpdateLock)
                {
                    prevCache = QueryableClusterState;
                    nextCache = new QueryableClusterState(stateMachine, PrimaryMachineId);
                    QueryableClusterState = nextCache;

                    if (EnableBinManagerUpdates && BinManager != null)
                    {
                        // Closed machines aren't included in the bin manager's update because they are expected to be back
                        // soon, so it doesn't make much sense to reorganize the stamp because of them.
                        var distributionMachines = nextCache.RecordsByMachineId
                            .Where(kvp => !kvp.Value.IsInactive())
                            .Select(record => record.Key)
                            .ToList();

                        BinManager.UpdateAll(distributionMachines, nextCache.InactiveMachines).TraceIfFailure(context);
                    }

                    if (nowUtc is not null)
                    {
                        LastUpdateTimeUtc = nowUtc.Value;
                    }
                }

                OnClusterStateUpdate?.Invoke(context, nextCache);

                // What follows is just diagnostic information, mostly useful for debugging issues when they happen.
                TraceMachineMappings(context, nextCache, prevCache);

                Tracer.TrackMetric(context, "OpenMachineCount", nextCache.OpenMachinesSet.Count);
                Tracer.TrackMetric(context, "ClosedMachineCount", nextCache.ClosedMachinesSet.Count);
                Tracer.TrackMetric(context, "InactiveMachineCount", nextCache.InactiveMachinesSet.Count);

                Tracer.Info(context, $"Cluster State Summary. " +
                    $"OpenCount=[{nextCache.OpenMachinesSet.Count}] " +
                    $"ClosedCount=[{nextCache.ClosedMachinesSet.Count}] " +
                    $"InactiveCount=[{nextCache.InactiveMachinesSet.Count}] " +
                    $"ClosedIds=[{string.Join(", ", nextCache.ClosedMachinesSet)}] " +
                    $"InactiveIds=[{string.Join(", ", nextCache.InactiveMachinesSet)}]",
                    operation: "PrintSummary");

                return BoolResult.Success;
            },
            traceOperationStarted: false);
        }

        private void TraceMachineMappings(OperationContext context, QueryableClusterState nextCache, QueryableClusterState prevCache)
        {
            foreach (var kvp in nextCache.RecordsByMachineId)
            {
                var current = kvp.Value;
                
                if (!prevCache.RecordsByMachineId.TryGetValue(kvp.Key, out var previous))
                {
                    Tracer.Debug(context, $"MachineMapping: Found new machine. Id=[{current.Id}] Location=[{current.Location}] State=[{current.State}]");
                    continue;
                }

                if (!previous.Location.Equals(current.Location))
                {
                    Tracer.Warning(context, $"MachineIdReclamation: Id takeover detected. Id=[{kvp.Key}] Previous=[{previous}] Current=[{kvp.Value}]");
                }
            }
        }

        /// <summary>
        /// Computes the max machine id
        /// </summary>
        public int ComputeMaxMachineId() => QueryableClusterState.RecordsByMachineId.Keys.Max(machineId => machineId.Index);

        #region Test-related methods

        /// <summary>
        /// Max known machine id.
        /// </summary>
        /// <remarks>
        /// Used only for tests.
        /// WARNING: not efficient!
        /// </remarks>
        public int MaxMachineIdSlowForTest => ComputeMaxMachineId();

        /// <summary>
        /// Create an empty cluster state.
        /// </summary>
        public static ClusterState CreateEmpty()
        {
            return new ClusterState(default(MachineId), Array.Empty<MachineMapping>());
        }

        /// <summary>
        /// Add a mapping from <paramref name="machineId"/> to <paramref name="machineLocation"/>.
        /// </summary>
        /// <remarks>
        /// Used only for testing. DO NOT USE OUTSIDE OF TESTS.
        /// </remarks>
        internal void AddMachineForTest(OperationContext context, MachineId machineId, MachineLocation machineLocation)
        {
            var next = QueryableClusterState.ClusterStateMachine.ForceRegisterMachine(machineId, machineLocation, SystemClock.Instance.UtcNow);
            Update(context, next).ThrowIfFailure();
        }

        #endregion
    }

    /// <summary>
    /// Ideally, we'd be able to work with just the <see cref="ClusterStateMachine"/>. However, the representation
    /// that it uses is optimized to be easy to operate with inside of <see cref="BlobClusterStateStorage"/>, rather
    /// than to be fast to query. Hence, we process that representation into something that's fast to query, and that's
    /// what we expose.
    /// </summary>
    public record QueryableClusterState
    {
        public ClusterStateMachine ClusterStateMachine { get; }

        public MachineId PrimaryMachineId { get; }

        public MachineRecord? PrimaryMachineRecord { get; }

        public ImmutableDictionary<MachineId, MachineRecord> RecordsByMachineId { get; }

        public ImmutableDictionary<MachineLocation, MachineRecord> RecordsByMachineLocation { get; }

        public HashSet<MachineId> OpenMachines { get; }

        public BitMachineIdSet OpenMachinesSet { get; }

        public HashSet<MachineId> ClosedMachines { get; }

        public BitMachineIdSet ClosedMachinesSet { get; }

        public HashSet<MachineId> InactiveMachines { get; }

        public IReadOnlyList<MachineId> InactiveMachinesList { get; }

        public BitMachineIdSet InactiveMachinesSet { get; }

        public QueryableClusterState(ClusterStateMachine stateMachine, MachineId primaryMachineId)
        {
            ClusterStateMachine = stateMachine;

            // TODO: this could obviously be made more efficient. I'm not sure it's worth the effort given the update
            // frequency.
            RecordsByMachineId = stateMachine.Records.ToImmutableDictionary(record => record.Id);
            RecordsByMachineLocation = stateMachine.Records.ToImmutableDictionary(record => record.Location);

            PrimaryMachineId = primaryMachineId;
            PrimaryMachineRecord = RecordsByMachineId.GetOrDefault(primaryMachineId);

            OpenMachines = stateMachine.Records
                .Where(record => record.IsOpen())
                .Select(record => record.Id)
                .ToHashSet();
            OpenMachinesSet = BitMachineIdSet.Create(OpenMachines);

            ClosedMachines = stateMachine.Records
                .Where(record => record.IsClosed())
                .Select(record => record.Id)
                .ToHashSet();
            ClosedMachinesSet = BitMachineIdSet.Create(ClosedMachines);

            InactiveMachines = stateMachine.Records
                .Where(record => record.IsInactive())
                .Select(record => record.Id)
                .ToHashSet();

            InactiveMachinesList = stateMachine.Records
                .Where(record => record.IsInactive())
                .Select(record => record.Id)
                .ToImmutableArray();

            InactiveMachinesSet = BitMachineIdSet.Create(InactiveMachines);
        }
    }
}
