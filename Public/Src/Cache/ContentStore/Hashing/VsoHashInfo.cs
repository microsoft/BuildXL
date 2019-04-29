// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Hash info for VSO
    /// </summary>
    public class VsoHashInfo : TaggedHashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 33;

        /// <summary>
        ///     Initializes a new instance of the <see cref="VsoHashInfo"/> class.
        /// </summary>
        private VsoHashInfo()
            : base(HashType.Vso0, Length)
        {
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly VsoHashInfo Instance = new VsoHashInfo();

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher()
        {
            return new VsoContentHasher();
        }

        /// <summary>
        /// The VsoContentHasher is the content hasher used by the local cache service for drop app.
        /// </summary>
        private class VsoContentHasher : ContentHasher<VsoHashAlgorithm>
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="VsoContentHasher"/> class.
            /// </summary>
            public VsoContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
