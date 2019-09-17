// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// This class is required for decompressing data streamed via grpc
    /// because the compression api insists on reading from a stream and
    /// the grpc api exposes a read-one-chunk method instead of a stream.
    /// </summary>
    internal class BufferedReadStream : Stream
    {
        private readonly Func<Task<byte[]>> _reader;

        private byte[] _storage; // buffer containing the next bytes to be read
        private int _readPointer; // the next index to be read from the storage buffer

        private int _position; // total bytes that have been read
        private int _length; // total bytes that have been ingested

        public BufferedReadStream(Func<Task<byte[]>> reader)
        {
            Contract.Requires(reader != null);
            _reader = reader;
        }

        public override bool CanRead => true;

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Debug.Assert(!(buffer is null));
            Debug.Assert(offset >= 0);
            Debug.Assert(count >= 0);
            Debug.Assert(count <= buffer.Length - offset);

            // Caller can ask for any count of bytes and all we can do is ask
            // reader for chunks of unknown length, which may be longer or
            // shorter than count. So we give caller bytes from our last
            // stored read until we have given them all, in which case we
            // ask reader for more. Keep track of how many bytes were
            // already returned from storage so we start at the right place.

            var writePointer = offset;
            var totalCount = 0;
            while (totalCount < count)
            {
                Debug.Assert(_readPointer >= 0);
                Debug.Assert((_storage is null) || (_readPointer < _storage.Length));

                Debug.Assert(writePointer >= 0);
                Debug.Assert(writePointer < buffer.Length);

                // If we don't have any more bytes, ask reader for some.
                if (_storage is null)
                {
                    _storage = await _reader().ConfigureAwait(false);

                    // If the reader doesn't give us any, we are done.
                    if (_storage is null)
                    {
                        return totalCount;
                    }

                    _readPointer = 0;
                    _length += _storage.Length;
                }

                Debug.Assert(!(_storage is null));

                // Copy as many bytes as we can to the caller's buffer
                var copyCount = Math.Min(count - totalCount, _storage.Length - _readPointer);
                Array.Copy(_storage, _readPointer, buffer, writePointer, copyCount);
                _readPointer += copyCount;
                writePointer += copyCount;
                totalCount += copyCount;
                _position += copyCount;

                Debug.Assert(_readPointer <= _storage.Length);

                // If we returned all the bytes we had, null out our storage
                // as a signal to get more next time.
                if (_readPointer == _storage.Length)
                {
                    _storage = null;
                    _readPointer = 0;
                }

            }

            Debug.Assert(totalCount == count);
            return totalCount;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override bool CanWrite => false;

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        // I want to use Task.CompletedTask here, but we still compile against 4.5.2 and it didn't appear until 4.6
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public override void Flush() { }

        public override long Length => _length;

        public override long Position { get => _position; set => throw new NotSupportedException(); }
    }

}
