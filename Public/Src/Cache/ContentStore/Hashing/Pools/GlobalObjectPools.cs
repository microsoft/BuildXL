// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// A global pool of objects that can be reused by different parts of the application to avoid creating redundant pools.
    /// </summary>
    public static class GlobalObjectPools
    {
        /// <summary>
        /// 2Mb byte array pool used by <see cref="VsoHashAlgorithm"/> and can be used by other parts of the system to avoid creating large buffers.
        /// </summary>
        public static ByteArrayPool TwoMbByteArrayPool { get; } = new ByteArrayPool(VsoHash.BlockSize);

        /// <summary>
        /// A size of File IO buffers.
        /// </summary>
        public static int FileIOBufferSize { get; set; } = 64 * 1024;

        /// <summary>
        /// An array pool used by file IO operations.
        /// </summary>
        public static ByteArrayPool FileIOBuffersArrayPool { get; } = new ByteArrayPool(FileIOBufferSize);
    }
}
