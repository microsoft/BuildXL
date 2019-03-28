// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Represents a sorted read only array. The underlying array is guaranteed not to change
    /// (in addition to this wrapper not allowing changes), and is guaranteed to be sorted.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TComparer"/> is not strictly required. One can simply specify <see cref="IComparer{T}"/>
    /// But by specifying a concrete type, one can specify the contract that two arrays must agree on ordering or
    /// that a particular well-known ordering is required (such as OrdinalFileArtifactComparer>)
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public readonly struct SortedReadOnlyArray<TValue, TComparer> : IReadOnlyList<TValue>, IEquatable<SortedReadOnlyArray<TValue, TComparer>>
        where TComparer : class, IComparer<TValue>
    {
        /// <summary>
        /// Comparer instance which can order the array elements.
        /// </summary>
        public readonly TComparer Comparer;

        private readonly ReadOnlyArray<TValue> m_array;

        /// <summary>
        /// Constructs a new readonly array. The array is assumed to be sorted.
        /// </summary>
        private SortedReadOnlyArray(ReadOnlyArray<TValue> array, TComparer comparer)
        {
            Contract.Requires(array.IsValid);
            Contract.Requires(comparer != null);

            m_array = array;
            Comparer = comparer;
        }

        /// <summary>
        /// Indicates if this instance represents a valid (non-null) array.
        /// </summary>
        public bool IsValid => Comparer != null && BaseArray.IsValid;

        /// <summary>
        /// Returns the underlying read-only array.
        /// </summary>
        public ReadOnlyArray<TValue> BaseArray => m_array;

        /// <summary>
        /// Gets the element at the specified index in the array
        /// </summary>
        /// <param name="index">the index</param>
        /// <returns>the element at the given index</returns>
        public TValue this[int index] => m_array[index];

        /// <summary>
        /// Searches for the given value with <see cref="Array.BinarySearch(System.Array,object)"/>
        /// (note that a negative value returned encodes an upper-bound index; i.e., first element greater than <paramref name="value"/>)
        /// </summary>
        [Pure]
        public int BinarySearch(TValue value, int start, int length)
        {
            Contract.Requires(IsValid);
            Contract.Requires(Range.IsValid(start, length, Length));
            Contract.Requires(start >= 0);  // TODO: shouldn't be necessary given Range.IsValid above, but the contract checker insists

            TValue[] mut = m_array.GetMutableArrayUnsafe();
            return Array.BinarySearch(mut, start, length, value, Comparer);
        }

        /// <summary>
        /// Indicates if the given element is contained in this array (via binary search).
        /// </summary>
        [Pure]
        public bool Contains(TValue value)
        {
            Contract.Requires(IsValid);
            return BinarySearch(value, 0, Length) >= 0;
        }

        /// <summary>
        /// Finds the elements in this array but not in any of the <paramref name="others"/>
        /// (<c>this - (∪ others)</c>). The returned array does not contain any duplicates, even
        /// if there are duplicates in the input arrays.
        /// </summary>
        /// <remarks>
        /// This implementation is optimized for the case in which the result array is empty (the union of the others
        /// is a superset of this).
        /// The algorithmic approach is to check membership of each item from 'this' in each of the 'others' with binary search;
        /// but since the 'this' array is also sorted, for each other array we can keep a lower-bound cursor (so the binary
        /// search space gets smaller each time) and we can trivially skip duplicates.
        /// Very roughly, the complexity is in <c>O(n * j lg m)</c> for j others of the same size m, but considering cases
        /// such as <c>s.ExceptWith(s + x, s + y, ...)</c> (excepting with j supersets) we can instead say
        /// <c>O(n * lg m)</c> since only a single 'other' need be used. Since the lower-bound cursor of that 'm'-sized other
        /// shrinks on every iteration (either due to finding a 'this' element immediately in O(1) or skipping one or more of the 'extras') and
        /// since the number of extras is (m - n) i.e., |x|, we then have <c>O(n + lg (m - n))</c> in that nice case.
        /// In short, this operation should be very fast for a 'small' number of possibly very large 'others'.
        /// </remarks>
        [Pure]
        public SortedReadOnlyArray<TValue, TComparer> ExceptWith(params SortedReadOnlyArray<TValue, TComparer>[] others)
        {
            Contract.Requires(IsValid);
            Contract.Requires(others != null);

            int size = ExceptWithVisitor(others, visit: null);

            if (size == 0)
            {
                // Ideally a common case.
                return FromSortedArrayUnsafe(ReadOnlyArray<TValue>.Empty, Comparer);
            }

            var result = new TValue[size];
            int secondSize = ExceptWithVisitor(others, visit: (i, v) => { result[i] = v; });
            Contract.Assume(secondSize == size);

            return FromSortedArrayUnsafe(ReadOnlyArray<TValue>.FromWithoutCopy(result), Comparer);
        }

        private int ExceptWithVisitor(SortedReadOnlyArray<TValue, TComparer>[] others, Action<int, TValue> visit = null)
        {
            // Number of non-excepted items seen so far (returned at the end)
            int size = 0;

            // Indicates if any of 'others' have valid cursors still (this instance may have items greater than all others).
            bool othersRemaining = true;

            // Cursor for the current item to compare with in each of the others.
            var otherCursors = new int[others.Length];

            // We always visit every item in this array since each is potentially (independently) not in the others.
            for (int i = 0; i < Length;)
            {
                bool found = false;
                TValue thisCurrent = this[i];

                // If there are others to look at, it is possible that we will find this item.
                if (othersRemaining)
                {
                    // We now try to find the item in at least one of the others (note we don't need to advance the cursors of the rest).
                    othersRemaining = false;
                    for (int otherIdx = 0; !found && otherIdx < others.Length; otherIdx++)
                    {
                        SortedReadOnlyArray<TValue, TComparer> other = others[otherIdx];
                        if (other.Length == otherCursors[otherIdx])
                        {
                            continue;
                        }

                        othersRemaining = true;

                        if (Comparer.Compare(other[otherCursors[otherIdx]], thisCurrent) == 0)
                        {
                            // Exact match (fast path for heavily overlapping sets).
                            otherCursors[otherIdx]++;
                            found = true;
                        }
                        else
                        {
                            // This cursor may be arbitrarily behind (note that we quit looking at additional others for an item when it is first found).
                            // Here we catch up efficiently via binary search. We either find an exact match (non-negative index) or an upper-bound
                            // (encoded negative).
                            int skipToEncoded = other.BinarySearch(thisCurrent, otherCursors[otherIdx], other.Length - otherCursors[otherIdx]);
                            if (skipToEncoded >= 0)
                            {
                                // Exact match. The binary search may have landed in the middle of a sequence of duplicates,
                                // but that's fine; the next cursor advance will take care of that (invariant is advancing cursor points
                                // it at an item at least as high, but not neccesarily higher).
                                otherCursors[otherIdx] = skipToEncoded + 1;
                                found = true;
                            }
                            else
                            {
                                otherCursors[otherIdx] = ~skipToEncoded;
                            }
                        }

                        Contract.Assume(otherCursors[otherIdx] == other.Length || Comparer.Compare(other[otherCursors[otherIdx]], thisCurrent) >= 0);
                    }
                }

                if (!found)
                {
                    if (visit != null)
                    {
                        visit(size, thisCurrent);
                    }

                    size++;
                }

                i++;

                // Skip duplicates in the input sequence (this value was either excepted or not already; cursors have advanced possibly past it).
                while (i < Length && Comparer.Compare(thisCurrent, this[i]) == 0)
                {
                    i++;
                }
            }

            return size;
        }

        /// <summary>
        /// Gets the length of the array
        /// </summary>
        public int Length => m_array.Length;

        int IReadOnlyCollection<TValue>.Count
        {
            get { return m_array.Length; }
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(SortedReadOnlyArray<TValue, TComparer> other)
        {
            return m_array == other.m_array;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_array.GetHashCode();
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare. </param>
        /// <param name="right">The second object to compare. </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator ==(SortedReadOnlyArray<TValue, TComparer> left, SortedReadOnlyArray<TValue, TComparer> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two objects instances are not equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <filterpriority>3</filterpriority>
        public static bool operator !=(SortedReadOnlyArray<TValue, TComparer> left, SortedReadOnlyArray<TValue, TComparer> right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Gets an enumerator over the elements of the array
        /// </summary>
        /// <remarks>
        /// Note the concrete return type (a scary mutable struct). In the event that someone writes
        /// <c>foreach (T item in array)</c> where 'array' has not been boxed as <see cref="IEnumerable"/>
        /// already, the compiler should target this implementation and avoid an allocation.
        /// </remarks>
        [Pure]
        public ReadOnlyArray<TValue>.Enumerator GetEnumerator()
        {
            return m_array.GetEnumerator();
        }

        /// <summary>
        /// Gets an enumerator over the elements of the array
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets an enumerator over the elements of the array
        /// </summary>
        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Converts (without a copy) to a sorted array with a different (compatible) comparer type.
        /// </summary>
        public SortedReadOnlyArray<TValue, TNewComparer> WithCompatibleComparer<TNewComparer>(TNewComparer comparer)
            where TNewComparer : class, ICompatibleComparer<TValue, TComparer>, IComparer<TValue>
        {
            Contract.Requires(comparer != null);

            return new SortedReadOnlyArray<TValue, TNewComparer>(m_array, comparer);
        }

        /// <summary>
        /// Convert a read-only array to a sorted read only array.
        /// The given array is assumed to be sorted.
        /// </summary>
        public static SortedReadOnlyArray<TValue, TComparer> FromSortedArrayUnsafe(ReadOnlyArray<TValue> array, TComparer comparer)
        {
            Contract.Requires(array.IsValid);
            Contract.Requires(comparer != null);

            return new SortedReadOnlyArray<TValue, TComparer>(array, comparer);
        }

        /// <summary>
        /// Sorts a given array and returns a <see cref="SortedReadOnlyArray{TValue, TComparer}"/> wrapper.
        /// The given array must be guaranteed to not change after it is provided
        /// (the array reference is used directly; the array is not copied).
        /// </summary>
        public static SortedReadOnlyArray<TValue, TComparer> SortUnsafe(TValue[] array, TComparer comparer)
        {
            Contract.Requires(array != null);
            Contract.Requires(comparer != null);

            Array.Sort(array, comparer);
            return new SortedReadOnlyArray<TValue, TComparer>(ReadOnlyArray<TValue>.FromWithoutCopy(array), comparer);
        }

        /// <summary>
        /// Convert an array to a read only array by copying and sorting its contents.
        /// The provided array may safely change after this call.
        /// </summary>
        public static SortedReadOnlyArray<TValue, TComparer> CloneAndSort(ReadOnlyArray<TValue> array, TComparer comparer)
        {
            Contract.Requires(array.IsValid);
            Contract.Requires(comparer != null);

            if (array.Length == 0)
            {
                return FromSortedArrayUnsafe(array, comparer);
            }
            else
            {
                TValue[] clone = array.ToArray();
                return SortUnsafe(clone, comparer);
            }
        }

        /// <summary>
        /// Convert an enumerable to a read only array by copying and sorting its contents.
        /// </summary>
        public static SortedReadOnlyArray<TValue, TComparer> CloneAndSort(IEnumerable<TValue> enumerable, TComparer comparer)
        {
            Contract.Requires(enumerable != null);
            Contract.Requires(comparer != null);

            var array = enumerable as ReadOnlyArray<TValue>?;
            if (array != null)
            {
                return CloneAndSort(array.Value, comparer);
            }

            TValue[] clone = enumerable.ToArray();
            return SortUnsafe(clone, comparer);
        }

        /// <summary>
        /// Implicit conversion to a read-only array (which is equivalent, but loses static type property of being sorted).
        /// </summary>
        public static implicit operator ReadOnlyArray<TValue>(SortedReadOnlyArray<TValue, TComparer> me)
        {
            return me.BaseArray;
        }
    }
}
