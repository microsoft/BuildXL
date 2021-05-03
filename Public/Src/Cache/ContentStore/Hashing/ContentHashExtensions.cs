// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     A set of extension methods for <see cref="ContentHash"/> and <see cref="ShortHash"/> types.
    /// </summary>
    public static class ContentHashExtensions
    {
        internal static readonly ByteArrayPool ContentHashBytesArrayPool = new ByteArrayPool(ContentHash.SerializedLength);
        internal static readonly ByteArrayPool ShortHashBytesArrayPool = new ByteArrayPool(ShortHash.SerializedLength);

        /// <summary>
        /// Gets the serialized form of <paramref name="contentHash"/> but returns a handled to a pooled byte array instead of allocating a new one.
        /// </summary>
        public static ByteArrayPool.PoolHandle ToPooledByteArray(this in ContentHash contentHash)
        {
            var handle = ContentHashBytesArrayPool.Get();
            contentHash.Serialize(handle.Value);
            return handle;
        }

        /// <summary>
        /// Gets the serialized form of <paramref name="contentHash"/> but returns a handled to a pooled byte array instead of allocating a new one.
        /// </summary>
        public static ByteArrayPool.PoolHandle ToPooledByteArray(this in ShortHash contentHash)
        {
            var handle = ShortHashBytesArrayPool.Get();
            contentHash.Serialize(handle.Value);
            return handle;
        }
    }
}
