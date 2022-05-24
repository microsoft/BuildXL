// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Manages bins so that every location inserted into it has the same amount assigned.
    /// Achieves balance by taking assignments from the ones with the most assignments and
    ///     giving them to the ones with the least amount.
    /// </summary>
    public class BinManager
    {
        /// <summary>
        /// Number of bins in the manager. We want this number to be as high as possible so that every bin
        /// assignment represent the least amount of hashes possible, so that when we rebalance bins,
        /// the least amount of content moves from one location to another.
        /// </summary>
        public const int NumberOfBins = 1 << 16;

        internal int LocationsPerBin { get; }

        /// <summary>
        /// Tracks the machine assignments sorted by the number of bins assigned. This is used to find the machine is least or most number of machines assigned
        /// when selecting which machine to add bins to or remove bins from respectively.
        /// </summary>
        private readonly SortedSet<MachineWithBinAssignments> _machineAssignmentsSortedByBinCount = new SortedSet<MachineWithBinAssignments>(new Comparer());

        private readonly Dictionary<MachineId, MachineWithBinAssignments> _machinesToBinsMap = new Dictionary<MachineId, MachineWithBinAssignments>();

        /// <summary>
        /// Tracks expired machine assignments for a given period of time (<see cref="_expiryTime"/>) (i.e. machine assignments which were present in the past for a given bin).
        /// This is used to ensure that shifts in bin assignments still allow the content to be considered important for some time
        /// </summary>
        private readonly Dictionary<uint, Dictionary<MachineId, DateTime>> _expiredAssignments = new Dictionary<uint, Dictionary<MachineId, DateTime>>();
        private readonly TimeSpan _expiryTime;
        private readonly IClock _clock;
        private readonly MachineId?[] _binToMachineMap;
        private readonly HashSet<MachineId> _machineSetBuffer = new HashSet<MachineId>();
        private uint[]? _previousBins;

        private MachineId[][]? _bins;
        private DateTime _lastPruneExpiry = DateTime.MinValue;

        // This class takes advantage of the fact that the lock statement is re-entrant. Make sure to double check if we are going to use a different synchronization mechanism.
        private readonly object _lockObject = new object(); 

        /// <nodoc />
        public BinManager(int locationsPerBin, IEnumerable<MachineId> startLocations, IClock clock, TimeSpan expiryTime)
        {
            LocationsPerBin = locationsPerBin;
            _clock = clock;
            _expiryTime = expiryTime;

            _binToMachineMap = new MachineId?[NumberOfBins];

            if(startLocations.Any())
            {
                foreach (var location in startLocations)
                {
                    var assignments = new MachineWithBinAssignments(location);
                    _machineAssignmentsSortedByBinCount.Add(assignments);
                    _machinesToBinsMap[location] = assignments;
                }

                for (uint i = 0; i < NumberOfBins; i++)
                {
                    // Min is not null, if SortedSet is not empty.
                    var min = _machineAssignmentsSortedByBinCount.Min!;

                    _machineAssignmentsSortedByBinCount.Remove(min);
                    min.BinsAssignedTo.Add(i);
                    _machineAssignmentsSortedByBinCount.Add(min);

                    _binToMachineMap[i] = min.MachineId;
                }
            }
        }

        // Used during deserialization.
        private BinManager(int locationsPerBin, MachineId?[] bins, Dictionary<uint, Dictionary<MachineId, DateTime>> expiredAssignments, IClock clock, TimeSpan expiryTime)
        {
            Contract.Requires(bins.Length == NumberOfBins);

            LocationsPerBin = locationsPerBin;
            _clock = clock;
            _expiryTime = expiryTime;

            _binToMachineMap = bins;

            // Populate machine to bin assignments map from bin->machine mapping.
            for (uint binNumber = 0; binNumber < NumberOfBins; binNumber++)
            {
                var location = bins[binNumber];
                if (location == null)
                {
                    continue;
                }

                var assignments = _machinesToBinsMap.GetOrAdd(location.Value, l => new MachineWithBinAssignments(l));
                assignments.BinsAssignedTo.Add(binNumber);
            }

            // Add populated machine assignments into sorted assignment set
            foreach (var assignments in _machinesToBinsMap.Values)
            {
                _machineAssignmentsSortedByBinCount.Add(assignments);
            }

            _expiredAssignments = expiredAssignments;
        }

        /// <summary>
        /// Makes sure that the bin manager has a specific view of what machines are active and inactive.
        /// </summary>
        public BoolResult UpdateAll(IEnumerable<MachineId> activeMachines, IEnumerable<MachineId> inactiveMachines)
        {
            try
            {
                lock (_lockObject)
                {
                    // Remove active machines that became inactive or disappeared.
                    var machinesToRemove = _machinesToBinsMap.Keys.Except(activeMachines).ToList();

                    foreach (var machine in activeMachines)
                    {
                        AddLocation(machine);
                    }

                    foreach (var machine in inactiveMachines.Concat(machinesToRemove))
                    {
                        RemoveLocation(machine);
                    }
                }

                return BoolResult.Success;
            }
            catch (Exception e)
            {
                return new BoolResult(e, $"{nameof(BinManager)}.{nameof(UpdateAll)} failed.");
            }
        }


        /// <summary>
        /// Gets an array of machine assignments for each bin. 
        /// </summary>
        /// <returns></returns>
        public Result<MachineId[][]> GetBins(bool force = false)
        {
            try
            {
                lock (_lockObject)
                {
                    if (force || _bins == null || _bins[0].Length < Math.Min(LocationsPerBin, _machinesToBinsMap.Count))
                    {
                        var result = new MachineId[NumberOfBins][];
                        if (_machinesToBinsMap.Count <= LocationsPerBin)
                        {
                            var machines = _machinesToBinsMap.Keys.ToArray();
                            for (var bin = 0; bin < NumberOfBins; bin++)
                            {
                                result[bin] = machines;
                            }
                        }
                        else
                        {
                            // NOTE: Other than primary machine specified in _binToMachineMap, other machines
                            // are picked by looking at a backup chain starting with the given bin and using the machine specified
                            // as the primary for that backup bin. Each bin has
                            // a unique, pseudorandom backup such that traversing the backup chain would eventually iterate
                            // all backups.
                            for (uint bin = 0; bin < NumberOfBins; bin++)
                            {
                                var currentBin = bin;
                                while (_machineSetBuffer.Count < LocationsPerBin)
                                {
                                    _machineSetBuffer.Add(_binToMachineMap[currentBin]!.Value);

                                    currentBin = GetNextBin(currentBin);
                                    if (currentBin == bin)
                                    {
                                        break;
                                    }
                                }

                                result[bin] = _machineSetBuffer.ToArray();
                                _machineSetBuffer.Clear();
                            }
                        }

                        _bins = result;
                    }

                    return _bins;
                }
            }
            catch (Exception e)
            {
                return new Result<MachineId[][]>(e, $"{nameof(BinManager)}.{nameof(GetBins)} failed."); ;
            }
        }

        /// <summary>
        /// Gets the designated locations for a hash
        /// </summary>
        /// <param name="hash">The hash</param>
        /// <param name="includeExpired">
        /// Whether to include assignments which have been marked for expiry. Notice that if set to false,
        /// assignments which have been marked for expiry will be excluded even if their expiry time hasn't been reached.
        /// </param>
        public Result<MachineId[]> GetDesignatedLocations(ContentHash hash, bool includeExpired)
        {
            uint binNumber = (uint)((hash[0] | hash[1] << 8) % NumberOfBins);
            return GetDesignatedLocations(binNumber, includeExpired);
        }

        /// <nodoc />
        public Result<byte[]> Serialize()
        {
            try
            {
                lock (_lockObject)
                {
                    PruneExpiries(force: true);

                    using var stream = new MemoryStream();
                    using var writer = new BuildXLWriter(false, stream, false, false);

                    writer.WriteCompact(_binToMachineMap.Length);
                    foreach (var assignment in _binToMachineMap)
                    {
                        writer.WriteCompact(assignment?.Index ?? -1);
                    }

                    writer.WriteCompact(_expiredAssignments.Count);
                    foreach (var expiry in _expiredAssignments)
                    {
                        writer.WriteCompact(expiry.Key);
                        writer.WriteCompact(expiry.Value.Count);
                        foreach (var kvp in expiry.Value)
                        {
                            writer.WriteCompact(kvp.Key.Index);
                            writer.Write(kvp.Value);
                        }
                    }

                    return stream.ToArray();
                }
            }
            catch (Exception e)
            {
                return new Result<byte[]>(e, $"{nameof(BinManager)}.{nameof(Serialize)} failed.");
            }
        }

        /// <nodoc />
        public static Result<BinManager> CreateFromSerialized(byte[] serializedBytes, int locationsPerBin, IClock clock, TimeSpan expiryTime)
        {
            try
            {
                using var stream = new MemoryStream(serializedBytes);
                using var reader = new BuildXLReader(false, stream, false);

                var count = reader.ReadInt32Compact();
                Contract.Assert(count == NumberOfBins);
                var assignments = new MachineId?[NumberOfBins];
                for (var i = 0; i < NumberOfBins; i++)
                {
                    var id = reader.ReadInt32Compact();
                    assignments[i] = id == -1 ? (MachineId?)null : new MachineId(id);
                }

                count = reader.ReadInt32Compact();
                var expiredAssignments = new Dictionary<uint, Dictionary<MachineId, DateTime>>();
                for (var i = 0; i < count; i++)
                {
                    var bin = reader.ReadUInt32Compact();
                    var newExpiredAssignments = new Dictionary<MachineId, DateTime>();
                    var expiryCount = reader.ReadInt32Compact();
                    for (var j = 0; j < expiryCount; j++)
                    {
                        var machineId = reader.ReadInt32Compact();
                        var expiry = reader.ReadDateTime();
                        newExpiredAssignments[new MachineId(machineId)] = expiry;
                    }

                    expiredAssignments[bin] = newExpiredAssignments;
                }

                return new BinManager(locationsPerBin, assignments, expiredAssignments, clock, expiryTime);
            }
            catch (Exception e)
            {
                return new Result<BinManager>(e, $"{nameof(BinManager)}.{nameof(CreateFromSerialized)} failed.");
            }
        }

        /// <summary>
        /// Adds the machine id and assigns bins by taking from machines which have most number of bins assigned
        /// </summary>
        internal void AddLocation(MachineId id)
        {
            lock (_lockObject)
            {
                var addedMachine = new MachineWithBinAssignments(id);
                if (!_machinesToBinsMap.TryAdd(id, addedMachine))
                {
                    // Machine already registered. Nothing to do.
                    return;
                }

                // If only one machine is registered, assign that machine to all bins
                if (_machinesToBinsMap.Count == 1)
                {
                    for (uint binNumber = 0; binNumber < NumberOfBins; binNumber++)
                    {
                        _binToMachineMap[binNumber] = id;
                        addedMachine.BinsAssignedTo.Add(binNumber);
                    }

                    _machinesToBinsMap[id] = addedMachine;
                    _machineAssignmentsSortedByBinCount.Add(addedMachine);

                    return;
                }

                // While the added machine's number of bins is less the the per machine bin count,
                // move bins from machines with greatest number of assigned bins
                while (addedMachine.BinsAssignedTo.Count < _machineAssignmentsSortedByBinCount.Max!.BinsAssignedTo.Count - 1)
                {
                    var machineWithMaxNumOfBins = _machineAssignmentsSortedByBinCount.Max!;
                    var binToReassign = machineWithMaxNumOfBins.BinsAssignedTo.First();

                    // Reassign bin from machine with most number of bin assigned to the added machine
                    MoveBinAssignment(oldMachine: machineWithMaxNumOfBins, newMachine: addedMachine, binToMove: binToReassign, machineToResort: machineWithMaxNumOfBins);
                }

                _machinesToBinsMap[id] = addedMachine;
                _machineAssignmentsSortedByBinCount.Add(addedMachine);
            }
        }

        /// <summary>
        /// Removes the machine id and reassigns its bins by progressively picking the machine with least number of bins assigned
        /// </summary>
        internal void RemoveLocation(MachineId id)
        {
            lock (_lockObject)
            {
                if (!_machinesToBinsMap.ContainsKey(id))
                {
                    return;
                }

                var assignments = _machinesToBinsMap[id];
                _machinesToBinsMap.Remove(id);
                _machineAssignmentsSortedByBinCount.Remove(assignments);

                foreach (var assignedBinNumber in assignments.BinsAssignedTo.ToList())
                {
                    var machineWithMinNumOfBins = _machineAssignmentsSortedByBinCount.Min;

                    // Reassign bin from removed machine to machine with least number of bin assigned
                    MoveBinAssignment(oldMachine: assignments, newMachine: machineWithMinNumOfBins, binToMove: assignedBinNumber, machineToResort: machineWithMinNumOfBins);
                }
            }
        }

        /// <summary>
        /// Moves a bin assignment from the given old machine to the new machine.
        /// </summary>
        /// <param name="oldMachine">the old machine from which the bin is removed</param>
        /// <param name="newMachine">the new machine to assign the bin to (may be null if no other machines are available)</param>
        /// <param name="binToMove">the bin moved between machines</param>
        /// <param name="machineToResort">a machine to resort into the _machineAssignmentsSortedByBinCount map (should be either <paramref name="oldMachine"/> or <paramref name="newMachine"/>)</param>
        private void MoveBinAssignment(MachineWithBinAssignments oldMachine, MachineWithBinAssignments? newMachine, uint binToMove, MachineWithBinAssignments? machineToResort)
        {
            if (machineToResort != null)
            {
                // Remove the machine to re-sort before any modifications
                _machineAssignmentsSortedByBinCount.Remove(machineToResort);
            }

            // Remove bin from old machine
            oldMachine.BinsAssignedTo.Remove(binToMove);

            // Add bin to new machine is specified
            if (newMachine != null)
            {
                newMachine.BinsAssignedTo.Add(binToMove);
                _binToMachineMap[binToMove] = newMachine.MachineId;
            }

            if (machineToResort != null)
            {
                // Remove the machine to resort after modifications
                _machineAssignmentsSortedByBinCount.Add(machineToResort);
            }

            // Accesses to the machine hash set (_machineSetBuffer) are not thread safe.
            foreach (var impactedBin in GetImpactedBins(binToMove, oldMachine.MachineId))
            {
                // Mark the old assignment as expired
                _expiredAssignments.GetOrAdd(impactedBin, bin => new Dictionary<MachineId, DateTime>())[oldMachine.MachineId] = _clock.UtcNow + _expiryTime;
            }

            // Invalidate bins
            _bins = null;
        }

        /// <summary>
        /// Get bins which should be expired in response to removing a machine from the given bin
        /// by looking back in from the bin's 'backup' chain to bins which would use this bin's machine
        /// assignment as a backup designated machine
        /// </summary>
        private IEnumerable<uint> GetImpactedBins(uint startBin, MachineId oldMachine)
        {
            try
            {
                var previousBins = InitializePreviousBinsMappings();

                var currentBin = startBin;
                while (true)
                {
                    var machine = _binToMachineMap[currentBin];
                    if (machine == null || machine == oldMachine)
                    {
                        yield return currentBin;

                        // Hit an empty bin, we must be removing the last machine.
                        // Or the bin is still assigned to the machine, so no need to mark it as expired
                        // Just stop iterating bins
                        break;
                    }

                    _machineSetBuffer.Add(machine.Value);
                    if (_machineSetBuffer.Count <= LocationsPerBin)
                    {
                        yield return currentBin;

                        // Set current bin, the previous bin in the bin's backup chain
                        currentBin = previousBins[currentBin];

                        if (currentBin == startBin)
                        {
                            // If currentBin is same as initial bin, just break because we have visited all bins
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                _machineSetBuffer.Clear();
            }
        }

        internal IEnumerable<uint> EnumeratePreviousBins(uint startBin)
        {
            uint bin = startBin;
            var previousBins = InitializePreviousBinsMappings();
            for (int i = 0; i < NumberOfBins; i++, bin = previousBins[bin])
            {
                yield return bin;
            }
        }

        private uint[] InitializePreviousBinsMappings()
        {
            lock (_lockObject)
            {
                var previousBins = _previousBins;
                if (previousBins == null)
                {
                    // Populate previous bins by querying next bin and adding back pointer
                    previousBins = new uint[NumberOfBins];
                    for (uint bin = 0; bin < NumberOfBins; bin++)
                    {
                        var nextBin = GetNextBin(bin);
                        previousBins[nextBin] = bin;
                    }

                    _previousBins = previousBins;
                }

                return previousBins;
            }
        }

        /// <summary>
        /// Gets a unique, pseudorandom next bin for the given bin based on the Linear congruential generator
        /// See https://en.wikipedia.org/wiki/Linear_congruential_generator
        /// 
        /// X_{n+1}= (a * X_{n} + c) mod m
        /// where m is the modulus (<see cref="NumberOfBins"/>)
        /// where a is the multiplier
        /// where c is the increment
        /// 
        /// Values are chosen such that each bin has a unique next bin (i.e. no two bins have the same next bin).
        /// This implies that every bin is the backup of some other bin. The following properties ensure this:
        /// See wikipedia article section 4.
        /// 1. m and c are relatively prime,
        /// 2. a-1 is divisible by all prime factors of m,
        /// 3. a-1 is divisible by 4 if m is divisible by 4.
        /// </summary>
        internal static uint GetNextBin(uint currentBin)
        {
            const uint M = NumberOfBins; // the modulus
            const uint A = 1664525; // the multiplier
            const uint C = 1013904223; // the increment

            return unchecked(((A * currentBin) + C) % M);
        }

        /// <summary>
        /// Gets the designated locations for a bin
        /// </summary>
        internal Result<MachineId[]> GetDesignatedLocations(uint binNumber, bool includeExpired)
        {
            lock (_lockObject)
            {
                var getBinsResult = GetBins();
                if (!getBinsResult.Succeeded)
                {
                    return new Result<MachineId[]>(getBinsResult);
                }

                var result = getBinsResult.Value[binNumber];

                if (includeExpired)
                {
                    PruneExpiries(force: false);

                    var expiredEntriesForBin = _expiredAssignments!.GetOrDefault(binNumber)?.Keys;
                    if (expiredEntriesForBin != null)
                    {
                        return result.Concat(expiredEntriesForBin).Distinct().ToArray();
                    }
                }

                return result;
            }
        }

        /// <nodoc />
        internal Dictionary<uint, Dictionary<MachineId, DateTime>> GetExpiredAssignments()
        {
            lock (_lockObject)
            {
                PruneExpiries(force: true);
                return _expiredAssignments;
            }
        }

        /// <summary>
        /// Prune expired machine assignments which have reached the age threshold (see <see cref="_expiryTime"/>) and can
        /// now be removed.
        /// </summary>
        private void PruneExpiries(bool force)
        {
            if (!force && _lastPruneExpiry.IsRecent(_clock.UtcNow, _expiryTime.Multiply(0.10)))
            {
                // Prune expiries if last prune was sufficiently long ago
                return;
            }

            lock (_lockObject)
            {
                _lastPruneExpiry = _clock.UtcNow;
                var binsToRemove = new List<uint>();
                foreach (var binWithExpiries in _expiredAssignments)
                {
                    var idsToRemove = new List<MachineId>();
                    foreach (var kvp in binWithExpiries.Value)
                    {
                        if (kvp.Value <= _clock.UtcNow)
                        {
                            idsToRemove.Add(kvp.Key);
                        }
                    }

                    foreach (var removal in idsToRemove)
                    {
                        binWithExpiries.Value.Remove(removal);
                    }

                    if (binWithExpiries.Value.Count == 0)
                    {
                        binsToRemove.Add(binWithExpiries.Key);
                    }
                }

                foreach (var removal in binsToRemove)
                {
                    _expiredAssignments.Remove(removal);
                }
            }
        }

        private class Comparer : IComparer<MachineWithBinAssignments>
        {
            public int Compare([AllowNull]MachineWithBinAssignments x, [AllowNull]MachineWithBinAssignments y)
            {
                Contract.Requires(x != null);
                Contract.Requires(y != null);
                return x.BinsAssignedTo.Count != y.BinsAssignedTo.Count
                    ? x.BinsAssignedTo.Count.CompareTo(y.BinsAssignedTo.Count)
                    : x.MachineId.Index.CompareTo(y.MachineId.Index);
            }
        }

        private class MachineWithBinAssignments
        {
            public MachineId MachineId { get; }
            public HashSet<uint> BinsAssignedTo { get; } = new HashSet<uint>();
            public MachineWithBinAssignments(MachineId machineId) => MachineId = machineId;
        }
    }
}
