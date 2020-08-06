// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Dedup Node or Chunk hash info.
    /// </summary>
    public class DedupNode64KHashInfo : TaggedHashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 33;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DedupNode64KHashInfo" /> class.
        /// </summary>
        private DedupNode64KHashInfo()
            : base(HashType.Dedup64K, Length)
        {
        }

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher() => new DedupNodeOrChunkContentHasher();

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly DedupNode64KHashInfo Instance = new DedupNode64KHashInfo();

        /// <summary>
        /// Deduplication node hash based on the chunk hash.
        /// </summary>
        private sealed class DedupNodeOrChunkContentHasher : ContentHasher<DedupNodeOrChunkHashAlgorithm>
        {
            public DedupNodeOrChunkContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
