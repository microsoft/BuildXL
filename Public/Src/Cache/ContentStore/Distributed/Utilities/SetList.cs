// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Collection for serializing list of keyed items
    /// </summary>
    public class SetList<TItem> : KeyedCollection<TItem, TItem>
    {
        public bool TryAdd(TItem item)
        {
            if (Contains(item))
            {
                return false;
            }

            Add(item);
            return true;
        }

        protected override TItem GetKeyForItem(TItem item)
        {
            return item;
        }
    }
}
