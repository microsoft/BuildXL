// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Wraps a concurrent big set as a map
    /// </summary>
    /// <typeparam name="TKey">the key type</typeparam>
    /// <typeparam name="TValue">the value type</typeparam>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public sealed class ConcurrentBigMap<TKey, TValue> : IReadOnlyCollection<ConcurrentBigMapEntry<TKey, TValue>>
    {
        /// <summary>
        /// The underlying set for the map
        /// </summary>
        public readonly ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>> BackingSet;

        /// <summary>
        /// The comparer used to comparer key-value pairs
        /// </summary>
        private readonly IEqualityComparer<TKey> m_keyComparer;

        /// <summary>
        /// The comparer used to comparer values
        /// </summary>
        private readonly IEqualityComparer<TValue> m_valueComparer;

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="backingSet">the backing set for the map</param>
        /// <param name="keyComparer">the comparer for keys</param>
        /// <param name="valueComparer">the comparer for values used in compare exchange operations</param>
        public ConcurrentBigMap(ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>> backingSet, IEqualityComparer<TKey> keyComparer = null, IEqualityComparer<TValue> valueComparer = null)
        {
            Contract.Requires(backingSet != null);

            BackingSet = backingSet;
            m_keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            m_valueComparer = valueComparer ?? EqualityComparer<TValue>.Default;
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="concurrencyLevel">the concurrency level (all values less than 1024 will be assumed to be 1024)</param>
        /// <param name="capacity">the initial capacity (ie number of buckets)</param>
        /// <param name="ratio">the desired ratio of items to buckets (must be greater than 0)</param>
        /// <param name="keyComparer">the comparer for keys</param>
        /// <param name="valueComparer">the comparer for values used in compare exchange operations</param>
        public ConcurrentBigMap(
            int concurrencyLevel = ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.DefaultConcurrencyLevel,
            int capacity = ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.DefaultCapacity,
            int ratio = ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.DefaultBucketToItemsRatio,
            IEqualityComparer<TKey> keyComparer = null, IEqualityComparer<TValue> valueComparer = null)
            : this(new ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>(concurrencyLevel, capacity, ratio), keyComparer, valueComparer)
        {
        }

        /// <summary>
        /// Creates and returns set and its backing items buffer.
        /// </summary>
        /// <param name="concurrencyLevel">the concurrency level (all values less than 1024 will be assumed to be 1024)</param>
        /// <param name="capacity">the initial capacity (ie number of buckets)</param>
        /// <param name="ratio">the desired ratio of items to buckets (must be greater than 0)</param>
        /// <param name="items">an enumeration of unique (!)  items to insert</param>
        /// <param name="keyComparer">the comparer for keys</param>
        /// <param name="valueComparer">the comparer for values used in compare exchange operations</param>
        public static ConcurrentBigMap<TKey, TValue> Create(
            int concurrencyLevel = ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.DefaultConcurrencyLevel,
            int capacity = ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.DefaultCapacity,
            int ratio = ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.DefaultBucketToItemsRatio,
            IEnumerable<ConcurrentBigMapEntry<TKey, TValue>> items = null,
            IEqualityComparer<TKey> keyComparer = null, IEqualityComparer<TValue> valueComparer = null)
        {
            var result = new ConcurrentBigMap<TKey, TValue>(concurrencyLevel: concurrencyLevel, capacity: capacity, ratio: ratio, keyComparer: keyComparer, valueComparer: valueComparer);
            if (items != null)
            {
                result.BackingSet.UnsafeAddItems(items.Select(item => result.CreateKeyValuePendingItem(item.Key, item.Value)));
            }

            return result;
        }

        /// <summary>
        /// Converts the map values to another type
        /// NOTE: This set cannot be safely used after conversion.
        /// </summary>
        /// <param name="convert">the function used to convert values</param>
        /// <param name="valueComparer">the comparer for values used in compare exchange operations</param>
        /// <returns>The set with converted map</returns>
        public ConcurrentBigMap<TKey, TNewValue> ConvertUnsafe<TNewValue>(Func<TValue, TNewValue> convert, IEqualityComparer<TNewValue> valueComparer = null)
        {
            Contract.Requires(convert != null);

            return new ConcurrentBigMap<TKey, TNewValue>(BackingSet.ConvertUnsafe(kvp => new ConcurrentBigMapEntry<TKey, TNewValue>(kvp.Key, convert(kvp.Value))), m_keyComparer, valueComparer);
        }

        /// <summary>
        /// Writes this set.
        /// </summary>
        /// <remarks>
        /// This method is not threadsafe.
        /// </remarks>
        public void Serialize(BinaryWriter writer, Action<ConcurrentBigMapEntry<TKey, TValue>> itemWriter)
        {
            Contract.Requires(writer != null);
            Contract.Requires(itemWriter != null);
            BackingSet.Serialize(writer, itemWriter);
        }

        /// <summary>
        /// Creates and returns set by deserialization
        /// </summary>
        /// <param name="reader">general reader</param>
        /// <param name="itemReader">item reader</param>
        /// <param name="concurrencyLevel">the concurrency level (all values less than 1024 will be assumed to be 1024)</param>
        /// <param name="keyComparer">the comparer for keys</param>
        /// <param name="valueComparer">the comparer for values used in compare exchange operations</param>
        public static ConcurrentBigMap<TKey, TValue> Deserialize(
            BinaryReader reader,
            Func<ConcurrentBigMapEntry<TKey, TValue>> itemReader,
            int concurrencyLevel = ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.DefaultConcurrencyLevel,
            IEqualityComparer<TKey> keyComparer = null,
            IEqualityComparer<TValue> valueComparer = null)
        {
            Contract.Ensures(Contract.Result<ConcurrentBigMap<TKey, TValue>>() != null);

            var set = ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.Deserialize(
                reader,
                itemReader,
                concurrencyLevel);
            return new ConcurrentBigMap<TKey, TValue>(set, keyComparer, valueComparer);
        }

        /// <summary>
        /// Checks if all known hashcodes are in line with a given equality comparer.
        /// </summary>
        public bool Validate()
        {
            return BackingSet.Validate(new Comparer(m_keyComparer));
        }

        private sealed class Comparer : IEqualityComparer<ConcurrentBigMapEntry<TKey, TValue>>
        {
            public readonly IEqualityComparer<TKey> KeyComparer;

            public Comparer(IEqualityComparer<TKey> keyComparer)
            {
                KeyComparer = keyComparer;
            }

            public bool Equals(ConcurrentBigMapEntry<TKey, TValue> x, ConcurrentBigMapEntry<TKey, TValue> y)
            {
                return KeyComparer.Equals(x.Key, y.Key);
            }

            public int GetHashCode(ConcurrentBigMapEntry<TKey, TValue> obj)
            {
                return KeyComparer.GetHashCode(obj.Key);
            }
        }

        /// <summary>
        /// Tries to retrieved the value for the given key
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <param name="value">The value found if this method returns true. Otherwise, the default value.</param>
        /// <returns>true if the key was found. Otherwise, false.</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            ConcurrentBigMapEntry<TKey, TValue> foundPair;
            if (BackingSet.TryGetItem(CreateKeyValuePendingItem(key), out foundPair))
            {
                value = foundPair.Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>
        /// Tries to retrieved the value for the given key
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>the result of the get operation</returns>
        public ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.GetAddOrUpdateResult TryGet(TKey key)
        {
            return BackingSet.GetOrAddItem(CreateKeyValuePendingItem(key), allowAdd: false);
        }

        /// <summary>
        /// Gets or sets the value for the specified key
        /// </summary>
        public TValue this[TKey key]
        {
            get
            {
                TValue value;
                if (!TryGetValue(key, out value))
                {
                    throw new KeyNotFoundException("Key not found: " + key);
                }

                return value;
            }

            set
            {
                BackingSet.UpdateItem(CreateKeyValuePendingItem(key, value));
            }
        }

        /// <summary>
        /// Checks if the map has an entry for the given key
        /// </summary>
        /// <param name="key">the key to find</param>
        /// <returns>true if the collection contains the key</returns>
        public bool ContainsKey(TKey key)
        {
            return BackingSet.ContainsItem(CreateKeyValuePendingItem(key));
        }

        /// <summary>
        /// Adds the key and value to the map
        /// </summary>
        /// <param name="key">the key to add</param>
        /// <param name="value">the value to add</param>
        public void Add(TKey key, TValue value)
        {
            var result = BackingSet.GetOrAddItem(CreateKeyValuePendingItem(key, value));
            if (result.IsFound)
            {
                throw new ArgumentException("Key already exists: " + key.ToString());
            }
        }

        /// <summary>
        /// Adds or updates the key and value to the map
        /// </summary>
        /// <param name="key">the key to add/update</param>
        /// <param name="value">the value</param>
        public ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.GetAddOrUpdateResult Update(TKey key, TValue value)
        {
            return BackingSet.UpdateItem(CreateKeyValuePendingItem(key, value));
        }

        /// <summary>
        /// Adds the key and value to the map
        /// </summary>
        /// <typeparam name="TData">the type of the additional data passed to the delegate</typeparam>
        /// <param name="key">the key to add</param>
        /// <param name="data">additional data used to create the value</param>
        /// <param name="addValueFactory">creates the value to add</param>
        public ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.GetAddOrUpdateResult GetOrAdd<TData>(TKey key, TData data, Func<TKey, TData, TValue> addValueFactory)
        {
            var result = BackingSet.GetOrAddItem(
                new KeyValuePendingItem<DelegateValueCreator<TData>>(
                    m_keyComparer,
                    key,
                    new DelegateValueCreator<TData>(data, addValueFactory)));

            return result;
        }

        /// <summary>
        /// Gets or adds the key value pair to the map
        /// </summary>
        /// <param name="key">the key to add</param>
        /// <param name="value">the value to add</param>
        public ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.GetAddOrUpdateResult GetOrAdd(TKey key, TValue value)
        {
            return BackingSet.GetOrAddItem(CreateKeyValuePendingItem(key, value));
        }

        /// <summary>
        /// Adds the key and value to the map
        /// </summary>
        /// <typeparam name="TData">the type of the additional data passed to the delegate</typeparam>
        /// <param name="key">the key to add</param>
        /// <param name="data">additional data used to create the value</param>
        /// <param name="addValueFactory">creates the value to add</param>
        /// <param name="updateValueFactory">updates the value</param>
        public ConcurrentBigSet<ConcurrentBigMapEntry<TKey, TValue>>.GetAddOrUpdateResult AddOrUpdate<TData>(TKey key, TData data, Func<TKey, TData, TValue> addValueFactory, Func<TKey, TData, TValue, TValue> updateValueFactory)
        {
            var result = BackingSet.UpdateItem(
                new KeyValuePendingItem<DelegateValueCreator<TData>>(
                    m_keyComparer,
                    key,
                    new DelegateValueCreator<TData>(data, addValueFactory, updateValueFactory)));

            return result;
        }

        /// <summary>
        /// Adds the key and value to the map
        /// </summary>
        /// <param name="key">the key to add</param>
        /// <param name="value">the value to add</param>
        /// <returns>true if the value was added. False, if an entry in map already matches the key.</returns>
        public bool TryAdd(TKey key, TValue value)
        {
            var result = BackingSet.GetOrAddItem(CreateKeyValuePendingItem(key, value));
            return !result.IsFound;
        }

        /// <summary>
        /// Adds the key and value to the map
        /// </summary>
        /// <param name="key">the key to add</param>
        /// <param name="value">the value to add</param>
        /// <param name="backingIndex">returns the index in the backing set</param>
        /// <returns>true if the value was added. False, if an entry in map already matches the key.</returns>
        public bool TryAdd(TKey key, TValue value, out int backingIndex)
        {
            var result = BackingSet.GetOrAddItem(CreateKeyValuePendingItem(key, value));
            backingIndex = result.Index;
            return !result.IsFound;
        }

        /// <summary>
        /// Compares the existing value for the specified key with a specified value,
        /// and if they are equal, updates the key with a third value.
        /// </summary>
        /// <param name="key">the key to add</param>
        /// <param name="newValue">the value to add</param>
        /// <param name="comparisonValue">the value to compare against the existing value</param>
        /// <returns>true if the value with the key was found and equal to the comparison value and replaced with newValue. Otherwise, false.</returns>
        public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
        {
            var result = BackingSet.UpdateItem(CreateKeyValuePendingItem(key, newValue, comparisonValue, m_valueComparer), allowAdd: false);
            return result.IsFound;
        }

        /// <summary>
        /// Removes the entry for the key from the map
        /// </summary>
        /// <param name="key">the key to remove</param>
        /// <returns>true if the value with the key was found and removed, otherwise false.</returns>
        public bool RemoveKey(TKey key)
        {
            var result = BackingSet.UpdateItem(CreateKeyValuePendingItem(key), allowAdd: false);
            return result.IsFound;
        }

        /// <summary>
        /// Compares the existing value for the specified key with a specified value,
        /// and if they are equal, removes the key.
        /// </summary>
        /// <param name="key">the key to add</param>
        /// <param name="comparisonValue">the value to compare against the existing value</param>
        /// <returns>true if the value with the key was found and equal to the comparison value and replaced with newValue. Otherwise, false.</returns>
        public bool CompareRemove(TKey key, TValue comparisonValue)
        {
            var result = BackingSet.UpdateItem(CreateKeyValuePendingItem(key, default(TValue), comparisonValue, m_valueComparer, remove: true), allowAdd: false);
            return result.IsFound;
        }

        /// <summary>
        /// Attempts to remove the entry for the key from the map and return the removed value
        /// </summary>
        /// <param name="key">the key to add</param>
        /// <param name="value">the value removed if this method returns true. Otherwise, the default value.</param>
        /// <returns>true if the value with the key was found and removed, otherwise false.</returns>
        public bool TryRemove(TKey key, out TValue value)
        {
            var result = BackingSet.UpdateItem(CreateKeyValuePendingItem(key), allowAdd: false);
            value = result.OldItem.Value;
            return result.IsFound;
        }

        /// <summary>
        /// Gets the count of the entries in the map
        /// </summary>
        public int Count => BackingSet.Count;

        /// <summary>
        /// Enumerates the keys in the set.
        /// NOTE: Enumeration NOT threadsafe with respect to update/remove operations.
        /// </summary>
        public IEnumerable<TKey> Keys
        {
            get
            {
                foreach (var item in BackingSet.UnsafeGetList())
                {
                    yield return item.Key;
                }
            }
        }

        /// <summary>
        /// Enumerates the values in the set
        /// NOTE: Enumeration NOT threadsafe with respect to update/remove operations.
        /// </summary>
        public IEnumerable<TValue> Values
        {
            get
            {
                foreach (var item in BackingSet.UnsafeGetList())
                {
                    yield return item.Value;
                }
            }
        }

        /// <summary>
        /// Gets the enumerator for the entries in the map
        /// NOTE: Enumeration NOT threadsafe with respect to update/remove operations.
        /// </summary>
        public IEnumerator<ConcurrentBigMapEntry<TKey, TValue>> GetEnumerator()
        {
            return BackingSet.UnsafeGetList().GetEnumerator();
        }

        /// <summary>
        /// Gets the enumerator for the entries in the map
        /// NOTE: Enumeration NOT threadsafe with respect to update/remove operations.
        /// </summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private KeyValuePendingItem<IdentityValueCreator> CreateKeyValuePendingItem(TKey key)
        {
            return new KeyValuePendingItem<IdentityValueCreator>(m_keyComparer, key);
        }

        private KeyValuePendingItem<IdentityValueCreator> CreateKeyValuePendingItem(TKey key, TValue value, TValue comparisonValue = default(TValue), IEqualityComparer<TValue> valueComparer = null, bool remove = false)
        {
            return new KeyValuePendingItem<IdentityValueCreator>(m_keyComparer, key, new IdentityValueCreator(value), comparisonValue, valueComparer, remove);
        }

        private interface IMapOperation
        {
            TValue CreateOrUpdateItem(TKey key, TValue oldValue, bool hasOldValue, out bool remove);
        }

        private readonly struct DelegateValueCreator<TData> : IMapOperation
        {
            private readonly Func<TKey, TData, TValue> m_addValueFactory;
            private readonly Func<TKey, TData, TValue, TValue> m_updateValueFactory;
            private readonly TData m_data;

            public DelegateValueCreator(TData data, Func<TKey, TData, TValue> addValueFactory, Func<TKey, TData, TValue, TValue> updateValueFactory = null)
            {
                m_addValueFactory = addValueFactory;
                m_updateValueFactory = updateValueFactory;
                m_data = data;
            }

            public TValue CreateOrUpdateItem(TKey key, TValue oldValue, bool hasOldValue, out bool remove)
            {
                remove = false;
                if (hasOldValue && m_updateValueFactory != null)
                {
                    return m_updateValueFactory(key, m_data, oldValue);
                }

                return m_addValueFactory(key, m_data);
            }
        }

        private readonly struct IdentityValueCreator : IMapOperation
        {
            private readonly TValue m_value;

            public IdentityValueCreator(TValue value)
            {
                m_value = value;
            }

            public TValue CreateOrUpdateItem(TKey key, TValue oldValue, bool hasOldValue, out bool remove)
            {
                remove = false;
                return m_value;
            }
        }

        private readonly struct KeyValuePendingItem<TMapOperation> : IPendingSetItem<ConcurrentBigMapEntry<TKey, TValue>>
            where TMapOperation : IMapOperation
        {
            private readonly IEqualityComparer<TKey> m_keyComparer;
            private readonly IEqualityComparer<TValue> m_valueComparer;
            private readonly bool m_allowCreate;
            public readonly TKey Key;
            public readonly TMapOperation ValueCreator;
            public readonly TValue ComparisonValue;

            public KeyValuePendingItem(IEqualityComparer<TKey> keyComparer, TKey key, TMapOperation valueCreator, TValue comparisonValue = default(TValue), IEqualityComparer<TValue> valueComparer = null, bool remove = false)
            {
                m_keyComparer = keyComparer;
                Key = key;
                ValueCreator = valueCreator;
                m_allowCreate = !remove;
                m_valueComparer = valueComparer;
                ComparisonValue = comparisonValue;
            }

            public KeyValuePendingItem(IEqualityComparer<TKey> keyComparer, TKey key)
            {
                m_keyComparer = keyComparer;
                Key = key;
                ValueCreator = default(TMapOperation);
                m_allowCreate = false;
                m_valueComparer = null;
                ComparisonValue = default(TValue);
            }

            public int HashCode => m_keyComparer.GetHashCode(Key);

            public bool Equals(ConcurrentBigMapEntry<TKey, TValue> other)
            {
                return m_keyComparer.Equals(Key, other.Key) &&

                    // TryUpdate (i.e., CompareExchange) operation sets valueComparer so only updated if ComparisonValue matches
                    (m_valueComparer == null || m_valueComparer.Equals(ComparisonValue, other.Value));
            }

            public ConcurrentBigMapEntry<TKey, TValue> CreateOrUpdateItem(ConcurrentBigMapEntry<TKey, TValue> oldItem, bool hasOldItem, out bool remove)
            {
                remove = !m_allowCreate;
                if (remove)
                {
                    return default(ConcurrentBigMapEntry<TKey, TValue>);
                }

                return new ConcurrentBigMapEntry<TKey, TValue>(Key, ValueCreator.CreateOrUpdateItem(Key, oldItem.Value, hasOldItem, out remove));
            }
        }
    }
}
