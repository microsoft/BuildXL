// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;

#nullable enable

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    ///     A wrapper around a stream. All it provides is counting the number of bytes written to the underlying stream.
    /// </summary>
    public sealed class CountingStream : Stream
    {
        /// <summary>
        /// Wrapped stream
        /// </summary>
        public Stream Stream { get; }

        private long _bytesWritten = 0;

        /// <nodoc />
        public long BytesWritten => _bytesWritten;

        /// <nodoc />
        public CountingStream(Stream stream)
        {
            if (!stream.CanWrite)
            {
                throw new System.InvalidOperationException("It is impossible to count written bytes on a non-writeable stream");
            }

            Stream = stream;
        }

        /// <inheritdoc />
        public override bool CanRead => Stream.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => Stream.CanSeek;

        /// <inheritdoc />
        public override bool CanWrite => Stream.CanWrite;

        /// <inheritdoc />
        public override long Length => Stream.Length;

        /// <inheritdoc />
        public override long Position { get => Stream.Position; set => Stream.Position = value; }

        /// <inheritdoc />
        public override void Flush() => Stream.Flush();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => Stream.Read(buffer, offset, count);

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => Stream.Seek(offset, origin);

        /// <inheritdoc />
        public override void SetLength(long value) => Stream.SetLength(value);

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) {
            Stream.Write(buffer, offset, count);
            Interlocked.Add(ref _bytesWritten, count);
        }
    }
}
