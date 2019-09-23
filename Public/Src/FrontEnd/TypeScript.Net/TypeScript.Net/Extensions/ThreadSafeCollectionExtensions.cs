// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    /// <summary>
    /// Set of extension methods that simplify making type checker thread safe.
    /// </summary>
    /// <remarks>
    /// Even that every method has 'Atomic' suffix, it is not 100% correct.
    /// Each method acquires lock on a collection instance, and atomicity will be achieved
    /// *only* when there is no other methods that reads/writes collection without a lock.
    /// </remarks>
    internal static class ThreadSafeCollectionExtensions
    {
        /// <summary>
        /// Adds or updates a value in the list at a given index.
        /// </summary>
        [NotNull]
        public static List<T> AddOrUpdateAtomic<T>(this List<T> list, T value, int index)
        {
            lock (list)
            {
                list.EnsureSize(index);
                list[index] = value;
            }

            return list;
        }

        [NotNull]
        public static List<T> AddAtomic<T>(this List<T> list, T value)
        {
            lock (list)
            {
                list.Add(value);
            }

            return list;
        }

        private static void EnsureSize<T>(this List<T> list, int count)
        {
            var listCount = list.Count;
            if (count >= listCount)
            {
                for (int i = 0; i < count - listCount + 1; i++)
                {
                    list.Add(default(T));
                }
            }
        }

        public static TValue GetOrAddAtomic<TKey, TState, TValue>(this Dictionary<TKey, TValue> map, TKey key, TState state, Func<TKey, TState, TValue> factory)
        {
            lock (map)
            {
                TValue result;
                if (map.TryGetValue(key, out result))
                {
                    return result;
                }

                result = factory(key, state);
                map[key] = result;
                return result;
            }
        }

        public static TValue GetOrAddAtomic<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key, Func<TKey, TValue> factory)
        {
            lock (map)
            {
                TValue result;
                if (map.TryGetValue(key, out result))
                {
                    return result;
                }

                result = factory(key);
                map[key] = result;
                return result;
            }
        }

        public static TValue GetOrAddAtomic<TKey, TState, TValue>(this Dictionary<TKey, TValue> map, TState state, TKey key, Func<TState, TKey, TValue> factory)
        {
            lock (map)
            {
                TValue result;
                if (map.TryGetValue(key, out result))
                {
                    return result;
                }

                result = factory(state, key);
                map[key] = result;
                return result;
            }
        }

        /// <summary>
        /// Gets a value at a given index or return default(T) if the index is out of range.
        /// </summary>
        public static T GetOrDefaultAtomic<T>(this List<T> list, int index)
        {
            var localList = list;
            return index < localList.Count ? localList[index] : default(T);
        }

        public static TValue GetValueAtomic<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key)
        {
            lock (map)
            {
                return map[key];
            }
        }

        public static bool ContainsKeyAtomic<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key)
        {
            lock (map)
            {
                return map.ContainsKey(key);
            }
        }

        public static bool TryGetValueAtomic<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key, out TValue value)
        {
            lock (map)
            {
                return map.TryGetValue(key, out value);
            }
        }

        [NotNull]
        public static ISymbolTable GetOrCreateSymbolTable([NotNull]this Map<ISymbolTable> map, [NotNull]string key)
        {
            ISymbolTable result;

            if (!map.TryGetValue(key, out result))
            {
                result = SymbolTable.Create();
                map[key] = result;
            }

            return result;
        }
    }
}
