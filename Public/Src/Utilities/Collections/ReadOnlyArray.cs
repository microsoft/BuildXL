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
    /// Represents a read only array. The underlying array is guaranteed not to change
    /// (in addition to this wrapper not allowing changes).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public readonly struct ReadOnlyArray<T> : IReadOnlyList<T>, IEquatable<ReadOnlyArray<T>>
    {
        private readonly T[] m_array;

        /// <summary>
        /// Constructs a new readonly array
        /// </summary>
        /// <param name="array">the wrapped array</param>
        private ReadOnlyArray(T[] array)
        {
            Contract.Requires(array != null);

            m_array = array;
        }

        /// <summary>
        /// Empty array.
        /// </summary>
        public static ReadOnlyArray<T> Empty => new ReadOnlyArray<T>(CollectionUtilities.EmptyArray<T>());

        /// <summary>
        /// Indicates if this instance represents a valid (non-null) array.
        /// </summary>
        public bool IsValid => m_array != null;

        /// <summary>
        /// Returns a subsegment of the current array starting at the current index with the given length
        /// </summary>
        /// <param name="start">the start index in the array</param>
        /// <param name="length">the length of the subsegment</param>
        /// <returns>the subsegment in the array</returns>
        public ArrayView<T> GetSubView(int start, int length)
        {
            Contract.Requires(IsValid);
            Contract.Requires(Range.IsValid(start, length, Length));

            return new ArrayView<T>(m_array, start, length);
        }

        /// <summary>
        /// Returns a subsegment of the current array starting at the given index and ending at the end
        /// of the array view
        /// </summary>
        /// <param name="start">the start index in the array</param>
        /// <returns>the subsegment in the array</returns>
        public ArrayView<T> GetSubView(int start)
        {
            Contract.Requires(IsValid);
            Contract.Requires(start >= 0);

            return new ArrayView<T>(m_array, start, Length - start);
        }

        /// <summary>
        /// Returns the wrapped array. The returned array should not be modified.
        /// </summary>
        public T[] GetMutableArrayUnsafe()
        {
            Contract.Requires(IsValid);
            return m_array;
        }

        /// <summary>
        /// Returns a copy of the wrapped array which can safely be mutated.
        /// </summary>
        public T[] ToArray()
        {
            return (T[])m_array.Clone();
        }

        /// <summary>
        /// Gets the element at the specified index in the array
        /// </summary>
        /// <param name="index">the index</param>
        /// <returns>the element at the given index</returns>
        public T this[int index] => m_array[index];

        /// <summary>
        /// Gets the length of the array
        /// </summary>
        public int Length
        {
            get
            {
                Contract.Requires(IsValid);
                return m_array.Length;
            }
        }

        int IReadOnlyCollection<T>.Count => Length;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(ReadOnlyArray<T> other)
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
        public static bool operator ==(ReadOnlyArray<T> left, ReadOnlyArray<T> right)
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
        public static bool operator !=(ReadOnlyArray<T> left, ReadOnlyArray<T> right)
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
        public Enumerator GetEnumerator()
        {
            Contract.Requires(IsValid);
            return new Enumerator(m_array);
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
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Convert an array to a read only array.
        /// The given array must be guaranteed to not change after it is provided
        /// (the array reference is used directly; the array is not copied).
        /// </summary>
        public static ReadOnlyArray<T> FromWithoutCopy(params T[] array)
        {
            Contract.Requires(array != null);

            return new ReadOnlyArray<T>(array);
        }

        /// <summary>
        /// Convert an array to a read only array by copying its contents.
        /// The provided array may safely change after this call.
        /// </summary>
        public static ReadOnlyArray<T> From(T[] array)
        {
            Contract.Requires(array != null);

            T[] clone;
            if (array.Length == 0)
            {
                // normalize
                clone = CollectionUtilities.EmptyArray<T>();
            }
            else
            {
                clone = new T[array.Length];
                array.CopyTo(clone, 0);
            }

            return new ReadOnlyArray<T>(clone);
        }

        /// <summary>
        /// Convert a range of an array to a read only array by copying its contents.
        /// The provided array may safely change after this call.
        /// </summary>
        public static ReadOnlyArray<T> From(T[] array, int start, int count)
        {
            Contract.Requires(array != null);
            Contract.Requires(start >= 0);
            Contract.Requires(count >= 0);
            Contract.Requires((uint)start + (uint)count <= (uint)array.Length);

            T[] trimmed;
            if (count == 0)
            {
                // normalize
                trimmed = CollectionUtilities.EmptyArray<T>();
            }
            else
            {
                trimmed = new T[count];
                Array.Copy(array, start, trimmed, 0, count);
            }

            return new ReadOnlyArray<T>(trimmed);
        }

        /// <summary>
        /// Convert an enumerable to a read only array.
        /// </summary>
        public static ReadOnlyArray<T> From(IEnumerable<T> enumerable)
        {
            Contract.Requires(enumerable != null);

            T[] a;
            if (enumerable is ICollection<T> c)
            {
                if (c.Count == 0)
                {
                    // normalize
                    a = CollectionUtilities.EmptyArray<T>();
                }
                else
                {
                    a = new T[c.Count];
                    int i = 0;
                    foreach (var item in c)
                    {
                        a[i++] = item;
                    }
                }
            }
            else
            {
                a = enumerable.ToArray();
                if (a.Length == 0)
                {
                    // normalize
                    a = CollectionUtilities.EmptyArray<T>();
                }
            }

            return new ReadOnlyArray<T>(a);
        }
        
        /// <summary>
        /// Convert a hash set to a read only array.
        /// </summary>
        public static ReadOnlyArray<T> From(HashSet<T> set)
        {
            Contract.Requires(set != null);

            if (set.Count == 0)
            {
                return new ReadOnlyArray<T>(CollectionUtilities.EmptyArray<T>());
            }

            var a = new T[set.Count];
            int i = 0;
            foreach (var item in set)
            {
                a[i++] = item;
            }

            return new ReadOnlyArray<T>(a);
        }

        /// <summary>
        /// Convert a list to a read only array.
        /// </summary>
        public static ReadOnlyArray<T> From(List<T> list)
        {
            Contract.Requires(list != null);

            T[] a = list.Count == 0 ? CollectionUtilities.EmptyArray<T>() : list.ToArray();

            return new ReadOnlyArray<T>(a);
        }

        /// <summary>
        /// Returns true if the array is not empty.
        /// </summary>
        public bool Any()
        {
            return Length != 0;
        }

        /// <summary>
        /// Returns true if the <paramref name="predicate"/> returns true for all elements.
        /// </summary>
        public bool All(Func<T, bool> predicate)
        {
            foreach (var element in this)
            {
                if (!predicate(element))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Enumerator for read-only array wrappers.
        /// </summary>
        /// <remarks>
        /// This is a mutable struct, which is a precarious matter. The goal is to avoid allocations
        /// for the <c>foreach (T item in array)</c> construct, though that requires the compiler to
        /// see this type rather than <see cref="IEnumerator{T}"/> (otherwise the enumerator is boxed anyway).
        /// </remarks>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[] m_array;
            private int m_index;

            internal Enumerator(T[] array)
            {
                m_array = array;
                m_index = -1;
            }

            /// <inheritdoc/>
            public T Current => m_array[m_index];

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <inheritdoc/>
            object System.Collections.IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext()
            {
                m_index++;
                if (m_index < m_array.Length)
                {
                    return true;
                }
                else
                {
                    m_index = m_array.Length;
                    return false;
                }
            }

            /// <inheritdoc/>
            public void Reset()
            {
                m_index = -1;
            }
        }
    }
}
