// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Caches values in a set for a given amount of time
    /// </summary>
    public sealed class VolatileSet<T>
    {
        private readonly IClock _clock;

        private readonly ConcurrentBigMap<T, DateTime> _cachedValues = new ConcurrentBigMap<T, DateTime>();
        private readonly ConcurrentQueue<T> _garbageCollectionQueue = new ConcurrentQueue<T>();

        /// <nodoc />
        public VolatileSet(IClock clock)
        {
            Contract.Requires(clock != null);
            _clock = clock;
        }

        /// <summary>
        /// Returns the number of elements in the set.
        /// </summary>
        public int Count => _cachedValues.Count;

        /// <summary>
        /// Sets the cached pin information.
        /// </summary>
        public bool Add(T item, TimeSpan timeToLive)
        {
            Contract.Requires(timeToLive >= TimeSpan.Zero);

            var now = _clock.UtcNow;
            var expiryTime = now + timeToLive;
            var result = _cachedValues.AddOrUpdate(
                item,
                expiryTime,
                addValueFactory: (k, expiry) => expiry,
                updateValueFactory: (k, currentExpiry, newExpiry) =>
                    new DateTime(Math.Max(currentExpiry.Ticks, newExpiry.Ticks)));

            // Attempt to clean up a stale item. This supplements the remove if expired logic which
            // would never clean up items if they are not pinned again.
            CleanStaleItems(dequeueCount: 1);

            if (result.IsFound)
            {
                // Update entry. Consider entry added if the current entry is expired
                return now > result.OldItem.Value;
            }
            else
            {
                // Only add to the garbage collection queue if the item is newly added
                _garbageCollectionQueue.Enqueue(item);

                // Added new entry
                return true;
            }
        }

        /// <summary>
        /// Dequeues items from the garbage collection queue and removes them if they have expired. Otherwise, item is added back to garbage collection queue.
        /// This supplements the remove if expired logic which would never clean up items if they are not pinned again.
        /// </summary>
        /// <returns>The number removed items.</returns>
        public int CleanStaleItems(int dequeueCount)
        {
            int removedItems = 0;

            // Only dequeue up to the number of items in the set
            dequeueCount = Math.Min(dequeueCount, _cachedValues.Count);

            for (int i = 0; i < dequeueCount; i++)
            {
                if (_garbageCollectionQueue.TryDequeue(out var item))
                {
                    if (ContainsCore(item, garbageCollect: true))
                    {
                        // Pin result is still valid. Add it back to queue
                        _garbageCollectionQueue.Enqueue(item);
                    }
                    else
                    {
                        removedItems++;
                    }
                }
                else
                {
                    break;
                }
            }

            return removedItems;
        }

        /// <summary>
        /// Removes the item from the pin cache.
        /// </summary>
        public void Invalidate(T item)
        {
            if (_cachedValues.TryGetValue(item, out var expiry))
            {
                // Update the entry to mark it as expired. NOTE: This does not remove the entry to
                // ensure the garbage collection queues items stay in sync with entries in set.
                _cachedValues.TryUpdate(item, newValue: DateTime.MinValue, comparisonValue: expiry);

                CleanStaleItems(dequeueCount: 1);
            }
        }

        /// <summary>
        /// Gets whether the set contains the value and the value has not expired.
        /// </summary>
        public bool Contains(T item)
        {
            return ContainsCore(item);
        }

        private bool ContainsCore(T item, bool garbageCollect = false)
        {
            while (_cachedValues.TryGetValue(item, out var expiry))
            {
                if (expiry > _clock.UtcNow)
                {
                    return true;
                }

                if (!garbageCollect)
                {
                    // Not being called from garbage collect. Just return false since
                    // the element is expired and there is no need to remove the element.
                    return false;
                }

                // This is being called to remove the expired element when cleaning
                // items via garbage collection queue. There can be a race where the item is
                // mutated after the TryGetValue call above. In that case, we should NOT remove
                // the item from the queue. Hence, we return true to indicate the item is still
                // available in the set.
                if (_cachedValues.CompareRemove(item, expiry))
                {
                    return false;
                }
            }

            return false;
        }
    }
}
