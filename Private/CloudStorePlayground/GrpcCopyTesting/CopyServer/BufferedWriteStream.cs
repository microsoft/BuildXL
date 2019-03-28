using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CopyServer
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
            if (storage is null) throw new ArgumentNullException(nameof(storage));
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            this.storage = storage;
            this.writer = writer;
            this.writePointer = 0;
            this.position = 0;
        }

        private readonly byte[] storage; // buffer to store content before written
        private readonly Func<byte[], int, int, Task> writer; // method to write
        private int writePointer; // pointer to next index in buffer to be written to
        private int position; // total bytes written

        public override bool CanWrite => true;

        public override async Task WriteAsync (byte[] buffer, int offset, int count, CancellationToken cts)
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

                Debug.Assert(writePointer >= 0);
                Debug.Assert(writePointer < storage.Length);

                int copyCount = Math.Min(count - totalCount, storage.Length - writePointer);
                Array.Copy(buffer, readPointer, storage, writePointer, copyCount);
                readPointer += copyCount;
                writePointer += copyCount;
                totalCount += copyCount;
                position += copyCount;

                if (writePointer == storage.Length)
                {
                    await FlushAsync().ConfigureAwait(false);
                    Debug.Assert(writePointer == 0);
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
            if (writePointer > 0)
            {
                await writer(this.storage, 0, writePointer).ConfigureAwait(false);
                writePointer = 0;
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

        public override long Position { get => position; set => throw new InvalidOperationException(); }

        public override long Length { get => position; }

        public override void SetLength(long value) => throw new InvalidOperationException();

    }

}
