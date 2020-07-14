// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Caches values in a set for a given amount of time
    /// </summary>
    public sealed class VolatileSet<T>
    {
        private readonly VolatileMap<T, Unit> _map;

        /// <nodoc />
        public VolatileSet(IClock clock)
        {
            _map = new VolatileMap<T, Unit>(clock);
        }

        /// <summary>
        /// Returns the number of elements in the set.
        /// </summary>
        public int Count => _map.Count;

        /// <summary>
        /// Add the item or updates ttl if already present
        /// </summary>
        public bool Add(T item, TimeSpan timeToLive)
        {
            return _map.TryAdd(
                item, 
                Unit.Void, 
                timeToLive, 
                // Replace=true in order to update ttl
                replaceIfExists: true);
        }

        /// <summary>
        /// Dequeues items from the garbage collection queue and removes them if they have expired. Otherwise, item is added back to garbage collection queue.
        /// This supplements the remove if expired logic which would never clean up items if they are no further adds to the set.
        /// </summary>
        /// <returns>The number removed items.</returns>
        public int CleanStaleItems(int dequeueCount)
        {
            return _map.CleanStaleItems(dequeueCount);
        }

        /// <summary>
        /// Removes the item from the pin cache.
        /// </summary>
        public void Invalidate(T item)
        {
            _map.Invalidate(item);
        }

        /// <summary>
        /// Gets whether the set contains the value and the value has not expired.
        /// </summary>
        public bool Contains(T item)
        {
            return _map.TryGetValue(item, out _);
        }
    }
}
