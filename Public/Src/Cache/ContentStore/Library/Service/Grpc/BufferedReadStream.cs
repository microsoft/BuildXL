using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{

    // This class is required for decompressing data streamed via grpc
    // because the compression api insists on reading from a stream and
    // the grpc api exposes a read-one-chunk method instead of a stream.

    internal class BufferedReadStream : Stream
    {
        public BufferedReadStream(Func<Task<byte[]>> reader)
        {
            if (reader is null)
            {
                throw new ArgumentNullException(nameof(reader));
            }
            _reader = reader;
        }

        private readonly Func<Task<byte[]>> _reader;

        private byte[] _storage; // buffer containing the next bytes to be read
        private int _readPointer; // the next index to be read from the storage buffer

        private int _position; // total bytes that have been read
        private int _length; // total bytes that have been ingested

        public override bool CanRead => true;

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Debug.Assert(!(buffer is null));
            Debug.Assert(offset >= 0);
            Debug.Assert(count >= 0);
            Debug.Assert(count <= buffer.Length - offset);

            int writePointer = offset;
            int totalCount = 0;
            while (totalCount < count)
            {
                Debug.Assert(_readPointer >= 0);
                Debug.Assert((_storage is null) || (_readPointer < _storage.Length));

                Debug.Assert(writePointer >= 0);
                Debug.Assert(writePointer < buffer.Length);

                if (_storage is null)
                {
                    _storage = await _reader().ConfigureAwait(false);
                    if (_storage is null)
                    {
                        return totalCount;
                    }
                    _readPointer = 0;
                    _length += _storage.Length;
                }

                Debug.Assert(!(_storage is null));

                int copyCount = Math.Min(count - totalCount, _storage.Length - _readPointer);
                Array.Copy(_storage, _readPointer, buffer, writePointer, copyCount);
                _readPointer += copyCount;
                writePointer += copyCount;
                totalCount += copyCount;
                _position += copyCount;

                Debug.Assert(_readPointer <= _storage.Length);

                if (_readPointer == _storage.Length)
                {
                    _storage = null;
                    _readPointer = 0;
                }

            }

            Debug.Assert(totalCount == count);
            return totalCount;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count).Result;
        }

        public override bool CanWrite => false;

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException();

        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException();

        public override void SetLength(long value) => throw new NotImplementedException();

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Flush() { }

        public override long Length => _length;

        public override long Position { get => _position; set => throw new InvalidOperationException(); }
    }

}
