// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Node of 1024K avg size chunks or a single 1024K chunk by itself.
    /// </summary>
    public sealed class Dedup1024KHashAlgorithm : DedupNodeOrChunkHashAlgorithm
    {
        private const HashType TargetHashType = HashType.Dedup1024K;

        /// <summary>
        /// Initializes a new instance of the <see cref="Dedup1024KHashAlgorithm"/> class.
        /// </summary>
        public Dedup1024KHashAlgorithm()
            : this(Chunker.Create(TargetHashType.GetChunkerConfiguration()))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Dedup1024KHashAlgorithm"/> class.
        /// </summary>
        public Dedup1024KHashAlgorithm(IChunker chunker)
            : base(chunker)
        {
            HashSizeValue = 8 * DedupSingleChunkHashInfo.Length;
            int expectedAvgChunkSize = TargetHashType.GetAvgChunkSize();
            Contract.Assert(chunker.Configuration.AvgChunkSize == expectedAvgChunkSize, $"Invalid average chunk size (in bytes) specified: {chunker.Configuration.AvgChunkSize} expected: {expectedAvgChunkSize}");
        }

        /// <nodoc />
        public override HashType DedupHashType => TargetHashType;
    }
}
