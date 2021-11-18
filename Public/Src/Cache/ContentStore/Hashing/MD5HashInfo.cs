// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
#pragma warning disable SYSLIB0021 // Type or member is obsolete. Temporarily suppressing the warning for .net 6. Work item: 1885580
        private sealed class MD5ContentHasher : ContentHasher<MD5CryptoServiceProvider>
#pragma warning restore SYSLIB0021 // Type or member is obsolete
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
