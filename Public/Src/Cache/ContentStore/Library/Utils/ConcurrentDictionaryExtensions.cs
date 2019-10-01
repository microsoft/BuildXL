// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;

namespace BuildXL.Cache.ContentStore.Utils
{
    internal static class ConcurrentDictionaryExtensions
    {
        /// <summary>
        /// Extension method that allows adding an element into <paramref name="dictionary"/> without allocating a closure.
        /// </summary>
        /// <remarks>
        /// This extension method is used only when we target net451 because the similar api was added only net46.
        /// </remarks>
        public static TValue GetOrAdd<TKey, TValue, TArg>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
        {
            if (!dictionary.TryGetValue(key, out TValue value))
            {
                dictionary.TryAdd(key, valueFactory(key, factoryArgument));
            }

            return value;
        }
    }
}
