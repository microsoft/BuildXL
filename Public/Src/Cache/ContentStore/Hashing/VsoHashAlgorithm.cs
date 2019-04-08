// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// The algorithm to compute the hash value of any given content (in form of byte array).
    /// </summary>
    /// <remarks>
    /// The implementation delegates the computation to <see cref="VsoHash"/>.
    /// </remarks>
    public class VsoHashAlgorithm : HashAlgorithm
    {
        private readonly byte[] _buffer = new byte[VsoHash.BlockSize];
        private readonly List<BlobBlockHash> _blockHashes = new List<BlobBlockHash>();
        private int _currentOffset;

        /// <inheritdoc />
        public override void Initialize()
        {
            _currentOffset = 0;
            _blockHashes.Clear();
        }

        /// <inheritdoc />
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
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
            var rollingId = new VsoHash.RollingBlobIdentifier();

            // Flush out buffer
            if (_currentOffset != 0)
            {
                _blockHashes.Add(VsoHash.HashBlock(_buffer, _currentOffset));
            }

            // if there are no blocks add an empty block
            if (_blockHashes.Count == 0)
            {
                _blockHashes.Add(VsoHash.HashBlock(new byte[] { }, 0));
            }

            for (int i = 0; i < _blockHashes.Count - 1; i++)
            {
                rollingId.Update(_blockHashes[i]);
            }

            return rollingId.Finalize(_blockHashes[_blockHashes.Count - 1]).Bytes;
        }
    }
}
