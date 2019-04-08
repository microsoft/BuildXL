// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Mapping from symbols (<see cref="SymbolAtom" />) to values.
    /// </summary>
    /// <typeparam name="TValue">Type of values.</typeparam>
    /// <remarks>
    /// TODO: This is an experimental data structure to get some performance.
    /// TODO: Currently there is no significant performance gain, but further improvement can be carried out in the future.
    /// TODO: Thus, this data structure is kept alive here. We may also want to remove it later.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public sealed class DsSymbolDictionary<TValue> : IEnumerable<KeyValuePair<SymbolAtom, TValue>>
    {
        private static readonly int s_invalidKey = SymbolAtom.Invalid.StringId.Value;
        private int m_key1 = s_invalidKey;
        private TValue m_value1;
        private int m_key2 = s_invalidKey;
        private TValue m_value2;
        private int m_key3 = s_invalidKey;
        private TValue m_value3;

        private readonly Dictionary<int, TValue> m_dictionary;

        /// <summary>
        /// Indexer.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The associated value.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1043:UseIntegralIndexer")]
        public TValue this[SymbolAtom key]
        {
            get
            {
                var id = key.StringId.Value;
                if (m_key1 == id)
                {
                    return m_value1;
                }

                if (m_key2 == id)
                {
                    return m_value2;
                }

                if (m_key3 == id)
                {
                    return m_value3;
                }

                return m_dictionary[id];
            }

            set
            {
                m_dictionary[key.StringId.Value] = value;
            }
        }

        /// <summary>
        /// Gets the size of dictionary.
        /// </summary>
        public int Count => m_dictionary.Count;

        /// <nodoc />
        public DsSymbolDictionary()
        {
            m_dictionary = new Dictionary<int, TValue>();
        }

        /// <nodoc />
        /// <param name="capacity">Initial capacity.</param>
        public DsSymbolDictionary(int capacity)
        {
            m_dictionary = new Dictionary<int, TValue>(capacity);
        }

        /// <nodoc />
        public DsSymbolDictionary(DsSymbolDictionary<TValue> dictionary)
        {
            m_key1 = dictionary.m_key1;
            m_value1 = dictionary.m_value1;
            m_key2 = dictionary.m_key2;
            m_value2 = dictionary.m_value2;
            m_key3 = dictionary.m_key3;
            m_value3 = dictionary.m_value3;

            m_dictionary = new Dictionary<int, TValue>(dictionary.m_dictionary);
        }

        /// <summary>
        /// Adds the specified key and value to the dictionary.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Add(SymbolAtom key, TValue value)
        {
            bool added = false;
            if (m_key1 == s_invalidKey)
            {
                m_key1 = key.StringId.Value;
                m_value1 = value;
                added = true;
            }

            if (!added && m_key2 == s_invalidKey)
            {
                m_key2 = key.StringId.Value;
                m_value2 = value;
                added = true;
            }

            if (!added && m_key3 == s_invalidKey)
            {
                m_key3 = key.StringId.Value;
                m_value3 = value;
            }

            m_dictionary.Add(key.StringId.Value, value);
        }

        /// <summary>
        /// Determines if this dictionary contains the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>True iff this dictionary contains the specified key.</returns>
        public bool ContainsKey(SymbolAtom key)
        {
            var id = key.StringId.Value;
            return m_key1 == id || m_key2 == id || m_key3 == id || m_dictionary.ContainsKey(id);
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>True iff there is a value associated with the specified key.</returns>
        public bool TryGetValue(SymbolAtom key, out TValue value)
        {
            var id = key.StringId.Value;
            if (m_key1 == id)
            {
                value = m_value1;
                return true;
            }

            if (m_key2 == id)
            {
                value = m_value2;
                return true;
            }

            if (m_key3 == id)
            {
                value = m_value3;
                return true;
            }

            return m_dictionary.TryGetValue(key.StringId.Value, out value);
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<SymbolAtom, TValue>> GetEnumerator()
        {
            return
                m_dictionary.Select(
                    kvp =>
                        new KeyValuePair<SymbolAtom, TValue>(
                            kvp.Key != StringId.Invalid.Value ? new SymbolAtom(new StringId(kvp.Key)) : SymbolAtom.Invalid,
                            kvp.Value)).GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets first or default.
        /// </summary>
        public KeyValuePair<SymbolAtom, TValue> FirstOrDefault(Func<KeyValuePair<SymbolAtom, TValue>, bool> predicate)
        {
            var result = m_dictionary.FirstOrDefault(kvp => predicate(new KeyValuePair<SymbolAtom, TValue>(new SymbolAtom(new StringId(kvp.Key)), kvp.Value)));

            return
                new KeyValuePair<SymbolAtom, TValue>(
                    result.Key != StringId.Invalid.Value ? new SymbolAtom(new StringId(result.Key)) : SymbolAtom.Invalid,
                    result.Value);
        }
    }
}
