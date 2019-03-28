// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using JetBrains.Annotations;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Abstract representation of lightweight array.
    /// </summary>
    public interface IReadOnlyArraySlim<out T>
    {
        /// <summary>
        /// The length of the array.
        /// </summary>
        int Length { get; }

        /// <summary>
        /// Gets the element at the specified index.
        /// </summary>
        T this[int index] { get; }

        /// <summary>
        /// For performance reasons of copy operation, slim arrays should expose underlying array.
        /// This is used only for <see cref="StructArraySlimWrapper{T}"/> in <see cref="ReadOnlyArrayExtensions.CopyTo{TArray,T}"/> method
        /// and should not be used by any other clients.
        /// </summary>
        /// <remarks>
        /// There are other potential solutions for the perf problem, but none of them are perfect.
        /// For instance, adding CopyTo method to this interface will require generic argument T to be invariant (currently it is contravariant).
        /// This is possible, but it will require other changes in the entire codebase.
        /// So current solution with exposing underlying array is not perfect, but not worst.
        /// </remarks>
        [CanBeNull]
        [SuppressMessage("Microsoft.Performance", "CA1819")]
        T[] UnderlyingArrayUnsafe { get; }
    }

    /// <nodoc/>
    public static class ReadOnlyArrayExtensions
    {
        /// <nodoc/>
        public static void CopyTo<TArray, T>(this TArray array, int sourceIndex, T[] destination, int destinationIndex, int length)
            where TArray : IReadOnlyArraySlim<T>
        {
            // Using more efficient implementation if array is a wrapper around real array.
            var underlyingArray = array.UnderlyingArrayUnsafe;
            if (underlyingArray != null)
            {
                Array.Copy(underlyingArray, sourceIndex, destination, destinationIndex, length);
            }

            // Otherwise (for small arrays) using regular for-based copy.
            for (int i = 0; i < length; i++)
            {
                destination[i + destinationIndex] = array[i + sourceIndex];
            }
        }

        /// <summary>
        /// Method that throw an <see cref="IndexOutOfRangeException"/>.
        /// </summary>
        /// <remarks>
        /// This method is not generic which means that the underlying array would be boxed.
        /// This will lead to additional memory allocation in a failure case, but will
        /// lead to more readable code, because C# langauge doesn't have partial generic arguments
        /// inferece.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes")]
        public static Exception ThrowOutOfRange<T>(this IReadOnlyArraySlim<T> array, int index)
        {
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "Index '{0}' was outside the bounds of the array with length '{1}'", index, array.Length);

            throw new IndexOutOfRangeException(message);
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        public static IReadOnlyList<T> ToReadOnlyList<TArray, T>(this TArray array)
            where TArray : IReadOnlyArraySlim<T>
        {
            return new ReadOnlyArrayList<T, TArray>(array);
        }

        /// <nodoc/>
        [System.Diagnostics.Contracts.Pure]
        public static IReadOnlyList<T> ToReadOnlyList<T>(this IReadOnlyArraySlim<T> array)
        {
            return new ReadOnlyArrayList<T, IReadOnlyArraySlim<T>>(array);
        }

        private sealed class ReadOnlyArrayList<T, TArray> : IReadOnlyList<T>
            where TArray : IReadOnlyArraySlim<T>
        {
            // Intentionally leaving this field as non-readonly to avoid defensive copy on each access.
            private TArray m_array;

            public ReadOnlyArrayList(TArray array)
            {
                m_array = array;
            }

            public IEnumerator<T> GetEnumerator()
            {
                for (int i = 0; i < m_array.Length; i++)
                {
                    yield return m_array[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public int Count => m_array.Length;

            public T this[int index]
            {
                get { return m_array[index]; }
            }
        }
    }

    /// <summary>
    /// Factory class that responsible for creating different struct arrays by different input.
    /// </summary>
    internal static class StructArray
    {
        /// <nodoc />
        public static StructArray0<T> Create<T>()
        {
            return default(StructArray0<T>);
        }

        /// <nodoc />
        public static StructArray1<T> Create<T>(T item)
        {
            return new StructArray1<T>(item);
        }

        /// <nodoc />
        public static StructArray2<T> Create<T>(T item1, T item2)
        {
            return new StructArray2<T>(item1, item2);
        }

        /// <nodoc />
        public static StructArray3<T> Create<T>(T item1, T item2, T item3)
        {
            return new StructArray3<T>(item1, item2, item3);
        }

        /// <nodoc />
        public static StructArray4<T> Create<T>(T item1, T item2, T item3, T item4)
        {
            return new StructArray4<T>(item1, item2, item3, item4);
        }

        /// <nodoc />
        public static StructArray5<T> Create<T>(T item1, T item2, T item3, T item4, T item5)
        {
            return new StructArray5<T>(item1, item2, item3, item4, item5);
        }

        /// <nodoc />
        public static StructArraySlimWrapper<T> Create<T>(T[] items)
        {
            return new StructArraySlimWrapper<T>(items);
        }
    }

    /// <summary>
    /// Lightweight empty struct array.
    /// </summary>
    internal readonly struct StructArray0<T> : IReadOnlyArraySlim<T>
    {
        public T this[int index]
        {
            get { throw this.ThrowOutOfRange(index); }
        }

        public int Length => 0;

        public T[] UnderlyingArrayUnsafe => null;
    }

    /// <summary>
    /// Lightweight struct array that holds one element.
    /// </summary>
    internal readonly struct StructArray1<T> : IReadOnlyArraySlim<T>
    {
        private readonly T m_item;

        public StructArray1(T item)
        {
            m_item = item;
        }

        public T this[int index]
        {
            get
            {
                if (index != 0)
                {
                    throw this.ThrowOutOfRange(index);
                }

                return m_item;
            }
        }

        public int Length => 1;

        public T[] UnderlyingArrayUnsafe => null;
    }

    /// <summary>
    /// Lightweight struct array that holds 2 elements.
    /// </summary>
    internal readonly struct StructArray2<T> : IReadOnlyArraySlim<T>
    {
        private readonly T m_item0;
        private readonly T m_item1;

        public StructArray2(T item0, T item1)
        {
            m_item0 = item0;
            m_item1 = item1;
        }

        public T this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return m_item0;
                    case 1:
                        return m_item1;
                    default:
                        throw this.ThrowOutOfRange(index);
                }
            }
        }

        public int Length => 2;

        public T[] UnderlyingArrayUnsafe => null;
    }

    /// <summary>
    /// Lightweight struct array that holds 3 elements.
    /// </summary>
    internal readonly struct StructArray3<T> : IReadOnlyArraySlim<T>
    {
        private readonly T m_item0;
        private readonly T m_item1;
        private readonly T m_item2;

        public StructArray3(T item0, T item1, T item2)
        {
            m_item0 = item0;
            m_item1 = item1;
            m_item2 = item2;
        }

        public T this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return m_item0;
                    case 1:
                        return m_item1;
                    case 2:
                        return m_item2;
                    default:
                        throw this.ThrowOutOfRange(index);
                }
            }
        }

        public int Length => 3;

        public T[] UnderlyingArrayUnsafe => null;
    }

    /// <summary>
    /// Lightweight struct array that holds 4 elements.
    /// </summary>
    internal readonly struct StructArray4<T> : IReadOnlyArraySlim<T>
    {
        private readonly T m_item0;
        private readonly T m_item1;
        private readonly T m_item2;
        private readonly T m_item3;

        public StructArray4(T item0, T item1, T item2, T item3)
        {
            m_item0 = item0;
            m_item1 = item1;
            m_item2 = item2;
            m_item3 = item3;
        }

        public T this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return m_item0;
                    case 1:
                        return m_item1;
                    case 2:
                        return m_item2;
                    case 3:
                        return m_item3;
                    default:
                        throw this.ThrowOutOfRange(index);
                }
            }
        }

        public int Length => 4;

        public T[] UnderlyingArrayUnsafe => null;
    }

    /// <summary>
    /// Lightweight struct array that holds 5 elements.
    /// </summary>
    internal readonly struct StructArray5<T> : IReadOnlyArraySlim<T>
    {
        private readonly T m_item0;
        private readonly T m_item1;
        private readonly T m_item2;
        private readonly T m_item3;
        private readonly T m_item4;

        public StructArray5(T item0, T item1, T item2, T item3, T item4)
        {
            m_item0 = item0;
            m_item1 = item1;
            m_item2 = item2;
            m_item3 = item3;
            m_item4 = item4;
        }

        public T this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return m_item0;
                    case 1:
                        return m_item1;
                    case 2:
                        return m_item2;
                    case 3:
                        return m_item3;
                    case 4:
                        return m_item4;
                    default:
                        throw this.ThrowOutOfRange(index);
                }
            }
        }

        public int Length => 5;

        public T[] UnderlyingArrayUnsafe => null;
    }

    /// <summary>
    /// Lightweight struct array that holds one element.
    /// </summary>
    internal readonly struct StructArraySlimWrapper<T> : IReadOnlyArraySlim<T>
    {
        private readonly T[] m_items;

        /// <nodoc/>
        public StructArraySlimWrapper(T[] items)
        {
            m_items = items;
        }

        /// <inheritdoc/>
        public T this[int index]
        {
            get { return m_items[index]; }
        }

        /// <inheritdoc/>
        public int Length => m_items.Length;

        /// <inheritdoc/>
        public T[] UnderlyingArrayUnsafe => m_items;
    }
}
