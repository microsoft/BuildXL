// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Cache.ContentStore.UtilitiesCore
{
    /// <summary>
    ///     A group of related counters. This class is not thread-safe, as it is not intended to actually calculate values: it is intended only to store them,
    ///     and then leave them untouched.
    /// </summary>
    public class CounterSet
    {
        private readonly HashSet<Counter> _counters = new HashSet<Counter>();

        /// <nodoc />
        public IReadOnlyCollection<Counter> Counters => (IReadOnlyCollection<Counter>)_counters;

        /// <summary>
        ///     Get a value with a name matching the given pattern, casting to some type.
        /// </summary>
        public long GetIntegralWithNameLike(string name)
        {
            // We'll try to find exact match first, and only if there is no one, will do a search.

            var match = _counters.FirstOrDefault(counter => counter.Name == name) ??
                        _counters.FirstOrDefault(counter => counter.Name.Contains(name));
            if (match == null)
            {
                throw new ArgumentException($"counter with name like [{name}] not found");
            }

            return match.Value;
        }

        /// <summary>
        ///     Add a new counter.
        /// </summary>
        public void Add(string name, long value) => Add(name, value, metricName: null);

        /// <summary>
        ///     Add a new counter.
        /// </summary>
        public void Add(string name, long value, string metricName) => 
            AddCounter(name, value, metricName);

        /// <summary>
        /// Adds a counter that will also function as a top-level metric
        /// </summary>
        public void AddMetric(string name, long value) =>
            AddCounter(name, value, name);

        /// <summary>
        ///     Merge another set into this one.
        /// </summary>
        public CounterSet Merge(CounterSet other)
        {
            foreach (var counter in other._counters)
            {
                AddCounter(counter.Name, counter.Value, counter.MetricName);
            }

            return this;
        }

        /// <summary>
        ///     Merge another set into this one, but prefix the incoming names.
        /// </summary>
        public void Merge(CounterSet other, string keyPrefix)
        {
            foreach (var counter in other._counters)
            {
                AddCounter(keyPrefix + counter.Name, counter.Value, counter.MetricName);
            }
        }

        /// <summary>
        ///     Return a dictionary containing all counters with integral values.
        /// </summary>
        public IDictionary<string, long> ToDictionaryIntegral()
        {
            return _counters.ToDictionary(counter => counter.Name, counter => counter.Value);
        }

        /// <summary>
        ///     Call an action for every counter name/value formatted string.
        /// </summary>
        public void LogOrderedNameValuePairs(Action<string> action)
        {
            foreach (var counter in _counters.OrderBy(counter => counter.Name))
            {
                action($"{counter.Name}={counter.Value}");
            }
        }

        private void AddCounter(string name, double value, string metricName)
        {
            // metricName is not necessarily unique.
            // Different counters could have the same metric name and in this case
            // we'll have more then record for the same metric from different counters.
            if (!_counters.Add(new Counter(name, (long)value, metricName)))
            {
                throw new ArgumentException($"An item with the same key '{name}' has already been added.");
            }
        }

        /// <nodoc />
        public class Counter : IEquatable<Counter>
        {
            /// <nodoc />
            public readonly string Name;

            /// <nodoc />
            public readonly long Value;

            /// <nodoc />
            public readonly string MetricName;

            internal Counter(string name, long value, string metricName)
            {
                Name = name;
                Value = value;
                MetricName = metricName;
            }

            /// <nodoc />
            public override int GetHashCode() => Name.GetHashCode();

            /// <nodoc />
            public override bool Equals(object obj) => obj is Counter other && Equals(other);

            /// <nodoc />
            public bool Equals(Counter other) => Name == other.Name;

            /// <inheritdoc />
            public override string ToString() => $"{nameof(Name)}: {Name}, {nameof(Value)}: {Value}";
        }
    }
}
