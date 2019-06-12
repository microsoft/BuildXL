// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// A concurrent set with heterogeneous lookup semantics designed specifically for cases where a very large number (> 1000) of items
    /// need to be stored.
    /// </summary>
    /// <remarks>
    /// This class provides an efficient implementation of a concurrent set of items with a
    /// specialized lookup model which allows arbitrary types to be used when looking up or adding items.
    ///
    /// Notable properties of this data structure are:
    /// 1. BigBuffers are used for storage items/nodes/buckets to prevent arrays from ending up on large object heap.
    /// 2. Resize is done incrementally so concurrency/performance as items are added does not decrease in order to perform resize.
    /// 3. Adds allow concurrent reads. Only bucket splitting requires write lock.
    ///
    /// Split Logic:
    /// When count of items reaches a certain threshold, 2 * prior threshold. A split will be initiated via
    /// Buckets.GrowIfNecessary.
    /// After split  has been initiated, every following insertion will take a single bucket and split it starting with the first
    /// bucket and iterating up. When searching for an item during a split, the bucket number may refer to a bucket which hasn't been
    /// create or initialized yet. In that case, the query is rerouted to the pre-split bucket which would contain the content.
    ///
    /// As buckets are split, more bits of hash code are used to determine bucket no. Given the bit string, x_k...x_j..x_0, representing a
    /// item hash code. If x_j...x_0 represents the bucket number, then after a split the bucket number is x_j+1...x_0. If x_j+1=0, then the
    /// item remains in the same bucket. If x_j+1=1, the new bucket is 2^x_j+1 + old bucket number (x_j...x_0).
    ///
    /// This implementation is add-only, we don't include the code to remove items from the set since we didn't need it.
    /// It could certainly be added if needed. WARNING: Behavior of ToList will need to change if remove is supported. It assumes
    /// that there is a contiguous block of items from 0 to Count for the items added to the set. If remove is supported there,
    /// may be empty spaces.
    ///
    /// TODO: Investigate have items which are associated with nodes that do not have next pointer to save memory
    /// in cases where buckets only contain a single item or generally for the tail node of the linked list associated
    /// with buckets. This would likely incur some additional computation but perhaps it would not have a significant impact
    /// on performance.
    /// </remarks>
    public sealed partial class ConcurrentBigSet<TItem> : ConcurrentBigSet
    {
        #region Constants

        /// <summary>
        /// This is a large random prime used for ensuring that bucket hash code derived from the
        /// item hash code is randomly distributed. This number is multiplied with the item's hash code
        /// into a 64-bit value. Then bits 16 through 47 are selected as the bucket hash code.
        /// </summary>
        private const long LargeRandomPrime = 1186785773;

        /// <summary>
        /// Minimum concurrency level is 1024. Which should prevent lock contention in high throughput scenarios even
        /// with many threads.
        /// </summary>
        private const int MinConcurrencyLevel = 1024;

        /// <summary>
        /// Default concurrency level is 1024. Which should prevent lock contention in high throughput scenarios even
        /// with many threads.
        /// </summary>
        internal const int DefaultConcurrencyLevel = 1024;

        /// <summary>
        /// Default capacity is 1024 to match DefaultConcurrencyLevel since actual capacity must always be greater
        /// or equal to the concurrency level.
        /// </summary>
        internal const int DefaultCapacity = 1024;

        /// <summary>
        /// Default bucket to items ratio is 4. Meaning on average there should be 4 items per bucket.
        /// </summary>
        internal const int DefaultBucketToItemsRatio = 4;

        /// <summary>
        /// Each node is 8 bytes; let's try to keep the entry buffers outside of the large object heap.
        /// </summary>
        private const int NodesPerEntryBufferBitWidth = 13;
        #endregion Constants

        /// <summary>
        /// Stores the actual items for the set
        /// </summary>
        private readonly BigBuffer<TItem> m_items;

        /// <summary>
        /// Stores linked lists of nodes
        /// </summary>
        private readonly BigBuffer<Node> m_nodes;

        /// <summary>
        /// Stores pointers into nodes array indicating the first node in the bucket
        /// </summary>
        private Buckets m_buckets;

        /// <summary>
        /// This indicates the length of nodes in use in the nodes buffer. It is used/updated when allocated
        /// new nodes at the end of the nodes buffer for storing inserted or moved items.
        /// </summary>
        private int m_nodeLength;

        /// <summary>
        /// Stores the index of the first free node. Free nodes are created after remove operations and they
        /// from a linked list like item nodes. A value of -1 indicates that no free nodes are available and that
        /// new nodes should be created from the end nodes buffer. There is a free node pointer per lock to prevent
        /// contention on adding/acquiring free nodes.
        /// </summary>
        private readonly int[] m_freeNodes;

        /// <summary>
        /// The count of items in the concurrent set.
        /// </summary>
        private int m_count;

        /// <summary>
        /// The bit mask for selecting the lock index from the bucket hash code
        /// </summary>
        private readonly int m_lockBitMask;

        // A set of reader-writer locks, each guarding a section of the table
        private readonly Locks m_locks;

        /// <summary>
        /// Stores the accessors to avoid calling the constructor multiple times.
        /// This field should always be copied into a local variable rather accessed directly.
        /// </summary>
        private readonly Accessors m_accessors;

        /// <summary>
        /// Creates an instance.
        /// </summary>
        /// <param name="concurrencyLevel">the concurrency level (all values less than 1024 will be assumed to be 1024)</param>
        /// <param name="capacity">the initial capacity (ie number of buckets)</param>
        /// <param name="ratio">the desired ratio of items to buckets (must be greater than 0)</param>
        /// <param name="backingItemsBuffer">the backing storage for items</param>
        /// <param name="itemsPerEntryBufferBitWidth">the bit width of number of entries in a buffer (buffer size is 2^<paramref name="itemsPerEntryBufferBitWidth"/>)</param>
        public ConcurrentBigSet(
            int concurrencyLevel = DefaultConcurrencyLevel,
            int capacity = DefaultCapacity,
            int ratio = DefaultBucketToItemsRatio,
            BigBuffer<TItem> backingItemsBuffer = null,
            int itemsPerEntryBufferBitWidth = 12)
            : this(concurrencyLevel, backingItemsBuffer, null, null, nodeLength: 0, capacity: capacity, ratio: ratio, itemsPerEntryBufferBitWidth: itemsPerEntryBufferBitWidth)
        {
            Contract.Requires(concurrencyLevel >= 1);
            Contract.Requires(ratio >= 1);
        }

        private ConcurrentBigSet(
            int concurrencyLevel,
            BigBuffer<TItem> backingItemsBuffer,
            BigBuffer<Node> backingNodesBuffer,
            Buckets backingBuckets,
            int nodeLength,
            int capacity = DefaultCapacity,
            int ratio = DefaultBucketToItemsRatio,
            int itemsPerEntryBufferBitWidth = 12)
        {
            Contract.Requires(concurrencyLevel >= 1);
            Contract.Requires(ratio >= 1);

            concurrencyLevel = Math.Max(concurrencyLevel, MinConcurrencyLevel);
            if (concurrencyLevel > capacity)
            {
                capacity = concurrencyLevel;
            }

            var actualConcurrencyLevel = (int)Bits.HighestBitSet((uint)concurrencyLevel);
            var actualCapacity = (int)Bits.HighestBitSet((uint)capacity);
            capacity = capacity > actualCapacity ? actualCapacity << 1 : actualCapacity;
            concurrencyLevel = concurrencyLevel > actualConcurrencyLevel ? actualConcurrencyLevel << 1 : actualConcurrencyLevel;

            m_locks = new Locks(concurrencyLevel);

            // Create free node pointer for every lock
            m_freeNodes = new int[m_locks.Length];
            for (int i = 0; i < m_freeNodes.Length; i++)
            {
                m_freeNodes[i] = -1;
            }

            m_lockBitMask = concurrencyLevel - 1;

            m_items = backingItemsBuffer ?? new BigBuffer<TItem>(itemsPerEntryBufferBitWidth);
            m_nodes = backingNodesBuffer ?? new BigBuffer<Node>(NodesPerEntryBufferBitWidth);
            m_buckets = backingBuckets ?? new Buckets(capacity, ratio);

            m_nodeLength = nodeLength;
            m_accessors = new Accessors(this);
        }

        /// <summary>
        /// Gets access to the underlying items buffer.
        /// </summary>
        public BigBuffer<TItem> GetItemsUnsafe()
        {
            return m_items;
        }

        /// <summary>
        /// Converts the set items to another type with equivalent hash codes.
        /// NOTE: This method is not threadsafe and the set cannot be safely used after conversion.
        /// </summary>
        /// <param name="convert">the function used to convert the items</param>
        /// <returns>The set with converted item set</returns>
        public ConcurrentBigSet<TNewItem> ConvertUnsafe<TNewItem>(Func<TItem, TNewItem> convert)
        {
            Contract.Requires(convert != null);

            var newItemsBuffer = new BigBuffer<TNewItem>(m_items.EntriesPerBufferBitWidth);
            var itemsCount = m_count;
            newItemsBuffer.Initialize(itemsCount, (startIndex, entryCount) =>
                {
                    TNewItem[] buffer = new TNewItem[entryCount];
                    var accessor = m_accessors.Items;
                    int itemsIndex = startIndex;

                    // entryIndex is index in specific buffer array whereas itemsIndex is an index into the big buffer
                    // Ensure entryIndex does not go past the set of valid items by constraining it to be less
                    // itemsCount - startIndex
                    for (int entryIndex = 0; entryIndex < entryCount && itemsIndex < itemsCount; entryIndex++, itemsIndex++)
                    {
                        buffer[entryIndex] = convert(accessor[itemsIndex]);
                    }

                    return buffer;
                });

            return new ConcurrentBigSet<TNewItem>(
                 concurrencyLevel: m_locks.Length,
                 backingItemsBuffer: newItemsBuffer,
                 backingNodesBuffer: m_nodes,
                 backingBuckets: m_buckets,
                 nodeLength: m_nodeLength)
            {
                m_count = itemsCount,
            };
        }

        /// <summary>
        /// Writes this set.
        /// </summary>
        /// <remarks>
        /// This method is not threadsafe.
        /// </remarks>
        public void Serialize(BinaryWriter writer, Action<TItem> itemWriter)
        {
            Contract.Requires(writer != null);
            Contract.Requires(itemWriter != null);

            writer.Write(m_count);
            writer.Write(m_nodeLength);
            writer.Write(m_items.EntriesPerBufferBitWidth);

            var nodes = m_accessors.Nodes;
            var items = m_accessors.Items;
            for (int i = 0; i < m_nodeLength; i++)
            {
                var node = nodes[i];
                writer.Write(node.Hashcode);
                if (node.Hashcode != Node.UnusedHashCode)
                {
                    writer.Write(node.Next);
                    var item = items[i];
                    itemWriter(item);
                }
            }

            m_buckets.Serialize(writer);
        }

        /// <summary>
        /// Creates and returns set by deserialization
        /// </summary>
        /// <param name="reader">general reader</param>
        /// <param name="itemReader">item reader</param>
        /// <param name="concurrencyLevel">the concurrency level (all values less than 1024 will be assumed to be 1024)</param>
        public static ConcurrentBigSet<TItem> Deserialize(
            BinaryReader reader,
            Func<TItem> itemReader,
            int concurrencyLevel = DefaultConcurrencyLevel)
        {
            var count = reader.ReadInt32();
            var nodeLength = reader.ReadInt32();
            var itemsPerBufferBitWidth = reader.ReadInt32();
            var capacity = Math.Max(MinConcurrencyLevel, Math.Max(nodeLength, concurrencyLevel));

            var items = new BigBuffer<TItem>(itemsPerBufferBitWidth);
            items.Initialize(capacity);
            var nodes = new BigBuffer<Node>(NodesPerEntryBufferBitWidth);
            nodes.Initialize(capacity);

            var itemsAccessor = items.GetAccessor();
            var nodesAccessor = nodes.GetAccessor();

            List<int> freeNodes = new List<int>();
            for (int i = 0; i < nodeLength; i++)
            {
                var hashCode = reader.ReadInt32();
                if (hashCode != Node.UnusedHashCode)
                {
                    var next = reader.ReadInt32();
                    var item = itemReader();
                    nodesAccessor[i] = new Node(hashCode, next);
                    itemsAccessor[i] = item;
                }
                else
                {
                    freeNodes.Add(i);
                }
            }

            var buckets = Buckets.Deserialize(reader);
            var result = new ConcurrentBigSet<TItem>(
                concurrencyLevel,
                items,
                nodes,
                buckets,
                nodeLength);
            result.m_count = count;

            // distribute free nodes
            var accessors = result.m_accessors;
            foreach (var i in freeNodes)
            {
                var lockNo = result.GetLockNo(i);
                result.AddFreeNode(lockNo, i, ref accessors);
            }

            return result;
        }

        /// <summary>
        /// Checks if all known hashcodes are in line with a given equality comparer.
        /// </summary>
        public bool Validate(IEqualityComparer<TItem> comparer)
        {
            comparer = comparer ?? EqualityComparer<TItem>.Default;
            var pageSize = 4096;
            var startPage = 0;
            var endPage = (m_nodeLength + pageSize - 1) / pageSize;
            var success = true;
            Parallel.For(
                startPage,
                endPage,
                page =>
                {
                    var accessors = m_accessors;
                    var start = page * pageSize;
                    var end = Math.Min(m_nodeLength, (page + 1) * pageSize);
                    for (int i = start; i < end; i++)
                    {
                        var node = accessors.Nodes[i];
                        if (node.Hashcode != Node.UnusedHashCode)
                        {
                            var item = accessors.Items[i];
                            var actualHashCode = comparer.GetHashCode(item);
                            var bucketHashCode = GetBucketHashCode(actualHashCode);
                            if (node.Hashcode != bucketHashCode)
                            {
                                success = false;
                            }
                        }
                    }
                });

            return success;
        }

        /// <summary>
        /// Gets the current number of items in the set
        /// </summary>
        public int Count => m_count;

        /// <summary>
        /// Gets the item at the given index
        /// </summary>
        public TItem this[int index]
        {
            get { return m_items[index]; }
        }

        /// <summary>
        /// Gets an unsafe read-only accessor over the items in the set.
        /// </summary>
        /// <remarks>
        /// Note: This operation is not safe with respect to modification operations.
        /// Ensure synchronization or no modifications when iterating this set.
        /// </remarks>
        public IReadOnlyCollection<TItem> UnsafeGetList()
        {
            return new ItemsCollection(this);
        }

        /// <summary>
        /// Gets whether an equivalent item exists in the set
        /// </summary>
        /// <param name="pendingItem">the equivalent pending item used to find the item</param>
        /// <returns>true if an equivalent item was found, otherwise false.</returns>
        public bool ContainsItem<TPendingItem>(TPendingItem pendingItem)
            where TPendingItem : IPendingSetItem<TItem>
        {
            var result = GetOrAddItem(pendingItem, allowAdd: false);
            return result.IsFound;
        }

        /// <summary>
        /// Gets whether an equivalent item exists in the set
        /// </summary>
        /// <param name="item">The value being sought in the set.</param>
        /// <param name="comparer">The comparer to use. Uses the default comparer is not specified.</param>
        /// <returns>true if an equivalent item was found, otherwise false.</returns>
        public bool Contains(TItem item, IEqualityComparer<TItem> comparer = null)
        {
            var result = GetOrAdd(item, comparer, allowAdd: false);
            return result.IsFound;
        }

        /// <summary>
        /// Attempts to retrieve the matching item with the given item from the set
        /// </summary>
        /// <param name="pendingItem">the equivalent pending item used to find the item</param>
        /// <param name="retrievedItem">the item if found</param>
        /// <returns>true if the item was found, otherwise false.</returns>
        public bool TryGetItem<TPendingItem>(TPendingItem pendingItem, out TItem retrievedItem)
            where TPendingItem : IPendingSetItem<TItem>
        {
            var result = GetOrAddItem(pendingItem, allowAdd: false);
            retrievedItem = result.Item;
            return result.IsFound;
        }

        /// <summary>
        /// Attempts to retrieve the matching item with the given item from the set
        /// </summary>
        /// <param name="item">The value being sought in the set.</param>
        /// <param name="retrievedItem">the item if found</param>
        /// <param name="comparer">The comparer to use. Uses the default comparer is not specified.</param>
        /// <returns>true if the item was found, otherwise false.</returns>
        public bool TryGet(TItem item, out TItem retrievedItem, IEqualityComparer<TItem> comparer = null)
        {
            var result = GetOrAdd(item, comparer, allowAdd: false);
            retrievedItem = result.Item;
            return result.IsFound;
        }

        /// <summary>
        /// Attempts to add the item to the set
        /// </summary>
        /// <param name="item">The value being sought/added in the set.</param>
        /// <param name="comparer">The comparer to use. Uses the default comparer is not specified.</param>
        public bool Add(TItem item, IEqualityComparer<TItem> comparer = null)
        {
            comparer = comparer ?? EqualityComparer<TItem>.Default;
            return AddItem(new PendingSetItem(item, comparer));
        }

        /// <summary>
        /// Attempts to add the item to the set
        /// </summary>
        /// <param name="pendingItem">The value being sought in the set.</param>
        public bool AddItem<TPendingItem>(TPendingItem pendingItem)
            where TPendingItem : IPendingSetItem<TItem>
        {
            var result = GetAddOrUpdateItem(pendingItem, allowAdd: true, update: false);
            return !result.IsFound;
        }

        /// <summary>
        /// Attempts to get the item associated with the specified sought value from the set.
        /// </summary>
        /// <param name="item">The value being sought/added in the set.</param>
        /// <param name="comparer">The comparer to use. Uses the default comparer is not specified.</param>
        /// <param name="allowAdd">indicates whether item will be added if not found.</param>
        public GetAddOrUpdateResult GetOrAdd(TItem item, IEqualityComparer<TItem> comparer = null, bool allowAdd = true)
        {
            comparer = comparer ?? EqualityComparer<TItem>.Default;
            return GetOrAddItem(new PendingSetItem(item, comparer), allowAdd);
        }

        /// <summary>
        /// Attempts to get the item associated with the specified sought value from the set.
        /// </summary>
        /// <param name="pendingItem">The value being sought in the set.</param>
        /// <param name="allowAdd">indicates whether item will be added if not found.</param>
        public GetAddOrUpdateResult GetOrAddItem<TPendingItem>(TPendingItem pendingItem, bool allowAdd = true)
            where TPendingItem : IPendingSetItem<TItem>
        {
            return GetAddOrUpdateItem(pendingItem, allowAdd, update: false);
        }

        /// <summary>
        /// Attempts to update the item associated with the specified sought value from the set (optionally adding the item).
        /// </summary>
        /// <param name="item">The value being sought in the set.</param>
        /// <param name="comparer">The comparer to use. Uses the default comparer is not specified.</param>
        /// <param name="allowAdd">indicates whether item will be added if not found.</param>
        public GetAddOrUpdateResult Update(TItem item, IEqualityComparer<TItem> comparer = null, bool allowAdd = true)
        {
            comparer = comparer ?? EqualityComparer<TItem>.Default;
            return UpdateItem(new PendingSetItem(item, comparer), allowAdd);
        }

        /// <summary>
        /// Attempts to update the item associated with the specified sought value from the set (optionally adding the item).
        /// </summary>
        /// <param name="pendingItem">The value being sought in the set.</param>
        /// <param name="allowAdd">indicates whether item will be added if not found.</param>
        public GetAddOrUpdateResult UpdateItem<TPendingItem>(TPendingItem pendingItem, bool allowAdd = true)
            where TPendingItem : IPendingSetItem<TItem>
        {
            return GetAddOrUpdateItem(pendingItem, allowAdd: allowAdd, update: true);
        }

        /// <summary>
        /// Attempts to remove the item associated with the specified sought value from the set.
        /// </summary>
        /// <param name="item">The value being removed in the set.</param>
        /// <param name="comparer">The comparer to use. Uses the default comparer is not specified.</param>
        public GetAddOrUpdateResult Remove(TItem item, IEqualityComparer<TItem> comparer = null)
        {
            comparer = comparer ?? EqualityComparer<TItem>.Default;
            return GetAddOrUpdateItem(new PendingSetItem(item, comparer, remove: true), allowAdd: false, update: true);
        }

        /// <summary>
        /// This method assumes no concurrent accesses, and that the new item isn't already in the list
        /// </summary>
        internal void UnsafeAddItems<TPendingItem>(IEnumerable<TPendingItem> pendingItems)
            where TPendingItem : IPendingSetItem<TItem>
        {
            Accessors accessors = m_accessors;
            foreach (var pendingItem in pendingItems)
            {
                int bucketHashCode = GetBucketHashCode(pendingItem.HashCode);

                int bucketNo;
                int lockNo = 0;
                int headNodeIndex;

                // Number of nodes searched to find the item or the total number of nodes if the item is not found
                int findCount;
                int priorNodeIndex;
                var result = FindItem(
                    pendingItem,
                    bucketHashCode,
                    out bucketNo,
                    out headNodeIndex,
                    ref accessors,
                    out findCount,
                    out priorNodeIndex);
                Contract.Assert(!result.IsFound);

                // now make an item from the lookup value
                bool remove;
                TItem item = pendingItem.CreateOrUpdateItem(result.Item, false, out remove);
                Contract.Assert(!remove, "Remove is only allowed when performing update operation");
                SetBucketHeadNode(bucketNo, lockNo, bucketHashCode, headNodeIndex, item, ref accessors);
                int countAfterAdd = Interlocked.Increment(ref m_count);

                if (countAfterAdd != 0)
                {
                    PerformPostInsertSplitOperations(ref accessors, countAfterAdd);
                }
            }
        }

        [SuppressMessage("Microsoft.Concurrency", "CA8001", Justification = "Reviewed for thread safety")]
        private GetAddOrUpdateResult GetAddOrUpdateItem<TPendingItem>(TPendingItem pendingItem, bool allowAdd = true, bool update = false)
            where TPendingItem : IPendingSetItem<TItem>
        {
            int bucketHashCode = GetBucketHashCode(pendingItem.HashCode);

            int bucketNo;
            int lockNo = GetLockNo(bucketHashCode);
            int headNodeIndex;
            GetAddOrUpdateResult result;
            Accessors accessors = m_accessors;
            int countAfterAdd = 0;

            // Number of nodes searched to find the item or the total number of nodes if the item is not found
            int findCount;
            int priorNodeIndex;
            if (!update)
            {
                int lockWriteCount;
                using (m_locks.AcquireReadLock(lockNo, out lockWriteCount))
                {
                    result = FindItem(pendingItem, bucketHashCode, out bucketNo, out headNodeIndex, ref accessors, out findCount, out priorNodeIndex);
                    if (result.IsFound || !allowAdd)
                    {
                        return result;
                    }
                }
            }

            // Prevent reads when performing update. Reads during add are safe, but because update may not be atomic
            // we need to prevent reads.
            bool allowReads = !update;
            int priorLockWriteCount;
            using (var writeLock = m_locks.AcquireWriteLock(lockNo, out priorLockWriteCount, allowReads: allowReads))
            {
                // Only attempt another find if a write happened on the lock since the first read.

                // TODO: Use prior lock write count to skip doing second read. Beware,
                // if not done appropriately things will break.
                result = FindItem(pendingItem, bucketHashCode, out bucketNo, out headNodeIndex, ref accessors, out findCount, out priorNodeIndex);
                if ((!result.IsFound && !allowAdd) || (result.IsFound && !update))
                {
                    return result;
                }

                // now make an item from the lookup value
                bool remove;
                TItem item = pendingItem.CreateOrUpdateItem(result.Item, result.IsFound, out remove);
                Contract.Assert(update || !remove, "Remove is only allowed when performing update operation");
                if (!result.IsFound)
                {
                    if (remove)
                    {
                        return result;
                    }

                    int addedItemIndex = SetBucketHeadNode(bucketNo, lockNo, bucketHashCode, headNodeIndex, item, ref accessors);
                    countAfterAdd = Interlocked.Increment(ref m_count);
                    result = new GetAddOrUpdateResult(item, isFound: false, index: addedItemIndex);
                }
                else
                {
                    if (remove)
                    {
                        item = default(TItem);
                        Interlocked.Decrement(ref m_count);

                        // Update the bucket pointer or prior node in the linked list next pointer to
                        // point to the node after this node.
                        var currentNode = accessors.Nodes[result.Index];
                        if (priorNodeIndex >= 0)
                        {
                            var priorNode = accessors.Nodes[priorNodeIndex];
                            accessors.Nodes[priorNodeIndex] = new Node(priorNode.Hashcode, currentNode.Next);
                        }
                        else
                        {
                            m_buckets[bucketNo] = currentNode.Next;
                        }

                        // Update the free node pointer for this lock
                        AddFreeNode(lockNo, result.Index, ref accessors);
                    }

                    accessors.Items[result.Index] = item;
                    result = new GetAddOrUpdateResult(item, result.Item, isFound: true, index: result.Index);
                }
            }

            if (countAfterAdd != 0)
            {
                PerformPostInsertSplitOperations(ref accessors, countAfterAdd);
            }

            return result;
        }

        private void AddFreeNode(int lockNo, int index, ref Accessors accessors)
        {
            accessors.Nodes[index] = new Node(Node.UnusedHashCode, m_freeNodes[lockNo]);
            m_freeNodes[lockNo] = index;
        }

        /// <summary>
        /// Sets the buckets head node and populates the node's item in the corresponding index in the item buffer
        /// </summary>
        private int SetBucketHeadNode(int bucketNo, int lockNo, int soughtHashCode, int headNodeIndex, TItem item, ref Accessors accessors)
        {
            var freeNodeIndex = GetFreeNode(lockNo, ref accessors);
            SetItemNode(freeNodeIndex, soughtHashCode, headNodeIndex, item, ref accessors);
            m_buckets[bucketNo] = freeNodeIndex;
            return freeNodeIndex;
        }

        /// <summary>
        /// Sets a node and item at the given index in the node and item buffers respectively
        /// </summary>
        private static void SetItemNode(int nodeIndex, int nodeHashcode, int next, TItem item, ref Accessors accessors)
        {
            var node = new Node(nodeHashcode, next);
            accessors.Nodes[nodeIndex] = node;

            // TODO: Should this be moved before setting node for safe enumeration
            accessors.Items[nodeIndex] = item;
        }

        /// <summary>
        /// Subroutine for finding an item
        /// </summary>
        private GetAddOrUpdateResult FindItem<TPendingItem>(TPendingItem pendingItem, int bucketHashCode, out int bucketNo, out int headNodeIndex, ref Accessors accessors, out int findCount, out int priorNodeIndex)
            where TPendingItem : IPendingSetItem<TItem>
        {
            findCount = 0;
            bucketNo = m_buckets.GetBucketNo(bucketHashCode);

            int nodeIndex = m_buckets[bucketNo];
            headNodeIndex = nodeIndex;
            priorNodeIndex = -1;

            while (nodeIndex >= 0)
            {
                findCount++;
                var node = accessors.Nodes[nodeIndex];
                if (bucketHashCode == node.Hashcode)
                {
                    var retrievedItem = accessors.Items[nodeIndex];
                    if (pendingItem.Equals(retrievedItem))
                    {
                        return new GetAddOrUpdateResult(retrievedItem, isFound: true, index: nodeIndex);
                    }
                }

                priorNodeIndex = nodeIndex;
                nodeIndex = node.Next;
            }

            return GetAddOrUpdateResult.NotFound;
        }

        /// <summary>
        /// Reserves an index in the backing buffer
        /// </summary>
        /// <param name="backingBuffer">the backing buffer used to ensure caller has access</param>
        /// <returns>the reserved index</returns>
        public int ReservedNextIndex(BigBuffer<TItem> backingBuffer)
        {
            Contract.Assert(backingBuffer == m_items, "ReservedNextIndex can only be called by owner of backing buffer for set");
            return GetTrailingFreeNodes();
        }

        /// <summary>
        /// Acquires nodes from end of node buffer
        /// </summary>
        private int GetTrailingFreeNodes(int nodeCount = 1)
        {
            var newNodeLength = Interlocked.Add(ref m_nodeLength, nodeCount);
            m_nodes.Initialize(newNodeLength);
            m_items.Initialize(newNodeLength);

            // Subtract count since we won't the node index of the first node acquired not the new node length
            return newNodeLength - nodeCount;
        }

        /// <summary>
        /// Acquires nodes from the free list or the end of the node buffer
        /// </summary>
        private int GetFreeNode(int lockNo, ref Accessors accessors)
        {
            int nextFreeNode = m_freeNodes[lockNo];
            if (nextFreeNode >= 0)
            {
                var node = accessors.Nodes[nextFreeNode];
                m_freeNodes[lockNo] = node.Next;
                return nextFreeNode;
            }

            return GetTrailingFreeNodes();
        }

        /// <summary>
        /// After insert, checks whether a split should be initiated and whether there are buckets which need to be split
        /// </summary>
        private void PerformPostInsertSplitOperations(ref Accessors accessors, int count)
        {
            // If necessary, trigger a split.
            if (Buckets.GrowIfNecessary(ref m_buckets, count))
            {
                return;
            }

            int splitBucketNo;
            int targetBucketNo;
            if (m_buckets.TryGetBucketToSplit(out splitBucketNo, out targetBucketNo))
            {
                // Only need to get lock for split bucket since target bucket
                // will always use the same lock
                int splitLockNo = GetLockNo(splitBucketNo);
                using (var writeLock = m_locks.AcquireWriteLock(splitLockNo, allowReads: true))
                {
                    var nodeIndex = m_buckets[splitBucketNo];
                    if (nodeIndex >= 0)
                    {
                        // We only need to exclude reads if the bucket actually has nodes which may need to be split.
                        writeLock.ExcludeReads();
                    }

                    int lastSplitNodeIndex = -3;
                    int lastTargetNodeIndex = -3;

                    while (nodeIndex >= 0)
                    {
                        var node = accessors.Nodes[nodeIndex];
                        var nextNodeIndex = node.Next;
                        int bucketNo = m_buckets.GetBucketNo(node.Hashcode);

                        Node newNode;
                        if (bucketNo == splitBucketNo)
                        {
                            newNode = new Node(node.Hashcode, lastSplitNodeIndex);
                            lastSplitNodeIndex = nodeIndex;
                        }
                        else
                        {
                            newNode = new Node(node.Hashcode, lastTargetNodeIndex);
                            lastTargetNodeIndex = nodeIndex;
                        }

                        accessors.Nodes[nodeIndex] = newNode;
                        nodeIndex = nextNodeIndex;
                    }

                    m_buckets.SetBucketHeadNodeIndexDirect(splitBucketNo, lastSplitNodeIndex);
                    m_buckets.SetBucketHeadNodeIndexDirect(targetBucketNo, lastTargetNodeIndex);

                    m_buckets.EndBucketSplit();
                }
            }
        }

        /// <summary>
        /// Computes the bucket number for a particular item.
        /// A large random prime is used for ensuring that bucket hash code derived from the
        /// item hash code is randomly distributed. This number is multiplied with the item's hash code
        /// into a 64-bit value. Then bits 16 through 46 are selected as the bucket hash code.
        /// </summary>
        private static int GetBucketHashCode(int hashcode)
        {
            unchecked
            {
                // Use middle 31 bits after multiplying by large prime
                // Don't use top bit because that is reserved for unused node hash code.
                long rotatedHashcode = (hashcode * LargeRandomPrime) >> 16;
                return (int)(rotatedHashcode & 0x7FFFFFFF);
            }
        }

        /// <summary>
        /// Gets the lock number for a particular bucket hash code
        /// </summary>
        private int GetLockNo(int bucketHashCode)
        {
            return bucketHashCode & m_lockBitMask;
        }

        /// <summary>
        /// Indicates the result of a Get/Add/Update operation
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct GetAddOrUpdateResult
        {
            /// <summary>
            /// The item in the dictionary which was found or added.
            /// </summary>
            public readonly TItem Item;

            /// <summary>
            /// The old item in the dictionary which was found.
            /// </summary>
            public readonly TItem OldItem;

            /// <summary>
            /// Indicates whether the item is found
            /// </summary>
            public readonly bool IsFound;

            /// <summary>
            /// The item index
            /// </summary>
            public readonly int Index;

            /// <summary>
            /// Result indicating that the item was not found.
            /// </summary>
            public static readonly GetAddOrUpdateResult NotFound = default(GetAddOrUpdateResult);

            /// <summary>
            /// Creates a new GetOrAddResult
            /// </summary>
            /// <param name="item">the item</param>
            /// <param name="isFound">indicates whether the item was already present in the set</param>
            /// <param name="index">the item index</param>
            public GetAddOrUpdateResult(TItem item, bool isFound, int index)
            {
                Item = item;
                IsFound = isFound;
                OldItem = default(TItem);
                Index = index;
            }

            /// <summary>
            /// Creates a new GetOrAddResult
            /// </summary>
            /// <param name="item">the item</param>
            /// <param name="oldItem">the old item</param>
            /// <param name="isFound">indicates whether the item was already present in the set</param>
            /// <param name="index">the item index</param>
            public GetAddOrUpdateResult(TItem item, TItem oldItem, bool isFound, int index)
            {
                Item = item;
                IsFound = isFound;
                OldItem = oldItem;
                Index = index;
            }

            /// <summary>
            /// Implicitly converts from an GetOrAddResult to the item.
            /// </summary>
            /// <param name="result">the get or add result</param>
            /// <returns>the item of the GetOrAddResult</returns>
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
            public static implicit operator TItem(GetAddOrUpdateResult result)
            {
                return result.Item;
            }
        }

        /// <summary>
        /// Accessors for the node/item/bucket buffers which avoids double array accesses into the
        /// big buffers when accessing indices from the same entry buffer.
        /// </summary>
        private struct Accessors
        {
            public BigBuffer<Node>.Accessor Nodes;
            public BigBuffer<TItem>.Accessor Items;

            public Accessors(ConcurrentBigSet<TItem> set)
            {
                Nodes = set.m_nodes.GetAccessor();
                Items = set.m_items.GetAccessor();
            }
        }

        private sealed class ItemsCollection : IReadOnlyCollection<TItem>
        {
            private readonly ConcurrentBigSet<TItem> m_set;

            public ItemsCollection(ConcurrentBigSet<TItem> set)
            {
                m_set = set;
            }

            public int Count => m_set.m_count;

            public IEnumerator<TItem> GetEnumerator()
            {
                var accessor = m_set.m_accessors;
                var length = m_set.m_nodeLength;
                for (int i = 0; i < length; i++)
                {
                    if (accessor.Nodes[i].Hashcode != Node.UnusedHashCode)
                    {
                        yield return accessor.Items[i];
                    }
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        /// <summary>
        /// Implements IPendingSetItem for adding a TItem using homogenous equality comparison semantics via
        /// the given comparer.
        /// </summary>
        private readonly struct PendingSetItem : IPendingSetItem<TItem>
        {
            private readonly int m_hashCode;
            private readonly TItem m_item;
            private readonly IEqualityComparer<TItem> m_comparer;
            private readonly bool m_remove;

            public PendingSetItem(TItem item, IEqualityComparer<TItem> comparer, bool remove = false)
            {
                m_item = item;
                m_hashCode = comparer.GetHashCode(item);
                m_comparer = comparer;
                m_remove = remove;
            }

            public PendingSetItem(TItem item, int hashCode, IEqualityComparer<TItem> comparer, bool remove = false)
            {
                m_item = item;
                m_hashCode = hashCode;
                m_comparer = comparer;
                m_remove = remove;
            }

            public int HashCode => m_hashCode;

            public bool Equals(TItem other)
            {
                return m_comparer.Equals(m_item, other);
            }

            public TItem CreateOrUpdateItem(TItem oldItem, bool hasOldItem, out bool remove)
            {
                remove = m_remove;
                return m_item;
            }
        }
    }
}
