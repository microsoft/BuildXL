// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Wraps a seekable stream to expose it with a configurable minimum size, left padding with zeros if needed.
    /// </summary>
    /// <remarks>
    /// No extra memory is taken by the padded portion of the stream
    /// If the underlying stream is modified, then the behavior of this class is non-deterministic
    /// </remarks>
    public class ZeroLeftPaddedStream : Stream
    {
        private readonly Stream m_stream;
        private long m_currentPos = 0;
        private readonly long m_gap;

        /// <nodoc/>
        public ZeroLeftPaddedStream(Stream stream, long minimumLength = 0)
        {
            Contract.RequiresNotNull(stream);
            Contract.Requires(stream.CanSeek);

            m_stream = stream;
            MinimumLength = minimumLength;
            m_gap = Math.Max(0, MinimumLength - m_stream.Length);
        }
        
        /// <summary>
        /// The miminum legth the stream has
        /// </summary>
        public long MinimumLength { set; get; }

        /// <inheritdoc/>
        public override bool CanRead => m_stream.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => m_stream.CanSeek;

        /// <summary>
        /// Always false
        /// </summary>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => m_stream.Length + m_gap;

        /// <inheritdoc/>
        public override long Position { 
            get => m_currentPos;
            set
            { 
                m_stream.Position = Math.Max(0, value - m_gap);
                m_currentPos = Math.Min(value, Length); 
            } 
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            m_stream.Flush();
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;

            if (Position < m_gap)
            {
                // Produce the padded area of the stream with 0-bytes
                long maxPaddedCount = m_gap - Position;
                int paddedCount = Math.Min(count, maxPaddedCount > int.MaxValue ? int.MaxValue : (int) maxPaddedCount);
                if (paddedCount > 0)
                {
                    Array.Clear(buffer, offset, paddedCount);
                    bytesRead = paddedCount;
                }

                // Read the reminder, if any, from the underlying stream
                if (paddedCount < count)
                {
                    bytesRead += m_stream.Read(buffer, offset + paddedCount, count - paddedCount);
                }
            }
            else
            {
                // The position is beyond the padded area, just do a regular read on the underlying stream
                bytesRead = m_stream.Read(buffer, offset, count);
            }

            Position += count;

            return bytesRead;
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            // Compute the origin of the seek in terms of the starting index on the stream
            long originIndex = origin switch 
            { 
                SeekOrigin.Begin => 0, 
                SeekOrigin.Current => Position, 
                SeekOrigin.End => Length, 
                _ => throw new ArgumentOutOfRangeException(nameof(origin), $"Not expected value {origin}") 
            };

            // This is the final index of the stream
            long finalIndex = Math.Min(Length, originIndex + offset);

            if (finalIndex < 0)
            {
                throw new ArgumentException("Attempt to seek before the beginning of the stream");
            }

            if (finalIndex >= m_gap)
            {
                m_stream.Seek(finalIndex - m_gap, SeekOrigin.Begin);
            }
            else
            {
                m_stream.Seek(0, SeekOrigin.Begin);
            }

            Position = finalIndex;

            return Position;
        }

        /// <summary>
        /// Always throws
        /// </summary>
        /// <remarks>
        /// Shouldn't be called since this stream is non-writable
        /// </remarks>
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Always throws
        /// </summary>
        /// <remarks>
        /// Shouldn't be called since this stream is non-writable
        /// </remarks>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
