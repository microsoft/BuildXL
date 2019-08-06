// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Hash info for MD5
    /// </summary>
    public class MD5HashInfo : HashInfo
    {
        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 16;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MD5HashInfo" /> class.
        /// </summary>
        private MD5HashInfo()
            : base(HashType.MD5, Length)
        {
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly MD5HashInfo Instance = new MD5HashInfo();

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher()
        {
            return new MD5ContentHasher();
        }

        /// <summary>
        ///     MD5 Content hasher
        /// </summary>
#if NET_FRAMEWORK
        private sealed class MD5ContentHasher : ContentHasher<MD5Cng>
#else
        private sealed class MD5ContentHasher : ContentHasher<MD5CryptoServiceProvider>
#endif
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="MD5ContentHasher" /> class.
            /// </summary>
            public MD5ContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
