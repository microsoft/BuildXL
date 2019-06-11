// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Engine.Cache.KeyValueStores
{
    /// <summary>
    /// Interface for persistent key value stores.
    /// </summary>
    public interface IKeyValueStore<in TKey, TValue>
    {
        /// <summary>
        /// Adds a new entry or overwrites an existing entry.
        /// </summary>
        /// <param name="key">
        /// The key of the entry.
        /// </param>
        /// <param name="value">
        /// The value of the entry.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        void Put(TKey key, TValue value, string columnFamilyName = null);

        /// <summary>
        /// Removes an entry.
        /// </summary>
        /// <param name="key">
        /// The key of the entry to remove.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        void Remove(TKey key, string columnFamilyName = null);

        /// <summary>
        /// Removes a batch of entries.
        /// </summary>
        /// <param name="keys">
        /// The keys of the entries to be removed.
        /// </param>
        /// <param name="columnFamilyNames">
        /// The column families to remove the keys across. Include a null string to represent the default column.
        /// If not specified, only the default column will be used.
        /// </param>
        void RemoveBatch(IEnumerable<TKey> keys, IEnumerable<string> columnFamilyNames = null);

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">
        /// The key of the value to get.
        /// </param>
        /// <param name="value">
        /// When this method returns, contains the value associated with the specified key, if the key is found; 
        /// otherwise, the default value for the type of the value parameter. This parameter is passed uninitialized.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        /// <returns>
        /// Returns true, if the key is found; 
        /// otherwise, false.
        /// </returns>
        bool TryGetValue(TKey key, out TValue value, string columnFamilyName = null);

        /// <summary>
        /// Whether the store contains an entry with the specified key.
        /// To get the value of the key and check existence simultaneously,
        /// <see cref="TryGetValue(TKey, out TValue, string)"/>.
        /// </summary>
        /// <param name="key">
        /// The key of the entry.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        /// <returns>
        /// Returns true, if the key is found; 
        /// otherwise, false.
        /// </returns>
        bool Contains(TKey key, string columnFamilyName = null);

        /// <summary>
        /// Applies a batch Put and Delete operations. Useful only in the case when a large number of operations have 
        /// to be performed on the store. May or may be atomic.
        /// </summary>
        /// <param name="keys">
        /// The key of each entry.
        /// </param>
        /// <param name="values">
        /// The value of each entry. Must be the same size as the keys. If a value is null, then a Delete will be 
        /// performed; otherwise, a Put.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        void ApplyBatch(IEnumerable<TKey> keys, IEnumerable<TValue> values, string columnFamilyName = null);
    }
}
