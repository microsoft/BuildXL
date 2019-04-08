using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CopyClient
{

    // This class is required for decompressing data streamed via grpc
    // because the compression api insists on reading from a stream and
    // the grpc api exposes an read-one-chunk method instead of a stream.

    public class BufferedReadStream : Stream
    {
        public BufferedReadStream(Func<Task<byte[]>> reader)
        {
            if (reader is null) throw new ArgumentNullException(nameof(reader));
            this.reader = reader;
        }

        private readonly Func<Task<byte[]>> reader;

        private byte[] storage; // buffer containing the next bytes to be read
        private int readPointer; // the next index to be read from the storage buffer

        private int position; // total bytes that have been read
        private int length; // total bytes that have been ingested

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
                Debug.Assert(readPointer >= 0);
                Debug.Assert((storage is null) || (readPointer < storage.Length));

                Debug.Assert(writePointer >= 0);
                Debug.Assert(writePointer < buffer.Length);

                if (storage is null)
                {
                    storage = await reader().ConfigureAwait(false);
                    if (storage is null)
                    {
                        return totalCount;
                    }
                    readPointer = 0;
                    length += storage.Length;
                }

                Debug.Assert(!(storage is null));

                int copyCount = Math.Min(count - totalCount, storage.Length - readPointer);
                Array.Copy(storage, readPointer, buffer, writePointer, copyCount);
                readPointer += copyCount;
                writePointer += copyCount;
                totalCount += copyCount;
                position += copyCount;

                Debug.Assert(readPointer <= storage.Length);

                if (readPointer == storage.Length)
                {
                    storage = null;
                    readPointer = 0;
                }

                //Debug.Assert(pointer < storage.Length);
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

        public override long Length => length;

        public override long Position { get => position; set => throw new InvalidOperationException(); }
    }

}
