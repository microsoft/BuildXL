// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Defines a hash partition encompassing hashes with prefixes from <paramref cref="StartValue"/>
    /// to <paramref cref="EndValue"/> inclusive.
    /// </summary>
    /// <param name="StartValue">The first included hash prefix of the range</param>
    /// <param name="EndValue">The last included hash prefix of the range</param>
    public record struct PartitionId(byte StartValue, byte EndValue)
    {
        private const int MaxPartitionCount = 256;

        /// <summary>
        /// Number of a hash prefixes included in the partition
        /// </summary>
        public int Width { get; } = (EndValue - StartValue) + 1;

        /// <summary>
        /// The index of the partition in the ordered list of partitions
        /// </summary>
        public int Index => StartValue / Width;

        /// <summary>
        /// The total number of partitions.
        /// </summary>
        public int PartitionCount => MaxPartitionCount / Width; 

        /// <summary>
        /// The prefix used for blobs representing this partition
        /// </summary>
        public string BlobPrefix => $"{PartitionCount}/{HexUtilities.BytesToHex(new[] { StartValue })}-{HexUtilities.BytesToHex(new[] { EndValue })}";

        /// <summary>
        /// Gets whether the partition contains the hash prefix (i.e. first byte of the hash)
        /// </summary>
        public bool Contains(byte hashPrefix)
        {
            return StartValue <= hashPrefix && hashPrefix <= EndValue;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[{StartValue}, {EndValue}]";
        }

        /// <summary>
        /// Gets an ordered array of partitions for the given count. NOTE: The input is coerced into a valid
        /// value (i.e. a power of 2 between 1 and 256 inclusive)
        /// </summary>
        public static ReadOnlyArray<PartitionId> GetPartitions(int partitionCount)
        {
            partitionCount = Math.Min(Math.Max(1, partitionCount), MaxPartitionCount);

            // Round to power of two
            var powerOfTwo = Bits.HighestBitSet((uint)partitionCount);
            partitionCount = (int)(powerOfTwo < partitionCount ? powerOfTwo << 1 : powerOfTwo);

            var perRangeCount = (MaxPartitionCount + partitionCount - 1) / partitionCount;

            return Enumerable.Range(0, partitionCount)
                .Select(i =>
                {
                    var start = i * perRangeCount;
                    var end = Math.Min(byte.MaxValue, (start + perRangeCount) - 1);
                    return new PartitionId((byte)start, (byte)end);
                })
                .ToArray();
        }
    }
}
