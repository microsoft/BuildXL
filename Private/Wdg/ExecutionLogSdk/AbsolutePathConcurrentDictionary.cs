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
    /// key type is hardcoded to AbsolutePath and the value type is generic.
    /// </summary>
    /// <remarks>
    /// This class can be exposed as a ReadOnlyDictionary where the key type is
    /// set to string. This allows us to expose strings to the user while
    /// only having to store AbsolutePaths internally. The translation from
    /// AbsolutePath to string is done via the PathTable. The purpose of this
    /// is to save memory at run time.
    /// </remarks>
    /// <typeparam name="TValue">The type of value to store in the dictionary</typeparam>
    internal sealed class AbsolutePathConcurrentDictionary<TValue> : IReadOnlyDictionary<string, TValue>, IDictionary<string, TValue>
    {
        #region Private properties

        /// <summary>
        /// The underlying dictionary that stores the AbsolutePaths and their respective TValues
        /// </summary>
        private ConcurrentDictionary<AbsolutePath, TValue> m_absolutePathsToValues;

        /// <summary>
        /// The PathTable instance used to convert between AbsolutePaths and strings
        /// </summary>
        private PathTable m_pathTable;
        #endregion

        #region Constructor

        /// <summary>
        /// Initializes dictionary and sets the PathTable instance to use
        /// </summary>
        /// <param name="pathTable">Used to convert between strings and AbsolutePaths</param>
        /// <param name="concurrencyLevel">This setting affects the number of locks that the dictionary is using; default value is 32</param>
        /// <param name="initialCapacity">Initial capacity of the collection; default value is 31</param>
        public AbsolutePathConcurrentDictionary(PathTable pathTable, int concurrencyLevel = 32, int initialCapacity = 31)
        {
            m_absolutePathsToValues = new ConcurrentDictionary<AbsolutePath, TValue>(concurrencyLevel, initialCapacity);
            m_pathTable = pathTable;
        }
        #endregion

        #region Internal methods
        internal TValue this[AbsolutePath key]
        {
            get
            {
                return m_absolutePathsToValues[key];
            }

            set
            {
                m_absolutePathsToValues[key] = value;
            }
        }

        internal TValue this[string key]
        {
            get
            {
                return this[m_pathTable.StringToAbsolutePath(key)];
            }

            set
            {
                this[m_pathTable.StringToAbsolutePath(key)] = value;
            }
        }

        internal TValue GetOrAdd(string key, Func<string, TValue> valueFactory)
        {
            return GetOrAdd(m_pathTable.StringToAbsolutePath(key), (str) => valueFactory(key));
        }

        internal TValue GetOrAdd(string key, TValue value)
        {
            return GetOrAdd(m_pathTable.StringToAbsolutePath(key), value);
        }

        internal TValue GetOrAdd(AbsolutePath absolutePath, Func<string, TValue> valueFactory)
        {
            return m_absolutePathsToValues.GetOrAdd(absolutePath, (str) => valueFactory(absolutePath.ToString(m_pathTable)));
        }

        internal TValue GetOrAdd(AbsolutePath key, TValue value)
        {
            return m_absolutePathsToValues.GetOrAdd(key, value);
        }
        #endregion

        #region Public and Interface methods
        TValue IReadOnlyDictionary<string, TValue>.this[string key]
        {
            get
            {
                return this[m_pathTable.StringToAbsolutePath(key)];
            }
        }

        TValue IDictionary<string, TValue>.this[string key]
        {
            get
            {
                return this[m_pathTable.StringToAbsolutePath(key)];
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
                foreach (var k in m_absolutePathsToValues.Keys)
                {
                    yield return m_pathTable.AbsolutePathToString(k);
                }
            }
        }

        IEnumerable<TValue> IReadOnlyDictionary<string, TValue>.Values => m_absolutePathsToValues.Values;

        public int Count => m_absolutePathsToValues.Count;

        public ICollection<string> Keys
        {
            get
            {
                IList<string> keys = new List<string>();
                foreach (var k in m_absolutePathsToValues.Keys)
                {
                    keys.Add(m_pathTable.AbsolutePathToString(k));
                }

                return keys;
            }
        }

        public ICollection<TValue> Values => m_absolutePathsToValues.Values;

        public bool IsReadOnly => true;

        public bool ContainsKey(string key)
        {
            return ContainsKey(m_pathTable.StringToAbsolutePath(key));
        }

        bool IDictionary<string, TValue>.ContainsKey(string key)
        {
            return ContainsKey(m_pathTable.StringToAbsolutePath(key));
        }

        public bool ContainsKey(AbsolutePath key)
        {
            return m_absolutePathsToValues.ContainsKey(key);
        }

        public bool Contains(KeyValuePair<string, TValue> item)
        {
            AbsolutePath keyAbsolutePath = m_pathTable.StringToAbsolutePath(item.Key);
            if (ContainsKey(keyAbsolutePath))
            {
                if (this[keyAbsolutePath].Equals(item.Value))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValue(AbsolutePath key, out TValue value)
        {
            return m_absolutePathsToValues.TryGetValue(key, out value);
        }

        public bool TryGetValue(string key, out TValue value)
        {
            return TryGetValue(m_pathTable.StringToAbsolutePath(key), out value);
        }

        bool IDictionary<string, TValue>.TryGetValue(string key, out TValue value)
        {
            return TryGetValue(m_pathTable.StringToAbsolutePath(key), out value);
        }

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            foreach (var kv in m_absolutePathsToValues)
            {
                yield return new KeyValuePair<string, TValue>(m_pathTable.AbsolutePathToString(kv.Key), kv.Value);
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

            if ((array.Length - arrayIndex) < m_absolutePathsToValues.Count)
            {
                throw new ArgumentException("Destination array is not large enough. Check array.Length and arrayIndex.");
            }

            foreach (KeyValuePair<AbsolutePath, TValue> kv in m_absolutePathsToValues)
            {
                array[arrayIndex++] = new KeyValuePair<string, TValue>(m_pathTable.AbsolutePathToString(kv.Key), kv.Value);
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
