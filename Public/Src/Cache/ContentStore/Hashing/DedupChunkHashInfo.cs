// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     NTFS Deduplication chunk hash: Hash info for SHA512 truncated to the first 256 bits
    /// </summary>
    public class DedupSingleChunkHashInfo : HashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 32;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DedupSingleChunkHashInfo" /> class.
        /// </summary>
        private DedupSingleChunkHashInfo()
            : base(HashType.DedupSingleChunk, Length)
        {
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly DedupSingleChunkHashInfo Instance = new DedupSingleChunkHashInfo();

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher()
        {
            return new DedupChunkContentHasher();
        }

        /// <summary>
        ///     NTFS Deduplication chunk hash: SHA-512 (truncated to 256 bits) Content hasher
        /// </summary>
        private sealed class DedupChunkContentHasher : ContentHasher<DedupChunkHashAlgorithm>
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="DedupChunkContentHasher" /> class.
            /// </summary>
            public DedupChunkContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
