// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Helpers for checking equality and hash codes.
    /// </summary>
    public static class EqualityHelper
    {
        /// <summary>
        ///     Create a hash code for a set of reference types.
        /// </summary>
        public static int GetCombinedHashCode(params object[] args)
        {
            var result = 23;
            unchecked
            {
                foreach (var x in args)
                {
                    result += ReferenceEquals(x, null) ? 0 : x.GetHashCode() * 17;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a hash code for a given sequence.
        /// </summary>
        public static int SequenceHashCode<TSource>(this IEnumerable<TSource> sequence, IEqualityComparer<TSource> comparer = null)
        {
            if (sequence == null)
            {
                return 0;
            }

            if (comparer == null)
            {
                comparer = EqualityComparer<TSource>.Default;
            }

            int result = 0;

            // Looking for the first 4 elements only for performance reasons.
            foreach (var e in sequence.Take(4))
            {
                result = (result * 397) ^ comparer.GetHashCode(e);
            }

            return result;
        }

        /// <summary>
        /// Returns true if two sequences are equal.
        /// </summary>
        public static bool SequenceEqual<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer = null)
        {
            if (comparer == null)
            {
                comparer = EqualityComparer<TSource>.Default;
            }

            if (ReferenceEquals(first, second))
            {
                return true;
            }

            if (ReferenceEquals(null, first))
            {
                return false;
            }

            if (ReferenceEquals(null, second))
            {
                return false;
            }

            using (IEnumerator<TSource> enumerator = first.GetEnumerator())
            {
                using (IEnumerator<TSource> enumerator2 = second.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        if (!enumerator2.MoveNext() || !comparer.Equals(enumerator.Current, enumerator2.Current))
                        {
                            return false;
                        }
                    }

                    if (enumerator2.MoveNext())
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
