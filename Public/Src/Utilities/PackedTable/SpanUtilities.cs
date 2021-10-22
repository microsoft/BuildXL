// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Utility methods for working with spans.
    /// </summary>
    public static class SpanUtilities
    {
        /// <summary>
        /// Return the portion of the span up to (and not including) the first occurrence of value.
        /// </summary>
        public static ReadOnlySpan<T> SplitPrefix<T>(this ReadOnlySpan<T> span, T value, Func<T, T, bool> equalityComparer)
        {
            int nextIndex = span.IndexOf(value, equalityComparer);
            if (nextIndex == -1)
            {
                return span;
            }
            return span.Slice(0, nextIndex);
        }

        /// <summary>
        /// Return the index of the first occurrence of value, or -1 if value not present.
        /// </summary>
        public static int IndexOf<T>(this ReadOnlySpan<T> span, T value, Func<T, T, bool> equalityComparer)
        {
            for (int i = 0; i < span.Length; i++)
            {
                if (equalityComparer(span[i], value))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Hashes the specified Span.
        /// </summary>
        public static int GetHashCode<T>(this ReadOnlySpan<T> values, Func<T, int> converter)
        {
            if (values.Length == 0)
            {
                return 0;
            }

            // It is hacky to make these values public, but until HashCodeHelper is a .NET Core project,
            // it's too inefficient to implement this any other way (and not acceptable to duplicate
            // the Fold implementation).
            int hash = HashCodeHelper.Fnv1Basis32;
            foreach (T value in values)
            {
                hash = HashCodeHelper.Fold(hash, converter(value));
            }

            return hash;
        }
    }
}
