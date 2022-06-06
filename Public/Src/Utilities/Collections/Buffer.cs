// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// A growable array buffer which exposes underlying array
    /// </summary>
    public class Buffer<T>
    {
        private T[] m_items = new T[4];

        /// <summary>
        /// The current capacity of underlying array
        /// </summary>
        public int Capacity => m_items.Length;

        /// <summary>
        /// The number of items in the buffer
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the item at the given index. Does NOT perform bounds checks
        /// </summary>
        public T this[int index] => m_items[index];

        /// <summary>
        /// Get the <see cref="Memory{T}"/> representing the used portion of the array
        /// </summary>
        public Memory<T> Items => m_items.AsMemory(0, Count);

        /// <summary>
        /// Get the <see cref="Span{T}"/> representing the used portion of the array
        /// </summary>
        public Span<T> ItemSpan => m_items.AsSpan(0, Count);

        /// <summary>
        /// Inserts the items
        /// </summary>
        public void InsertRange(ReadOnlySpan<T> span)
        {
            Allocate(span.Length);
            span.CopyTo(m_items.AsSpan(Count - span.Length, span.Length));
        }

        /// <summary>
        /// Allocates a given number of elements
        /// </summary>
        public void Allocate(int count)
        {
            Count += count;
            var newCapacity = m_items.Length;
            while (Count > newCapacity)
            {
                newCapacity *= 2;
            }

            if (newCapacity != m_items.Length)
            {
                Array.Resize(ref m_items, newCapacity);
            }
        }

        /// <summary>
        /// Adds the item at the current cursor position
        /// </summary>
        public void Add(T item)
        {
            if (m_items.Length == Count)
            {
                System.Array.Resize(ref m_items, Count * 2);
            }

            m_items[Count] = item;
            Count++;
        }

        /// <summary>
        /// Resets the count. Does NOT clear the underlying array.
        /// </summary>
        public void Reset()
        {
            Count = 0;
        }

        /// <summary>
        /// Resets the count and clears the underlying array
        /// </summary>
        public void Clear()
        {

            m_items.AsSpan().Clear();
            Reset();
        }
    }
}
