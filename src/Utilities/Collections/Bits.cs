// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Bit manipulation utilities
    /// </summary>
    /// <remarks>
    /// Derived from Midori's Platform.IO.Binary.Bits
    /// </remarks>
    public static class Bits
    {
#pragma warning disable SA1137 // Elements should have the same indentation
        // maps from a byte to the bit number of the first bit set in that byte.
        private static readonly sbyte[] s_firstBitSetMap = new sbyte[]
                                                           {
                                                              -127, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x4, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x5, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x4, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x6, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x4, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x5, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x4, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x7, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x4, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x5, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x4, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x6, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x4, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x5, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                               0x4, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0, 0x3, 0x0, 0x1, 0x0, 0x2, 0x0, 0x1, 0x0,
                                                           };
#pragma warning restore SA1137 // Elements should have the same indentation

        /// <summary>
        /// Finds the lowest numbered bit (starting at 0) which is 1 within the supplied value. A negative return value
        /// indicates that the value is zero.
        /// </summary>
        public static int FindLowestBitSet(byte value)
        {
            return s_firstBitSetMap[value];
        }

        /// <summary>
        /// Finds the lowest numbered bit (starting at 0) which is 1 within the supplied value. A negative return value
        /// indicates that the value is zero.
        /// </summary>
        public static int FindLowestBitSet(ushort value)
        {
            if ((value & 0xff) != 0)
            {
                return s_firstBitSetMap[value & 0xff];
            }
            else
            {
                return s_firstBitSetMap[value >> 8] + 8;
            }
        }

        /// <summary>
        /// Finds the lowest numbered bit (starting at 0) which is 1 within the supplied value. A negative return value
        /// indicates that the value is zero.
        /// </summary>
        public static int FindLowestBitSet(uint value)
        {
            if ((value & 0xffff) != 0)
            {
                if ((value & 0xff) != 0)
                {
                    return s_firstBitSetMap[value & 0xff];
                }
                else
                {
                    return s_firstBitSetMap[(value >> 8) & 0xff] + 8;
                }
            }
            else
            {
                if ((value & 0xffffff) != 0)
                {
                    return s_firstBitSetMap[(value >> 16) & 0xff] + 16;
                }
                else
                {
                    return s_firstBitSetMap[value >> 24] + 24;
                }
            }
        }

        /// <summary>
        /// Finds the lowest numbered bit (starting at 0) which is 1 within the supplied value. A negative return value
        /// indicates that the value is zero.
        /// </summary>
        public static int FindLowestBitSet(ulong value)
        {
            unchecked
            {
                if ((uint)value != 0)
                {
                    return FindLowestBitSet((uint)value);
                }

                int bitNumber = FindLowestBitSet((uint)(value >> 32));
                if (bitNumber >= 0)
                {
                    bitNumber += 32;
                }

                return bitNumber;
            }
        }

        /// <summary>
        /// Writes the specified value to the buffer at the given index
        /// </summary>
        /// <param name="buffer">the buffer to write bytes to</param>
        /// <param name="index">the start index updated to index after written bytes</param>
        /// <param name="value">the integer value to write</param>
        public static void WriteInt32(byte[] buffer, ref int index, int value)
        {
            buffer[index++] = (byte)((value >> 24) & 0xff);
            buffer[index++] = (byte)((value >> 16) & 0xff);
            buffer[index++] = (byte)((value >> 8) & 0xff);
            buffer[index++] = (byte)((value >> 0) & 0xff);
        }

        /// <summary>
        /// Reads the specified value to the buffer at the given index
        /// </summary>
        /// <typeparam name="TBytes">the byte array/list type</typeparam>
        /// <param name="buffer">the buffer to write bytes to</param>
        /// <param name="index">the start index updated to index after read bytes</param>
        /// <returns>the read integer value</returns>
        public static int ReadInt32<TBytes>(TBytes buffer, ref int index)
            where TBytes : IReadOnlyList<byte>
        {
            return (buffer[index++] << 24)
                | (buffer[index++] << 16)
                | (buffer[index++] << 8)
                | (buffer[index++] << 0);
        }

        /// <summary>
        /// Shift-and-rotate (left shift, and the bits that fall off reappear on the right)
        /// </summary>
        public static ulong RotateLeft(ulong value, int shiftBy)
        {
#if DEBUG
            Contract.Assert(shiftBy >= 0 && shiftBy < 64);
#endif
            checked
            {
                return value << shiftBy | (value >> (64 - shiftBy));
            }
        }

        /// <summary>
        /// Returns an integer with only the highest bit set
        /// </summary>
        public static uint HighestBitSet(uint n)
        {
            unchecked
            {
                n |= n >> 1;
                n |= n >> 2;
                n |= n >> 4;
                n |= n >> 8;
                n |= n >> 16;
                return n - (n >> 1);
            }
        }

        /// <summary>
        /// Gets the high 32-bits of a 64-bit value.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
        public static uint GetHighInt(ulong value)
        {
            return (uint)((value >> 32) & uint.MaxValue);
        }

        /// <summary>
        /// Gets the low 32-bits of a 64-bit value.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
        public static uint GetLowInt(ulong value)
        {
            return (uint)(value & uint.MaxValue);
        }

        /// <summary>
        /// Combines high and lows 32-bit integers into a 64-bit integer.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
        public static ulong GetLongFromInts(uint high, uint low)
        {
            return low | ((ulong)high << 32);
        }

        /// <summary>
        /// Gets the number of bits set using hamming weight
        /// </summary>
        public static int BitCount(uint value)
        {
            // This can overflow if not run in unchecked scope
            unchecked
            {
                value = value - ((value >> 1) & 0x55555555);
                value = (value & 0x33333333) + ((value >> 2) & 0x33333333);
                value = (((value + (value >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24;
                return (int)value;
            }
        }

        /// <summary>
        /// Gets the number of bits set in an array.
        /// </summary>
        public static int BitCount(byte[] data, int offset = 0)
        {
            Contract.Requires(data != null);
            Contract.Requires(offset <= data.Length);

            int result = 0;
            for (int i = offset; i < data.Length; i++)
            {
                result += BitCount(data[i]);
            }

            return result;
        }

        /// <summary>
        /// Gets whether the bit specified by the given offset is set
        /// </summary>
        public static bool IsBitSet(uint bits, byte bitOffset)
        {
            Contract.Requires(bitOffset < 32);

            return (bits & (1 << bitOffset)) != 0;
        }

        /// <summary>
        /// Gets whether the bit specified by the given offset is set
        /// </summary>
        public static void SetBit(ref uint bits, byte bitOffset, bool value)
        {
            Contract.Requires(bitOffset < 32);

            if (value)
            {
                bits |= unchecked((uint)(1 << bitOffset));
            }
            else
            {
                bits &= unchecked((uint)(~(1 << bitOffset)));
            }
        }
    }
}