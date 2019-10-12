// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Represents a structure capable of allocating blocks for containing a large sequence of entries
    /// </summary>
    /// <typeparam name="TEntry">the entry type</typeparam>
    public sealed class BigBuffer<TEntry>
    {
        /// <summary>
        /// The default number of bits for an entry buffer
        /// </summary>
        public const int DefaultEntriesPerBufferBitWidth = 12;

        /// <summary>
        /// Create a new entry buffer for a range
        /// </summary>
        public delegate TEntry[] BufferInitializer(int start, int count);

        /// <summary>
        /// Lock used to protect expansion
        /// </summary>
        private readonly object m_syncRoot = new object();

        // Constants
        private readonly int m_entriesPerBufferBitWidth;
        private readonly int m_entriesPerBufferMask;
        private readonly int m_entriesPerBuffer;

        private TEntry[][] m_entryBuffers;

        /// <summary>
        /// The current capacity of the buffer
        /// </summary>
        private int m_capacity = 0;

        private readonly Accessor m_accessor;

        /// <summary>
        /// The bit width of number of entries in a buffer (buffer size is 2^<ref name="EntriesPerBufferBitWidth"/>).
        /// </summary>
        public int EntriesPerBufferBitWidth => m_entriesPerBufferBitWidth;

        /// <summary>
        /// Creates a new concurrent block allocator
        /// </summary>
        /// <param name="entriesPerBufferBitWidth">the bit width of number of entries in a buffer (buffer size is 2^<paramref name="entriesPerBufferBitWidth"/>)</param>
        /// <param name="initialBufferSlotCount">the initial number of buffer slots</param>
        public BigBuffer(int entriesPerBufferBitWidth = DefaultEntriesPerBufferBitWidth, int initialBufferSlotCount = 4)
        {
            Contract.Requires(entriesPerBufferBitWidth > 0 && entriesPerBufferBitWidth < 32);
            Contract.Requires(initialBufferSlotCount > 0);

            m_entriesPerBufferBitWidth = entriesPerBufferBitWidth;
            m_entriesPerBuffer = 1 << m_entriesPerBufferBitWidth;
            m_entriesPerBufferMask = m_entriesPerBuffer - 1;
            Resize(initialBufferSlotCount);
            m_accessor = new Accessor(this);
        }

        /// <summary>
        /// Gets the capacity of the buffer
        /// </summary>
        public int Capacity => Volatile.Read(ref m_capacity);

        /// <summary>
        /// Gets the number of internal buffers;
        /// </summary>
        public int NumberOfBuffers => Volatile.Read(ref m_capacity) / m_entriesPerBuffer;

        /// <summary>
        /// Gets or sets the entry for the given index
        /// </summary>
        public TEntry this[int index]
        {
            get
            {
                GetBufferNumberAndEntryIndexFromId(index, out int bufferNumber, out int entryIndex);
                return m_entryBuffers[bufferNumber][entryIndex];
            }

            set
            {
                GetBufferNumberAndEntryIndexFromId(index, out int bufferNumber, out int entryIndex);
                m_entryBuffers[bufferNumber][entryIndex] = value;
            }
        }

        /// <summary>
        /// Ensures that the buffer has the given capacity
        /// NOTE: This method must be called prior to getting or setting entries in the buffer
        /// </summary>
        /// <param name="minimumCapacity">Minimum capacity</param>
        /// <param name="initializer">Optional initializer for new buffers</param>
        /// <param name="initializeSequentially">Whether the initializer needs to run sequentially</param>
        /// <returns>Actual new capacity</returns>
        public int Initialize(int minimumCapacity, BufferInitializer initializer = null, bool initializeSequentially = false)
        {
            if (m_capacity < minimumCapacity)
            {
                lock (m_syncRoot)
                {
                    var currentCapacity = m_capacity;
                    var newCapacity = checked(minimumCapacity + m_entriesPerBuffer - 1) & ~m_entriesPerBufferMask;
                    if (currentCapacity < newCapacity)
                    {
                        // No more entry buffers. We need to allocate more.
                        // At least twice as many, but maybe more depending on requested capacity
                        newCapacity = Math.Max(m_entryBuffers.Length * m_entriesPerBuffer * 2, newCapacity);
                        InternalInitializeToNewCapacity(newCapacity, initializer, initializeSequentially);
                    }
                }
            }

            return m_capacity;
        }

        private void InternalInitializeToNewCapacity(int newCapacity, BufferInitializer initializer, bool initializeSequentially)
        {
            Resize(newCapacity / m_entriesPerBuffer);

            Action<int> initBuffer =
                bufferNumber =>
                {
                    m_entryBuffers[bufferNumber] = initializer != null
                        ? initializer(bufferNumber * m_entriesPerBuffer, m_entriesPerBuffer)
                        : new TEntry[m_entriesPerBuffer];
                };

            var start = m_capacity / m_entriesPerBuffer;
            var end = newCapacity / m_entriesPerBuffer;
            var count = end - start;
            if ((count > 16 || (count > 1 && initializer != null)) && !initializeSequentially)
            {
                // parallelization initialization of buffers brings significant improvement during deserialization where big buffer sizes are known beforehand
                // (seen initialization of directed graph edges speed up from 300ms to 90ms)
                Parallel.For(start, end, initBuffer);
            }
            else
            {
                for (int bufferNumber = start; bufferNumber < end; bufferNumber++)
                {
                    initBuffer(bufferNumber);
                }
            }

            m_capacity = newCapacity;
        }

        private void Resize(int newSize)
        {
            var newEntryBuffers = m_entryBuffers;
            Array.Resize(ref newEntryBuffers, newSize);
            m_entryBuffers = newEntryBuffers;
        }

        /// <summary>
        /// Gets the entry buffer at the given index and the corresponding index in the entry buffer
        /// </summary>
        /// <param name="index">the index in the big buffer</param>
        public BufferPointer<TEntry> GetBufferPointer(int index)
        {

            GetBufferNumberAndEntryIndexFromId(index, out int bufferNumber, out int entryIndex);
            TEntry[] entryBuffer = m_entryBuffers[bufferNumber];
            return new BufferPointer<TEntry>(entryBuffer, entryIndex);
        }

        /// <summary>
        /// Gets the entry buffer at the given index and the corresponding index in the entry buffer
        /// </summary>
        /// <param name="index">the index in the big buffer</param>
        /// <param name="entryIndex">the entry buffer which contains the entry at the given index</param>
        /// <param name="entryBuffer">the index in the entry buffer which corresponds to the given index</param>
        public void GetEntryBuffer(int index, out int entryIndex, out TEntry[] entryBuffer)
        {

            GetBufferNumberAndEntryIndexFromId(index, out int bufferNumber, out entryIndex);
            entryBuffer = m_entryBuffers[bufferNumber];
        }

        private void GetBufferNumberAndEntryIndexFromId(int current, out int bufferNum, out int entryIndex)
        {
            bufferNum = current >> m_entriesPerBufferBitWidth;
            entryIndex = current & m_entriesPerBufferMask;
        }

        private int GetIdFromArrayAndEntryIndex(int bufferNum, int entryIndex)
        {
            return (bufferNum << m_entriesPerBufferBitWidth) + entryIndex;
        }

        /// <summary>
        /// Gets an accessor used to provide optimized access to the buffer's entry buffers
        /// </summary>
        public Accessor GetAccessor()
        {
            return m_accessor;
        }

        /// <summary>
        /// Accessor used for optimized access to a big buffer
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct Accessor
        {
            /// <summary>
            /// Gets the buffer associated with the accessor
            /// </summary>
            public readonly BigBuffer<TEntry> Buffer;

            /// <summary>
            /// The buffer number of the last entry buffer requested for the accessor
            /// </summary>
            private int m_lastBufferNumber;

            /// <summary>
            /// The last entry buffer requested for the accessor
            /// </summary>
            private TEntry[] m_lastBuffer;

            /// <summary>
            /// Constructor
            /// </summary>
            public Accessor(BigBuffer<TEntry> buffer)
            {
                Buffer = buffer;
                m_lastBufferNumber = -1;
                m_lastBuffer = null;
            }

            /// <summary>
            /// Gets or sets the entry for the given index in the big buffer
            /// </summary>
            public TEntry this[int index]
            {
                get
                {
                    GetEntryBuffer(index, out int entryIndex, out TEntry[] entryBuffer);

                    return entryBuffer[entryIndex];
                }

                set
                {
                    GetEntryBuffer(index, out int entryIndex, out TEntry[] entryBuffer);

                    entryBuffer[entryIndex] = value;
                }
            }

            /// <summary>
            /// Gets the entry buffer at the given index and the corresponding index in the entry buffer
            /// </summary>
            /// <param name="index">the index in the big buffer</param>
            /// <param name="entryIndex">the entry buffer which contains the entry at the given index</param>
            /// <param name="entryBuffer">the index in the entry buffer which corresponds to the given index</param>
            public void GetEntryBuffer(int index, out int entryIndex, out TEntry[] entryBuffer)
            {

                Buffer.GetBufferNumberAndEntryIndexFromId(index, out int bufferNumber, out entryIndex);
                entryBuffer = m_lastBuffer;
                if (bufferNumber != m_lastBufferNumber)
                {
                    entryBuffer = Buffer.m_entryBuffers[bufferNumber];
                    m_lastBuffer = entryBuffer;
                    m_lastBufferNumber = bufferNumber;
                }
            }
        }
    }
}
