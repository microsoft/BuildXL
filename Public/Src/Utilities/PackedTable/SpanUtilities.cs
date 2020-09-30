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

        /// <summary>
        /// Sorting for Spans.
        /// </summary>
        /// <remarks>
        /// source: https://github.com/kevin-montrose/Cesil/blob/master/Cesil/Common/Utils.cs#L870
        ///     via https://github.com/dotnet/runtime/issues/19969
        /// todo: once MemoryExtensions.Sort() lands we can remove all of this (tracking issue: https://github.com/kevin-montrose/Cesil/issues/29)
        ///       coming as part of .NET 5, as a consequence of https://github.com/dotnet/runtime/issues/19969
        ///       
        /// Editor: This is a suboptimal implementation and we don't currently guarantee in PackedExecution that we *don't* call this
        /// on unsorted data.
        /// </remarks>
        public static void Sort<T>(this Span<T> span, Comparison<T> comparer)
        {
            // crummy quick sort implementation, all of this should get killed

            var len = span.Length;

            if (len <= 1)
            {
                return;
            }

            if (len == 2)
            {
                var a = span[0];
                var b = span[1];

                var res = comparer(a, b);
                if (res > 0)
                {
                    span[0] = b;
                    span[1] = a;
                }

                return;
            }

            // we only ever call this when the span isn't _already_ sorted,
            //    so our sort can be really dumb
            // basically Lomuto (see: https://en.wikipedia.org/wiki/Quicksort#Lomuto_partition_scheme)

            var splitIx = Partition(span, comparer);

            var left = span[..splitIx];
            var right = span[(splitIx + 1)..];

            Sort(left, comparer);
            Sort(right, comparer);

            // re-order subSpan such that items before the returned index are less than the value
            //    at the returned index
            static int Partition(Span<T> subSpan, Comparison<T> comparer)
            {
                var len = subSpan.Length;

                var pivotIx = len - 1;
                var pivotItem = subSpan[pivotIx];

                var i = 0;

                for (var j = 0; j < len; j++)
                {
                    var item = subSpan[j];
                    var res = comparer(item, pivotItem);

                    if (res < 0)
                    {
                        Swap(subSpan, i, j);
                        i++;
                    }
                }

                Swap(subSpan, i, pivotIx);

                return i;
            }

            static void Swap(Span<T> subSpan, int i, int j)
            {
                var oldI = subSpan[i];
                subSpan[i] = subSpan[j];
                subSpan[j] = oldI;
            }
        }
    }
}
