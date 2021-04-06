// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <nodoc />
    public static class ConcurrentDictionaryExtensions
    {
        /// <summary>
        /// Extension method that allows adding an element into <paramref name="dictionary"/> without allocating a closure.
        /// </summary>
        /// <remarks>
        /// This extension method is used only when we target net451 because the similar api was added only net46.
        /// </remarks>
        [return: MaybeNull]
        public static TValue GetOrAdd<TKey, TValue, TArg>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument) where TKey : notnull
        {
            if (dictionary.TryGetValue(key, out var value))
            {
                return value;
            }

            bool added = dictionary.TryAdd(key, value = valueFactory(key, factoryArgument));
            if (added)
            {
                // We added the value, so the 'value' is what was added.
                return value;
            }

            // We lost the race and someone already added the value.
            // Calling the same method recursively to get the value or to create a new one if the value will be very quickly removed.
            return dictionary.GetOrAdd<TKey, TValue, TArg>(key, valueFactory, factoryArgument);
        }
    }
}
