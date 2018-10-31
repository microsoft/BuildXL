// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Constructors for <see cref="ArrayView{T}"/>.
    /// </summary>
    public static class ArrayView
    {
        /// <summary>
        /// Constructs a new array view
        /// </summary>
        /// <param name="array">the wrapped array</param>
        /// <param name="start">the start index in the wrapped array</param>
        /// <param name="length">the length of the segment</param>
        public static ArrayView<T> Create<T>(T[] array, int start, int length)
        {
            Contract.Requires(array != null);
            Contract.Requires(Range.IsValid(start, length, array.Length));
            return new ArrayView<T>(array, start, length);
        }
    }

    /// <summary>
    /// Represents a read only segment of an array
    /// </summary>
    /// <typeparam name="T">the element type</typeparam>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public readonly struct ArrayView<T> : IReadOnlyList<T>, IEquatable<ArrayView<T>>
    {
        /// <summary>
        /// Empty array.
        /// </summary>
        public static readonly ArrayView<T> Empty = new ArrayView<T>(CollectionUtilities.EmptyArray<T>(), 0, 0);

        private readonly T[] m_array;
        private readonly int m_start;

        /// <summary>
        /// Gets the length of the array segment
        /// </summary>
        public readonly int Length;

        /// <summary>
        /// Constructs a new array view
        /// </summary>
        /// <param name="array">the wrapped array</param>
        /// <param name="start">the start index in the wrapped array</param>
        /// <param name="length">the length of the segment</param>
        public ArrayView(T[] array, int start, int length)
        {
            Contract.Requires(array != null);
            Contract.Requires(Range.IsValid(start, length, array.Length));

            m_array = array;
            m_start = start;
            Length = length;
        }

        /// <summary>
        /// Returns a subsegment of the current array view starting at the current index with the given length
        /// </summary>
        /// <param name="start">the start index in the array view</param>
        /// <param name="length">the length of the subsegment</param>
        /// <returns>the subsegment in the array view</returns>
        public ArrayView<T> GetSubView(int start, int length)
        {
            Contract.Requires(Range.IsValid(start, length, Length));

            return new ArrayView<T>(m_array, m_start + start, length);
        }

        /// <summary>
        /// Returns a subsegment of the current array view starting at the given index and ending at the end
        /// of the array view
        /// </summary>
        /// <param name="start">the start index in the array view</param>
        /// <returns>the subsegment in the array view</returns>
        public ArrayView<T> GetSubView(int start)
        {
            Contract.Requires((uint)start <= Length);

            return new ArrayView<T>(m_array, m_start + start, Length - start);
        }

        /// <summary>
        /// Gets the element at the specified index in the array view
        /// </summary>
        /// <param name="index">the index</param>
        /// <returns>the element at the given index</returns>
        public T this[int index] => m_array[m_start + index];

        int IReadOnlyCollection<T>.Count => Length;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(ArrayView<T> other)
        {
            return m_array == other.m_array &&
                m_start == other.m_start &&
                Length == other.Length;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(m_array?.GetHashCode() ?? 0, m_start, Length);
        }

        /// <summary>
        /// Copy the contents of the array to the given array
        /// </summary>
        public void CopyTo(int index, T[] destinationArray, int destinationIndex, int length)
        {
            Contract.Requires(Range.IsValid(index, length, Length));
            Array.Copy(m_array, sourceIndex: m_start + index, destinationArray: destinationArray, destinationIndex: destinationIndex, length: length);
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
        public static bool operator ==(ArrayView<T> left, ArrayView<T> right)
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
        public static bool operator !=(ArrayView<T> left, ArrayView<T> right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Gets an enumerator over the elements of the array view
        /// </summary>
        /// <returns>the enumerator</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumerator(this);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>
        /// Convert an array to an array view over the entire array.
        /// </summary>
        public static ArrayView<T> FromArray(T[] array)
        {
            Contract.Requires(array != null);

            return new ArrayView<T>(array, 0, array.Length);
        }

        /// <summary>
        /// Implicitly convert an array to an array view over the entire array.
        /// </summary>
        public static implicit operator ArrayView<T>(T[] array)
        {
            Contract.Requires(array != null);

            return new ArrayView<T>(array, 0, array.Length);
        }

        /// <summary>
        /// Enumerator over the array view
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly ArrayView<T> m_arrayView;
            private int m_index;

            internal Enumerator(ArrayView<T> arrayView)
            {
                m_arrayView = arrayView;
                m_index = -1;
            }

            /// <summary>
            /// Gets the element in the collection at the current position of the enumerator.
            /// </summary>
            public T Current => m_arrayView[m_index];

            /// <summary>
            /// Disposes the enumerator.
            /// </summary>
            public void Dispose()
            {
            }

            /// <summary>
            /// Gets the element in the collection at the current position of the enumerator.
            /// </summary>
            object System.Collections.IEnumerator.Current => Current;

            /// <summary>
            /// Advances the enumerator to the next element of the collection.
            /// </summary>
            /// <returns>true if the enumerator was successfully advanced to the next position. Otherwise, false.</returns>
            public bool MoveNext()
            {
                m_index++;
                if (m_index < m_arrayView.Length)
                {
                    return true;
                }

                m_index = m_arrayView.Length;
                return false;
            }

            /// <summary>
            /// Resets the enumerator to its initial state
            /// </summary>
            public void Reset()
            {
                m_index = -1;
            }
        }
    }
}
