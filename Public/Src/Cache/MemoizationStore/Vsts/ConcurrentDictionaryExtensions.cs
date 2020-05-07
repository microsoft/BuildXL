// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    internal static class ConcurrentDictionaryExtensions
    {
        /// <summary>
        /// Removes key-value pair from a given concurrent dictionary.
        /// </summary>
        public static bool Remove<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> map, TKey key, TValue value)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)map).Remove(new KeyValuePair<TKey, TValue>(key, value));
        }
    }
}
