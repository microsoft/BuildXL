// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// This class implements a priority queue with 2 special characteristics:
    ///     - there is an unlimited number of priorities.
    ///     - An option to walk through all items and dequeue some while leaving others.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix", Justification = "But it is a queue...")]
    public sealed class PriorityQueue<T>
    {
        #region Private data

        private const int BlockCapacity = 512;

        private struct ItemWithPriority
        {
            public T Item;
            public int Priority;
        }

        private sealed class ItemBlock
        {
            internal int MinPriority;
            internal int MaxPriority;
            private readonly ItemWithPriority[] m_items = new ItemWithPriority[BlockCapacity];
            private int m_firstItemIndex;

            /// <summary>
            /// The number of items in the block
            /// </summary>
            internal int Count { get; private set; }

            #region Constructors

            internal ItemBlock()
                : this(0, 0, int.MaxValue) { }

            private ItemBlock(int count, int minPriority, int maxPriority)
            {
                Count = count;
                m_firstItemIndex = (BlockCapacity - count) / 2;
                MinPriority = minPriority;
                MaxPriority = maxPriority;
            }

            #endregion

            /// <summary>
            /// Insert a new priority item
            /// </summary>
            internal void Insert(int priority, T item)
            {
                // Find the place using binary search;
                // We don't use Array.BinarySearch because it needs to copy ItemWithPriority items when calling the comparer
                // which proved to add noticeable delay for Windows builds (140ms for phone).
                int l = m_firstItemIndex, r = m_firstItemIndex + Count - 1;
                while (l <= r)
                {
                    int idx = (l + r) / 2;
                    int idxPriority = m_items[idx].Priority;
                    if (priority > idxPriority)
                    {
                        r = idx - 1;
                    }
                    else if (priority < idxPriority)
                    {
                        l = idx + 1;
                    }
                    else
                    {
                        // Found an item with the same priority. It should be a rare event.
                        // Althouhg FIFO for items with the same priority is nice, it is too expensive.
                        // However at least we can make the new item go after the just discovered one.
                        l = idx + 1;
                        break;
                    }
                }

                int index = l;

                if (m_firstItemIndex > 0 && ((m_firstItemIndex + Count) == BlockCapacity || index < m_firstItemIndex + (Count / 2)))
                {
                    Array.Copy(m_items, m_firstItemIndex, m_items, m_firstItemIndex - 1, index - m_firstItemIndex);
                    --m_firstItemIndex;
                    --index;
                }
                else
                {
                    Array.Copy(m_items, index, m_items, index + 1, m_firstItemIndex + Count - index);
                }

                m_items[index] = new ItemWithPriority { Priority = priority, Item = item };
                ++Count;
            }

            /// <summary>
            /// Cycle through all items and call a delegate. Remove the items if the delegate says so.
            /// </summary>
            /// <param name="processItem">The delegate to call</param>
            /// <param name="stopProcessing">A flag whether to stop the processing</param>
            public void ProcessItems(ProcessItem processItem, ref bool stopProcessing)
            {
                for (int index = m_firstItemIndex; index < m_firstItemIndex + Count && !stopProcessing; ++index)
                {
                    if (processItem(m_items[index].Priority, m_items[index].Item, out stopProcessing))
                    {
                        --Count;
                        if (index == m_firstItemIndex)
                        {
                            ++m_firstItemIndex;
                        }
                        else
                        {
                            Array.Copy(m_items, index + 1, m_items, index, m_firstItemIndex + Count - index);
                            --index;
                        }
                    }
                }
            }

            /// <summary>
            /// Create a new block and moves half if the items to the new block
            /// </summary>
            /// <returns>The newly created block</returns>
            public ItemBlock Split()
            {
                Contract.Assert(Count == BlockCapacity && m_firstItemIndex == 0);
                Contract.Assert(Count >= 2);

                // Split the block in 2.
                int newBlockCount = Count / 2;
                int splitIndex = m_firstItemIndex + Count - newBlockCount;
                int splitPriority = m_items[splitIndex].Priority;

                ItemBlock newBlock = new ItemBlock(newBlockCount, MinPriority, splitPriority);
                MinPriority = splitPriority;

                // Add half of the items to the new block.
                Array.Copy(m_items, splitIndex, newBlock.m_items, newBlock.m_firstItemIndex, newBlockCount);
                Count -= newBlockCount;

                return newBlock;
            }
        }

        private readonly List<ItemBlock> m_itemBlocks = new List<ItemBlock>();
        private readonly object m_lockObject = new object();
        private bool m_inProcessItems;

        #endregion

        /// <summary>
        /// A delegate used to process all items in the queue. See ProcessItems().
        /// Note: the delegate implementation must be fast because it is invoked on a locked queue.
        /// </summary>
        /// <param name="priority">The item priority.</param>
        /// <param name="item">The item to process.</param>
        /// <param name="stopProcessing">If set to true, ProcessItems will stop processing more items.</param>
        /// <returns>True if the item was processed and must be removed from the queue; false otherwise.</returns>
        public delegate bool ProcessItem(int priority, T item, out bool stopProcessing);

        /// <summary>
        /// Return the count of all currently enqueued items.
        /// For testing purposes only; do not use in real code as it is not performent
        /// </summary>
        public int Count
        {
            get
            {
                int count = 0;

                lock (m_lockObject)
                {
                    foreach (ItemBlock itemBlock in m_itemBlocks)
                    {
                        count += itemBlock.Count;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Enqueue an item
        /// </summary>
        public void Enqueue(int priority, T item)
        {
            Contract.Requires(priority >= 0);

            lock (m_lockObject)
            {
                ThrowIfInProcessItems();

                // A shortcut for an empty queue
                if (m_itemBlocks.Count == 0)
                {
                    var newBlock = new ItemBlock { MinPriority = 0, MaxPriority = int.MaxValue };
                    newBlock.Insert(priority, item);
                    m_itemBlocks.Add(newBlock);
                    return;
                }

                // Find the right block using a binary search; then insert the item in the block.
                // Note that we guaranteed to always find a block.
                int l = 0, r = m_itemBlocks.Count - 1;
                while (true)
                {
                    int blockIdx = (l + r) / 2;
                    ItemBlock itemBlock = m_itemBlocks[blockIdx];
                    if (priority > itemBlock.MaxPriority)
                    {
                        r = blockIdx - 1;
                    }
                    else if (priority < itemBlock.MinPriority)
                    {
                        l = blockIdx + 1;
                    }
                    else
                    {
                        if (itemBlock.Count == BlockCapacity)
                        {
                            // Split the block in 2.
                            ItemBlock newBlock = itemBlock.Split();
                            m_itemBlocks.Insert(blockIdx + 1, newBlock);
                            if (priority < itemBlock.MinPriority)
                            {
                                itemBlock = newBlock;
                            }
                        }

                        // Insert the new item
                        itemBlock.Insert(priority, item);
                        break;  // Done
                    }
                }
            }
        }

        /// <summary>
        /// Dequeue the highest priority item
        /// </summary>
        public T Dequeue()
        {
            T result = default(T);
            ProcessItems((int priority, T item, out bool stopProcessing) =>
            {
                result = item;
                stopProcessing = true;
                return true;
            });

            return result;
        }

        /// <summary>
        /// Calls a delegate to all items in the queue. The queue is locked during the whole call.
        /// The items are processed in order from highest to lowest priority.
        /// </summary>
        /// <param name="processItem">The delegate to call.</param>
        public void ProcessItems(ProcessItem processItem)
        {
            bool stopProcessing = false;

            lock (m_lockObject)
            {
                ThrowIfInProcessItems();
                m_inProcessItems = true;

                try
                {
                    for (int blockIdx = 0; blockIdx < m_itemBlocks.Count && !stopProcessing; ++blockIdx)
                    {
                        ItemBlock itemBlock = m_itemBlocks[blockIdx];
                        itemBlock.ProcessItems(processItem, ref stopProcessing);

                        if (itemBlock.Count == 0)
                        {
                            if (blockIdx > 0)
                            {
                                m_itemBlocks[blockIdx - 1].MinPriority = itemBlock.MinPriority;
                            }
                            else if (m_itemBlocks.Count > 1)
                            {
                                m_itemBlocks[blockIdx + 1].MaxPriority = itemBlock.MaxPriority;
                            }

                            m_itemBlocks.RemoveAt(blockIdx--);
                        }
                    }
                }
                finally
                {
                    m_inProcessItems = false;
                }
            }
        }

        #region Private Members

        private void ThrowIfInProcessItems()
        {
            if (m_inProcessItems)
            {
                throw new InvalidOperationException("This operation is prohibited in the thread processing queued items.");
            }
        }

        #endregion
    }
}
