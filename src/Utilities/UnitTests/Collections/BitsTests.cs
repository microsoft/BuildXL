// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Makes sure all the <see cref="Bits" /> are in the right places.
    /// </summary>
    public class BitsTests
    {
        [Fact]
        public void LowestBit8()
        {
            for (int b = byte.MinValue; b <= byte.MaxValue; b++)
            {
                AssertLowestSetBitIndexEqual(GetLowestBitSetTrivial((byte)b), Bits.FindLowestBitSet((byte)b));
            }
        }

        [Fact]
        public void LowestBit16()
        {
            for (int i = 0; i < 16; i++)
            {
                ushort value = (ushort)(1 << i);
                AssertLowestSetBitIndexEqual(i, Bits.FindLowestBitSet(value));

                // value - 1 has all low bits set and i unset; top bits the same and so cleared with xor.
                ushort valueWithAllHighBitsSet = unchecked((ushort)(value | ~((value - 1) ^ value)));
                AssertLowestSetBitIndexEqual(i, Bits.FindLowestBitSet(valueWithAllHighBitsSet));
            }
        }

        [Fact]
        public void LowestBit32()
        {
            for (int i = 0; i < 32; i++)
            {
                uint value = 1U << i;
                AssertLowestSetBitIndexEqual(i, Bits.FindLowestBitSet(value));

                // value - 1 has all low bits set and i unset; top bits the same and so cleared with xor.
                uint valueWithAllHighBitsSet = value | ~((value - 1) ^ value);
                AssertLowestSetBitIndexEqual(i, Bits.FindLowestBitSet(valueWithAllHighBitsSet));
            }
        }

        [Fact]
        public void LowestBit64()
        {
            for (int i = 0; i < 64; i++)
            {
                ulong value = 1UL << i;
                AssertLowestSetBitIndexEqual(i, Bits.FindLowestBitSet(value));

                // value - 1 has all low bits set and i unset; top bits the same and so cleared with xor.
                ulong valueWithAllHighBitsSet = value | ~((value - 1) ^ value);
                AssertLowestSetBitIndexEqual(i, Bits.FindLowestBitSet(valueWithAllHighBitsSet));
            }
        }

        private void AssertLowestSetBitIndexEqual(int expected, int actual)
        {
            if (expected < 0 && actual >= 0)
            {
                XAssert.Fail("Bit set in position {0}, but was not found", expected);
            }

            if (expected >= 0 && actual < 0)
            {
                XAssert.Fail("No bits set, but {0} was reported", actual);
            }

            if (expected < 0 && actual < 0)
            {
                return;
            }

            XAssert.AreEqual(expected, actual, "Disagreement in lowest-set-bit position");
        }

        private int GetLowestBitSetTrivial(long value)
        {
            int index = 0;
            while (value != 0)
            {
                if ((value & 1) != 0)
                {
                    return index;
                }

                value >>= 1;
                index++;
            }

            return -1;
        }

        [Fact]
        public void RotateLeft()
        {
           XAssert.AreEqual<ulong>(2, Bits.RotateLeft(1, 1));
           XAssert.AreEqual<ulong>(1, Bits.RotateLeft(1, 0));
           XAssert.AreEqual<ulong>(1UL << 63, Bits.RotateLeft(1, 63));
           XAssert.AreEqual<ulong>(1, Bits.RotateLeft(1UL << 63, 1));
           XAssert.AreEqual<ulong>(2, Bits.RotateLeft(1UL << 63, 2));
           XAssert.AreEqual<ulong>(1UL << 62, Bits.RotateLeft(1UL << 63, 63));
           XAssert.AreEqual<ulong>(0xffffffff, Bits.RotateLeft(0xffffffffUL << 32, 32));
           XAssert.AreEqual<ulong>(0xffffffff | (0xeeeeeeeeUL << 32), Bits.RotateLeft(0xeeeeeeee | (0xffffffffUL << 32), 32));
        }
    }
}
