// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Stream capable of recording the bytes read from its inner stream.
    /// </summary>
    internal class RecordingStream : Stream
    {
        private readonly byte[] _recordedBytes;
        private readonly Stream _inner;
        private readonly long _capacity;
        private readonly MemoryStream _memoryStream;
        private long _readBytes = 0;

        /// <summary>
        /// RecordingStream constructor.
        /// </summary>
        /// <param name="inner">Inner stream that it will record.</param>
        /// <param name="size">The amount of bytes that will be recorded. Will throw if more bytes are read from underlying stream.</param>
        public RecordingStream(Stream inner, long size)
        {
            _inner = inner;
            _capacity = size;
            _recordedBytes = new byte[size];
            _memoryStream = new MemoryStream(_recordedBytes);
        }

        /// <summary>
        /// Returns the bytes that have been recorded. Size will always be the same as size.
        /// </summary>
        public byte[] RecordedBytes
        {
            get
            {
                Contract.Assert(_memoryStream.Position == _capacity, "RecordingStream should record the entire content of a stream.");
                return _recordedBytes;
            }
        }

        /// <inheritdoc />
        public override bool CanRead => _inner.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => _inner.CanSeek;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => _inner.Length;

        /// <inheritdoc />
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        /// <inheritdoc />
        public override void Flush() => _inner.Flush();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _inner.Read(buffer, offset, count);

            if (_readBytes + bytesRead > _capacity)
            {
                throw new InvalidOperationException($"Cannot exceed capacity set to {_capacity} bytes.");
            }
            else
            {
                _readBytes += bytesRead;
                _memoryStream.Write(buffer, offset, bytesRead);
            }

            return bytesRead;
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        /// <inheritdoc />
        public override void SetLength(long value) => _inner.SetLength(value);

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("The stream does not support writing.");
    }
}
