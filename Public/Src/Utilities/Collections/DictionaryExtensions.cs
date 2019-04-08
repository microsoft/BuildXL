// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Set of extension methods for <see cref="IDictionary{TKey,TValue}"/> interface.
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        /// Adds a range of values into the dictionary.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="IDictionary{TKey,TValue}.Add(TKey,TValue)"/> method, this one does not throw if a value is already presented in the dictionary.
        /// </remarks>
        public static IDictionary<TKey, TValue> AddRange<TKey, TValue>(this IDictionary<TKey, TValue> @this, IEnumerable<KeyValuePair<TKey, TValue>> other)
        {
            foreach (var kvp in other)
            {
                @this[kvp.Key] = kvp.Value;
            }

            return @this;
        }

        /// <summary>
        /// Adds a value to a dictionary.
        /// Returns true if the value was not presented in the dictionary, and false otherwise.
        /// </summary>
        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> @this, TKey key, TValue value)
        {
            if (@this.ContainsKey(key))
            {
                return false;
            }

            @this.Add(key, value);
            return true;
        }
    }
}
