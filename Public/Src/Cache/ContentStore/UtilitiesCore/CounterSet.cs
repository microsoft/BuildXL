// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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

            return match.ValueAsLong;
        }

        /// <summary>
        ///     Add a new counter.
        /// </summary>
        public void Add(string name, long value) => Add(name, value, metricName: null);

        /// <summary>
        ///     Add a new counter.
        /// </summary>
        public void Add(string name, double value) => AddCounter(Counter.FromDouble(name, value));

        /// <summary>
        ///     Add a new counter.
        /// </summary>
        public void Add(string name, long value, string metricName) => 
            AddCounter(Counter.FromLong(name, value, metricName));

        /// <summary>
        ///     Merge another set into this one.
        /// </summary>
        public CounterSet Merge(CounterSet other)
        {
            foreach (var counter in other._counters)
            {
                AddCounter(counter);
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
                AddCounter(counter.WithPrefix(keyPrefix));
            }
        }

        /// <summary>
        ///     Return a dictionary containing all counters with integral values.
        /// </summary>
        public IDictionary<string, long> ToDictionaryIntegral()
        {
            return _counters.ToDictionary(counter => counter.Name, counter => counter.ValueAsLong);
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

        private void AddCounter(Counter counter)
        {
            // metricName is not necessarily unique.
            // Different counters could have the same metric name and in this case
            // we'll have more then record for the same metric from different counters.
            if (!_counters.Add(counter))
            {
                throw new ArgumentException($"An item with the same key '{counter.Name}' has already been added.");
            }
        }

        /// <nodoc />
        public class Counter : IEquatable<Counter>
        {
            // The counter has either double or long.
            private readonly double? _doubleValue;
            private readonly long? _longValue;

            /// <nodoc />
            public string Name { get; }

            /// <nodoc />
            public string Value => _longValue != null ? _longValue.ToString() : _doubleValue!.Value.ToString("F3");

            /// <nodoc />
            public long ValueAsLong => _longValue ?? (long)_doubleValue!.Value;

            /// <nodoc />
            public string MetricName { get; }

            private Counter(string name, long? longValue, double? doubleValue, string metricName)
            {
                Contract.Requires(longValue is not null || doubleValue is not null);
                Name = name;
                _longValue = longValue;
                _doubleValue = doubleValue;
                
                MetricName = metricName;
            }

            /// <summary>
            /// Gets a copy of the counter with a given <paramref name="prefix"/>.
            /// </summary>
            public Counter WithPrefix(string prefix)
            {
                return new Counter(prefix + Name, _longValue, _doubleValue, MetricName);
            }

            /// <nodoc />
            public static Counter FromDouble(string name, double value, string metricName = null)
            {
                return new Counter(name, longValue: null, doubleValue: value, metricName: metricName);
            }

            /// <nodoc />
            public static Counter FromLong(string name, long value, string metricName = null)
            {
                return new Counter(name, longValue: value, doubleValue: null, metricName: metricName);
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
