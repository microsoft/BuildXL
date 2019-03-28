// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// Defines a stream composed of multiple interleaved part streams
    /// </summary>
    /// <remarks>
    /// Listing Part Stream - the internal part stream which is automatically created to write
    /// part stream definitions
    /// 
    /// Part Stream Definition - data need to recreate a part stream during read with all associated
    /// block regions
    /// 
    /// Format Schema:
    /// 
    /// ----------------------------------------------------------------------
    /// Format Name                    | Schema                              |
    /// ----------------------------------------------------------------------
    /// Compound Stream                | Part Stream Definition (for Listing)|
    ///                                | Part Stream Blocks*                 |
    /// ----------------------------------------------------------------------
    /// Listing Part Stream            | Part Stream Count                   |
    ///                                | Part Stream Definition(x Part Count)|
    /// ----------------------------------------------------------------------
    /// Part Stream Definition         | Block Size                          |
    ///                                | Length                              |
    ///                                | Block Count                         |
    ///                                | Block Start Position(x Block Count) |
    /// ----------------------------------------------------------------------    
    /// </remarks>
    public partial class CompoundStream : IDisposable
    {
        /// <summary>
        /// Defines the length of the predefined region which contains information for creating the
        /// listing part stream which contains the information about blocks in the compound stream
        /// </summary>
        private const int ListingPartStreamInitializationRegionLength = 4096;

        /// <summary>
        /// The default block size for part streams.
        /// </summary>
        public const int DefaultBlockSize = 16 * 1024;

        /// <summary>
        /// Size of blocks for the listing stream
        /// </summary>
        private const int ListingPartStreamBlockSize = DefaultBlockSize;

        /// <summary>
        /// The value returned by <see cref="PartStream.Index"/> for the listing stream.
        /// </summary>
        private const int ListingPartStreamIndex = -1;

        private const int DefaultPartStreamIndex = 0;

        /// <summary>
        /// The part streams
        /// </summary>
        private readonly List<PartStreamImpl> m_parts = new List<PartStreamImpl>();

        /// <summary>
        /// The start position of the next block which is allocated with <see cref="AllocateBlock(int)"/>
        /// </summary>
        private long m_nextBlockStart;

        /// <summary>
        /// Lock for controlling access when writing or allocating blocks
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// Lock for creating new writable part streams
        /// </summary>
        private readonly object m_createWritePartLock = new object();

        /// <summary>
        /// The start position in the underlying stream of the compound stream
        /// </summary>
        private readonly long m_startPosition;

        /// <summary>
        /// Default part stream created for every <see cref="CompoundStream"/> instance for writing top level
        /// data such as listing information and part stream indices (see <see cref="PartStream.Index"/>) for use
        /// when reading (see <see cref="OpenReadPartStream(int)"/>)
        /// </summary>
        public Stream InitialPartStream { get; }

        #region Write State

        /// <summary>
        /// The global underlying stream for the compound stream from which bytes can be read 
        /// </summary>
        private Stream m_writeStream;

        private PartStreamImpl m_listingPartStream;

        #endregion

        #region Read State

        private readonly Func<Stream> m_readStreamFactory;

        #endregion

        /// <summary>
        /// Create a compound stream for write
        /// </summary>
        private CompoundStream(Stream writeStream, int initialStreamBlockSize)
        {
            Contract.Requires(writeStream.CanSeek, "Compound stream requires a seekable stream");

            m_writeStream = writeStream;
            m_startPosition = m_writeStream.Position;
            m_nextBlockStart = m_startPosition;

            // Allow initial region for blocks of listing block starts
            AllocateBlock(ListingPartStreamInitializationRegionLength);

            m_listingPartStream = new PartStreamImpl(this, ListingPartStreamIndex, blockSize: ListingPartStreamBlockSize);

            var initialPartStream = (PartStreamImpl)CreateWritePartStream(initialStreamBlockSize);
            Contract.Assert(initialPartStream.Index == DefaultPartStreamIndex);
            InitialPartStream = initialPartStream;
        }

        /// <summary>
        /// Create a compound stream for read
        /// </summary>
        private CompoundStream(Func<Stream> readStreamFactory, long startPosition = 0)
        {
            m_readStreamFactory = readStreamFactory;

            using (var listingPartBaseReadStream = m_readStreamFactory())
            {
                m_startPosition = startPosition;
                listingPartBaseReadStream.Position = startPosition;

                using (var listingPartReader = BuildXLReader.Create(listingPartBaseReadStream))
                using (var listingPartStream = PartStreamImpl.ReadPartStream(this, ListingPartStreamIndex, listingPartReader))
                {
                    listingPartStream.InitializeRead(listingPartBaseReadStream);
                    ReadParts(listingPartStream);
                }
            }

            InitialPartStream = OpenReadPartStream(DefaultPartStreamIndex);
        }

        /// <summary>
        /// Opens a compound stream for read with the given factory for opening independent
        /// streams to read from. 
        /// NOTE: The streams must be seekable and independent (i.e. support concurrent access of
        /// different streams on multiple threads) if part streams will be read concurrently.
        /// </summary>
        /// <param name="readStreamFactory">factory for creating independent streams</param>
        /// <param name="startPosition">the start position of the compound stream</param>
        public static CompoundStream OpenRead(Func<Stream> readStreamFactory, long startPosition = 0)
        {
            Contract.Requires(readStreamFactory != null);
            return new CompoundStream(readStreamFactory, startPosition: startPosition);
        }

        /// <summary>
        /// Opens a compound stream for write on the given underlying write stream.
        /// NOTE: The stream must be seekable.
        /// </summary>
        /// <param name="writeStream">the stream to write the compound and nested part stream data to</param>
        /// <param name="initialStreamBlockSize">the size of blocks for the initial stream</param>
        /// <returns>a compound stream which writes to the given underlying stream</returns>
        public static CompoundStream OpenWrite(Stream writeStream, int initialStreamBlockSize = DefaultBlockSize)
        {
            Contract.Requires(writeStream != null);
            // Wrap stream in tracked stream to avoid unnecessary seeking
            return new CompoundStream(writeStream, initialStreamBlockSize);
        }

        private void ReadParts(PartStreamImpl listingPartStream)
        {
            using (var reader = BuildXLReader.Create(listingPartStream))
            {
                var partStreamCount = reader.ReadInt32();
                for (int i = 0; i < partStreamCount; i++)
                {
                    m_parts.Add(PartStreamImpl.ReadPartStream(this, i, reader));
                }
            }
        }

        private void WriteParts()
        {
            using (var writer = BuildXLWriter.Create(m_listingPartStream))
            {
                writer.Write(m_parts.Count);
                foreach (var part in m_parts)
                {
                    part.WritePartStreamData(writer);
                }
            }
        }

        /// <summary>
        /// Opens a part stream corresponding to the <see cref="PartStream.Index"/> of the
        /// <see cref="PartStream"/>returned from <see cref="CreateWritePartStream(int)"/> during serialization
        /// </summary>
        public Stream OpenReadPartStream(int partStreamIndex)
        {
            Contract.Assert(partStreamIndex >= 0 && partStreamIndex < m_parts.Count, "Invalid part id");
            var partStream = m_parts[partStreamIndex];
            partStream.InitializeRead();
            return partStream;
        }

        /// <summary>
        /// Creates a part stream using the specified block size.
        /// </summary>
        /// <remarks>
        /// Ensure that <see cref="PartStream.Index"/> is persisted in order to open the part stream
        /// during deserialization using <see cref="OpenReadPartStream(int)"/>
        /// </remarks>
        public PartStream CreateWritePartStream(int blockSize = DefaultBlockSize)
        {
            Contract.Requires(blockSize >= 1024, "Part block size must be >= 1024 bytes");

            lock (m_createWritePartLock)
            {
                var partId = m_parts.Count;
                var partStream = new PartStreamImpl(this, partId, blockSize);
                m_parts.Add(partStream);
                return partStream;
            }
        }

        /// <summary>
        /// Reserve the given amount of bytes and return the start position of the reserved region
        /// </summary>
        private long AllocateBlock(int blockSize)
        {
            lock (m_lock)
            {
                var blockStart = m_nextBlockStart;
                m_nextBlockStart += blockSize;
                return blockStart;
            }
        }

        /// <summary>
        /// Writes a block for the current buffer in the part stream
        /// </summary>
        private long WriteBlock(PartStreamImpl partStream, bool finalBlock = false)
        {
            lock (m_lock)
            {
                var buffer = partStream.CurrentBlockWriteBufferStream.GetBuffer();
                Contract.Assert(finalBlock || partStream.BlockSize == buffer.Length);

                var blockStart = AllocateBlock(buffer.Length);
                m_writeStream.Seek(blockStart, SeekOrigin.Begin);
                m_writeStream.Write(buffer, 0, (int)partStream.CurrentBlockWriteBufferStream.Position);
                return blockStart;
            }
        }

        /// <summary>
        /// Dispose the compound stream flushing all data to the underlying stream.
        /// </summary>
        public void Dispose()
        {
            InitialPartStream.Dispose();

            if (m_writeStream != null)
            {
                using (m_listingPartStream)
                {
                    WriteParts();
                }

                m_writeStream.Seek(m_startPosition, SeekOrigin.Begin);
                using (var writer = BuildXLWriter.Create(m_writeStream, leaveOpen: true))
                {
                    // Writing the position of the ListingPartStream's blocks at the start of the compound stream in the of the underlying stream
                    m_listingPartStream.WritePartStreamData(writer);
                }
                
                // Seek to end of compound stream on close. This mimics the behavior if the compound stream was
                // simply a sequential process which wrote to the stream
                m_writeStream.Seek(m_nextBlockStart, SeekOrigin.Begin);
                m_writeStream = null;
            }
        }
    }
}
