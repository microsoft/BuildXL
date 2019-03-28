// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Simple description of a hash.
    /// </summary>
    public abstract class HashInfo
    {
        private readonly object _obj = new object();
        private ContentHash _emptyHash = default;

        /// <summary>
        ///     Initializes a new instance of the <see cref="HashInfo" /> class.
        /// </summary>
        protected HashInfo(HashType hashType, int length)
        {
            HashType = hashType;
            ByteLength = length;
        }

        /// <summary>
        ///     Create a content hasher of this type.
        /// </summary>
        public abstract IContentHasher CreateContentHasher();

        /// <summary>
        ///     The content hash for empty content.
        /// </summary>
        public ContentHash EmptyHash
        {
            get
            {
                if (_emptyHash == default)
                {
                    lock (_obj)
                    {
                        if (_emptyHash == default)
                        {
                            _emptyHash = CreateContentHasher().GetContentHash(CollectionUtilities.EmptyArray<byte>());
                        }
                    }
                }

                return _emptyHash;
            }
        }

        /// <summary>
        ///     Gets hash algorithm.
        /// </summary>
        public HashType HashType { get; }

        /// <summary>
        ///     Gets name of the hash type.
        /// </summary>
        public string Name => HashType.ToString();

        /// <summary>
        ///     Gets hash length in bytes.
        /// </summary>
        public int ByteLength { get; }

        /// <summary>
        ///     Gets number of characters in the string representation.
        /// </summary>
        public int StringLength => ByteLength * 2;
    }
}
