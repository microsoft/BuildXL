// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Threading;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// State of all known machines in the stamp.
    /// </summary>
    /// <remarks>
    /// The cluster state tracks inactive machines as well as a bi-directional map for machineId to machine location and other way around.
    /// </remarks>
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
        /// Gets whether a machine is marked inactive
        /// </summary>
        public bool IsMachineMarkedInactive(MachineId machineId)
        {
            return _inactiveMachinesSet[machineId]; 
        }

        /// <nodoc />
        public void SetInactiveMachines(BitMachineIdSet inactiveMachines)
        {
            _inactiveMachinesSet = inactiveMachines;
            InactiveMachines = inactiveMachines.EnumerateMachineIds().ToArray();
        }

        /// <summary>
        /// Marks that a machine with <paramref name="machineId"/> is Active.
        /// </summary>
        public void MarkMachineActive(MachineId machineId)
        {
            if (_inactiveMachinesSet[machineId.Index])
            {
                SetInactiveMachines((BitMachineIdSet)_inactiveMachinesSet.Remove(machineId));
            }
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
                    .Where((location, index) => location.Data != null && !_inactiveMachinesSet[index])
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
    }
}
