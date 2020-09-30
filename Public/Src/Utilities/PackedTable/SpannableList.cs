// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// A List implementation that allows Spans to be built over its backing store.
    /// </summary>
    /// <remarks>
    /// This should clearly be in the framework
    /// </remarks>
    public class SpannableList<T> : IList<T>
        where T : unmanaged
    {
        private T[] m_elements;

        /// <summary>
        /// Construct a SpannableList.
        /// </summary>
        public SpannableList(int capacity = 100)
        {
            if (capacity <= 0) { throw new ArgumentException($"Capacity {capacity} must be >= 0)"); }

            m_elements = new T[capacity];
        }

        private void CheckIndex(int index)
        {
            if (index < 0) { throw new ArgumentException($"Index {index} must be >= 0"); }
            if (index >= Count) { throw new ArgumentException($"Index {index} must be < Count {Count}"); }
        }

        /// <summary>
        /// Accessor.
        /// </summary>
        public T this[int index]
        {
            get
            {
                CheckIndex(index);
                return m_elements[index];
            }

            set
            {
                CheckIndex(index);
                m_elements[index] = value;
            }
        }

        /// <summary>
        /// Count of values.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Is this list read-only? (no)
        /// </summary>
        public bool IsReadOnly => false;

        private const float GrowthFactor = 1.4f; // 2 would eat too much when list gets very big

        private void EnsureCapacity(int numItems)
        {
            int nextSize = m_elements.Length;
            if (Count + numItems >= nextSize)
            {
                do
                {
                    nextSize = (int)(nextSize * GrowthFactor) + 1;
                }
                while (Count + numItems >= nextSize);

                T[] newElements = new T[nextSize];
                m_elements.CopyTo(newElements, 0);
                m_elements = newElements;
            }
        }

        /// <summary>
        /// Add the given item to the end of the list.
        /// </summary>
        public void Add(T item)
        {
            EnsureCapacity(1);
            if (m_elements.Length <= Count) { throw new InvalidOperationException($"SpannableList.Add: capacity {m_elements.Length}, count {Count}"); }
            m_elements[Count++] = item;
        }

        /// <summary>
        /// Add all the given items to the end of the list.
        /// </summary>
        public void AddRange(IEnumerable<T> range)
        {
            foreach (T t in range)
            {
                Add(t);
            }
        }

        /// <summary>
        /// Clear the list; do not reset the list's backing store (e.g. leave capacity unchanged).
        /// </summary>
        public void Clear()
        {
            m_elements.AsSpan(0, Count).Fill(default);
            Count = 0;
        }

        /// <summary>
        /// Is this item in the list?
        /// </summary>
        public bool Contains(T item) => IndexOf(item) != -1;

        /// <summary>
        /// Add this many more of this item.
        /// </summary>
        public void Fill(int count, T value)
        {
            int originalCount = Count;
            EnsureCapacity(count);
            m_elements.AsSpan().Slice(originalCount, count).Fill(value);
            Count += count;
        }

        /// <summary>
        /// Add all these values to the list.
        /// </summary>
        public void AddRange(ReadOnlySpan<T> values)
        {
            EnsureCapacity(values.Length);
            values.CopyTo(m_elements.AsSpan().Slice(Count, values.Length));
            Count += values.Length;
        }

        /// <summary>
        /// Size of the list's backing store.
        /// </summary>
        public int Capacity
        {
            get
            {
                return m_elements.Length;
            }
            set
            {
                if (value < Capacity) { return; } // we never shrink at the moment

                EnsureCapacity(value - Capacity);
            }
        }

        /// <summary>
        /// Copy the whole list to the destination array starting at arrayIndex.
        /// </summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            for (int i = 0; i < Count; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        /// <summary>
        /// Enumerate the list.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        /// <summary>
        /// Enumerate the given sublist.
        /// </summary>
        public IEnumerable<T> Enumerate(int index, int count)
        {
            if (index < 0 || count < 0 || index + count > Count)
            {
                throw new ArgumentException($"Cannot enumerate list of count {Count} with index {index} and count {count}");
            }

            for (int i = 0; i < count; i++)
            {
                yield return this[index + i];
            }
        }

        /// <summary>
        /// Get the index of item, or return -1 if item is not present.
        /// </summary>
        public int IndexOf(T item)
        {
            for (int i = 0; i < Count; i++)
            {
                T t = this[i];
                if (t.Equals(item))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Insert the item at the given index, pushing the item previously at that index (and all subsequent items)
        /// to the next higher index.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="item"></param>
        public void Insert(int index, T item)
        {
            EnsureCapacity(1);
            for (int i = Count; i > index; i--)
            {
                m_elements[i] = m_elements[i - 1];
            }
            m_elements[index] = item;
            Count++;
        }

        /// <summary>
        /// Remove the given item.
        /// </summary>
        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index == -1)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Remove the item at the given index.
        /// </summary>
        public void RemoveAt(int index)
        {
            for (int i = index; i < Count - 1; i++)
            {
                m_elements[i] = m_elements[i + 1];
            }
            m_elements[Count - 1] = default;
            Count--;
        }

        /// <summary>
        /// Legacy GetEnumerator method.
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// The whole point of this class: get a span over the backing store.
        /// </summary>
        public Span<T> AsSpan()
        {
            return m_elements.AsSpan().Slice(0, Count);
        }

        /// <summary>
        /// Diagnostic string; prints element type and count.
        /// </summary>
        public override string ToString()
        {
            return $"SpannableList<{typeof(T).Name}>[{Count}]";
        }

        /// <summary>
        /// Debugging only: print the ENTIRE list.
        /// </summary>
        public string ToFullString()
        {
            StringBuilder b = new StringBuilder(ToString());
            b.Append("{");
            for (int i = 0; i < Count; i++)
            {
                b.Append($" {this[i]}");
                if (i < Count - 1)
                {
                    b.Append(",");
                }
            }
            b.Append(" }");
            return b.ToString();
        }
    }
}
