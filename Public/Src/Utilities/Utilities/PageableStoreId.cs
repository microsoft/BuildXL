// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    internal readonly struct PageableStoreId
    {
        internal readonly uint Value;

        public bool IsValid => Value > 0;

        internal PageableStoreId(uint value)
        {
            Contract.Requires(value > 0);
            Value = value;
        }

        internal static PageableStoreId Deserialize(BinaryReader reader)
        {
            uint value = reader.ReadUInt32();
            return new PageableStoreId(value);
        }

        internal void Serialize(BinaryWriter writer)
        {
            Contract.Requires(IsValid, "Invalid PageableStoreId can not be serialized");
            writer.Write(Value);
        }
    }

    /// <summary>
    /// A repository for binary representations of items.
    /// </summary>
    /// <remarks>
    /// All methods of this class are thread-safe. There can be many concurrent readers and writers.
    /// The binary representations are stored in an efficient way.
    /// When held in memory, the OS may page out large chunks.
    /// When deserializing a previously serialized store,
    /// there is an option to defer actual loading from disk until a particular item is queried.
    /// (TODO: Also implement option to write through items to disk as they are written, and release the memory eagerly.)
    /// Internally, the store maintains multiple page streams (which eventually get sealed to byte arrays);
    /// each stream may contain multiple items.
    /// </remarks>
    internal abstract class PageableStore
    {
        public const uint EntryMarker = 0x12345678;

        private readonly int m_initialBufferSize;
        private readonly List<PageStreamBase> m_pageStreams = new List<PageStreamBase>();
        private readonly ConcurrentStack<PageStreamBase> m_availableWritablePageStreams = new ConcurrentStack<PageStreamBase>();
        private readonly ConcurrentDenseIndex<ItemLocation> m_itemLocations;
        private int m_lastId;

        /// <summary>
        /// Whether to embed additional Debug payload into the streams (markers before and after each data type).
        /// </summary>
        /// <remarks>
        /// Note that this will (by design) increase the size of the payload significantly.
        /// </remarks>
        public readonly bool Debug;

        public readonly bool CanWrite;

        protected PageableStore()
        {
            throw Contract.AssertFailure("Do not call --- only here to make it easy to write code contracts");
        }

        protected PageableStore(PathTable pathTable, SymbolTable symbolTable, int initialBufferSize, bool debug)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(symbolTable != null);
            Contract.Requires(initialBufferSize >= 0);
            PathTable = pathTable;
            SymbolTable = symbolTable;
            m_initialBufferSize = initialBufferSize;
            m_itemLocations = new ConcurrentDenseIndex<ItemLocation>(debug);
            Debug = debug;
            CanWrite = true;
        }

        /// <summary>
        /// Constructor used when deserializing a PageableStore
        /// </summary>
        protected PageableStore(PathTable pathTable, SymbolTable symbolTable, SerializedState state, int initialBufferSize)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(symbolTable != null);
            PathTable = pathTable;
            SymbolTable = symbolTable;
            m_lastId = state.LastId;

            // We expect no more writes after deserializing
            m_initialBufferSize = initialBufferSize;
            m_itemLocations = state.ItemLocations;
            Debug = state.Debug;
            m_pageStreams = state.PageStreams;
        }

        public PathTable PathTable { get; }

        public StringTable StringTable => PathTable.StringTable;

        public SymbolTable SymbolTable { get; }

        /// <summary>
        /// A page stream holds the binary representation of some subset of all stored items
        /// </summary>
        protected abstract class PageStreamBase
        {
            /// <summary>
            /// How many bytes are needed to represent the binary representations stored in this page stream.
            /// </summary>
            public abstract int Length { get; }

            /// <summary>
            /// Reads an item.
            /// </summary>
            public abstract T Read<T>(PageableStore store, int offset, Func<BuildXLReader, T> deserializer);

            /// <summary>
            /// Whether this page stream is writable.
            /// </summary>
            public virtual bool CanWrite => false;

            /// <summary>
            /// Writes an item.
            /// </summary>
            public virtual int Write(PageableStore store, Action<BuildXLWriter> serializer, out bool gotSealed)
            {
                Contract.Requires(CanWrite);
                gotSealed = false;
                return 0;
            }

            /// <summary>
            /// Read away debugging marker if needed
            /// </summary>
            protected static void ReadEntryMarker(PageableStore store, BuildXLReader reader)
            {
                if (store.Debug)
                {
                    uint marker = reader.ReadUInt32();
                    Contract.Assume(marker == EntryMarker);
                }
            }

            /// <summary>
            /// Writes debugging marker if needed
            /// </summary>
            protected static void WriteEntryMarker(PageableStore store, BuildXLWriter writer)
            {
                if (store.Debug)
                {
                    writer.Write(EntryMarker);
                }
            }

            /// <summary>
            /// Write the binary representations stored in this page store.
            /// </summary>
            public abstract void Serialize(BinaryWriter writer);
        }

        /// <summary>
        /// A memory page stream holds the binary representation of its stored items in memory. It offers a function to write new items.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
            Justification = "A MemoryStream doesn't actually need to be disposed.")]
        private sealed class MemoryPageStream : PageStreamBase
        {
            #region State used while writing

            private MemoryStream m_stream;
            private BuildXLWriter m_writer;
            private int m_length;
            private int m_capacity;
            #endregion

            #region Serialization

            public override void Serialize(BinaryWriter writer)
            {
                if (m_writer != null)
                {
                    Seal();
                }

                Contract.Assert(m_length <= m_buffer.Length);
                writer.Write(m_buffer, 0, m_length);
            }

            public static MemoryPageStream Deserialize(BinaryReader reader, int length)
            {
                byte[] buffer = reader.ReadBytes(length);
                return new MemoryPageStream(buffer, length);
            }

            private MemoryPageStream(byte[] buffer, int used)
            {
                m_buffer = buffer;
                m_length = m_capacity = used;
            }

            #endregion

            #region Steady state used for reading

            // There may be multiple concurrent readers.
            // Until sealed, m_buffer is a pointer to a byte array for which there might be an active writer.
            private byte[] m_buffer;

            #endregion

            private byte[] GetBuffer()
            {
                return m_buffer;
            }

            public override int Length => m_length;

            public int AllocatedLength => m_capacity;

            public MemoryPageStream(PageableStore store)
            {
                Contract.Requires(store != null);
                Contract.Requires(store.CanWrite);
                Contract.Ensures(CanWrite);
                Contract.Assume(
                    store.m_initialBufferSize > 0,
                    "Initial buffer size for store was 0. This implies an attempt to write to a non-writable store.");
                m_stream = new MemoryStream(store.m_initialBufferSize);
                m_writer = store.CreateWriter(m_stream, leaveOpen: true);
                m_buffer = m_stream.GetBuffer();
            }

            public override T Read<T>(PageableStore store, int offset, Func<BuildXLReader, T> deserializer)
            {
                var buffer = GetBuffer();
                using (var stream = new MemoryStream(buffer, writable: false, index: offset, count: buffer.Length - offset))
                using (var reader = store.CreateReader(stream, leaveOpen: true))
                {
                    ReadEntryMarker(store, reader);
                    T value = deserializer(reader);
                    Contract.Assume(reader.Depth == 0);
                    return value;
                }
            }

            public override bool CanWrite => m_writer != null;

            public override int Write(PageableStore store, Action<BuildXLWriter> serializer, out bool gotSealed)
            {
                lock (this)
                {
                    m_stream.Seek(0, SeekOrigin.End);
                    int start = (int)m_stream.Position;
                    WriteEntryMarker(store, m_writer);

                    int depth = m_writer.Depth;
                    serializer(m_writer);
                    Contract.Assert(depth == m_writer.Depth);
                    m_writer.Flush();

                    // Make sure that whoever may pick up m_buffer concurrently will see the actual buffer contents
                    Volatile.Write(ref m_buffer, m_stream.GetBuffer());

                    m_length = (int)m_stream.Length;
                    m_capacity = m_stream.Capacity;

                    if (m_stream.Length * 1D / m_capacity > 0.9)
                    {
                        Seal();
                        gotSealed = true;
                    }
                    else
                    {
                        // Otherwise, make it available for more writes.
                        gotSealed = false;
                    }

                    return start;
                }
            }

            private void Seal()
            {
                Contract.Ensures(m_buffer != null);
                m_writer.Dispose();

                // Calling GetBuffer has side effect of making sure m_buffer is defined.
                Analysis.IgnoreResult(GetBuffer());
                m_stream.Dispose();
                m_writer = null;
                m_stream = null;
            }
        }

        /// <summary>
        /// All the information needed to find a serialized item
        /// </summary>
        protected readonly struct ItemLocation
        {
            public readonly PageStreamBase PageStream;
            public readonly int Offset;

            public ItemLocation(PageStreamBase pageStream, int offset)
            {
                Contract.Requires(pageStream != null);
                Contract.Requires(offset >= 0);
                PageStream = pageStream;
                Offset = offset;
            }
        }

        protected abstract BuildXLWriter CreateWriter(Stream stream, bool leaveOpen);

        protected abstract BuildXLReader CreateReader(Stream stream, bool leaveOpen);

        public int PageStreamsCount
        {
            get
            {
                lock (m_pageStreams)
                {
                    return m_pageStreams.Count;
                }
            }
        }

        private IEnumerable<MemoryPageStream> GetMemoryPageStreams()
        {
            foreach (var s in m_pageStreams)
            {
                var mps = s as MemoryPageStream;
                if (mps != null)
                {
                    yield return mps;
                }
            }
        }

        public long MemorySize
        {
            get
            {
                lock (m_pageStreams)
                {
                    return GetMemoryPageStreams().Sum(s => (long)s.AllocatedLength);
                }
            }
        }

        public long MemoryUsed
        {
            get
            {
                lock (m_pageStreams)
                {
                    return GetMemoryPageStreams().Sum(s => (long)s.Length);
                }
            }
        }

        [Pure]
        public bool Contains(PageableStoreId id)
        {
            Contract.Requires(id.IsValid);
            return id.Value > 0 && id.Value <= m_lastId;
        }

        /// <summary>
        /// Invokes a serializer to store some data.
        /// </summary>
        /// <returns>Unique id for stored value.</returns>
        public PageableStoreId Write(Action<BuildXLWriter> serializer)
        {
            Contract.Requires(serializer != null);
            Contract.Ensures(Contract.Result<PageableStoreId>().IsValid);

            PageStreamBase writablePageStream;
            if (!m_availableWritablePageStreams.TryPop(out writablePageStream))
            {
                writablePageStream = new MemoryPageStream(this);
                Contract.Assert(writablePageStream.CanWrite);
                lock (m_pageStreams)
                {
                    m_pageStreams.Add(writablePageStream);
                }
            }

            Contract.Assume(writablePageStream != null);

            var idValue = (uint)Interlocked.Increment(ref m_lastId);
            Contract.Assume(idValue > 0);
            bool gotSealed;
            int offset = writablePageStream.Write(this, serializer, out gotSealed);
            m_itemLocations[idValue] = new ItemLocation(writablePageStream, offset);
            if (!gotSealed)
            {
                m_availableWritablePageStreams.Push(writablePageStream);
            }

            return new PageableStoreId(idValue);
        }

        /// <summary>
        /// Given an id obtained by some previous <code>Write</code> and a deserializer, recreates some data.
        /// </summary>
        /// <returns>Deserialized data.</returns>
        public T Read<T>(PageableStoreId id, Func<BuildXLReader, T> deserializer)
        {
            Contract.Requires(id.IsValid);
            Contract.Requires(Contains(id));
            Contract.Requires(deserializer != null);
            ItemLocation itemLocation = m_itemLocations[id.Value];
            return itemLocation.PageStream.Read<T>(this, itemLocation.Offset, deserializer);
        }

        #region Serialization

        /// <summary>
        /// State persisted when saving/reloading a PageableStore
        /// </summary>
        protected sealed class SerializedState
        {
            /// <summary>
            /// Debug
            /// </summary>
            public bool Debug;

            /// <summary>
            /// Streams containing underlying data
            /// </summary>
            public List<PageStreamBase> PageStreams;

            /// <summary>
            /// Offsets into streams where data is held
            /// </summary>
            public ConcurrentDenseIndex<ItemLocation> ItemLocations;

            /// <summary>
            /// Last offset ID given out
            /// </summary>
            public int LastId;
        }

        /// <summary>
        /// Serializes
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            lock (m_pageStreams)
            {
                Serialize(writer, new SerializedState()
                    {
                        Debug = Debug,
                        PageStreams = m_pageStreams,
                        ItemLocations = m_itemLocations,
                        LastId = m_lastId,
                    });
            }
        }

        private static void Serialize(BuildXLWriter writer, SerializedState state)
        {
            writer.Write(state.Debug);

            var count = state.PageStreams.Count;
            writer.Write(count);
            foreach (var stream in state.PageStreams)
            {
                writer.Write(stream.Length);
            }

            long position = writer.BaseStream.Position;

            // Serialize each stream and assign it an identifier that can be looked up later
            var pageStreamIdentifiers = new Dictionary<PageStreamBase, int>();
            for (int i = 0; i < count; i++)
            {
                pageStreamIdentifiers.Add(state.PageStreams[i], i);
                state.PageStreams[i].Serialize(writer);
                var expectedPosition = position + state.PageStreams[i].Length;
                Contract.Assert(expectedPosition == writer.BaseStream.Position);
                position = expectedPosition;
            }

            // Serialize the item location offsets
            writer.Write(state.LastId);
            for (uint i = 0; i < state.LastId; i++)
            {
                ItemLocation offset = state.ItemLocations[i + 1];
                var id = pageStreamIdentifiers[offset.PageStream];
                writer.Write(id);
                writer.Write(offset.Offset);
            }
        }

        /// <summary>
        /// Reads the serialized state
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        protected static SerializedState ReadSerializedState(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            bool debug = reader.ReadBoolean();
            int streamCount = reader.ReadInt32();
            int[] pageStreamLengths = new int[streamCount];
            for (int i = 0; i < streamCount; i++)
            {
                pageStreamLengths[i] = reader.ReadInt32();
            }

            List<PageStreamBase> streams = new List<PageStreamBase>(streamCount);

            for (int i = 0; i < streamCount; i++)
            {
                streams.Add(MemoryPageStream.Deserialize(reader, pageStreamLengths[i]));
            }

            Contract.Assert(streams.Count == streamCount);
            int lastId = reader.ReadInt32();
            ConcurrentDenseIndex<ItemLocation> offsets = new ConcurrentDenseIndex<ItemLocation>(debug);
            for (uint i = 0; i < lastId; i++)
            {
                PageableStoreId id = new PageableStoreId(i + 1);

                var streamIdentifier = reader.ReadInt32();
                var offset = reader.ReadInt32();

                offsets[id.Value] = new ItemLocation(streams[streamIdentifier], offset);
            }

            return new SerializedState()
                   {
                       Debug = debug,
                       PageStreams = streams,
                       ItemLocations = offsets,
                       LastId = lastId,
                   };
        }

        #endregion
    }
}
