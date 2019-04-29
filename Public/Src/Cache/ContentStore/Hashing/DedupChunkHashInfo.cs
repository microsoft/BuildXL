// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     NTFS Deduplication chunk hash: Hash info for SHA512 truncated to the first 256 bits
    /// </summary>
    public class DedupChunkHashInfo : HashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 32;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DedupChunkHashInfo" /> class.
        /// </summary>
        private DedupChunkHashInfo()
            : base(HashType.DedupChunk, Length)
        {
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly DedupChunkHashInfo Instance = new DedupChunkHashInfo();

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
