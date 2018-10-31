// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Text;
using System.Threading;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Manages a compact array of bit values and allows concurrent updates, which are represented as Booleans,
    /// where true indicates that the bit is on (1) and false indicates the bit is
    /// off (0).
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public sealed class ConcurrentBitArray
    {
        private const int BitsPerInt32 = 32;
        private const int AllSetInt32 = -1;
        private int[] m_array;

        /// <summary>
        /// Gets the number of bits in the bit array
        /// </summary>
        public int Length { get; private set; }

        private ConcurrentBitArray(int[] array, int length)
        {
            Contract.Requires(array != null, "array != null");
            Contract.Requires(length > 0, "length > 0");

            m_array = array;
            Length = length;
        }

        /// <summary>
        /// Creates a new BitArray
        /// </summary>
        /// <param name="length">the length of the array</param>
        /// <param name="defaultValue">the set state for the bits</param>
        public ConcurrentBitArray(int length, bool defaultValue = false)
        {
            Contract.Requires(length >= 0, "length >= 0");

            m_array = length == 0 ? CollectionUtilities.EmptyArray<int>() : new int[GetArrayLength(length)];
            Length = length;

            if (defaultValue)
            {
                var last = m_array.Length - 1;
                for (int i = 0; i < m_array.Length; i++)
                {
                    // Set all the bits
                    // The last element only has the bits set for those which are actually used by the BitArray
                    m_array[i] = unchecked((int)(i != last ? AllSetInt32 : (AllSetInt32 >> (length % BitsPerInt32))));
                }
            }
        }

        /// <summary>
        /// Unsafe function that creates <see cref="ConcurrentBitArray"/> from the int array.
        /// </summary>
        /// <remarks>
        /// This function is unsafe because a client may modify the given array and break the abstraction.
        /// The function should be used only for deserializing a bit vector from a disk.
        /// </remarks>
        public static ConcurrentBitArray UnsafeCreateFrom(int[] array, int length)
        {
            Contract.Requires(array != null, "array != null");
            Contract.Requires(array.Length * 32 >= length, "Given length should not exceed the array capacity");

            return new ConcurrentBitArray(array, length);
        }

        /// <summary>
        /// Gets or sets the bit at the given index
        /// </summary>
        /// <param name="index">the index of the bit</param>
        /// <returns>true if the bit is set (1). Otherwise, false.</returns>
        public bool this[int index]
        {
            get
            {
                // Using relatively expensive check (if the bit array is on the critical path) to debug builds only.
#if DEBUG
                Contract.Requires(Range.IsValid(index, Length));
#endif
                return (Volatile.Read(ref m_array[index / 32]) & (1 << (index % 32))) != 0;
            }

            set
            {
                TrySet(index, value);
            }
        }

        /// <summary>
        /// Attempts to set the bit to the given value
        /// </summary>
        /// <param name="index">the index</param>
        /// <param name="value">the value to set the bit to</param>
        /// <returns>true if the bit was updated. False, if the bit's already matched.</returns>
        public bool TrySet(int index, bool value)
        {
#if DEBUG
            Contract.Requires(Range.IsValid(index, Length));
#endif
            var arrayIndex = index / 32;
            var bitIndex = index % 32;
            bool updated = false;

            while (!updated)
            {
                int shifted = 1 << bitIndex;
                var oldValue = Volatile.Read(ref m_array[arrayIndex]);

                // If the bit is already correct, we don't want to fight with other concurrent updaters.
                // (consider the behavior otherwise if an update occurs between reading oldValue and the cmpxchg below, but the bit is already correct).
                if (((oldValue & shifted) != 0) == value)
                {
                    return false;
                }

                var newValue = value ? (oldValue | shifted) : (oldValue & ~shifted);

                updated = Interlocked.CompareExchange(ref m_array[arrayIndex], newValue, oldValue) == oldValue;
            }

            return true;
        }

        /// <summary>
        /// Modifies the current bit array to be the bit-wise union with the given bit array
        /// </summary>
        /// <param name="other">the bit array to union with</param>
        /// <returns>the current modified bit array</returns>
        public ConcurrentBitArray Or(ConcurrentBitArray other)
        {
            Contract.Requires(other != null && other.Length == Length);

            var array = m_array;

            for (int i = 0; i < array.Length; i++)
            {
                bool updated = false;
                int otherValue = Volatile.Read(ref other.m_array[i]);

                while (!updated)
                {
                    var oldValue = Volatile.Read(ref array[i]);
                    var newValue = oldValue | otherValue;
                    updated = Interlocked.CompareExchange(ref array[i], newValue, oldValue) == oldValue;
                }
            }

            return this;
        }

        /// <summary>
        /// Modifies the current bit array to be the bit-wise intersect with the given bit array
        /// </summary>
        /// <param name="other">the bit array to intersect with</param>
        /// <returns>the current modified bit array</returns>
        public ConcurrentBitArray And(ConcurrentBitArray other)
        {
            Contract.Requires(other != null && other.Length == Length);

            var array = m_array;

            for (int i = 0; i < array.Length; i++)
            {
                bool updated = false;
                int otherValue = Volatile.Read(ref other.m_array[i]);

                while (!updated)
                {
                    var oldValue = Volatile.Read(ref array[i]);
                    var newValue = oldValue & otherValue;
                    updated = Interlocked.CompareExchange(ref array[i], newValue, oldValue) == oldValue;
                }
            }

            return this;
        }

        /// <summary>
        /// Clears all bits in the bit array
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe with other modifications to the BitArray.
        /// </remarks>
        public void UnsafeClear()
        {
            Array.Clear(m_array, 0, m_array.Length);
        }

        /// <summary>
        /// Ensures that the bit array has the given length.
        /// </summary>
        /// <param name="newLength">the new length</param>
        /// <remarks>
        /// This method is NOT thread safe with other modifications to the BitArray.
        /// </remarks>
        public void UnsafeSetLength(int newLength)
        {
            Contract.Requires(newLength >= 0);

            if (newLength == Length)
            {
                return;
            }

            var newArrayLength = GetArrayLength(newLength);

            Array.Resize(ref m_array, newArrayLength);
            Length = newLength;
        }

        private static int GetArrayLength(int n)
        {
            Contract.Requires(n >= 0);
            return unchecked((int)(((uint)n + 31) / 32));
        }

        /// <summary>
        /// Debug helper to print the bitarray in the debugger
        /// </summary>
        /// <remarks>
        /// Thread unsave
        /// </remarks>
        [ExcludeFromCodeCoverage]
        public string ToDebuggerDisplay()
        {
            Contract.Requires((Length * 3) >= 0);

            var builder = new StringBuilder(Length * 3);
            builder.Append('[');
            for (int i = 0; i < Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(this[i] ? '1' : '0');
            }

            builder.Append(']');

            return builder.ToString();
        }

        /// <summary>
        /// Unsafe function that returns an underlying array.
        /// </summary>
        /// <remarks>
        /// This function breaks an encapsulation and should be used only for serialization purposes.
        /// </remarks>
        public int[] UnsafeGetUnderlyingArray() => m_array;
    }
}
