// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Security.Cryptography;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// The algorithm to compute the hash value of any given content (in form of byte array).
    /// </summary>
    /// <remarks>
    /// The implementation delegates the computation to <see cref="VsoHash"/>.
    /// </remarks>
    public class Murmur3HashAlgorithm : HashAlgorithm
    {
        private const int AlgorithmSeed = 0;
        private const byte AlgorithmId = 1;
        private const int PagesPerBlock = 1;
        private const int PageSize = 64 * 1024;
        private const int BlockSize = PagesPerBlock * PageSize; // 1 * 64 * 1024 = 64KB
        private static readonly byte[] EmptyHashBytes = HexUtilities.HexToBytes("1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00");
        private readonly byte[] _buffer = new byte[BlockSize];
        private readonly byte[] _blockHashes = new byte[BlockSize];
        private readonly byte[] finalHash = new byte[33];
        private uint _currentBlockOffset;
        private uint _currentOffset;

        /// <inheritdoc />
        public override void Initialize()
        {
            _currentOffset = 0;
            _currentBlockOffset = 0;
            HashSizeValue = 33 * 8;
        }

        /// <inheritdoc />
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            unsafe
            {
                fixed (byte* arrayPtr = array)
                {
                    while (cbSize > 0)
                    {
                        int bytesToCopy = (int)Math.Min(cbSize, BlockSize - _currentOffset);
                        if (bytesToCopy < BlockSize)
                        {
                            Buffer.BlockCopy(array, ibStart, _buffer, (int)_currentOffset, bytesToCopy);
                            _currentOffset += (uint)bytesToCopy;
                        }
                        else
                        {
                            AddBlockHash(MurmurHash3.Create(arrayPtr + ibStart, BlockSize, AlgorithmSeed));
                            _currentOffset = 0;
                        }

                        ibStart += bytesToCopy;
                        cbSize -= bytesToCopy;
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override byte[] HashFinal()
        {
            // Flush out buffer
            if (_currentOffset != 0)
            {
                unsafe
                {
                    fixed (byte* arrayPtr = _buffer)
                    {
                        AddBlockHash(MurmurHash3.Create(arrayPtr, _currentOffset, AlgorithmSeed));
                    }
                }
            }

            // if there are no blocks add an empty block
            if (_currentBlockOffset == 0)
            {
                return EmptyHashBytes;
            }
            else if (_currentBlockOffset > 8)
            {
                CombineBlockHashes();
            }

            for (int i = 0; i < 8; i++)
            {
                finalHash[i] = _blockHashes[i];
            }

            return finalHash;
        }

        private void AddBlockHash(MurmurHash3 hash)
        {
            hash.GetHashBytes(_blockHashes, _currentBlockOffset);
            _currentBlockOffset += 8;
            if (_currentBlockOffset == BlockSize)
            {
                CombineBlockHashes();
            }
        }

        private void CombineBlockHashes()
        {
            unsafe
            {
                fixed (byte* arrayPtr = _blockHashes)
                {
                    MurmurHash3 combinedHash = MurmurHash3.Create(arrayPtr, _currentBlockOffset, AlgorithmSeed);
                    combinedHash.GetHashBytes(_blockHashes, 0);
                    _currentBlockOffset = 8;
                }
            }
        }
    }
}
