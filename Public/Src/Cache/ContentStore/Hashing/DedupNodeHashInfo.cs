// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// VSTS chunk-level deduplication file node.
    /// </summary>
    public class DedupNodeHashInfo : HashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        private const int Length = 32;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DedupNodeHashInfo" /> class.
        /// </summary>
        private DedupNodeHashInfo()
            : base(HashType.DedupNode, Length)
        {
        }

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher()
        {
            return new DedupNodeContentHasher();
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly DedupNodeHashInfo Instance = new DedupNodeHashInfo();

        /// <summary>
        /// Deduplication node hash based on the chunk hash.
        /// </summary>
        private sealed class DedupNodeContentHasher : ContentHasher<DedupNodeHashAlgorithm>
        {
            public DedupNodeContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
