// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Class used to track several counter collections. Useful for the case where stores want to track the counters of all their
    /// children.
    /// </summary>
    public class CounterTracker
    {
        internal ConcurrentDictionary<Type, CounterCollection> _counters = new ConcurrentDictionary<Type, CounterCollection>();
        internal ConcurrentDictionary<string, CounterTracker> _trackers = new ConcurrentDictionary<string, CounterTracker>();

        /// <nodoc />
        public IReadOnlyDictionary<Type, CounterCollection> CounterColletions => (IReadOnlyDictionary<Type, CounterCollection>)_counters;

        /// <nodoc />
        public IReadOnlyDictionary<string, CounterTracker> ChildCounterTrackers => (IReadOnlyDictionary<string, CounterTracker>)_trackers;

        /// <nodoc />
        public CounterCollection<T> AddOrGetCounterCollection<T>() where T : struct
            => (CounterCollection<T>)_counters.GetOrAdd(typeof(T), _ => new CounterCollection<T>());

        /// <nodoc />
        public CounterTracker AddOrGetChildCounterTracker(string name)
            => _trackers.GetOrAdd(name, _ => new CounterTracker());
    }
}
