// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Structure that holds the configuration for the chunker
    /// </summary>
    public struct ChunkerConfiguration
    {
        /// <summary>
        /// This is the default that was used before the chunker was configurable.
        /// </summary>
        public static readonly ChunkerConfiguration Default;

        /// <summary>
        /// Smallest chunker the chunker will split.  Small files will be smaller than this.
        /// </summary>
        public readonly int MinChunkSize;

        /// <summary>
        /// The average size of a chunk when splitting large files.
        /// </summary>
        public readonly int AvgChunkSize;

        /// <summary>
        /// The maximum size of a chunk - regardless of input size.
        /// </summary>
        public readonly int MaxChunkSize;

        static ChunkerConfiguration()
        {
            int avgChunkSize;
            if (!int.TryParse(Environment.GetEnvironmentVariable("BUILDXL_TEST_AVG_CHUNK_SIZE"), out avgChunkSize))
            {
                avgChunkSize = 64 * 1024;
            }

            int minChunkSize;
            if (!int.TryParse(Environment.GetEnvironmentVariable("BUILDXL_TEST_MIN_CHUNK_SIZE"), out minChunkSize))
            {
                minChunkSize = avgChunkSize / 2;
            }

            int maxChunkSize;
            if (!int.TryParse(Environment.GetEnvironmentVariable("BUILDXL_TEST_MAX_CHUNK_SIZE"), out maxChunkSize))
            {
                maxChunkSize = avgChunkSize * 2;
            }

            Default = new ChunkerConfiguration(minChunkSize, avgChunkSize, maxChunkSize);
        }

        /// <summary>
        /// Create a chunk configuration based on the given average chunk size.
        /// See: https://docs.microsoft.com/en-us/windows-server/storage/data-deduplication/overview
        /// </summary>
        public ChunkerConfiguration(int avgChunkSize) : this(avgChunkSize / 2, avgChunkSize, 2 * avgChunkSize) { }

        private ChunkerConfiguration(int minChunkSize, int avgChunkSize, int maxChunkSize)
        {
            Contract.Assert(minChunkSize >= 4096);

            // avg size must be power-of-two. Not sure about min and max but being conservative here.
            Contract.Assert((minChunkSize & (minChunkSize - 1)) == 0);
            Contract.Assert((avgChunkSize & (avgChunkSize - 1)) == 0);
            Contract.Assert((maxChunkSize & (maxChunkSize - 1)) == 0);

            Contract.Assert(minChunkSize <= avgChunkSize);
            Contract.Assert(avgChunkSize <= maxChunkSize);

            MinChunkSize = minChunkSize;
            AvgChunkSize = avgChunkSize;
            MaxChunkSize = maxChunkSize;
        }

        /// <summary>
        /// To get deterministic chunks out of the chunker, only give it buffers of at least 256KB, unless EOF.
        /// Cosmin Rusu recommends larger buffers for performance, so going with 1MB.
        /// </summary>
        private const int OriginalPushBufferSize = 1024 * 1024;

        /// <summary>
        /// Consumers should push buffers of at least this size when possible to prevent extra copying.
        /// </summary>
        public int MinPushBufferSize => Math.Max(OriginalPushBufferSize, 2 * MaxChunkSize);
    }
}
