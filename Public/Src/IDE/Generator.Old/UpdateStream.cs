// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using static BuildXL.Utilities.Collections.CollectionUtilities;

namespace BuildXL.Ide.Generator.Old
{
    /// <summary>
    /// Creates a stream which only writes to an inner stream if there is a change.
    /// </summary>
    /// <remarks>
    /// For every write operation, the underlying stream is read to detect
    /// a difference. If no difference is detected, no bytes are written to the underlying stream.
    /// Otherwise, the writes are written to the underlying stream, and all subsequent write operations,
    /// write to the underlying stream without reading to detect differences.
    /// </remarks>
    public class UpdateStream : Stream
    {
        private byte[] m_readBuffer;
        private readonly bool m_leaveOpen;

        /// <summary>
        /// Returns true if a difference was detected while writing the stream.
        /// </summary>
        public bool DifferenceDetected { get; private set; }

        /// <summary>
        /// Returns the underlying stream.
        /// </summary>
        public Stream BaseStream { get; private set; }

        /// <summary>
        /// Creates a new instance of UpdateStream with the given underlying stream.
        /// </summary>
        /// <param name="stream">the underlying stream</param>
        /// <param name="leaveOpen">true to leave the underlying stream open when the stream is closed. Otherwise,
        /// the underlying stream is truncated to the length of the current stream and closed when the stream is closed.</param>
        public UpdateStream(Stream stream, bool leaveOpen = true)
        {
            Contract.Requires(stream != null);
            Contract.Requires(stream.CanSeek);

            BaseStream = stream;
            m_leaveOpen = leaveOpen;

            // Initialize to empty
            // This will be updated to a suitable buffer size
            // when a write is performed
            m_readBuffer = EmptyArray<byte>();
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
            return BaseStream.ReadByte();
        }

        /// <summary>
        /// Flushes the underlying stream if an edit was performed (i.e., difference was detected).
        /// </summary>
        public override void Flush()
        {
            if (DifferenceDetected)
            {
                BaseStream.Flush();
            }
        }

        /// <summary>
        /// Gets the current length of the underlying stream.
        /// </summary>
        public override long Length => BaseStream.Length;

        /// <summary>
        /// Gets or sets the position in the underlying stream.
        /// </summary>
        public override long Position
        {
            get { return BaseStream.Position; }
            set { BaseStream.Position = value; }
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
            return BaseStream.Read(buffer, offset, count);
        }

        /// <summary>
        /// Sets the position within the underlying stream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter. </param>
        /// <param name="origin">A value of type <see cref="SeekOrigin"/> indicating the reference point used to obtain the new position. </param>
        /// <returns>The new position within the current stream.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            return BaseStream.Seek(offset, origin);
        }

        /// <summary>
        /// Sets the length of the underlying stream to value if it is different from
        /// current length of the underlying stream.
        /// </summary>
        /// <param name="value">the new length of the underlying stream.</param>
        public override void SetLength(long value)
        {
            SetLengthIfChanged(value);
        }

        private void SetLengthIfChanged(long value)
        {
            if (value != BaseStream.Length)
            {
                DifferenceDetected = true;
                BaseStream.SetLength(value);
            }
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
            // If no difference was detected yet, we need to read from the underlying
            // stream to check if this operation would write different bytes to the stream.
            if (!DifferenceDetected)
            {
                // Enlarge the read buffer if necessary to support
                // reading the bytes that would be written by this
                // operation
                if (count > m_readBuffer.Length)
                {
                    m_readBuffer = new byte[count];
                }

                var readByteCount = Read(m_readBuffer, 0, count);

                if (readByteCount != count)
                {
                    // The number of bytes read was less than what was requested.
                    // This means the remaining stream length is less than what is
                    // being written so there is a difference.
                    DifferenceDetected = true;
                }
                else
                {
                    // Compare the read bytes to bytes that would be written
                    for (int i = 0; i < count; i++)
                    {
                        if (m_readBuffer[i] != buffer[i + offset])
                        {
                            DifferenceDetected = true;
                            break;
                        }
                    }
                }

                if (DifferenceDetected)
                {
                    // Detected a difference so we need to go back
                    // so we can write from the original position
                    Position = Position - readByteCount;
                }
            }

            if (DifferenceDetected)
            {
                BaseStream.Write(buffer, offset, count);
            }
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
                if (disposing && !m_leaveOpen && BaseStream != null)
                {
                    // Truncate the stream if necessary.
                    // The BaseStream should represent the contents
                    // written to this stream so it will need to be truncated
                    // if the length is longer than the current position.
                    SetLengthIfChanged(Position);

                    BaseStream.Dispose();
                }
            }
            finally
            {
                m_readBuffer = null;
                BaseStream = null;

                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Asynchronously ensures that the two streams contain identical length and contents by
        /// reading the bytes from the source stream and writing them to target stream if there is a change.
        /// </summary>
        /// <param name="source">The stream from which the bytes will be copied.</param>
        /// <param name="target">The stream to which the bytes will be copied.</param>
        /// <param name="bufferSize">The size of the buffer to use. Nonpositive to use default buffer size.</param>
        /// <returns>A task that represents the asynchronous copy operation. The result of the task indicates whether a difference
        /// was detected in the streams.</returns>
        public static async Task<bool> MirrorIfChangedAsync(Stream source, Stream target, int bufferSize = 0)
        {
            Contract.Requires(source != null);
            Contract.Requires(target != null);

            // Ensures streams are at the beginning
            source.Seek(0, SeekOrigin.Begin);
            target.Seek(0, SeekOrigin.Begin);

            using (UpdateStream updateTargetStream = new UpdateStream(target))
            {
                updateTargetStream.SetLength(source.Length);

                if (bufferSize <= 0)
                {
                    // unspecified buffer size, use default buffer size by calling
                    // CopyToAsync without buffer size specified
                    await source.CopyToAsync(updateTargetStream).ConfigureAwait(continueOnCapturedContext: false);
                }
                else
                {
                    await source.CopyToAsync(updateTargetStream, bufferSize).ConfigureAwait(continueOnCapturedContext: false);
                }

                return updateTargetStream.DifferenceDetected;
            }
        }
    }
}
