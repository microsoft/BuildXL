// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Stream capable of recording the bytes read from or written to its inner stream.
    /// </summary>
    internal class RecordingStream : Stream
    {
        private enum RecordingType
        {
            Read,
            Write,
        }

        private readonly byte[] _recordedBytes;
        private readonly Stream _inner;
        private readonly long? _capacity;
        private readonly MemoryStream _memoryStream;
        private long _readBytes = 0;

        private readonly RecordingType _recordingType;

        private bool FixedSizeStream => _capacity != null;

        /// <summary>
        /// RecordingStream constructor.
        /// </summary>
        /// <param name="inner">Inner stream that it will record.</param>
        /// <param name="size">
        /// The amount of bytes that will be recorded. Will throw if more bytes are read/written from underlying stream.
        /// Ignored if <paramref name="inner"/> is not seekable.
        /// </param>
        /// <param name="recordingType">Type of the recorder: read recorder or write recorder.</param>
        private RecordingStream(Stream inner, long? size, RecordingType recordingType)
        {
            _inner = inner;
            _recordingType = recordingType;

            // Immediately throw if a given stream does not support the mode we expected.
            // This is quite important, because otherwise the current instance will return false for both 'CanRead' and 'CanWrite'
            // and the stream that is neither readable nor writable is consider to be a disposed stream by some API (like Stream.CopyTo).
            if (recordingType == RecordingType.Read && !inner.CanRead)
            {
                throw new ArgumentException($"Failed to create RecordingStream instance with {recordingType} mode, but a given stream is not readable.");
            }

            if (recordingType == RecordingType.Write && !inner.CanWrite)
            {
                throw new ArgumentException($"Failed to create RecordingStream instance with {recordingType} mode, but a given stream is not writable.");
            }

            if (recordingType == RecordingType.Read && inner.CanSeek && size != null)
            {
                _capacity = size;
                _recordedBytes = new byte[size.Value];
                _memoryStream = new MemoryStream(_recordedBytes);
            }
            else
            {
                // If the inner stream is not seekable, create a backing expandable memory stream
                // and do not validate length against _capacity.
                _memoryStream = new MemoryStream();
            }
        }

        /// <summary>
        /// Create a stream for recording reads.
        /// </summary>
        /// <remarks>
        /// Writes won't be supported by the created instance.
        /// </remarks>
        public static RecordingStream ReadRecordingStream(Stream inner, long? size) => new RecordingStream(inner, size, RecordingType.Read);

        /// <summary>
        /// Create a stream for recording writes.
        /// </summary>
        /// <remarks>
        /// Reads won't be supported by the created instance.
        /// </remarks>
        public static RecordingStream WriteRecordingStream(Stream inner) => new RecordingStream(inner, size: null, RecordingType.Write);

        /// <summary>
        /// Returns the bytes that have been recorded. Size will always be the same as size.
        /// </summary>
        public byte[] RecordedBytes
        {
            get
            {

                Contract.Assert(!FixedSizeStream || _memoryStream.Position == _capacity, "RecordingStream should record the entire content of a stream.");
                return FixedSizeStream ? _recordedBytes : _memoryStream.ToArray();
            }
        }

        /// <inheritdoc />
        public override bool CanRead => _recordingType == RecordingType.Read;

        /// <inheritdoc />
        public override bool CanSeek => _inner.CanSeek;

        /// <inheritdoc />
        public override bool CanWrite => _recordingType == RecordingType.Write;

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

            if (FixedSizeStream && _readBytes + bytesRead > _capacity)
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
        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);

            if (FixedSizeStream && _readBytes + count > _capacity)
            {
                throw new InvalidOperationException($"Cannot exceed capacity set to {_capacity} bytes.");
            }
            else
            {
                _readBytes += count;
                _memoryStream.Write(buffer, offset, count);
            }
        }
    }
}
