// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.UtilitiesCore.Internal
{
    /// <summary>
    /// Custom implementation of Xunit collection runner.
    /// </summary>
    public static class CollectionUtilities
    {
        /// <summary>
        /// Returns an empty instance of <see cref="List{T}"/>.
        /// </summary>
        public static List<T> EmptyList<T>() => Empty<T>.EmptyList;

        /// <summary>
        /// Returns an empty instance of <typeparamref name="T"/>[].
        /// </summary>
        public static T[] EmptyArray<T>() => Empty<T>.EmptyArray;

        private static class Empty<T>
        {
            public static readonly List<T> EmptyList = new List<T>();

            public static readonly T[] EmptyArray = new T[] { };
        }

        public static bool IsCompareEquals<T>(T x1, T x2, out int compareResult)
            where T : IComparable<T>
        {
            compareResult = x1.CompareTo(x2);
            return compareResult == 0;
        }
    }
}
