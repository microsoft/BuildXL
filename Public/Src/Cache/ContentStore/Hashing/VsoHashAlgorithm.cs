// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    public class VsoHashAlgorithm : HashAlgorithm, IHashAlgorithmWithCleanup, IHashAlgorithmInputLength
    {
        private static readonly byte[] EmptyArray = new byte[0]; // Not using Array.Empty<byte>() because this assembly can target lower framework versions.
        private Pool<byte[]>.PoolHandle _bufferHandle;

        // The 2Mb buffer used for block hashing (obtained from the pool on demand).
        // It is an important optimization for .net core case, because in some cases
        // we can skip copying the data into the buffer altogether.
        private byte[]? _buffer;
        private readonly List<BlobBlockHash> _blockHashes = new List<BlobBlockHash>();
        private VsoHash.RollingBlobIdentifier _rollingId = new VsoHash.RollingBlobIdentifier();
        private int _currentOffset;
        private long _expectedInputLength = -1;
        private byte[]? _finalized;

#if NET_COREAPP
        private readonly byte[] _hashBuffer = new byte[VsoHash.HashSize];
#endif

        /// <inheritdoc />
        public override void Initialize()
        {
            _currentOffset = 0;
            _rollingId = new VsoHash.RollingBlobIdentifier();
            _blockHashes.Clear();
            _finalized = null;
        }

        private byte[] Buffer
        {
            get
            {
                // Using lazy initialization for the buffer, because in some cases the buffer is not needed at all
                // (for instance, when span-based hashing is used for content with a known size).
                if (_buffer is null)
                {
                    // Initialize can be called more than once per instance, once during construction, and another time by HasherToken.
                    // This is not happening concurrently, so no need for any synchronization.
                    _bufferHandle = GlobalObjectPools.TwoMbByteArrayPool.Get();
                    _buffer = _bufferHandle.Value;
                }

                return _buffer;
            }
        }

        void IHashAlgorithmWithCleanup.Cleanup()
        {
            if (_buffer is not null)
            {
                _buffer = null;
                _bufferHandle.Dispose();
            }
        }

        void IHashAlgorithmInputLength.SetInputLength(long inputLength)
        {
            if (inputLength <= int.MaxValue)
            {
                // Spans only support up to int.MaxValue number of items.
                // It means that the files/blobs larger than 2Gb can't use the length-based optimization
                _expectedInputLength = inputLength;
            }
        }

#if NET_COREAPP
        /// <inheritdoc />
        protected override bool TryHashFinal(Span<byte> destination, out int bytesWritten)
        {
            byte[] finalBytes = HashFinal();
            finalBytes.CopyTo(destination);
            bytesWritten = finalBytes.Length;
            return true;
        }

        /// <inheritdoc />
        protected override void HashCore(ReadOnlySpan<byte> source)
        {
            HashBytes(source);
        }

        private byte[]? TryHashAllBytesWithNoCopy(ReadOnlySpan<byte> source)
        {
            int startIndex = 0;
            byte[]? finalizedBytes = null;

            // If we know the size of the input upfront, we can hash all the data
            // without copying them into a temporary buffer.
            if (source.Length == _expectedInputLength && _currentOffset == 0)
            {
                // The span still maybe bigger than 2Mb buffer the hash algorithm uses to compute hash blocks.
                // Need to split the input source into the 2Mb pieces.
                int remaining = source.Length;
                int blockSize = GlobalObjectPools.TwoMbByteArrayPool.ArraySize;

                while (remaining > 0)
                {
                    int bytesToCopy = Math.Min(blockSize, source.Length - startIndex);
                    var currentBatch = source.Slice(startIndex, bytesToCopy);
                    startIndex += bytesToCopy;
                    remaining -= bytesToCopy;

                    var blockHash = HashBlock(currentBatch);

                    if (remaining == 0)
                    {
                        // Finalizing the hash. We're done.
                        finalizedBytes = _rollingId.Finalize(blockHash).Bytes;
                    }
                    else
                    {
                        // Not done yet.
                        _rollingId.Update(blockHash);
                    }
                }
            }

            return finalizedBytes;
        }
#endif //NET_COREAPP

        /// <inheritdoc />
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            HashBytes(array.AsSpan(ibStart, cbSize));
        }

        private void HashBytes(ReadOnlySpan<byte> source)
        {
#if NET_COREAPP // The following optimization is only available for .net core
            _finalized ??= TryHashAllBytesWithNoCopy(source);
            if (_finalized is not null)
            {
                // We managed to hash the entire source at one shot.
                return;
            }
#endif //NET_COREAPP
            // Can't hash the entire content. Hashing the current piece only.
            int startIndex = 0;
            var buffer = Buffer;
            int cbSize = source.Length;
            while (cbSize > 0)
            {
                if (_currentOffset == buffer.Length)
                {
                    var blockHash = HashBlock(buffer, buffer.Length);
                    _rollingId.Update(blockHash);
                    _currentOffset = 0;
                }

                int bytesToCopy = Math.Min(cbSize, buffer.Length - _currentOffset);
                source.Slice(startIndex, bytesToCopy).CopyTo(buffer.AsSpan(_currentOffset));

                _currentOffset += bytesToCopy;
                startIndex += bytesToCopy;
                cbSize -= bytesToCopy;
            }
        }

        /// <inheritdoc />
        protected override byte[] HashFinal()
        {
            // Special case: the full hash was already computed
            if (_finalized is not null)
            {
                return _finalized;
            }

            // Here, either the buffer has data, or there were no blocks.

            // Flush out buffer
            if (_currentOffset != 0)
            {
                var blockHash = HashBlock(Buffer, _currentOffset);
                return _rollingId.Finalize(blockHash).Bytes;
            }

            // if there are no blocks add an empty block
            var emptyBlockHash = HashBlock(EmptyArray, 0);
            return _rollingId.Finalize(emptyBlockHash).Bytes;
        }

#if NET_COREAPP
        private byte[] HashBlock(byte[] block, int lengthToHash)
        {
            VsoHash.HashBlockBytes(block, lengthToHash, _hashBuffer);
            return _hashBuffer;
        }

        private byte[] HashBlock(ReadOnlySpan<byte> block)
        {
            VsoHash.HashBlockBytes(block, _hashBuffer);
            return _hashBuffer;
        }
#else // !NET_COREAPP
        private BlobBlockHash HashBlock(byte[] block, int lengthToHash)
        {
            return VsoHash.HashBlock(block, lengthToHash);
        }
#endif
    }
}
