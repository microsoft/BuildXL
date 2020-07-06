// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

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
        [return: MaybeNull]
        public static TValue GetOrAdd<TKey, TValue, TArg>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument) where TKey : notnull
        {
            if (!dictionary.TryGetValue(key, out var value))
            {
                dictionary.TryAdd(key, valueFactory(key, factoryArgument));
            }

            return value;
        }
    }
}
