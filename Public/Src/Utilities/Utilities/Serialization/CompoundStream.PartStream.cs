// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities.Serialization
{
    public partial class CompoundStream
    {
        /// <summary>
        /// A partial stream in the compound stream
        /// </summary>
        public abstract class PartStream : Stream
        {
            /// <summary>
            /// The index of the part stream which should be persisted and used
            /// to open the part stream for read using <see cref="OpenReadPartStream(int)"/>
            /// </summary>
            public abstract int Index { get; }
        }

        /// <summary>
        /// Describes a partial stream in a compound stream. The stream essentially consists of a sequence
        /// of blocks of a predefined size specified by their start position in the underlying stream.
        /// 
        /// When writing, writes are buffered until the block size is reached, then the buffer space for the
        /// buffer bytes are reserved in the underlying stream, and finally the buffer is written to the reserved
        /// region.
        /// 
        /// When reading, the stream is initialized with the stream length and blocks reserved during write. 
        /// And reads iterate through the blocks moving to the next block when all bytes have been read
        /// from the current block and stream ends when the recorded length of the stream is reached.
        /// </summary>
        private class PartStreamImpl : PartStream
        {
            /// <summary>
            /// The owning compound stream
            /// </summary>
            private readonly CompoundStream m_ownerStream;

            /// <summary>
            /// The start positions of blocks in the underlying stream
            /// </summary>
            private readonly List<long> m_blockStarts = new List<long>();

            /// <summary>
            /// The position in the stream (not the same as position in underlying stream referred to as global position)
            /// </summary>
            private long m_position = 0;

            /// <summary>
            /// The length of the stream (not the same as the length of the underlying stream)
            /// </summary>
            private long m_length = 0;

            /// <summary>
            /// Indicates whether stream is closed
            /// </summary>
            private bool m_isClosed = false;

            /// <inheritdoc />
            public override int Index { get; }

            /// <summary>
            /// The size of block for this part stream
            /// </summary>
            public int BlockSize { get; }

            /// <summary>
            /// The index of the block in <see cref="m_blockStarts"/> from which bytes will be read or to which bytes will be written.
            /// </summary>
            private int CurrentPositionBlockIndex
            {
                get
                {
                    var blockIndex = (int)(m_position / BlockSize);
                    Contract.Assert(blockIndex <= (CanWrite ? m_blockStarts.Count + 1 : m_blockStarts.Count));
                    return blockIndex;
                }
            }

            /// <summary>
            /// The remaining number of bytes in the current block (i.e. block for <see cref="CurrentPositionBlockIndex"/>)
            /// </summary>
            private int RemainingBlockBytes => BlockSize - BlockOffset;

            /// <summary>
            /// The offset in the current block (i.e. block for <see cref="CurrentPositionBlockIndex"/>)
            /// </summary>
            private int BlockOffset => (int)(m_position % BlockSize);

            #region Write Only State

            /// <summary>
            /// Buffer for writes the current block. When full, the buffer is written to the underlying stream
            /// </summary>
            public MemoryStream CurrentBlockWriteBufferStream;

            #endregion

            #region Read Only State

            /// <summary>
            /// The global underlying stream for the compound stream from which bytes can be read 
            /// </summary>
            public Stream BaseGlobalReadStream;

            #endregion

            /// <inheritdoc />
            public override bool CanRead { get; }

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite { get; }

            /// <inheritdoc />
            public override long Length => m_length;

            /// <inheritdoc />
            public override long Position
            {
                get { return m_position; }
                set { Seek(value, SeekOrigin.Begin); }
            }

            /// <summary>
            /// Opens a part stream for read-only access
            /// </summary>
            public PartStreamImpl(CompoundStream ownerStream, int index, int blockSize, long length, long[] blockStarts)
            {
                Index = index;
                CanRead = true;
                CanWrite = false;
                m_ownerStream = ownerStream;
                BlockSize = blockSize;
                m_length = length;
                m_blockStarts.AddRange(blockStarts);
            }

            /// <summary>
            /// Opens a part stream for write-only access
            /// </summary>
            public PartStreamImpl(CompoundStream ownerStream, int index, int blockSize)
            {
                Index = index;
                CanRead = false;
                CanWrite = true;
                m_ownerStream = ownerStream;
                BlockSize = blockSize;
                CurrentBlockWriteBufferStream = new MemoryStream();
            }

            public void InitializeRead(Stream overrideReadStream = null)
            {
                Contract.Assert(CanRead, "InitializeRead can only be called on readable PartStream");
                Contract.Assert(BaseGlobalReadStream == null, "Stream is already initialized");

                // Set or create the read stream using the factory from the compound stream
                BaseGlobalReadStream = overrideReadStream ?? m_ownerStream.m_readStreamFactory();

                // Verify properties of the stream after it is created
                Contract.Assert(BaseGlobalReadStream.CanRead, "Underlying stream must be readable");
                Contract.Assert(BaseGlobalReadStream.CanSeek, "Underlying stream must be seekable");
            }

            public override void Flush()
            {
                // Do nothing on flush
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                Contract.Assert(BaseGlobalReadStream != null, "Stream is not initialized");
                Contract.Assert(CanRead, "Stream is not readable");
                Contract.Assert(!m_isClosed, "Cannot read stream. Stream is not closed.");
                int totalReadBytes = 0;
                var remainingCount = count;
                while (remainingCount > 0)
                {
                    long blockStart;
                    var blockIndex = CurrentPositionBlockIndex;
                    if (blockIndex == m_blockStarts.Count)
                    {
                        // No more blocks to read
                        break;
                    }
                    else
                    {
                        // Get the start of the current block
                        blockStart = m_blockStarts[blockIndex];
                    }

                    // Calculate the position in the underlying stream and
                    // seek to that position to start the read
                    var globalPosition = blockStart + BlockOffset;
                    GotoGlobalPosition(globalPosition);

                    // For the current iteration, the maximum number of bytes to read is limited by the remaining bytes 
                    // in the block
                    var maxBlockReadCount = (int)Math.Min(remainingCount, Math.Min(m_length - m_position, RemainingBlockBytes));

                    // Read the bytes from the underlying stream
                    var localReadBytes = BaseGlobalReadStream.Read(buffer, offset, maxBlockReadCount);

                    // Update local data with number of bytes read
                    totalReadBytes += localReadBytes;
                    remainingCount -= localReadBytes;
                    offset += localReadBytes;

                    // Update instance data with read bytes
                    m_position += localReadBytes;

                    if (localReadBytes == 0 || localReadBytes != maxBlockReadCount)
                    {
                        // Ran out of bytes to read on the stream
                        break;
                    }
                }

                return totalReadBytes;
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                Contract.Assert(CanWrite, "Stream is not writable");
                Contract.Assert(!m_isClosed, "Cannot write stream. Stream is not closed.");
                var remainingCount = count;
                while (remainingCount > 0)
                {
                    // Capture the block index before any bytes are written for comparison later
                    var blockIndex = CurrentPositionBlockIndex;

                    // For the current iteration, we can only write up to the bytes left in the block
                    var maxBlockWriteCount = (int)Math.Min(remainingCount, RemainingBlockBytes);

                    // Write to the write buffer
                    CurrentBlockWriteBufferStream.Write(buffer, offset, maxBlockWriteCount);

                    // Update local information to reflect the amount of bytes written
                    remainingCount -= maxBlockWriteCount;
                    offset += maxBlockWriteCount;

                    // Update global information to reflect the bytes written to the stream
                    m_position += maxBlockWriteCount;
                    m_length = Math.Max(m_length, m_position);

                    // Check if the block index has changed indicating that the
                    // block which was written in this iteration is full and should
                    // be written to the underlying write stream of the compound stream
                    var updatedBlockIndex = CurrentPositionBlockIndex;
                    if (blockIndex != updatedBlockIndex)
                    {
                        // Block is full, write to next block before switching to next block
                        WriteBlock(finalBlock: false);
                        CurrentBlockWriteBufferStream.Position = 0;
                    }
                }
            }

            /// <summary>
            /// Writes the final block to the underlying stream
            /// </summary>
            public override void Close()
            {
                if (m_isClosed)
                {
                    return;
                }

                m_isClosed = true;
                if (CurrentBlockWriteBufferStream != null && CurrentBlockWriteBufferStream.Position != 0)
                {
                    // Block has data. Write out before closing
                    WriteBlock(finalBlock: true);
                }

                CurrentBlockWriteBufferStream = null;
                base.Close();
            }

            private void WriteBlock(bool finalBlock)
            {
                m_blockStarts.Add(m_ownerStream.WriteBlock(this, finalBlock: finalBlock));
            }

            public long AllocateBlock()
            {
                var blockStart = m_ownerStream.AllocateBlock(BlockSize);
                m_blockStarts.Add(blockStart);
                return blockStart;
            }

            private void GotoGlobalPosition(long absolutePosition)
            {
                BaseGlobalReadStream.Position = absolutePosition;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException("Stream does not support seeking");
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException("Stream does not support SetLength");
            }

            public static PartStreamImpl ReadPartStream(CompoundStream compoundStream, int index, BuildXLReader reader)
            {
                var blockSize = reader.ReadInt32Compact();
                var partLength = reader.ReadInt64Compact();
                var blockCount = reader.ReadInt32Compact();
                long[] blockStarts = new long[blockCount];
                long lastBlockStart = 0;
                for (int i = 0; i < blockCount; i++)
                {
                    var blockStart = reader.ReadInt64Compact() + lastBlockStart;
                    blockStarts[i] = blockStart;
                    lastBlockStart = blockStart;
                }

                return new PartStreamImpl(compoundStream, index, blockSize, partLength, blockStarts);
            }

            public void WritePartStreamData(BuildXLWriter writer)
            {
                Contract.Assert(m_isClosed, "Cannot write part metadata to compound stream. Part is not closed.");
                writer.WriteCompact((int)BlockSize);
                writer.WriteCompact((long)m_length);
                writer.WriteCompact((int)m_blockStarts.Count);
                long lastBlockStart = 0;
                foreach (var blockStart in m_blockStarts)
                {
                    writer.WriteCompact((long)blockStart - lastBlockStart);
                    lastBlockStart = blockStart;
                }
            }
        }
    }
}
