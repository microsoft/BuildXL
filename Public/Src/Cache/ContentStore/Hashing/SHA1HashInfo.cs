// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Hash info for SHA1
    /// </summary>
    public class SHA1HashInfo : HashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 20;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SHA1HashInfo" /> class.
        /// </summary>
        private SHA1HashInfo()
            : base(HashType.SHA1, Length)
        {
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly SHA1HashInfo Instance = new SHA1HashInfo();

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher()
        {
            return new SHA1ContentHasher();
        }

        /// <summary>
        ///     SHA-1 Content hasher
        /// </summary>
        private sealed class SHA1ContentHasher : ContentHasher<SHA1Managed>
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="SHA1ContentHasher" /> class.
            /// </summary>
            public SHA1ContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
