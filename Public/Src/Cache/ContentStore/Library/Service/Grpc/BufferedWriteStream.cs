using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    // A write-only stream backed by a fixed-size buffer that
    // processes content when the buffer is full and then
    // re-uses the buffer.

    // This class is required for serving compressed data via grpc
    // because the compression api insists on writing to a stream and
    // the grpc api exposes an write-one-chunk method instead of a stream.

    // This class is not safe for simultaneous use by multiple threads.

    // Be sure to call Flush when done writing, otherwise the last bit
    // of content will (probably) not be processed, because it will (probably)
    // not fill the buffer.
    internal class BufferedWriteStream : Stream
    {
        public BufferedWriteStream(byte[] storage, Func<byte[], int, int, Task> writer)
        {
            if (storage is null) { throw new ArgumentNullException(nameof(storage)); }
            if (writer is null) { throw new ArgumentNullException(nameof(writer)); }
            _storage = storage;
            _writer = writer;
            _writePointer = 0;
            _position = 0;
        }

        private readonly byte[] _storage; // buffer to store content before written
        private readonly Func<byte[], int, int, Task> _writer; // method to write
        private int _writePointer; // pointer to next index in buffer to be written to
        private int _position; // total bytes written

        public override bool CanWrite => true;

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cts)
        {
            Debug.Assert(!(buffer is null));
            Debug.Assert(offset >= 0);
            Debug.Assert(count >= 0);
            Debug.Assert(count <= buffer.Length - offset);

            int readPointer = offset;
            int totalCount = 0;
            while (totalCount < count)
            {
                Debug.Assert(readPointer >= 0);
                Debug.Assert(readPointer < buffer.Length);

                Debug.Assert(_writePointer >= 0);
                Debug.Assert(_writePointer < _storage.Length);

                int copyCount = Math.Min(count - totalCount, _storage.Length - _writePointer);
                Array.Copy(buffer, readPointer, _storage, _writePointer, copyCount);
                readPointer += copyCount;
                _writePointer += copyCount;
                totalCount += copyCount;
                _position += copyCount;

                if (_writePointer == _storage.Length)
                {
                    await FlushAsync().ConfigureAwait(false);
                    Debug.Assert(_writePointer == 0);
                }
            }

            Debug.Assert(totalCount == count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count, CancellationToken.None).Wait();
        }

        public async override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_writePointer > 0)
            {
                await _writer(_storage, 0, _writePointer).ConfigureAwait(false);
                _writePointer = 0;
            }
        }

        public override void Flush()
        {
            FlushAsync().Wait();
        }

        public override bool CanRead => false;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException();

        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException();

        public override long Position { get => _position; set => throw new InvalidOperationException(); }

        public override long Length { get => _position; }

        public override void SetLength(long value) => throw new InvalidOperationException();

    }

}
