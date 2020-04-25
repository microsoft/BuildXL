// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Threading;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// State of all known machines in the stamp
    /// </summary>
    public sealed class ClusterState
    {
        /// <summary>
        /// The minimum valid machine id.
        /// NOTE: This value is implied from the logic in GetOrAddMachine.lua. 
        /// </summary>
        public const int MinValidMachineId = 1;

        /// <summary>
        /// The de facto invalid machine id.
        /// NOTE: This value is implied from the logic in GetOrAddMachine.lua. 
        /// </summary>
        public const int InvalidMachineId = 0;

        private readonly ReadWriteLock _lock = ReadWriteLock.Create();

        // Index is machine Id.
        private MachineLocation[] _locationByIdMap = new MachineLocation[4];

        private readonly ConcurrentDictionary<MachineLocation, MachineId> _idByLocationMap = new ConcurrentDictionary<MachineLocation, MachineId>();

        private BitMachineIdSet _inactiveMachinesSet = BitMachineIdSet.EmptyInstance;

        private BitMachineIdSet _closedMachinesSet = BitMachineIdSet.EmptyInstance;

        /// <summary>
        /// The time at which the machine was last in an inactive state
        /// </summary>
        public DateTime LastInactiveTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Max known machine id.
        /// </summary>
        public int MaxMachineId { get; private set; } = InvalidMachineId;

        /// <summary>
        /// Returns a list of inactive machines.
        /// </summary>
        public IReadOnlyList<MachineId> InactiveMachines { get; private set; } = CollectionUtilities.EmptyArray<MachineId>();

        /// <summary>
        /// Returns a list of closed machines.
        /// </summary>
        public IReadOnlyList<MachineId> ClosedMachines { get; private set; } = CollectionUtilities.EmptyArray<MachineId>();

        /// <summary>
        /// Gets a list of machine ids representing unique CAS instances on the current machine
        /// </summary>
        public IReadOnlyList<MachineMapping> LocalMachineMappings { get; }

        /// <summary>
        /// The machine id representing the primary CAS instance on the machine. 
        /// In multi-drive scenarios where one CAS instance is on SSD and others are on HDD
        /// this should correspond to the SSD CAS instance.
        /// </summary>
        public MachineId PrimaryMachineId { get; }

        /// <nodoc />
        public BinManager BinManager { get; set; }

        /// <nodoc />
        public bool EnableBinManagerUpdates { get; set; }

        /// <nodoc />
        public ClusterState(MachineId primaryMachineId, IReadOnlyList<MachineMapping> allLocalMachineIds)
        {
            PrimaryMachineId = primaryMachineId;
            LocalMachineMappings = allLocalMachineIds;
        }

        /// <summary>
        /// Create an empty cluster state only suitable for testing purposes.
        /// </summary>
        public static ClusterState CreateForTest()
        {
            return new ClusterState(default(MachineId), Array.Empty<MachineMapping>());
        }

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

        /// <nodoc />
        public BoolResult SetMachineStates(BitMachineIdSet inactiveMachines, BitMachineIdSet closedMachines = null)
        {
            bool updateBinManager = false;
            if (inactiveMachines != null)
            {
                _inactiveMachinesSet = inactiveMachines;
                InactiveMachines = inactiveMachines.EnumerateMachineIds().ToArray();
                updateBinManager = true;
            }

            if (closedMachines != null)
            {
                _closedMachinesSet = closedMachines;
                ClosedMachines = closedMachines.EnumerateMachineIds().ToArray();
            }

            if (EnableBinManagerUpdates && BinManager != null && updateBinManager)
            {
                // Closed machines aren't included in the bin manager's update because they are expected to be back
                // soon, so it doesn't make much sense to reorganize the stamp because of them.
                var activeMachines = _idByLocationMap.Values.Except(InactiveMachines).ToArray();
                return BinManager.UpdateAll(activeMachines, InactiveMachines);
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// Marks that a machine with <paramref name="machineId"/> is Active.
        /// </summary>
        public BoolResult MarkMachineActive(MachineId machineId)
        {
            if (_inactiveMachinesSet[machineId.Index] || _closedMachinesSet[machineId.Index])
            {
                return SetMachineStates((BitMachineIdSet)_inactiveMachinesSet.Remove(machineId), (BitMachineIdSet)_closedMachinesSet.Remove(machineId));
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// Sets max known machine id and unknown machines.
        /// </summary>
        public void AddUnknownMachines(int maxMachineId, Dictionary<MachineId, MachineLocation> unknownMachines)
        {
            Contract.Requires(unknownMachines != null);

            foreach (var entry in unknownMachines)
            {
                AddMachine(entry.Key, entry.Value);
            }

            MaxMachineId = maxMachineId;
        }

        /// <summary>
        /// Add a mapping from <paramref name="machineId"/> to <paramref name="machineLocation"/>.
        /// </summary>
        public void AddMachine(MachineId machineId, MachineLocation machineLocation)
        {
            using (_lock.AcquireWriteLock())
            {
                while (machineId.Index >= _locationByIdMap.Length)
                {
                    Array.Resize(ref _locationByIdMap, _locationByIdMap.Length * 2);
                }

                _locationByIdMap[machineId.Index] = machineLocation;
            }

            _idByLocationMap[machineLocation] = machineId;
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineLocation"/> by a machine id (<paramref name="machine"/>).
        /// </summary>
        public bool TryResolve(MachineId machine, out MachineLocation machineLocation)
        {
            using (_lock.AcquireReadLock())
            {
                if (machine.Index < _locationByIdMap.Length)
                {
                    machineLocation = _locationByIdMap[machine.Index];
                    return machineLocation.Data != null;
                }

                machineLocation = default;
                return false;
            }
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineId"/> by <paramref name="machineLocation"/>.
        /// </summary>
        public bool TryResolveMachineId(MachineLocation machineLocation, out MachineId machineId)
        {
            return _idByLocationMap.TryGetValue(machineLocation, out machineId);
        }

        /// <summary>
        /// Gets a random locations excluding the specified location. Returns default if operation is not possible.
        /// </summary>
        public Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except)
        {
            using (_lock.AcquireReadLock())
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

            return locationsResult.Value
                .Where(machineId => !_inactiveMachinesSet[machineId] && !_closedMachinesSet[machineId])
                .Select(id =>_locationByIdMap[id.Index])
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

        /// <summary>
        /// Initializes the BinManager if it is required.
        /// </summary>
        internal void InitializeBinManagerIfNeeded(int locationsPerBin, IClock clock, TimeSpan expiryTime)
        {
            BinManager ??= new BinManager(locationsPerBin, _idByLocationMap.Values, clock, expiryTime);
        }
    }
}
