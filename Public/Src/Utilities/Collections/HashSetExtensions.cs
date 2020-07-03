// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Set of extension methods for the <see cref="HashSet{T}"/> class.
    /// </summary>
    public static class HashSetExtensions
    {
        /// <summary>
        /// Adds the elements into a given hash set.
        /// </summary>
        public static HashSet<T> AddRange<T>(this HashSet<T> hashSet, IEnumerable<T> elements)
        {
            foreach (var e in elements)
            {
                hashSet.Add(e);
            }

            return hashSet;
        }
    }
}
