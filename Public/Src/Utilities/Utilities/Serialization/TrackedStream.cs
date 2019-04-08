// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// Creates a stream that counts the bytes that are read and written.
    /// </summary>
    public class TrackedStream : Stream
    {
        private bool m_leaveOpen;
        private long m_position;
        private byte[] m_seekBuffer;
        private readonly byte[] m_singleByteBuffer = new byte[1];

        /// <summary>
        /// The length of the stream in contexts where it is known prior to construction (i.e. from a zip archive entry)
        /// </summary>
        private readonly long? m_precomputedLength;

        /// <summary>
        /// Returns the underlying stream.
        /// </summary>
        public Stream BaseStream { get; private set; }

        /// <summary>
        /// Creates a new instance of StatsStream with the given underlying stream.
        /// </summary>
        public TrackedStream(Stream stream, bool leaveOpen = false, long? precomputedLength = null)
        {
            Contract.Requires(stream != null);

            BaseStream = stream;
            m_leaveOpen = leaveOpen;
            m_precomputedLength = precomputedLength;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the underlying stream supports read operations.
        /// </summary>
        public override bool CanRead => BaseStream.CanRead;

        /// <summary>
        /// Gets or sets a value indicating whether the underlying stream supports seeking.
        /// </summary>
        public override bool CanSeek => BaseStream.CanSeek;

        /// <summary>
        /// Gets or sets a value indicating whether the underlying stream supports write operations.
        /// </summary>
        public override bool CanWrite => BaseStream.CanWrite;

        /// <summary>
        /// Gets or sets a value indicating whether the underlying stream can timeout.
        /// </summary>
        public override bool CanTimeout => BaseStream.CanTimeout;

        /// <summary>
        /// Gets or sets a value, in miliseconds, that determines how long the underlying stream will attempt to read before timing out.
        /// </summary>
        public override int ReadTimeout
        {
            get { return BaseStream.ReadTimeout; }
            set { BaseStream.ReadTimeout = value; }
        }

        /// <summary>
        /// Gets or sets a value, in miliseconds, that determines how long the underlying stream will attempt to read before timing out.
        /// </summary>
        public override int WriteTimeout
        {
            get { return BaseStream.WriteTimeout; }
            set { BaseStream.WriteTimeout = value; }
        }

        /// <summary>
        /// Reads a byte from the underlying stream and returns the byte cast to an <see cref="int"/>,
        /// or returns -1 if reading from the end of the stream.
        /// </summary>
        /// <returns>The byte cast to an <see cref="int"/>, or -1 if reading from the end of the stream</returns>
        public override int ReadByte()
        {
            int r = Read(m_singleByteBuffer, 0, 1);
            if (r == 0)
            {
                return -1;
            }

            return m_singleByteBuffer[0];
        }

        /// <inheritdoc />
        public override void WriteByte(byte value)
        {
            m_singleByteBuffer[0] = value;
            Write(m_singleByteBuffer, 0, 1);
        }

        /// <summary>
        /// Flushes the underlying stream if an edit was performed (i.e., difference was detected).
        /// </summary>
        public override void Flush()
        {
            BaseStream.Flush();
        }

        /// <summary>
        /// Gets the current length of the underlying stream.
        /// </summary>
        public override long Length => m_precomputedLength ?? m_position;

        /// <summary>
        /// Gets or sets the position in the underlying stream.
        /// </summary>
        public override long Position
        {
            get { return m_position; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        /// <summary>
        /// Reads a sequence of bytes from the underlying stream and advances the position within the stream by the number of bytes read.
        /// </summary>
        /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array
        /// with the values between offset and (offset + count - 1) replaced by the bytes read from the underlying source.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the underlying stream.</param>
        /// <param name="count">The maximum number of bytes to be read from the underlying stream. </param>
        /// <returns>The total number of bytes read into the buffer. This can be less than the number of bytes requested if that
        /// many bytes are not currently available, or zero (0) if the end of the underlying stream has been reached.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            var readBytes = BaseStream.Read(buffer, offset, count);
            m_position += readBytes;
            return readBytes;
        }

        /// <summary>
        /// Sets the position within the underlying stream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter. </param>
        /// <param name="origin">A value of type <see cref="SeekOrigin"/> indicating the reference point used to obtain the new position. </param>
        /// <returns>The new position within the current stream.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin && m_position == offset)
            {
                return m_position;
            }

            if (!BaseStream.CanSeek)
            {
                if (m_seekBuffer == null)
                {
                    m_seekBuffer = new byte[4096];
                }

                if (origin == SeekOrigin.Begin)
                {
                    var currentPositionSeekOffset = offset - m_position;

                    while (currentPositionSeekOffset > 0)
                    {
                        currentPositionSeekOffset -= Read(m_seekBuffer, 0, (int)Math.Min(m_seekBuffer.Length, currentPositionSeekOffset));
                    }

                    Contract.Assert(currentPositionSeekOffset == 0);
                    return m_position;
                }
            }

            m_position = BaseStream.Seek(offset, origin);
            return m_position;
        }

        /// <summary>
        /// Sets the length of the underlying stream to value if it is different from
        /// current length of the underlying stream.
        /// </summary>
        /// <param name="value">the new length of the underlying stream.</param>
        public override void SetLength(long value)
        {
            BaseStream.SetLength(value);
        }

        /// <summary>
        /// Writes to the underlying stream if there is a difference between the
        /// underlying stream and bytes which will be written.
        /// </summary>
        /// <param name="buffer">An array of bytes from which the number of bytes specified in the buffer
        /// parameter are written to the underlying stream.</param>
        /// <param name="offset">The 0-based byte offset in the buffer at which copying bytes to the underlying
        /// stream is to begin. count</param>
        /// <param name="count">The number of bytes to be written to the underlying stream.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            m_position += count;
            BaseStream.Write(buffer, offset, count);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the Stream and optionally releases the managed resources.
        /// If this instance was constructed with m_leaveOpen equal to false,<see cref="Stream.Dispose()"/> is called on the underlying stream.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && !m_leaveOpen)
                {
                    BaseStream?.Dispose();
                }
            }
            finally
            {
                BaseStream = null;

                base.Dispose(disposing);
            }
        }
    }
}
