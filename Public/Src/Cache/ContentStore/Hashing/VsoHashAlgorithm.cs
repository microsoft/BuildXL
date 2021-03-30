// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// The algorithm to compute the hash value of any given content (in form of byte array).
    /// </summary>
    /// <remarks>
    /// The implementation delegates the computation to <see cref="VsoHash"/>.
    /// </remarks>
    public class VsoHashAlgorithm : HashAlgorithm, IHashAlgorithmWithCleanup
    {
        private static readonly byte[] EmptyArray = new byte[0]; // Not using Array.Empty<byte>() because this assembly can target lower framework versions.
        private Pool<byte[]>.PoolHandle _bufferHandle;
        private byte[]? _buffer;
        private readonly List<BlobBlockHash> _blockHashes = new List<BlobBlockHash>();
        private int _currentOffset;

        /// <inheritdoc />
        public override void Initialize()
        {
            _currentOffset = 0;
            _blockHashes.Clear();

            if (_buffer == null)
            {
                // Initialize can be called more than once per instance, once during construction, and another time by HasherToken.
                // This is not happening concurrently, so no need for any synchronization.
                _bufferHandle = GlobalObjectPools.TwoMbByteArrayPool.Get();
                _buffer = _bufferHandle.Value;
            }
        }

        void IHashAlgorithmWithCleanup.Cleanup()
        {
            _buffer = null;
            _bufferHandle.Dispose();
        }

        /// <inheritdoc />
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            Contract.AssertDebug(_buffer != null);
            
            while (cbSize > 0)
            {
                if (_currentOffset == _buffer.Length)
                {
                    _blockHashes.Add(VsoHash.HashBlock(_buffer, _buffer.Length));
                    _currentOffset = 0;
                }

                int bytesToCopy = Math.Min(cbSize, _buffer.Length - _currentOffset);
                Buffer.BlockCopy(array, ibStart, _buffer, _currentOffset, bytesToCopy);
                _currentOffset += bytesToCopy;
                ibStart += bytesToCopy;
                cbSize -= bytesToCopy;
            }
        }

        /// <inheritdoc />
        protected override byte[] HashFinal()
        {
            Contract.AssertDebug(_buffer != null);
            
            var rollingId = new VsoHash.RollingBlobIdentifier();

            // Flush out buffer
            if (_currentOffset != 0)
            {
                _blockHashes.Add(VsoHash.HashBlock(_buffer, _currentOffset));
            }

            // if there are no blocks add an empty block
            if (_blockHashes.Count == 0)
            {
                _blockHashes.Add(VsoHash.HashBlock(EmptyArray, 0));
            }

            for (int i = 0; i < _blockHashes.Count - 1; i++)
            {
                rollingId.Update(_blockHashes[i]);
            }

            return rollingId.Finalize(_blockHashes[_blockHashes.Count - 1]).Bytes;
        }
    }
}
