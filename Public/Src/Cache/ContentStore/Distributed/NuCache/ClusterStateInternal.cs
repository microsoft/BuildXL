// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <nodoc />
    public class ClusterStateInternal
    {
        // Index is machine Id.
        private ImmutableArray<MachineLocation> _locationByIdMap = Enumerable.Range(0, 4).Select<int, MachineLocation>(_ => default).ToImmutableArray();

        private ImmutableDictionary<MachineLocation, MachineId> _idByLocationMap = ImmutableDictionary<MachineLocation, MachineId>.Empty;

        private BitMachineIdSet _inactiveMachinesSet = BitMachineIdSet.EmptyInstance;

        internal IReadOnlyList<MachineLocation> Locations => _locationByIdMap;

        /// <summary>
        /// Returns a list of inactive machines.
        /// </summary>
        public IReadOnlyList<MachineId> InactiveMachines { get; private set; } = CollectionUtilities.EmptyArray<MachineId>();

        private BitMachineIdSet _closedMachinesSet = BitMachineIdSet.EmptyInstance;

        /// <summary>
        /// Returns a list of closed machines.
        /// </summary>
        public IReadOnlyList<MachineId> ClosedMachines { get; private set; } = CollectionUtilities.EmptyArray<MachineId>();

        /// <summary>
        /// The time at which the machine was last in an inactive state
        /// </summary>
        public DateTime LastInactiveTime { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Max known machine id.
        /// </summary>
        public int MaxMachineId { get; private set; } = ClusterState.InvalidMachineId;

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

        /// <nodoc />
        public BinManager? BinManager { get; private set; }

        /// <nodoc />
        public bool EnableBinManagerUpdates { get; private set; }

        /// <nodoc />
        public MachineLocation? MasterMachineLocation { get; private set; } = null;

        /// <nodoc />
        public MachineId? MasterMachineId { get; private set; } = null;

        #region Constructors
        /// <nodoc />
        internal ClusterStateInternal(MachineId primaryMachineId, IReadOnlyList<MachineMapping> localMachineMappings)
        {
            PrimaryMachineId = primaryMachineId;
            LocalMachineMappings = localMachineMappings;
        }
        #endregion

        #region Immutable Operations
        /// <summary>
        /// Gets whether a machine is marked inactive
        /// </summary>
        public bool IsMachineMarkedInactive(MachineId machineId)
        {
            return _inactiveMachinesSet[machineId];
        }

        /// <summary>
        /// Gets whether a machine is marked closed
        /// </summary>
        public bool IsMachineMarkedClosed(MachineId machineId)
        {
            return _closedMachinesSet[machineId];
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineLocation"/> by a machine id (<paramref name="machine"/>).
        /// </summary>
        public (bool Succeeded, MachineLocation MachineLocation) TryResolve(MachineId machine)
        {
            if (machine.Index < _locationByIdMap.Length)
            {
                var machineLocation = _locationByIdMap[machine.Index];
                return (Succeeded: machineLocation.Data != null, MachineLocation: machineLocation);
            }

            return (Succeeded: false, MachineLocation: default);
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineId"/> by <paramref name="machineLocation"/>.
        /// </summary>
        public (bool Succeeded, MachineId MachineId) TryResolveMachineId(MachineLocation machineLocation)
        {
            if (_idByLocationMap.TryGetValue(machineLocation, out var machineId))
            {
                return (Succeeded: true, MachineId: machineId);
            }

            return (Succeeded: false, MachineId: default);
        }

        /// <summary>
        /// Gets a random locations excluding the specified location. Returns default if operation is not possible.
        /// </summary>
        public Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except)
        {
            var candidates = _locationByIdMap
                .Where((location, index) => location.Data != null && !_inactiveMachinesSet[index] && !_closedMachinesSet[index])
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
        internal Result<MachineId[][]> GetBinMappings() => BinManager?.GetBins() ?? new Result<MachineId[][]>("Failed to get mappings since BinManager is null");

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
            if (!locationsResult)
            {
                return new Result<MachineLocation[]>(locationsResult);
            }

            return locationsResult.Value!
                .Where(machineId => !_inactiveMachinesSet[machineId] && !_closedMachinesSet[machineId])
                .Select(id => _locationByIdMap[id.Index])
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
            if (!locations)
            {
                return false;
            }

            return locations.Value!.Contains(machineId);
        }

        /// <nodoc />
        public int ApproximateNumberOfMachines()
        {
            return _idByLocationMap.Count - _inactiveMachinesSet.Count;
        }
        #endregion

        #region Mutable Operations

        /// <nodoc />
        public ClusterStateInternal With(
            ImmutableArray<MachineLocation>? locationByIdMap = null,
            ImmutableDictionary<MachineLocation, MachineId>? idByLocationMap = null,
            BitMachineIdSet? inactiveMachinesSet = null,
            BitMachineIdSet? closedMachinesSet = null,
            DateTime? lastInactiveTime = null,
            int? maxMachineId = null,
            IReadOnlyList<MachineId>? inactiveMachines = null,
            IReadOnlyList<MachineId>? closedMachines = null,
            IReadOnlyList<MachineMapping>? localMachineMappings = null,
            MachineId? primaryMachineId = null,
            BinManager? binManager = null,
            bool? enableBinManagerUpdates = null,
            MachineLocation? masterMachineLocation = null,
            MachineId? masterMachineId = null)
        {
            ClusterStateInternal clone = (ClusterStateInternal)MemberwiseClone();

            if (locationByIdMap != null)
            {
                clone._locationByIdMap = locationByIdMap.Value;
            }

            if (idByLocationMap != null)
            {
                clone._idByLocationMap = idByLocationMap;
            }

            if (inactiveMachinesSet != null)
            {
                clone._inactiveMachinesSet = inactiveMachinesSet;
            }

            if (closedMachinesSet != null)
            {
                clone._closedMachinesSet = closedMachinesSet;
            }

            if (lastInactiveTime != null)
            {
                clone.LastInactiveTime = lastInactiveTime.Value;
            }

            if (maxMachineId != null)
            {
                clone.MaxMachineId = maxMachineId.Value;
            }

            if (inactiveMachines != null)
            {
                clone.InactiveMachines = inactiveMachines;
            }

            if (closedMachines != null)
            {
                clone.ClosedMachines = closedMachines;
            }

            if (localMachineMappings != null)
            {
                clone.LocalMachineMappings = localMachineMappings;
            }

            if (primaryMachineId != null)
            {
                clone.PrimaryMachineId = primaryMachineId.Value;
            }

            if (binManager != null)
            {
                clone.BinManager = binManager;
            }

            if (enableBinManagerUpdates != null)
            {
                clone.EnableBinManagerUpdates = enableBinManagerUpdates.Value;
            }

            if (masterMachineLocation != null)
            {
                clone.MasterMachineLocation = masterMachineLocation;
            }

            if (masterMachineId != null)
            {
                clone.MasterMachineId = masterMachineId;
            }

            return clone;
        }

        /// <nodoc />
        public Result<ClusterStateInternal> SetMachineStates(BitMachineIdSet? inactiveMachines, BitMachineIdSet? closedMachines = null)
        {
            var updatedClusterState = With(
                inactiveMachinesSet: inactiveMachines,
                inactiveMachines: inactiveMachines?.EnumerateMachineIds().ToArray(),
                closedMachinesSet: closedMachines,
                closedMachines: closedMachines?.EnumerateMachineIds().ToArray());

            if (EnableBinManagerUpdates && BinManager != null && inactiveMachines != null)
            {
                // Closed machines aren't included in the bin manager's update because they are expected to be back
                // soon, so it doesn't make much sense to reorganize the stamp because of them.
                var activeMachines = _idByLocationMap.Values.Except(updatedClusterState.InactiveMachines);

                // Try to exclude the master from proactive operations to reduce load.
                if (MasterMachineLocation.HasValue && _idByLocationMap.ContainsKey(MasterMachineLocation.Value))
                {
                    activeMachines = activeMachines.Except(new[] { _idByLocationMap[MasterMachineLocation.Value] });
                }

                var binManagerUpdateResult = BinManager.UpdateAll(activeMachines.ToArray(), updatedClusterState.InactiveMachines);
                if (!binManagerUpdateResult)
                {
                    return Result.FromError<ClusterStateInternal>(binManagerUpdateResult);
                }
            }

            return updatedClusterState;
        }

        /// <summary>
        /// Marks that a machine with <paramref name="machineId"/> is Active.
        /// </summary>
        public Result<ClusterStateInternal> MarkMachineActive(MachineId machineId)
        {
            if (_inactiveMachinesSet[machineId.Index] || _closedMachinesSet[machineId.Index])
            {
                return SetMachineStates((BitMachineIdSet)_inactiveMachinesSet.Remove(machineId), (BitMachineIdSet)_closedMachinesSet.Remove(machineId));
            }

            return this;
        }

        /// <summary>
        /// Sets max known machine id and unknown machines.
        /// </summary>
        public Result<ClusterStateInternal> AddUnknownMachines(int maxMachineId, IReadOnlyDictionary<MachineId, MachineLocation> unknownMachines)
        {
            var idByLocationMap = _idByLocationMap.ToBuilder();
            var locationByIdMap = _locationByIdMap.ToBuilder();

            foreach (var kvp in unknownMachines)
            {
                locationByIdMap.Insert(kvp.Key.Index, kvp.Value);
                idByLocationMap[kvp.Value] = kvp.Key;
            }

            return With(
                maxMachineId: maxMachineId,
                idByLocationMap: idByLocationMap.ToImmutable(),
                locationByIdMap: locationByIdMap.ToImmutable());
        }

        /// <summary>
        /// Add a mapping from <paramref name="machineId"/> to <paramref name="machineLocation"/>.
        /// </summary>
        public Result<ClusterStateInternal> AddMachine(MachineId machineId, MachineLocation machineLocation)
        {
            return With(
                idByLocationMap: _idByLocationMap.SetItem(machineLocation, machineId),
                locationByIdMap: _locationByIdMap.Insert(machineId.Index, machineLocation));
        }

        /// <summary>
        /// Initializes the BinManager if it is required.
        /// </summary>
        internal Result<ClusterStateInternal> InitializeBinManagerIfNeeded(int locationsPerBin, IClock clock, TimeSpan expiryTime)
        {
            var startLocations = _idByLocationMap.Values;
            if (MasterMachineLocation.HasValue && _idByLocationMap.ContainsKey(MasterMachineLocation.Value))
            {
                startLocations = startLocations.Except(new[] { _idByLocationMap[MasterMachineLocation.Value] }).ToArray();
            }

            return With(binManager: new BinManager(locationsPerBin, startLocations, clock, expiryTime));
        }

        /// <nodoc />
        internal Result<ClusterStateInternal> SetMasterMachine(MachineLocation producer)
        {
            var masterMachineId = TryResolveMachineId(producer);
            return With(
                masterMachineLocation: producer,
                // WARNING: this doesn't update the masterMachineId to null if the lookup doesn't succeeds, just leaves it as-is.
                masterMachineId: masterMachineId.Succeeded ? masterMachineId.MachineId : (MachineId?)null);
        }
        #endregion
    }
}
