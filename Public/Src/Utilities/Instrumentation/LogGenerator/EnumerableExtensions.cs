// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.LogGenerator
{
    /// <summary>
    /// Contains extension methods for <see cref="IEnumerable{T}"/>.
    /// </summary>
    internal static class Extensions
    {
        /// <summary>
        /// Creates a "multi-dictionary" from a given sequence.
        /// </summary>
        public static Dictionary<TKey, IReadOnlyList<TValue>> ToMultiValueDictionary<TSource, TKey, TValue>(
            this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> elementSelector) where TKey : notnull
        {
            var result = new Dictionary<TKey, IReadOnlyList<TValue>>();
            foreach (var item in source)
            {
                var key = keySelector(item);
                var element = elementSelector(item);

                if (!result.TryGetValue(key, out var value))
                {
                    value = new List<TValue>();
                }

                ((List<TValue>)value).Add(element);
            }

            return result;
        }
    }
}