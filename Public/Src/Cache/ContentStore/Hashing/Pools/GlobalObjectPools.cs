// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// A global pool of objects that can be reused by different parts of the application to avoid creating redundant pools.
    /// </summary>
    public static class GlobalObjectPools
    {
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        static GlobalObjectPools()
        {
            Reset();
        }
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.

        /// <summary>
        /// 2Mb byte array pool used by <see cref="VsoHashAlgorithm"/> and can be used by other parts of the system to avoid creating large buffers.
        /// </summary>
        public static ByteArrayPool TwoMbByteArrayPool { get; private set; }

        /// <summary>
        /// An array pool used by file IO operations.
        /// </summary>
        public static ByteArrayPool FileIOBuffersArrayPool { get; private set; }

        /// <summary>
        /// Initialize
        /// </summary>
        public static void Reset()
        {
            TwoMbByteArrayPool = new ByteArrayPool(VsoHash.BlockSize);
            FileIOBuffersArrayPool = new ByteArrayPool(FileSystemConstants.FileIOBufferSize);
        }
    }
}
