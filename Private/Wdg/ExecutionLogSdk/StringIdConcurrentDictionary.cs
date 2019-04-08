// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// This class functions as a wrapper for a ConcurrentDictionary where the
    /// key type is hardcoded to StringId and the value type is generic.
    /// </summary>
    /// <remarks>
    /// This class can be exposed as a ReadOnlyDictionary where the key type is
    /// set to string. This allows us to expose strings to the user while only
    /// having to store StringIds internally. The translation from StringID to
    /// string is done via the StringTable. The purpose of this is to save
    /// memory at run time.
    /// </remarks>
    /// <typeparam name="TValue">The type of value to store in the dictionary</typeparam>
    internal sealed class StringIdConcurrentDictionary<TValue> : IReadOnlyDictionary<string, TValue>, IDictionary<string, TValue>
    {
        #region Private properties

        /// <summary>
        /// The underlying dictionary that stores the StringIds and their respective TValues
        /// </summary>
        private ConcurrentDictionary<StringId, TValue> m_stringIdsToValues;

        /// <summary>
        /// The StringTable instance used to convert between StringIds and strings
        /// </summary>
        private StringTable m_stringTable;
        #endregion

        #region Constructor

        /// <summary>
        /// Initializes dictionary and sets the StringTable instance to use
        /// </summary>
        /// <param name="stringTable">Used to convert between strings and StringIds</param>
        /// <param name="concurrencyLevel">This setting affects the number of locks that the dictionary is using; default value is 32</param>
        /// <param name="initialCapacity">Initial capacity of the collection; default value is 31</param>
        internal StringIdConcurrentDictionary(StringTable stringTable, int concurrencyLevel = 32, int initialCapacity = 31)
        {
            m_stringIdsToValues = new ConcurrentDictionary<StringId, TValue>(concurrencyLevel, initialCapacity);
            m_stringTable = stringTable;
        }
        #endregion

        #region Internal methods
        internal TValue this[StringId key]
        {
            get
            {
                return m_stringIdsToValues[key];
            }

            set
            {
                m_stringIdsToValues[key] = value;
            }
        }

        internal TValue this[string key]
        {
            get
            {
                return this[m_stringTable.StringToId(key)];
            }

            set
            {
                this[m_stringTable.StringToId(key)] = value;
            }
        }

        internal TValue GetOrAdd(string key, Func<string, TValue> valueFactory)
        {
            return GetOrAdd(m_stringTable.StringToId(key), (str) => valueFactory(key));
        }

        internal TValue GetOrAdd(string key, TValue value)
        {
            return GetOrAdd(m_stringTable.StringToId(key), value);
        }

        internal TValue GetOrAdd(StringId stringId, Func<string, TValue> valueFactory)
        {
            return m_stringIdsToValues.GetOrAdd(stringId, (str) => valueFactory(m_stringTable.IdToString(stringId)));
        }

        internal TValue GetOrAdd(StringId stringId, TValue value)
        {
            return m_stringIdsToValues.GetOrAdd(stringId, value);
        }
        #endregion

        #region Public and Interface methods
        TValue IReadOnlyDictionary<string, TValue>.this[string key]
        {
            get
            {
                return this[m_stringTable.StringToId(key)];
            }
        }

        TValue IDictionary<string, TValue>.this[string key]
        {
            get
            {
                return this[m_stringTable.StringToId(key)];
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        IEnumerable<string> IReadOnlyDictionary<string, TValue>.Keys
        {
            get
            {
                foreach (var k in m_stringIdsToValues.Keys)
                {
                    yield return m_stringTable.IdToString(k);
                }
            }
        }

        IEnumerable<TValue> IReadOnlyDictionary<string, TValue>.Values => m_stringIdsToValues.Values;

        public int Count => m_stringIdsToValues.Count;

        public ICollection<string> Keys
        {
            get
            {
                IList<string> keys = new List<string>();
                foreach (var k in m_stringIdsToValues.Keys)
                {
                    keys.Add(m_stringTable.IdToString(k));
                }

                return keys;
            }
        }

        public ICollection<TValue> Values => m_stringIdsToValues.Values;

        public bool IsReadOnly => true;

        public bool ContainsKey(string key)
        {
            return ContainsKey(m_stringTable.StringToId(key));
        }

        bool IDictionary<string, TValue>.ContainsKey(string key)
        {
            return ContainsKey(m_stringTable.StringToId(key));
        }

        public bool ContainsKey(StringId key)
        {
            return m_stringIdsToValues.ContainsKey(key);
        }

        public bool Contains(KeyValuePair<string, TValue> item)
        {
            StringId keyStringId = m_stringTable.StringToId(item.Key);
            if (ContainsKey(keyStringId))
            {
                if (this[keyStringId].Equals(item.Value))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValue(StringId key, out TValue value)
        {
            return m_stringIdsToValues.TryGetValue(key, out value);
        }

        public bool TryGetValue(string key, out TValue value)
        {
            return TryGetValue(m_stringTable.StringToId(key), out value);
        }

        bool IDictionary<string, TValue>.TryGetValue(string key, out TValue value)
        {
            return TryGetValue(m_stringTable.StringToId(key), out value);
        }

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            foreach (var kv in m_stringIdsToValues)
            {
                yield return new KeyValuePair<string, TValue>(m_stringTable.IdToString(kv.Key), kv.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                throw new ArgumentOutOfRangeException("arrayIndex");
            }

            if ((array.Length - arrayIndex) < m_stringIdsToValues.Count)
            {
                throw new ArgumentException("Destination array is not large enough. Check array.Length and arrayIndex.");
            }

            foreach (KeyValuePair<StringId, TValue> kv in m_stringIdsToValues)
            {
                array[arrayIndex++] = new KeyValuePair<string, TValue>(m_stringTable.IdToString(kv.Key), kv.Value);
            }
        }
        #endregion

        #region Unimplemented Interface Methods
        public void Add(string key, TValue value)
        {
            throw new NotImplementedException();
        }

        public bool Remove(string key)
        {
            throw new NotImplementedException();
        }

        public void Add(KeyValuePair<string, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<string, TValue> item)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
