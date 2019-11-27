// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class BinManager
    {
        private const int MaxBins = ushort.MaxValue + 1;

        private readonly HashSet<MachineLocation>[] _locationEntries;
        private readonly int _entriesPerLocation;
        private readonly int _hashSeeds;
        private readonly int _locationsPerBin;
        private readonly object _lockObject = new object();

        private ConcurrentDictionary<MachineLocation, bool> machines = new ConcurrentDictionary<MachineLocation, bool>();

        private MachineLocation[][] _bins;

        /// <nodoc />
        public BinManager(int locationsPerBin, int entriesPerLocation, int numberOfBins)
        {
            Contract.Assert(entriesPerLocation <= byte.MaxValue);
            Contract.Assert(IsNumberOfBinsValid(numberOfBins), $"{nameof(numberOfBins)} should be in range [1, {MaxBins}] and be a power of 2.");
            _locationsPerBin = locationsPerBin;
            _entriesPerLocation = entriesPerLocation;
            _locationEntries = new HashSet<MachineLocation>[numberOfBins];
        }

        private static bool IsNumberOfBinsValid(int amount)
        {
            return amount > 0 && amount <= MaxBins &&
                ((amount & (amount - 1)) == 0); // Is power of 2.
        }

        /// <nodoc />
        public void AddLocation(MachineLocation location) => ProcessLocation(location, isAdd: true);

        /// <nodoc />
        public void RemoveLocation(MachineLocation location) => ProcessLocation(location, isAdd: false);


        private void ProcessLocation(MachineLocation location, bool isAdd)
        {
            if (isAdd)
            {
                machines[location] = true;
            }
            else
            {
                machines.TryRemove(location, out _);
            }

            var hasher = ContentHashers.Get(HashType.MD5);
            for (var i = 0; i < _entriesPerLocation; i++)
            {
                var hash = hasher.GetContentHash(Encoding.UTF8.GetBytes(location.Path + i));//  HashCodeHelper.GetOrdinalHashCode(location.Path + i);
                var index = unchecked((uint)BitConverter.ToUInt32(hash.ToByteArray(), 1)) % _locationEntries.Length;
                var entrySet = _locationEntries[index];
                if (entrySet == null)
                {
                    entrySet = new HashSet<MachineLocation>();
                    _locationEntries[index] = entrySet;
                }

                if (isAdd)
                {
                    entrySet.Add(location);
                }
                else
                {
                    entrySet.Remove(location);
                }
            }

            // Invalidate current configuration.
            lock (_lockObject)
            {
                _bins = null;
            }
        }

        /// <nodoc />
        public MachineLocation[] GetLocations(ContentHash hash)
        {
            lock (_lockObject)
            {
                _bins ??= ComputeBins();
                var index = hash[0] | hash[1] << 8;
                return _bins[index % _locationEntries.Length];
            }
        }

        /// <nodoc />
        public IEnumerable<IReadOnlyList<MachineLocation>> GetBins()
        {
            lock (_lockObject)
            {
                _bins ??= ComputeBins();
                return _bins;
            }
        }

        /// <summary>
        ///     Computes the designated locations for each of the bins.
        ///     The way this is done is by getting the next x machines (clockwise in a "circular array"),
        /// avoiding repetitions.
        ///
        /// Steps:
        /// 1: Calculate first bin, just enumerating until we find x different locations
        /// 2: If first bin has less than x locations, just assign that for all bins, as there are not x distinct machines
        /// 3: Calculate the rest of the bins starting from the end. If the bin that we're calculating has a location assigned to it,
        ///     replace the last bin's last location with the current location. If last bin already contains the current location, just move
        ///     that location to the front of the bin.
        /// </summary>
        private MachineLocation[][] ComputeBins()
        {
            var bins = new MachineLocation[_locationEntries.Length][];

            // Calculate first bin.
            var first = new List<MachineLocation>();

            ConcurrentDictionary<MachineLocation, int> locationUsage = new ConcurrentDictionary<MachineLocation, int>();

            void computeFirst()
            {
                for (var entryIndex = 0; entryIndex < _locationEntries.Length; entryIndex++)
                {
                    foreach (var location in GetLocationsAt(entryIndex))
                    {
                        if (!first.Contains(location))
                        {
                            first.Add(location);
                            if (first.Count == _locationsPerBin)
                            {
                                return;
                            }
                        }
                    }
                }
            }

            computeFirst();

            // If not enough locations, all bins will be equal.
            if (first.Count < _locationsPerBin)
            {
                var entry = first.ToArray();
                for (var i = 0; i < bins.Length; i++)
                {
                    bins[i] = entry;
                }

                return bins;
            }

            var maxMachineUsage = 1 + (_locationEntries.Length * _locationsPerBin) / machines.Count;

            // Calculate the rest of the bins starting from the back
            bins[0] = first.ToArray();
            var bin = bins[0];
            for (var entryIndex = _locationEntries.Length - 1; entryIndex > 0; entryIndex--)
            {
                foreach (var location in GetLocationsAt(entryIndex))
                {
                    if (locationUsage.AddOrUpdate(location, 1, (k, v) => v + 1) < maxMachineUsage)
                    {
                        bin = insertFirst(bin, location);
                    }
                }

                bins[entryIndex] = bin;
            }

            return bins;

            static MachineLocation[] insertFirst(MachineLocation[] source, MachineLocation value)
            {
                var target = new MachineLocation[source.Length];
                target[0] = value;

                int targetIndex = 1;
                for (int sourceIndex = 0; sourceIndex < source.Length && targetIndex < target.Length; sourceIndex++)
                {
                    if (source[sourceIndex].Equals(value))
                    {
                        continue;
                    }

                    target[targetIndex] = source[sourceIndex];
                    targetIndex++;
                }

                return target;
            }
        }

        private IEnumerable<MachineLocation> GetLocationsAt(int entryIndex)
        {
            var entry = _locationEntries[entryIndex];
            if (entry != null)
            {
                return entry.OrderBy(l => l.Path);
            }

            return Enumerable.Empty<MachineLocation>();
        }

        public override string ToString()
        {
            var locationMappings = string.Join(Environment.NewLine, _locationEntries.Select(e => e == null ? string.Empty : string.Join(", ", e)));

            var bins = GetBins();
            var binText = string.Join(Environment.NewLine, bins.SelectMany((bin, index) => bin.Select(l => (l, index))).GroupBy(e => e.l).OrderBy(g => g.Count()).Select(g => $"{g.Key}: ({g.Count()}) [{string.Join(", ", g.Select(t => t.index))}] "));

            var collisions = _locationEntries.Where(e => e?.Count > 1).Sum(e => e.Count - 1);
            return string.Join(Environment.NewLine, $"Collisions: {collisions}", locationMappings, binText);
        }
    }
}
