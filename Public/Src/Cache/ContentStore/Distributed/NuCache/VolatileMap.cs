// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Utilities.Collections;
using Microsoft.WindowsAzure.Storage.Table;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Caches values in a set for a given amount of time
    /// </summary>
    public sealed class VolatileMap<TKey, TValue>
    {
        private readonly IClock _clock;

        private readonly ConcurrentBigMap<TKey, (DateTime expiry, TValue value)> _cachedValues 
            = new ConcurrentBigMap<TKey, (DateTime expiry, TValue value)>(valueComparer: new ValueExpiryComparer());
        private readonly ConcurrentQueue<TKey> _garbageCollectionQueue = new ConcurrentQueue<TKey>();

        /// <nodoc />
        public VolatileMap(IClock clock)
        {
            Contract.Requires(clock != null);
            _clock = clock;
        }

        /// <summary>
        /// Returns the number of elements in the set.
        /// </summary>
        public int Count => _cachedValues.Count;

        /// <summary>
        /// Enumerates the entries 
        /// </summary>
        public IEnumerable<(TKey Key, TValue Value)> Enumerate()
        {
            foreach (var key in _garbageCollectionQueue)
            {
                if (TryGetValue(key, out var value))
                {
                    yield return (key, value);
                }
            }
        }

        /// <summary>
        /// Sets the cached pin information.
        /// </summary>
        public bool TryAdd(TKey key, TValue value, TimeSpan timeToLive, bool replaceIfExists = false, bool extendExpiryIfExists = false)
        {
            Contract.Requires(timeToLive >= TimeSpan.Zero);

            var now = _clock.UtcNow;
            var expiry = now + timeToLive;

            var result = _cachedValues.AddOrUpdate(
                key,
                (expiry, value, replaceIfExists, now, extendExpiryIfExists),
                addValueFactory: (k, entry) => (entry.expiry, entry.value),
                updateValueFactory: (k, t, entry) =>
                {
                    if (!t.replaceIfExists && (t.now < t.expiry))
                    {
                        if (t.extendExpiryIfExists)
                        {
                            return (new DateTime(Math.Max(t.expiry.Ticks, entry.expiry.Ticks)), entry.value);
                        }

                        return entry;
                    }
                    else
                    {
                        return (new DateTime(Math.Max(t.expiry.Ticks, entry.expiry.Ticks)), t.value);
                    }
                });

            // Attempt to clean up a stale item. This supplements the remove if expired logic which
            // would never clean up items if they are not pinned again.
            CleanStaleItems(dequeueCount: 1);

            if (result.IsFound)
            {
                // Update entry. Consider entry added if the current entry is expired
                return now > result.OldItem.Value.expiry;
            }
            else
            {
                // Only add to the garbage collection queue if the item is newly added
                _garbageCollectionQueue.Enqueue(key);

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
                    if (TryGetValueCore(item, out _, garbageCollect: true))
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
        public void Invalidate(TKey key)
        {
            if (_cachedValues.TryGetValue(key, out var entry))
            {
                // Update the entry to mark it as expired. NOTE: This does not remove the entry to
                // ensure the garbage collection queues items stay in sync with entries in set.
                _cachedValues.TryUpdate(key, newValue: (DateTime.MinValue, default(TValue)), comparisonValue: entry);

                CleanStaleItems(dequeueCount: 1);
            }
        }

        /// <summary>
        /// Gets whether the set contains the value and the value has not expired.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return TryGetValueCore(key, out value);
        }

        private bool TryGetValueCore(TKey key, out TValue value, bool garbageCollect = false)
        {
            value = default;

            while (_cachedValues.TryGetValue(key, out var entry))
            {
                if (entry.expiry > _clock.UtcNow)
                {
                    value = entry.value;
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
                if (_cachedValues.CompareRemove(key, entry))
                {
                    return false;
                }
            }

            return false;
        }

        private class ValueExpiryComparer : IEqualityComparer<(DateTime expiry, TValue value)>
        {
            public bool Equals((DateTime expiry, TValue value) x, (DateTime expiry, TValue value) y)
            {
                return x.expiry == y.expiry;
            }

            public int GetHashCode((DateTime expiry, TValue value) obj)
            {
                return obj.expiry.GetHashCode();
            }
        }
    }
}
