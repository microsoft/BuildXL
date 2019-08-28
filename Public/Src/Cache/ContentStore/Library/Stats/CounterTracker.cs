// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Stats
{
    /// <summary>
    /// Class used to track several counter collections. Useful for the case where stores want to track the counters of all their
    /// children.
    /// </summary>
    internal class CounterTracker
    {
        private ConcurrentDictionary<Type, CounterCollection> _counters = new ConcurrentDictionary<Type, CounterCollection>();
        private ConcurrentDictionary<string, CounterTracker> _trackers = new ConcurrentDictionary<string, CounterTracker>();

        /// <nodoc />
        public CounterCollection<T> AddOrGetCounterCollection<T>() where T : struct
            => (CounterCollection<T>)_counters.GetOrAdd(typeof(T), _ => new CounterCollection<T>());

        /// <nodoc />
        public CounterTracker AddOrGetChildCounterTracker(string name)
            => _trackers.GetOrAdd(name, _ => new CounterTracker());

        /// <nodoc />
        public CounterSet ToCounterSet()
        {
            var result = new CounterSet();

            foreach (var kvp in _counters)
            {
                var counterType = kvp.Key;
                var counterCollection = kvp.Value;

                result.Merge(counterCollection.ToCounterSet(counterType));
            }

            foreach (var kvp in _trackers)
            {
                var name = kvp.Key;
                var tracker = kvp.Value;

                result.Merge(tracker.ToCounterSet(), name);
            }

            return result;
        }
    }
}
