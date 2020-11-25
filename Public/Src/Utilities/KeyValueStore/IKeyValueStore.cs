// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace BuildXL.Engine.Cache.KeyValueStores
{
    /// <summary>
    /// Interface for persistent key value stores.
    /// </summary>
    public interface IKeyValueStore<TKey, TValue>
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
        void Put(TKey key, TValue value, string? columnFamilyName = null);

        /// <summary>
        /// Adds (or overwrite) multiple entries in a batch.
        /// </summary>
        /// <param name="entries">Entries, where each entry is a tuple of key, value, and column family name.</param>
        void PutMultiple(IEnumerable<(TKey key, TValue value, string columnFamilyName)> entries);

        /// <summary>
        /// Removes an entry.
        /// </summary>
        /// <param name="key">
        /// The key of the entry to remove.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        void Remove(TKey key, string? columnFamilyName = null);

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
        void RemoveBatch(IEnumerable<TKey> keys, IEnumerable<string?>? columnFamilyNames = null);

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
        bool TryGetValue(TKey key, [MaybeNull][NotNullWhen(true)]out TValue value, string? columnFamilyName = null);

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
        bool Contains(TKey key, string? columnFamilyName = null);

        /// <summary>
        /// Applies a batch Put and Delete operations. Useful only in the case when a large number of operations have 
        /// to be performed on the store. May or may not be atomic.
        /// </summary>
        /// <param name="keyValuePairs">
        /// The key-value pair for each entry. If a value is null, then a Delete will be 
        /// performed; otherwise, a Put.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        /// 
        void ApplyBatch(IEnumerable<KeyValuePair<TKey, TValue?>> keyValuePairs, string? columnFamilyName = null);

        /// <summary>
        /// Fetches keys and values with the same prefix. Order is dependant on the underlying store's guarantees.
        /// </summary>
        /// <param name="prefix">
        /// Common prefix
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        IEnumerable<KeyValuePair<TKey, TValue>> PrefixSearch([AllowNull]TKey prefix, string? columnFamilyName = null);

        /// <summary>
        /// Forces compaction of a range of keys. What exactly this means depends on the underlying store.
        /// </summary>
        /// <param name="start">
        /// First key in the range (inclusive). If null, the first key in the column family.
        /// </param>
        /// <param name="limit">
        /// Last key in the range (exclusive). If null, a "key" past the end of the column family.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use.
        /// </param>
        /// <remarks>
        /// Set both start and limit to null to force compaction of the entire key space.
        /// 
        /// Compaction may happen in parallel with other operations, no exclusive usage is required.
        /// 
        /// Compaction will run in the background, and can not corrupt the database if the process crashes.
        /// 
        /// Start and Limit parameters are prefixes to the key range. A few examples:
        ///     - start="a" end="b" will match all files that have keys starting with those values. For example, 
        ///       anything overlapping with the range ["aa", "bz"] will be covered.
        ///     - start="z" end="null" will match all keys starting with "z" (from the left) until the end of the 
        ///       family. For example, "za", "zz", "zzfffff".
        ///  For details, see: https://dev.azure.com/mseng/Domino/_git/BuildXL.Internal/pullrequest/534147
        /// </remarks>
        void CompactRange([AllowNull]TKey start, [AllowNull]TKey limit, string? columnFamilyName = null);
    }
}
