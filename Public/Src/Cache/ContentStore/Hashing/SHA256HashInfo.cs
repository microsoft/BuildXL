// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Hash info for SHA256
    /// </summary>
    public class SHA256HashInfo : HashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 32;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SHA256HashInfo" /> class.
        /// </summary>
        private SHA256HashInfo()
            : base(HashType.SHA256, Length)
        {
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly SHA256HashInfo Instance = new SHA256HashInfo();

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher()
        {
            return new SHA256ContentHasher();
        }
        
        /// <summary>
        ///     SHA-256 Content hasher
        /// </summary>
        private sealed class SHA256ContentHasher : ContentHasher<SHA256Managed>
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="SHA256ContentHasher" /> class.
            /// </summary>
            public SHA256ContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
