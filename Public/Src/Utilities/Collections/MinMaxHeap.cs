// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// A double-ended priority queue which allows rapid removal of both the minimum and maximum items.
    /// </summary>
    /// <remarks>
    /// The maximum capacity is fixed and all required memory is allocated at instantiation time.
    /// 
    /// <see cref="Push"/>, <see cref="PopMinimum"/>, and <see cref="PopMaximum"/> are all O(log n) operations.
    /// 
    /// Peeking at <see cref="Minimum"/> and <see cref="Maximum"/> are O(1) operations.
    /// 
    /// This class is not safe for multi-threaded operations.
    /// 
    /// See Atkinson, Sack, Santoro, and Strothotte, "Min-Max Heaps and Generalized Priority Queues",
    /// Communications of the ACM 29 (1986) 996-1000.
    /// </remarks>
    /// <typeparam name="T">The type of the items.</typeparam>
    public class MinMaxHeap<T>
    {
        // Scan of paper found at http://www.cs.otago.ac.nz/staffpriv/mike/Papers/MinMaxHeaps/MinMaxHeaps.pdf
        //
        // As per usual heap implementation, binary tree is kept in an array with index mapping            
        //         +----0----+  
        //      +--1--+   +--2--+
        //    +-3-+ +-4   5     6
        //    7   8 9
        // Unlike usual heap implementation, the relationship between children and parents alternates:
        //   * Root node is smaller than all descendents (and is therefore minimum element)
        //   * Nodes in the next tier are larger than all descendents (and therefore one of them is maximum element)
        //   * Nodes in the next tier are smaller than all descendents
        //   * And so on...
        //
        // Once this structure is obtained, adding or removing an item can of course violate these rules. But just as
        // for a heap, this violation can be repaired by a series of operations that compare and swap items only along
        // a single line of descent, and so this repair is a log(n) operation. The repair rules are only slightly
        // more complicated than for a usual heap.

        /// <summary>
        /// Creates a new instance of a <see cref="MinMaxHeap{T}"/>.
        /// </summary>
        /// <param name="capacity">The maximum capacity of the priority queue. This value must be positive.</param>
        /// <param name="comparer">The comparer used to determine item ordering. This value must be non-null.</param>
        public MinMaxHeap(int capacity, Comparer<T> comparer)
        {
            Contract.Requires(capacity >= 1);
            Contract.RequiresNotNull(comparer);

            m_store = new T[capacity];
            m_compare = comparer;
        }

        private readonly IComparer<T> m_compare;

        private readonly T[] m_store;

        /// <summary>
        /// Gets the number of items currently in the priority queue.
        /// </summary>
        public int Count { get; private set; } = 0;

        /// <summary>
        /// Gets the maximum capacity of the priority queue.
        /// </summary>
        public int Capacity => m_store.Length;

        /// <summary>
        /// Removes all items from the priority queue.
        /// </summary>
        /// <remarks>This is an O(1) operation.</remarks>
        public void Clear() => Count = 0;

        /// <summary>
        /// Gets the minimum item in the priority queue.
        /// </summary>
        /// <remarks>Reading this property does not alter the queue. To read and remove the minimum item, use 
        /// <see cref="PopMinimum"/></remarks>
        public T Minimum
        {
            get
            {
                Contract.Requires(Count > 0);

                return m_store[0];
            }
        }

        /// <summary>
        /// Gets the maximum item in the priority queue.
        /// </summary>
        /// <remarks>Reading this property does not alter the queue. To read and remove the maximum item, use 
        /// <see cref="PopMaximum"/>.</remarks>
        public T Maximum
        {
            get
            {
                Contract.Requires(Count > 0);

                if (Count == 1)
                {
                    return m_store[0];
                }

                var leftValue = m_store[1];
                if (Count == 2)
                {
                    return leftValue;
                }

                var rightValue = m_store[2];
                return m_compare.Compare(leftValue, rightValue) >= 0 ? leftValue : rightValue;
            }
        }

        /// <summary>
        /// Compute the integer base-2 log, which is just the place of the higher non-zero bit.
        /// This is used to compute to which tier of the heap a given array index belongs.
        /// 
        /// Should be replaced by a popcnt by the runtime. Not in a tight loop anyways.
        /// </summary>
        private static int Lg2(int i)
        {
            int r = 0;
            while (i > 0)
            {
                i >>= 1;
                r++;
            }
            return r;
        }

        /// <summary>
        /// Compute whether the given array index is on an even/minimum tier.
        /// </summary>
        private static bool IsOnMinLevel(int index) => (Lg2(index + 1) % 2) != 0;

        /// <summary>
        /// Compute the array index of the first (left) child of the given array index.
        /// </summary>
        private static int FirstChildIndex(int index) => 2 * index + 1;

        /// <summary>
        /// Compute the array index of the parent of the given array index.
        /// </summary>
        private static int ParentIndex(int index) => (index - 1) / 2;

        /// <summary>
        /// Fast swap of two elements.
        /// Using ref means that we don't have to do extra computations of array offsets.
        /// </summary>
        private static void Swap(ref T a, ref T b)
        {
            var t = a;
            a = b;
            b = t;
        }

        private void BubbleUp(int index, int flag)
        {
            while (true)
            {
                if (index == 0)
                {
                    break;
                }

                var parentIndex = ParentIndex(index);
                if (parentIndex == 0)
                {
                    break;
                }

                var grandParentIndex = ParentIndex(parentIndex);
                if (flag * m_compare.Compare(m_store[index], m_store[grandParentIndex]) <= 0)
                {
                    return;
                }

                Swap(ref m_store[index], ref m_store[grandParentIndex]);
                index = grandParentIndex;
            }
        }

        private void BubbleUp(int index)
        {
            if (index == 0)
            {
                return;
            }

            var parentIndex = ParentIndex(index);
            if (IsOnMinLevel(index))
            {
                if (m_compare.Compare(m_store[index], m_store[parentIndex]) > 0)
                {
                    Swap(ref m_store[index], ref m_store[parentIndex]);
                    BubbleUp(parentIndex, +1);
                }
                else
                {
                    BubbleUp(index, -1);
                }
            }
            else
            {
                if (m_compare.Compare(m_store[index], m_store[parentIndex]) < 0)
                {
                    Swap(ref m_store[index], ref m_store[parentIndex]);
                    BubbleUp(parentIndex, -1);
                }
                else
                {
                    BubbleUp(index, +1);
                }
            }
        }

        /// <summary>
        /// Adds a new item to the priority queue.
        /// </summary>
        /// <param name="value">The item to be added.</param>
        public void Push(T value)
        {
            Contract.Requires(Count < m_store.Length);

            m_store[Count] = value;
            BubbleUp(Count);
            Count++;
        }

        private (int mIndex, T mValue, int mGeneration) FindExtremumAmongChildrenAndGrandchildren(int index, int flag)
        {
            Contract.Requires(index >= 0 && index < Count);

            // Left child
            var leftChildIndex = FirstChildIndex(index);

            // Note default(T)! in the following line is a lie: default(T) is quite possibly null, and ! asserts that it is not null.
            // But when we return in this case, mValue should not be used, so it won't matter. Just be careful never to change the
            // calling code to use mValue when mIndex == 0.
            if (leftChildIndex >= Count)
            {
                return (0, default(T), 0);
            }

            var mIndex = leftChildIndex;
            var mValue = m_store[leftChildIndex];
            var mGeneration = 1;

            // Right child
            var rightChildIndex = leftChildIndex + 1;
            if (rightChildIndex >= Count)
            {
                return (mIndex, mValue, mGeneration);
            }

            var value = m_store[rightChildIndex];
            if (flag * m_compare.Compare(value, mValue) > 0)
            {
                mIndex = rightChildIndex;
                mValue = value;
            }

            // Grandchildren
            var firstGrandchildIndex = FirstChildIndex(leftChildIndex);
            var lastGrandchildIndex = Math.Min(firstGrandchildIndex + 3, Count - 1);
            for (var grandchildIndex = firstGrandchildIndex; grandchildIndex <= lastGrandchildIndex; grandchildIndex++)
            {
                value = m_store[grandchildIndex];
                if (flag * m_compare.Compare(value, mValue) > 0)
                {
                    mIndex = grandchildIndex;
                    mValue = value;
                    mGeneration = 2;
                }
            }

            return (mIndex, mValue, mGeneration);

        }

        private void TrickleDown(int index, int flag)
        {
            while (index < Count)
            {
                Contract.Assert(index >= 0);
                
                (var mIndex, var mValue, var mGeneration) = FindExtremumAmongChildrenAndGrandchildren(index, flag);
                Contract.Assert(mGeneration == 0 || mGeneration == 1 || mGeneration == 2);

                if (mGeneration == 0)
                {
                    // There are no children. We are done.
                    return;
                }
                else if (mGeneration == 1)
                {
                    // Extremum was among children. Swap with child if necessary, then we are done.
                    if (flag * m_compare.Compare(mValue, m_store[index]) > 0)
                    {
                        Swap(ref m_store[mIndex], ref m_store[index]);
                    }
                    return;
                }
                else
                {
                    // Extremum was among grandchildren.
                    if (flag * m_compare.Compare(mValue, m_store[index]) > 0)
                    {
                        Swap(ref m_store[mIndex], ref m_store[index]);
                        var mParentIndex = ParentIndex(mIndex);
                        if (flag * m_compare.Compare(m_store[mIndex], m_store[mParentIndex]) < 0)
                        {
                            Swap(ref m_store[mIndex], ref m_store[mParentIndex]);
                        }
                        index = mIndex;
                    }
                    else
                    {
                        return;
                    }
                }
            }

        }

        /// <summary>
        /// Removes and returns the minimum item in the priority queue.
        /// </summary>
        /// <returns>The removed minimum item.</returns>
        /// <remarks>To peek at the minimum item without removing it, use <see cref="Minimum"/>.</remarks>
        public T PopMinimum()
        {
            Contract.Requires(Count > 0);

            var minimum = m_store[0];
            Count--;
            if (Count > 0)
            {
                m_store[0] = m_store[Count];
                TrickleDown(0, -1);
            }

            Contract.Assert(Count >= 0);
            return minimum;
        }

        /// <summary>
        /// Removes and returns the maximum item in the priority queue.
        /// </summary>
        /// <returns>The removed maximum item.</returns>
        /// <remarks>To peek at the maximum item without removing it, use <see cref="Maximum"/>.</remarks>
        public T PopMaximum()
        {
            Contract.Requires(Count > 0);

            if (Count <= 2)
            {
                Count--;
                Contract.Assert(Count >= 0);
                return m_store[Count];
            }
            Contract.Assert(Count >= 3);

            T maximum;
            Count--;
            if (m_compare.Compare(m_store[1], m_store[2]) > 0)
            {
                maximum = m_store[1];
                m_store[1] = m_store[Count];
                TrickleDown(1, +1);
            }
            else
            {
                maximum = m_store[2];
                m_store[2] = m_store[Count];
                TrickleDown(2, +1);
            }

            Contract.Assert(Count >= 0);

            return maximum;
        }
    }

}
