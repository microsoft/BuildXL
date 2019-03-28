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
    /// key type is hardcoded to FullSymbol and the value type is generic.
    /// </summary>
    /// <remarks>
    /// This class can be exposed as a ReadOnlyDictionary where the key type is
    /// set to string. This allows us to expose strings to the user while
    /// only having to store FullSymbols internally. The translation from
    /// FullSymbol to string is done via the SymbolTable. The purpose of this
    /// is to save memory at run time.
    /// </remarks>
    /// <typeparam name="TValue">The type of value to store in the dictionary</typeparam>
    internal sealed class FullSymbolConcurrentDictionary<TValue> : IReadOnlyDictionary<string, TValue>, IDictionary<string, TValue>
    {
        #region Private properties

        /// <summary>
        /// The underlying dictionary that stores the FullSymbols and their respective TValues
        /// </summary>
        private ConcurrentDictionary<FullSymbol, TValue> m_fullSymbolsToValues;

        /// <summary>
        /// The SymbolTable instance used to convert between FullSymbols and strings
        /// </summary>
        private SymbolTable m_symbolTable;
        #endregion

        #region Constructor

        /// <summary>
        /// Initializes dictionary and sets the SymbolTable instance to use
        /// </summary>
        /// <param name="symbolTable">Used to convert between strings and FullSymbols</param>
        /// <param name="concurrencyLevel">This setting affects the number of locks that the dictionary is using; default value is 32</param>
        /// <param name="initialCapacity">Initial capacity of the collection; default value is 31</param>
        public FullSymbolConcurrentDictionary(SymbolTable symbolTable, int concurrencyLevel = 32, int initialCapacity = 31)
        {
            m_fullSymbolsToValues = new ConcurrentDictionary<FullSymbol, TValue>(concurrencyLevel, initialCapacity);
            m_symbolTable = symbolTable;
        }
        #endregion

        #region Internal methods
        internal TValue this[FullSymbol key]
        {
            get
            {
                return m_fullSymbolsToValues[key];
            }

            set
            {
                m_fullSymbolsToValues[key] = value;
            }
        }

        internal TValue this[string key]
        {
            get
            {
                return this[m_symbolTable.StringToFullSymbol(key)];
            }

            set
            {
                this[m_symbolTable.StringToFullSymbol(key)] = value;
            }
        }

        internal TValue GetOrAdd(string key, Func<string, TValue> valueFactory)
        {
            return GetOrAdd(m_symbolTable.StringToFullSymbol(key), (str) => valueFactory(key));
        }

        internal TValue GetOrAdd(string key, TValue value)
        {
            return GetOrAdd(m_symbolTable.StringToFullSymbol(key), value);
        }

        internal TValue GetOrAdd(FullSymbol fullSymbol, Func<string, TValue> valueFactory)
        {
            return m_fullSymbolsToValues.GetOrAdd(fullSymbol, (str) => valueFactory(m_symbolTable.FullSymbolToString(fullSymbol)));
        }

        internal TValue GetOrAdd(FullSymbol key, TValue value)
        {
            return m_fullSymbolsToValues.GetOrAdd(key, value);
        }
        #endregion

        #region Public and Interface methods
        TValue IReadOnlyDictionary<string, TValue>.this[string key]
        {
            get
            {
                return this[m_symbolTable.StringToFullSymbol(key)];
            }
        }

        TValue IDictionary<string, TValue>.this[string key]
        {
            get
            {
                return this[m_symbolTable.StringToFullSymbol(key)];
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
                foreach (var k in m_fullSymbolsToValues.Keys)
                {
                    yield return m_symbolTable.FullSymbolToString(k);
                }
            }
        }

        IEnumerable<TValue> IReadOnlyDictionary<string, TValue>.Values => m_fullSymbolsToValues.Values;

        public int Count => m_fullSymbolsToValues.Count;

        public ICollection<string> Keys
        {
            get
            {
                IList<string> keys = new List<string>();
                foreach (var k in m_fullSymbolsToValues.Keys)
                {
                    keys.Add(m_symbolTable.FullSymbolToString(k));
                }

                return keys;
            }
        }

        public ICollection<TValue> Values => m_fullSymbolsToValues.Values;

        public bool IsReadOnly => true;

        public bool ContainsKey(string key)
        {
            return ContainsKey(m_symbolTable.StringToFullSymbol(key));
        }

        public bool ContainsKey(FullSymbol key)
        {
            return m_fullSymbolsToValues.ContainsKey(key);
        }

        bool IDictionary<string, TValue>.ContainsKey(string key)
        {
            return ContainsKey(m_symbolTable.StringToFullSymbol(key));
        }

        public bool Contains(KeyValuePair<string, TValue> item)
        {
            FullSymbol keyFullSymbol = m_symbolTable.StringToFullSymbol(item.Key);
            if (ContainsKey(keyFullSymbol))
            {
                if (this[keyFullSymbol].Equals(item.Value))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValue(FullSymbol key, out TValue value)
        {
            return m_fullSymbolsToValues.TryGetValue(key, out value);
        }

        public bool TryGetValue(string key, out TValue value)
        {
            return TryGetValue(m_symbolTable.StringToFullSymbol(key), out value);
        }

        bool IDictionary<string, TValue>.TryGetValue(string key, out TValue value)
        {
            return TryGetValue(m_symbolTable.StringToFullSymbol(key), out value);
        }

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            foreach (var kv in m_fullSymbolsToValues)
            {
                yield return new KeyValuePair<string, TValue>(m_symbolTable.FullSymbolToString(kv.Key), kv.Value);
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

            if ((array.Length - arrayIndex) < m_fullSymbolsToValues.Count)
            {
                throw new ArgumentException("Destination array is not large enough. Check array.Length and arrayIndex.");
            }

            foreach (KeyValuePair<FullSymbol, TValue> kv in m_fullSymbolsToValues)
            {
                array[arrayIndex++] = new KeyValuePair<string, TValue>(m_symbolTable.FullSymbolToString(kv.Key), kv.Value);
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
