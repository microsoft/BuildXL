// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET_FRAMEWORK
#define DEFINETRYGET
#elif NET_STANDARD_20
#define DEFINETRYGET
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// An item which exposes a key
    /// </summary>
    public interface IKeyedItem<TKey>
    {
        TKey GetKey();
    }

    /// <summary>
    /// Collection for serializing list of keyed items
    /// </summary>
    public class KeyedList<TKey, TValue> : KeyedCollection<TKey, TValue>
        where TValue : IKeyedItem<TKey>
    {
        public KeyedList()
        {
        }

        public KeyedList(IEqualityComparer<TKey> comparer) : base(comparer)
        {
        }

        protected override TKey GetKeyForItem(TValue item)
        {
            return item.GetKey();
        }

        public bool TryAdd(TValue item)
        {
            if (!Contains(GetKeyForItem(item)))
            {
                Add(item);
                return true;
            }

            return false;
        }

#if DEFINETRYGET
        public bool TryGetValue(TKey key, [NotNullWhen(true)] out TValue value)
        {
            if (Contains(key))
            {
                value = this[key];
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }
#endif
    }
}
