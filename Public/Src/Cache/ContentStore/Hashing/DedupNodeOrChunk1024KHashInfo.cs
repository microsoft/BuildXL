// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Dedup Node or Chunk hash info for 1024K sized chunk(s).
    /// </summary>
    public class DedupNodeOrChunk1024KHashInfo : TaggedHashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 33;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DedupNodeOrChunk1024KHashInfo" /> class.
        /// </summary>
        private DedupNodeOrChunk1024KHashInfo()
            : base(HashType.Dedup1024K, Length)
        {
        }

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher() => new DedupNodeOrChunk1024KContentHasher();

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly DedupNodeOrChunk1024KHashInfo Instance = new DedupNodeOrChunk1024KHashInfo();

        /// <summary>
        /// Deduplication node hash based on the chunk hash.
        /// </summary>
        private sealed class DedupNodeOrChunk1024KContentHasher : ContentHasher<DedupNodeOrChunkHashAlgorithm>
        {
            public DedupNodeOrChunk1024KContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
