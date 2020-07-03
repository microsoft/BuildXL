// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Hash info for VSO
    /// </summary>
    public class MurmurHashInfo : HashInfo
    {
        /// <summary>
        ///     Id used for deduping.
        /// </summary>
        public const byte MurmurAlgorithmId = 3;

        /// <summary>
        ///     Number of bytes in hash value.
        /// </summary>
        public const int Length = 33;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MurmurHashInfo"/> class.
        /// </summary>
        private MurmurHashInfo()
            : base(HashType.Murmur, Length)
        {
        }

        /// <summary>
        ///     A convenient ready-made instance.
        /// </summary>
        public static readonly MurmurHashInfo Instance = new MurmurHashInfo();

        /// <inheritdoc />
        public override IContentHasher CreateContentHasher()
        {
            return new MurmurContentHasher();
        }

        /// <summary>
        /// The VsoContentHasher is the content hasher used by the local cache service for drop app.
        /// </summary>
        private class MurmurContentHasher : ContentHasher<Murmur3HashAlgorithm>
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="MurmurContentHasher"/> class.
            /// </summary>
            public MurmurContentHasher()
                : base(Instance)
            {
            }
        }
    }
}
