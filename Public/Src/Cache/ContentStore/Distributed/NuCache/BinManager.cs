// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class BinManager
    {
        private const int MaxBins = 64 * 1024;

        private readonly MachineLocation[] _bins;
        private readonly int _locationsPerBin;
        private readonly int _entriesPerMachine;

        private MachineLocation[][] _machinesForBins;

        private readonly IContentHasher _contentHasher = ContentHashers.Get(HashType.MD5);

        public BinManager(int machinesPerBin, int entriesPerMachine = 4, int amountOfBins = MaxBins)
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

        public void AddLocation(MachineLocation location) => ProcessLocation(location, valueToSet: location);

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

        public MachineLocation[] GetLocations(ContentHash hash)
        {
            _machinesForBins ??= ComputeBins();
            var index = BitConverter.ToUInt16(hash.ToByteArray(), 1);
            return _machinesForBins[index % _bins.Length];
        }

        private MachineLocation[][] ComputeBins()
        {
            var result = new MachineLocation[_bins.Length][];
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

            if (first.Count < _locationsPerBin)
            {
                var entry = first.ToArray();
                for (var i = 0; i < _bins.Length; i++)
                {
                    result[i] = entry;
                }

                return result;
            }

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

        public override string ToString()
        {
            _machinesForBins ??= ComputeBins();

            var bins = string.Join(", ", _bins.Select(bin => bin.Path));
            return bins + "\n" + string.Join("\n", _machinesForBins.Select(machines => string.Join(",", machines.Select(machine => machine.Path))));
        }
    }
}
