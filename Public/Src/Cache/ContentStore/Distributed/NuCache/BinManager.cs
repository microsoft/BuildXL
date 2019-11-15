// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    internal class BinManager
    {
        private const int MaxBins = 64 * 1024;

        private readonly MachineLocation[] _bins;
        private readonly int _locationsPerBin;
        private readonly int _entriesPerMachine;

        private MachineLocation[][] _machinesForBins;

        private readonly IContentHasher _contentHasher = ContentHashers.Get(HashType.MD5);

        /// <nodoc />
        public BinManager(int machinesPerBin, int entriesPerMachine, int amountOfBins)
        {
            Contract.Assert(entriesPerMachine <= byte.MaxValue);
            Contract.Assert(AmountOfBinsIsValid(amountOfBins), $"{nameof(amountOfBins)} should be in range [1, {MaxBins}] and be a power of 2.");
            _locationsPerBin = machinesPerBin;
            _entriesPerMachine = entriesPerMachine;
            _bins = new MachineLocation[amountOfBins];
        }

        private static bool AmountOfBinsIsValid(int amount)
        {
            return amount > 0 && amount <= MaxBins &&
                ((amount & (amount - 1)) == 0); // Is power of 2.
        }

        /// <nodoc />
        public void AddLocation(MachineLocation location) => ProcessLocation(location, valueToSet: location);

        /// <nodoc />
        public void RemoveLocation(MachineLocation location) => ProcessLocation(location, valueToSet: default);

        private void ProcessLocation(MachineLocation location, MachineLocation valueToSet)
        {
            var keyBytes = new byte[location.Data.Length + 1];
            location.Data.CopyTo(keyBytes, 0);
            for (byte entry = 0; entry < _entriesPerMachine; entry++)
            {
                keyBytes[keyBytes.Length - 1] = entry;
                var key = _contentHasher.GetContentHash(keyBytes);
                var index = BitConverter.ToUInt16(key.ToByteArray(), 1);
                _bins[index % _bins.Length] = valueToSet;
            }

            // Invalidate current configuration.
            _machinesForBins = null;
        }

        /// <nodoc />
        public MachineLocation[] GetLocations(ContentHash hash)
        {
            _machinesForBins ??= ComputeBins();
            var index = BitConverter.ToUInt16(hash.ToByteArray(), 1);
            return _machinesForBins[index % _bins.Length];
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
        ///     replace the last bin's last entry with the current one. If last bin already contains the current location, just move
        ///     that location to the front of the bin.
        /// </summary>
        private MachineLocation[][] ComputeBins()
        {
            var result = new MachineLocation[_bins.Length][];

            // Calculate first set of bins.
            var first = new List<MachineLocation>();
            foreach (var bin in _bins)
            {
                if (!bin.Equals(default) && !first.Contains(bin))
                {
                    first.Add(bin);
                    if (first.Count == _locationsPerBin)
                    {
                        break;
                    }
                }
            }

            // If not enough locations, all bins will be equal.
            if (first.Count < _locationsPerBin)
            {
                var entry = first.ToArray();
                for (var i = 0; i < _bins.Length; i++)
                {
                    result[i] = entry;
                }

                return result;
            }

            // Calculate the rest of the bins starting from the back
            result[0] = first.ToArray();
            var prev = result[0];
            for (var bin = _bins.Length - 1; bin > 0; bin--)
            {
                if (!_bins[bin].Equals(default))
                {
                    prev = insertFirst(prev, _bins[bin]);
                }

                result[bin] = prev;
            }

            return result;

            static MachineLocation[] insertFirst(MachineLocation[] from, MachineLocation withValue)
            {
                var result = new MachineLocation[from.Length];
                result[0] = withValue;
                var repeated = false;
                for (var i = 1; i < result.Length; i++)
                {
                    if (!repeated && withValue.Equals(from[i - 1]))
                    {
                        repeated = true;
                    }

                    result[i] = from[i - (repeated ? 0 : 1)];
                }

                return result;
            }
        }
    }
}
