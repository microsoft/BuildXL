// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
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
        private static int s_entrySize = TryComputeEntrySize();

        /// <summary>
        /// The default number of bits for an entry buffer
        /// </summary>
        public const int DefaultEntriesPerBufferBitWidth = 12;

        /// <summary>
        /// The default number of items in an entry buffer.
        /// </summary>
        public static int DefaultEntriesPerBuffer = 1 << DefaultEntriesPerBufferBitWidth;

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

        /// <summary>
        /// Entries. Keeping the entries as a lazy array to avoid excessive allocations when the arrays are not actually being used.
        /// </summary>
        private Lazy<TEntry[]>[] m_entryBuffers;

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
        /// Gets the number of entries per buffer.
        /// </summary>
        public int EntriesPerBuffer => m_entriesPerBuffer;

        /// <summary>
        /// Creates a new concurrent block allocator
        /// </summary>
        /// <param name="entriesPerBufferBitWidth">the bit width of number of entries in a buffer (buffer size is 2^<paramref name="entriesPerBufferBitWidth"/>)</param>
        /// <param name="initialBufferSlotCount">the initial number of buffer slots</param>
        public BigBuffer(int entriesPerBufferBitWidth, int initialBufferSlotCount = 4)
        {
            Contract.Requires(entriesPerBufferBitWidth > 0 && entriesPerBufferBitWidth < 32);
            Contract.Requires(initialBufferSlotCount > 0);

            m_entriesPerBufferBitWidth = entriesPerBufferBitWidth;
            m_entriesPerBuffer = 1 << m_entriesPerBufferBitWidth;
            m_entriesPerBufferMask = m_entriesPerBuffer - 1;

            m_entryBuffers = Resize(initialBufferSlotCount);
            m_accessor = new Accessor(this);
        }

        /// <summary>
        /// Creates a new concurrent block allocator
        /// </summary>
        public BigBuffer(int? entriesPerBufferBitWidth)
            : this(entriesPerBufferBitWidth ?? ComputeDefaultEntriesPerBufferBitWidth())
        {
        }
        
        /// <summary>
        /// Creates a new concurrent block allocator
        /// </summary>
        public BigBuffer()
            : this(ComputeDefaultEntriesPerBufferBitWidth())
        {
        }

        /// <summary>
        /// A helper method that computes the default buffer bit width to avoid allocating an array in the LOH.
        /// </summary>
        internal static int ComputeDefaultEntriesPerBufferBitWidth(int defaultEntriesPerBufferBitWidth = DefaultEntriesPerBufferBitWidth)
        {
            if (s_entrySize == -1)
            {
                return defaultEntriesPerBufferBitWidth;
            }

            try
            {
                // (2 ^ x) * sizeof(T) < 85K
                // x = log(85K / sizeof(T)) / log(2)
                const int LargeObjectHeapLimit = 85_000;
                return (int)(Math.Log((double)LargeObjectHeapLimit / s_entrySize) / Math.Log(2));
            }
            catch
            {
                // Making sure that any issues with type inspection won't break anything.
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                return defaultEntriesPerBufferBitWidth;
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

            }
        }

        private static int TryComputeEntrySize()
        {
            try
            {
                return TypeInspector.GetSize(typeof(TEntry)).size;
            }
            catch
            {
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                return -1;
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
            }
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                GetBufferNumberAndEntryIndexFromId(index, out int bufferNumber, out int entryIndex);
                return m_entryBuffers[bufferNumber].Value[entryIndex];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                GetBufferNumberAndEntryIndexFromId(index, out int bufferNumber, out int entryIndex);
                m_entryBuffers[bufferNumber].Value[entryIndex] = value;
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
        public int Initialize(int minimumCapacity, BufferInitializer? initializer = null, bool initializeSequentially = false)
        {
            if (m_capacity < minimumCapacity)
            {
                // This method is called a lot. Making it inline friendly.
                initializeSlow();
            }

            return m_capacity;

            void initializeSlow()
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
        }
        
        private void InternalInitializeToNewCapacity(int newCapacity, BufferInitializer? initializer, bool initializeSequentially)
        {
            Resize(newCapacity / m_entriesPerBuffer);

            Action<int> initBuffer =
                bufferNumber =>
                {
                    Lazy<TEntry[]> value;
                    if (initializer != null)
                    {
                        // initializer might have a side effect (like reading actual elements during deserialization)
                        // so we have to call it eagerly.
                        var result = initializer(bufferNumber * m_entriesPerBuffer, m_entriesPerBuffer);
                        value = new Lazy<TEntry[]>(() => result);
                    }
                    else
                    {
                        value = new Lazy<TEntry[]>(() => new TEntry[m_entriesPerBuffer]);
                    }

                    m_entryBuffers[bufferNumber] = value;
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

        private Lazy<TEntry[]>[] Resize(int newSize)
        {
            var newEntryBuffers = m_entryBuffers;
            Array.Resize(ref newEntryBuffers, newSize);
            m_entryBuffers = newEntryBuffers;
            return m_entryBuffers;
        }

        /// <summary>
        /// Gets the entry buffer at the given index and the corresponding index in the entry buffer
        /// </summary>
        /// <param name="index">the index in the big buffer</param>
        public BufferPointer<TEntry> GetBufferPointer(int index)
        {
            GetBufferNumberAndEntryIndexFromId(index, out int bufferNumber, out int entryIndex);
            Lazy<TEntry[]> entryBuffer = m_entryBuffers[bufferNumber];
            return new BufferPointer<TEntry>(entryBuffer.Value, entryIndex);
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
            entryBuffer = m_entryBuffers[bufferNumber].Value;
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
            private TEntry[]? m_lastBuffer;

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
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    GetEntryBuffer(index, out int entryIndex, out TEntry[] entryBuffer);

                    return entryBuffer[entryIndex];
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                entryBuffer = m_lastBuffer!;
                if (bufferNumber != m_lastBufferNumber)
                {
                    entryBuffer = Buffer.m_entryBuffers[bufferNumber].Value;
                    m_lastBuffer = entryBuffer;
                    m_lastBufferNumber = bufferNumber;
                }
            }
        }
    }
}
