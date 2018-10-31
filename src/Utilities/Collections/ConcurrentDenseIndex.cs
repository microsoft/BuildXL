// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// A dynamically growing concurrent dictionary, indexed by 32-bit integers.
    /// </summary>
    /// <remarks>
    /// All instance methods of this class are thread-safe.
    /// While the buffer allows for random indexing accessing patterns, it is by far most efficient to populate it with increasing
    /// indices, starting from 0.
    /// The buffer is backed by an array of arrays.
    /// </remarks>
    public sealed class ConcurrentDenseIndex<TItem>
    {
        internal const int DefaultBuffersCount = 4096;
        private const int BitsPerBuffer = 12;
        private const int BufferSelectorBits = 32 - BitsPerBuffer;
        private const uint EntriesPerBuffer = 1 << BitsPerBuffer;
        private const uint BufferMask = EntriesPerBuffer - 1;

        /// <summary>
        /// This lock is used for two tasks: growing m_buffers, and adding individual buffers to m_buffers.
        /// </summary>
        private readonly object m_syncRoot = new object();
        private TItem[][] m_buffers;

        /// <summary>
        /// Creates a buffer instance
        /// </summary>
        /// <param name="debug">Whether to enable debugging features.</param>
        public ConcurrentDenseIndex(bool debug)
        {
            m_buffers = new TItem[debug ? 0 : DefaultBuffersCount][];
        }

        internal int BuffersCount
        {
            get { return m_buffers.Sum(buffer => buffer == null ? 0 : 1); }
        }

        private TItem[] GetBuffer(uint index)
        {
            Contract.Ensures(Contract.Result<TItem[]>() != null);
            Contract.Ensures((index & BufferMask) < (uint)Contract.Result<TItem[]>().Length);

            // The lock-free path is safe:
            // - We grow under the lock and allocate buffers under the lock, so those operations are serialized and no new buffers can be lost.
            // - We only allocate any buffer once, so if it is there - use it! no need to lock.
            if (index >> BitsPerBuffer >= (uint)m_buffers.Length)
            {
                lock (m_syncRoot)
                {
                    if (index >> BitsPerBuffer >= (uint)m_buffers.Length)
                    {
                        // 1. We need enough buffers to be able to reference 'index'
                        // 2. We should always double in size for low amortized cost
                        // 3. We need not allocate more buffers than are necessary to address (1<<32) many items
                        var newBuffersLength = checked(Math.Min(Math.Max((uint)m_buffers.Length * 2, (index >> BitsPerBuffer) + 1), 1 << BufferSelectorBits));
                        var newBuffers = new TItem[newBuffersLength][];
                        Array.Copy(m_buffers, newBuffers, m_buffers.Length);
                        m_buffers = newBuffers;
                        Contract.Assert(index >> BitsPerBuffer < (uint)m_buffers.Length);
                    }
                }
            }

            TItem[] buffer = m_buffers[index >> BitsPerBuffer];
            if (buffer == null)
            {
                lock (m_syncRoot)
                {
                    buffer = m_buffers[index >> BitsPerBuffer];
                    if (buffer == null)
                    {
                        m_buffers[index >> BitsPerBuffer] = buffer = new TItem[EntriesPerBuffer];
                    }
                }
            }

            return buffer;
        }

        /// <summary>
        /// Gets a pointer to the buffer slot at specified index
        /// </summary>
        public BufferPointer<TItem> GetBufferPointer(uint index)
        {
            return new BufferPointer<TItem>(GetBuffer(index), (int)(index & BufferMask));
        }

        /// <summary>
        /// Gets or sets a value at an index
        /// </summary>
        /// <remarks>
        /// Conceptually, every index is associated with some value at all times.
        /// Initially, all values are equal to<code>default(TItem)</code>.
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1043:UseIntegralIndexer")]
        public TItem this[uint index]
        {
            get { return GetBuffer(index)[index & BufferMask]; }
            set { GetBuffer(index)[index & BufferMask] = value; }
        }
    }
}
