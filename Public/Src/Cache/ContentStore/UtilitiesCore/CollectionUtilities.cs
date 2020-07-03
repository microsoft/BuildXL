// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        /// <summary>
        /// Compare two operands and returns true if two instances are equivalent.
        /// </summary>
        public static bool IsCompareEquals<T>(T x1, T x2, out int compareResult, bool greatestFirst = false)
            where T : IComparable<T>
        {
            compareResult = (int)Order(x1, x2, greatestFirst: greatestFirst);
            return compareResult == 0;
        }

        /// <summary>
        /// The result of comparing two values
        /// </summary>
        public enum OrderResult
        {
            /// <summary>
            /// The values are equal
            /// </summary>
            Equal = 0,

            /// <summary>
            /// Prefer the first value
            /// </summary>
            PreferFirst = -1,

            /// <summary>
            /// Prefer the second value
            /// </summary>
            PreferSecond = 1,
        }

        /// <summary>
        /// Comverts a integer comparison result (from <see cref="IComparer{T}.Compare(T, T)"/> or <see cref="IComparer{T}.Compare(T, T)"/>) to
        /// an <see cref="OrderResult"/> value
        /// </summary>
        public static OrderResult ToOrderResult(int compareResult)
        {
            if (compareResult == 0)
            {
                return OrderResult.Equal;
            }
            else
            {
                return compareResult < 0 ? OrderResult.PreferFirst : OrderResult.PreferSecond;
            }
        }

        /// <summary>
        /// Compare two operands and returns true if two instances are equivalent.
        /// </summary>
        public static OrderResult Order<T>(T x1, T x2, bool greatestFirst = false)
            where T : IComparable<T>
        {
            return ToOrderResult(greatestFirst
                ? x2.CompareTo(x1)
                : x1.CompareTo(x2));
        }
    }
}
