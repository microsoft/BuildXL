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
        private ImmutableDictionary<MachineId, MachineLocation> _locationByIdMap = ImmutableDictionary<MachineId, MachineLocation>.Empty;

        private ImmutableDictionary<MachineLocation, MachineId> _idByLocationMap = ImmutableDictionary<MachineLocation, MachineId>.Empty;

        private BitMachineIdSet _inactiveMachinesSet = BitMachineIdSet.EmptyInstance;

        internal IReadOnlyList<MachineLocation> Locations => _locationByIdMap.Values.AsReadOnlyList();

        /// <summary>
        /// Returns a set of inactive machines.
        /// </summary>
        public IReadOnlySet<MachineId> InactiveMachines { get; private set; } = CollectionUtilities.EmptySet<MachineId>();

        /// <summary>
        /// Returns a list of inactive machines.
        /// </summary>
        public IReadOnlyList<MachineId> InactiveMachineList { get; private set; } = CollectionUtilities.EmptyArray<MachineId>();

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
        public int MaxMachineId { get; private set; } = MachineId.Invalid.Index;

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
            if (_locationByIdMap.TryGetValue(machine, out var machineLocation))
            {
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
                .Values
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
            if (!locationsResult.Succeeded)
            {
                return new Result<MachineLocation[]>(locationsResult);
            }

            return locationsResult.Value
                .Where(machineId => !_inactiveMachinesSet[machineId] && !_closedMachinesSet[machineId])
                .Select(id => _locationByIdMap[id])
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

        /// <nodoc />
        public int ApproximateNumberOfMachines()
        {
            return _idByLocationMap.Count - _inactiveMachinesSet.Count;
        }
        #endregion

        #region Mutable Operations

        /// <nodoc />
        public ClusterStateInternal With(
            ImmutableDictionary<MachineId, MachineLocation>? locationByIdMap = null,
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
            bool? enableBinManagerUpdates = null)
        {
            ClusterStateInternal clone = (ClusterStateInternal)MemberwiseClone();

            if (locationByIdMap != null)
            {
                clone._locationByIdMap = locationByIdMap;
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
                clone.InactiveMachines = inactiveMachines.ToReadOnlySet();
                clone.InactiveMachineList = inactiveMachines;
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
                var binManagerUpdateResult = BinManager.UpdateAll(activeMachines.ToArray(), updatedClusterState.InactiveMachines);
                if (!binManagerUpdateResult)
                {
                    return Result.FromError<ClusterStateInternal>(binManagerUpdateResult);
                }
            }

            return updatedClusterState;
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
                locationByIdMap[kvp.Key] = kvp.Value;
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
                locationByIdMap: _locationByIdMap.SetItem(machineId, machineLocation));
        }

        /// <summary>
        /// Initializes the BinManager if it is required.
        /// </summary>
        internal Result<ClusterStateInternal> InitializeBinManagerIfNeeded(int locationsPerBin, IClock clock, TimeSpan expiryTime)
        {

            return With(binManager: new BinManager(locationsPerBin, _idByLocationMap.Values, clock, expiryTime));
        }

        #endregion
    }
}
