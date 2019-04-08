// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Dedup Node or Chunk hash info.
    /// </summary>
    public class DedupNodeOrChunkHashInfo : TaggedHashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 33;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DedupNodeOrChunkHashInfo" /> class.
        /// </summary>
        private DedupNodeOrChunkHashInfo()
            : base(HashType.DedupNodeOrChunk, Length)
        {
        }

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher()
        {
            return new DedupNodeOrChunkContentHasher();
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly DedupNodeOrChunkHashInfo Instance = new DedupNodeOrChunkHashInfo();

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