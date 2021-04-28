// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// An object pool for re-using byte arrays of a fixed size.
    /// </summary>
    public sealed class ByteArrayPool : Pool<byte[]>
    {
        private static readonly Action<byte[]> Reset = b =>
                                                       {
#if DEBUG
                                                           b[0] = 0xcc;
                                                           b[b.Length - 1] = 0xcc;
#endif
                                                       };

        private static byte[] CreateNew(int bufferSize)
        {
            var bytes = new byte[bufferSize];
            Reset(bytes);
            return bytes;
        }

        /// <nodoc />
        public ByteArrayPool(int bufferSize)
            : base(() => CreateNew(bufferSize), Reset)
        {
            ArraySize = bufferSize;
        }

        /// <inheritdoc />
        public override PoolHandle Get()
        {
            var bytes = base.Get();
#if DEBUG
            Contract.Assert(bytes.Value[0] == 0xcc);
            Contract.Assert(bytes.Value[bytes.Value.Length - 1] == 0xcc);
#endif
            return bytes;
        }

        /// <summary>
        /// Gets the size of the array created by the pool.
        /// </summary>
        public int ArraySize { get; }
    }
}
