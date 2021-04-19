// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Extension methods for <see cref="IComparer{T}" />
    /// </summary>
    public static class IComparerExtensions
    {
        /// <summary>
        /// Returns a new <see cref="IComparer{T}"/> which uses this comparer first and 
        /// the specified comparer as a tie-breaker between instances considered equal by this comparer
        /// </summary>
        public static IComparer<T> AndThen<T>(this IComparer<T> comparer, IComparer<T> comparerIfEqual)
        {
            return Comparer<T>.Create((x, y) => comparer.Compare(x, y) == 0 ? 0 : comparerIfEqual.Compare(x, y));
        }
    }
}
