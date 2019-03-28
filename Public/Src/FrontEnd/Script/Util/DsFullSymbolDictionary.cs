// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Mapping from symbols (<see cref="FullSymbol" />) to values.
    /// </summary>
    /// <typeparam name="TValue">Type of values.</typeparam>
    /// <remarks>
    /// TODO: This is an experimental data structure to get some performance.
    /// TODO: Currently there is no significant performance gain, but further improvement can be carried out in the future.
    /// TODO: Thus, this data structure is kept alive here. We may also want to remove it later.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public sealed class DsFullSymbolDictionary<TValue> : IEnumerable<KeyValuePair<FullSymbol, TValue>>
    {
        private readonly Dictionary<int, TValue> m_dictionary;

        /// <summary>
        /// Indexer.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The associated value.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1043:UseIntegralIndexer")]
        public TValue this[FullSymbol key]
        {
            get { return m_dictionary[key.Value.Value]; }
            set { m_dictionary[key.Value.Value] = value; }
        }

        /// <summary>
        /// Gets the size of dictionary.
        /// </summary>
        public int Count => m_dictionary.Count;

        /// <nodoc />
        public DsFullSymbolDictionary()
        {
            m_dictionary = new Dictionary<int, TValue>();
        }

        /// <nodoc />
        public DsFullSymbolDictionary(int capacity)
        {
            m_dictionary = new Dictionary<int, TValue>(capacity);
        }

        /// <nodoc />
        public DsFullSymbolDictionary(DsFullSymbolDictionary<TValue> dictionary)
        {
            m_dictionary = new Dictionary<int, TValue>(dictionary.m_dictionary);
        }

        /// <summary>
        /// Adds the specified key and value to the dictionary.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Add(FullSymbol key, TValue value)
        {
            m_dictionary.Add(key.Value.Value, value);
        }

        /// <summary>
        /// Determines if this dictionary contains the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>True iff this dictionary contains the specified key.</returns>
        public bool ContainsKey(FullSymbol key)
        {
            return m_dictionary.ContainsKey(key.Value.Value);
        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>True iff there is a value associated with the specified key.</returns>
        public bool TryGetValue(FullSymbol key, out TValue value)
        {
            return m_dictionary.TryGetValue(key.Value.Value, out value);
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<FullSymbol, TValue>> GetEnumerator()
        {
            return m_dictionary.Select(kvp => new KeyValuePair<FullSymbol, TValue>(new FullSymbol(kvp.Key), kvp.Value)).GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
