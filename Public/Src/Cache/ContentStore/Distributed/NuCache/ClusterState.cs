// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
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
        private Tracer Tracer { get; } = new Tracer(nameof(ClusterState));

        private readonly object _clusterUpdateLock = new object();

        private QueryableClusterState _clusterStateCache = new QueryableClusterState(new ClusterStateMachine());

        #region Local Machine

        /// <summary>
        /// Most recent time the ClusterState was updated with information from the remote storage
        /// </summary>
        public DateTime LastUpdateTimeUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// The time at which the machine was last in an inactive state
        /// </summary>
        public DateTime LastInactiveTime { get; set; }

        /// <summary>
        /// The machine id representing the primary CAS instance on the machine. 
        /// In multi-drive scenarios where one CAS instance is on SSD and others are on HDD
        /// this should correspond to the SSD CAS instance.
        /// </summary>
        public MachineId PrimaryMachineId { get; init; }

        /// <summary>
        /// Gets a list of machine ids representing unique CAS instances on the current machine
        /// </summary>
        public IReadOnlyList<MachineMapping> LocalMachineMappings { get; init; }

        #endregion

        #region Read-Only Machine Management

        public MachineIdSet OpenMachines => _clusterStateCache.OpenMachinesSet;

        /// <summary>
        /// Returns a set of inactive machines.
        /// </summary>
        public MachineIdSet InactiveMachines => _clusterStateCache.InactiveMachinesSet;

        /// <summary>
        /// Returns a list of inactive machines.
        /// </summary>
        public IReadOnlyList<MachineId> InactiveMachineList => _clusterStateCache.InactiveMachinesList;

        /// <summary>
        /// Returns a list of closed machines.
        /// </summary>
        /// <remarks>
        /// Used only for tests.
        /// </remarks>
        public IReadOnlyCollection<MachineId> ClosedMachines => _clusterStateCache.ClosedMachines;

        /// <nodoc />
        public IEnumerable<MachineLocation> Locations => _clusterStateCache.RecordsByMachineLocation.Keys;

        /// <summary>
        /// Gets whether a machine is marked inactive
        /// </summary>
        public bool IsMachineMarkedInactive(MachineId machineId)
        {
            return _clusterStateCache.InactiveMachinesSet[machineId.Index];
        }

        /// <summary>
        /// Gets whether a machine is marked closed
        /// </summary>
        public bool IsMachineMarkedClosed(MachineId machineId)
        {
            return _clusterStateCache.ClosedMachinesSet[machineId.Index];
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineLocation"/> by a machine id (<paramref name="machine"/>).
        /// </summary>
        public bool TryResolve(MachineId machine, out MachineLocation machineLocation)
        {
            if (_clusterStateCache.RecordsByMachineId.TryGetValue(machine, out var record))
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
            if (_clusterStateCache.RecordsByMachineLocation.TryGetValue(machineLocation, out var record))
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

        /// <nodoc />
        public int ApproximateNumberOfMachines()
        {
            return _clusterStateCache.OpenMachinesSet.Count;
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
            BinManager = new BinManager(locationsPerBin, _clusterStateCache.OpenMachines, clock, expiryTime);
        }

        /// <summary>
        /// Gets a random locations excluding the specified location. Returns default if operation is not possible.
        /// </summary>
        public Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except)
        {
            var candidates = _clusterStateCache.RecordsByMachineLocation
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
        /// This should only be used from the master.
        /// </summary>
        internal Result<MachineId[][]> GetBinMappings()
        {
            return BinManager?.GetBins() ?? new Result<MachineId[][]>("Failed to get mappings since BinManager is null");
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
                .Where(machineId => !_clusterStateCache.InactiveMachinesSet[machineId] && !_clusterStateCache.ClosedMachinesSet[machineId])
                .Select(id => _clusterStateCache.RecordsByMachineId[id].Location)
                .ToArray();
        }

        /// <summary>
        /// Gets whether the given machine is a designated location for the hash
        /// </summary>
        internal bool IsDesignatedLocation(MachineId machineId, ContentHash hash, bool includeExpired)
        {
            if (BinManager == null)
            {
                return false;
            }

            var locations = BinManager.GetDesignatedLocations(hash, includeExpired);
            if (!locations.Succeeded)
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
        }

        public BoolResult Update(OperationContext context, ClusterStateMachine stateMachine, DateTime? nowUtc = null)
        {
            // This is an operation just for performance tracking purposes (i.e., if this takes too long, it'd be
            // interesting to know).
            return context.PerformOperation(Tracer, () =>
            {
                QueryableClusterState prevCache, nextCache;

                // It is not impossible for this function to be called concurrently, hence this lock. It's very
                // unlikely that it will be called concurrently in practice, though.
                lock (_clusterUpdateLock)
                {
                    prevCache = _clusterStateCache;
                    nextCache = new QueryableClusterState(stateMachine);
                    _clusterStateCache = nextCache;

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

                // What follows is just diagnostic information, mostly useful for debugging issues when they happen.
                foreach (var kvp in nextCache.RecordsByMachineId)
                {
                    if (!prevCache.RecordsByMachineId.ContainsKey(kvp.Key))
                    {
                        var nextRecord = kvp.Value;
                        Tracer.Debug(context, $"Found new machine. Id=[{nextRecord.Id}] Location=[{nextRecord.Location}] State=[{nextRecord.State}]");
                    }
                }

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

        #region Test-related methods

        /// <summary>
        /// Max known machine id.
        /// </summary>
        /// <remarks>
        /// Used only for tests.
        /// WARNING: not efficient!
        /// </remarks>
        public int MaxMachineIdSlowForTest => _clusterStateCache.RecordsByMachineId.Keys.Max(machineId => machineId.Index);

        /// <summary>
        /// Create an empty cluster state only suitable for testing purposes.
        /// </summary>
        public static ClusterState CreateForTest()
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
            var next = _clusterStateCache.ClusterStateMachine.ForceRegisterMachine(machineId, machineLocation, SystemClock.Instance.UtcNow);
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

        public ImmutableDictionary<MachineId, MachineRecord> RecordsByMachineId { get; }

        public ImmutableDictionary<MachineLocation, MachineRecord> RecordsByMachineLocation { get; }

        public HashSet<MachineId> OpenMachines { get; }

        public BitMachineIdSet OpenMachinesSet { get; }

        public HashSet<MachineId> ClosedMachines { get; }

        public BitMachineIdSet ClosedMachinesSet { get; }

        public HashSet<MachineId> InactiveMachines { get; }

        public IReadOnlyList<MachineId> InactiveMachinesList { get; }

        public BitMachineIdSet InactiveMachinesSet { get; }

        public QueryableClusterState(ClusterStateMachine stateMachine)
        {
            ClusterStateMachine = stateMachine;

            // TODO: this could obviously be made more efficient. I'm not sure it's worth the effort given the update
            // frequency.
            RecordsByMachineId = stateMachine.Records.ToImmutableDictionary(record => record.Id);
            RecordsByMachineLocation = stateMachine.Records.ToImmutableDictionary(record => record.Location);

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
