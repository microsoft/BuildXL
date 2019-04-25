// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities.Collections
{
    public partial class CollectionUtilities
    {
        private static partial class Empty
        {
            internal static class Array<T>
            {
#pragma warning disable CA1825 // Avoid unncecessary zero-length array allocations.
                public static readonly T[] Instance = new T[0];
#pragma warning restore CA1825 // Avoid unncecessary zero-length array allocations.
            }

            internal static class Set<T>
            {
#pragma warning disable CA1825 // Avoid unncecessary zero-length array allocations.
                public static readonly IReadOnlySet<T> Instance = new ReadOnlyHashSet<T>();
#pragma warning restore CA1825 // Avoid unncecessary zero-length array allocations.
            }

            internal static class Dictionary<TKey,TValue>
            {
#pragma warning disable CA1825 // Avoid unncecessary zero-length array allocations.
                public static readonly IReadOnlyDictionary<TKey, TValue> Instance = new System.Collections.Generic.Dictionary<TKey, TValue>(0);
#pragma warning restore CA1825 // Avoid unncecessary zero-length array allocations.
            }

            internal static class SortedArray<TValue, TComparer> where TComparer : class, IComparer<TValue>
            {
                public static SortedReadOnlyArray<TValue, TComparer> Instance(TComparer comparer) =>
                    SortedReadOnlyArray<TValue, TComparer>.CloneAndSort(
                        EmptyArray<TValue>().ToReadOnlyArray(),
                        comparer);
            }
        }
    }
}
