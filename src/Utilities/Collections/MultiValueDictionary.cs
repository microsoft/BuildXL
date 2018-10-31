// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Dictionary for which a key can contain multiple values.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public sealed class MultiValueDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, IReadOnlyList<TValue>>
    {
        private readonly Dictionary<TKey, List<TValue>> m_backingDictionary;

        /// <nodoc />
        public MultiValueDictionary()
        {
            m_backingDictionary = new Dictionary<TKey, List<TValue>>();
        }

        /// <nodoc />
        public MultiValueDictionary(int capacity)
        {
            m_backingDictionary = new Dictionary<TKey, List<TValue>>(capacity);
        }

        /// <nodoc />
        public MultiValueDictionary(IEqualityComparer<TKey> comparer)
        {
            m_backingDictionary = new Dictionary<TKey, List<TValue>>(comparer);
        }

        /// <nodoc />
        public MultiValueDictionary(MultiValueDictionary<TKey, TValue> multiValueDictionary)
        {
            var sourceBackingDictionary = multiValueDictionary.m_backingDictionary;

            m_backingDictionary = new Dictionary<TKey, List<TValue>>(sourceBackingDictionary.Count, sourceBackingDictionary.Comparer);
            foreach (var key in sourceBackingDictionary.Keys)
            {
                var values = new List<TValue>(sourceBackingDictionary[key]);
                m_backingDictionary[key] = values;
            }
        }

        /// <summary>
        /// Adds the value to the list of values for the given key.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            List<TValue> multiValues;
            if (!m_backingDictionary.TryGetValue(key, out multiValues))
            {
                multiValues = new List<TValue>();
                m_backingDictionary.Add(key, multiValues);
            }

            multiValues.Add(value);
        }

        /// <summary>
        /// Adds the value to the list of values for the given key.
        /// </summary>
        public void Add(TKey key, params TValue[] values)
        {
            List<TValue> multiValues;
            if (!m_backingDictionary.TryGetValue(key, out multiValues))
            {
                multiValues = new List<TValue>();
                m_backingDictionary.Add(key, multiValues);
            }

            multiValues.AddRange(values);
        }

        /// <summary>
        /// Removes a specific key and its associated values
        /// </summary>
        public bool Remove(TKey key)
        {
            if (!m_backingDictionary.ContainsKey(key))
            {
                return false;
            }

            return m_backingDictionary.Remove(key);
        }

        /// <summary>
        /// Clears the content of the dictionary.
        /// </summary>
        public void Clear() => m_backingDictionary.Clear();

        /// <inheritdoc />
        public int Count => m_backingDictionary.Count;

        /// <inheritdoc />
        public IReadOnlyList<TValue> this[TKey key]
        {
            get
            {
                return m_backingDictionary[key];
            }
        }

        /// <inheritdoc />
        public IEnumerable<IReadOnlyList<TValue>> Values => m_backingDictionary.Values;

        /// <inheritdoc />
        public IEnumerable<TKey> Keys => m_backingDictionary.Keys;

        /// <inheritdoc />
        public bool ContainsKey(TKey key)
        {
            return m_backingDictionary.ContainsKey(key);
        }

        /// <inheritdoc />
        public bool TryGetValue(TKey key, out IReadOnlyList<TValue> multiValues)
        {
            List<TValue> mutableMultiValues;
            if (m_backingDictionary.TryGetValue(key, out mutableMultiValues))
            {
                multiValues = mutableMultiValues;
                return true;
            }

            multiValues = null;
            return false;
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<TKey, IReadOnlyList<TValue>>> GetEnumerator()
        {
            // KeyValuePair is not covariant, so we have to create new structs here.
            foreach (var kv in m_backingDictionary)
            {
                yield return new KeyValuePair<TKey, IReadOnlyList<TValue>>(kv.Key, kv.Value);
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <nodoc/>
    public static class MultiValueDictionaryExtensionMethods
    {
        /// <summary>
        /// Creates a multi-value dictionary from an enumeration of pairs.
        /// </summary>
        public static MultiValueDictionary<TKey, TValue> ToMultiValueDictionary<TKey, TValue>(
            this IEnumerable<KeyValuePair<TKey, IReadOnlyList<TValue>>> enumerable)
        {
            var result = new MultiValueDictionary<TKey, TValue>();
            foreach (var pair in enumerable)
            {
                result.Add(pair.Key, pair.Value.ToArray());
            }

            return result;
        }

        /// <summary>
        /// Creates a multi-value dictionary according to specified key selector and element selector functions.
        /// </summary>
        public static MultiValueDictionary<TKey, TValue> ToMultiValueDictionary<TSource, TKey, TValue>(
            this IEnumerable<TSource> enumerable,
            Func<TSource, TKey> keySelector,
            Func<TSource, TValue> elementSelector)
        {
            var result = new MultiValueDictionary<TKey, TValue>();
            foreach (var element in enumerable)
            {
                result.Add(keySelector(element), elementSelector(element));
            }

            return result;
        }
    }
}
