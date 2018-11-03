// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for <see cref="BitSet" />
    /// </summary>
    public class BitSetTests
    {
        [Fact]
        public void EnumEmptyCapacity()
        {
            var set = new BitSet(initialCapacity: 0);

            foreach (int i in set)
            {
                XAssert.Fail("Zero-capacity set contained {0}", i);
            }
        }

        [Fact]
        public void EnumEmptyLength()
        {
            var set = new BitSet(initialCapacity: 64 * 4);
            XAssert.AreEqual(0, set.Length);

            foreach (int i in set)
            {
                XAssert.Fail("Zero-length set contained {0}", i);
            }
        }

        [Fact]
        public void SetLengthWithExpansion()
        {
            var set = new BitSet(initialCapacity: 0);
            set.SetLength(64);
            XAssert.AreEqual(64, set.Length);
            XAssert.AreEqual(64, set.Capacity);

            set.SetLength(128);
            XAssert.AreEqual(128, set.Length);
            XAssert.AreEqual(128, set.Capacity);
        }

        [Fact]
        public void SetLengthWithoutExpansion()
        {
            var set = new BitSet(initialCapacity: 192);
            set.SetLength(64);
            XAssert.AreEqual(64, set.Length);
            XAssert.AreEqual(192, set.Capacity);

            set.SetLength(128);
            XAssert.AreEqual(128, set.Length);
            XAssert.AreEqual(192, set.Capacity);
        }

        [Fact]
        public void SingleWordNoEntries()
        {
            var set = new BitSet(initialCapacity: 64);
            set.SetLength(64);

            VerifyContents(set);
        }

        [Fact]
        public void SingleWordSingleEntry()
        {
            var set = new BitSet(initialCapacity: 64);
            set.SetLength(64);

            set.Add(32);
            VerifyContents(set, 32);
        }

        [Fact]
        public void SingleWordAllEntries()
        {
            var set = new BitSet(initialCapacity: 64);
            set.SetLength(64);

            for (int i = 0; i < 64; i++)
            {
                set.Add(i);
            }

            VerifyContents(set, Enumerable.Range(0, 64).ToArray());
        }

        [Fact]
        public void MultipleWordsNoEntries()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            VerifyContents(set);
        }

        [Fact]
        public void MultipleWordsMultipleEntries()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            set.Add(45);
            set.Add(64);
            set.Add(127);
            VerifyContents(set, 45, 127, 64);
        }

        [Fact]
        public void MultipleWordsAllEntries()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            for (int i = 0; i < 128; i++)
            {
                set.Add(i);
            }

            VerifyContents(set, Enumerable.Range(0, 128).ToArray());
        }

        [Fact]
        public void ContentsAfterExpansion()
        {
            var set = new BitSet(initialCapacity: 64);
            set.SetLength(64);

            set.Add(32);
            set.SetLength(128);

            VerifyContents(set, 32);
        }

        [Fact]
        public void ContentsAfterContraction()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            set.Add(32);
            set.Add(127);
            set.Add(64);
            set.Add(63);

            set.SetLength(64);

            VerifyContents(set, 32, 63);
        }

        [Fact]
        public void Clear()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            for (int i = 0; i < 128; i++)
            {
                set.Add(i);
            }

            set.Clear();

            VerifyContents(set);
        }

        [Fact]
        public void FillAndEnumerateComplete()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            set.Fill(128);

            for (int i = 0; i < 128; i++)
            {
                XAssert.IsTrue(set.Contains(i), "Wrong membership for {0}", i);
            }

            XAssert.AreEqual(128, set.Count());
        }

        [Fact]
        public void FillAndEnumeratePartial()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            set.Fill(123);

            for (int i = 0; i < 128; i++)
            {
                XAssert.AreEqual(i < 123, set.Contains(i), "Wrong membership for {0}", i);
            }

            XAssert.AreEqual(123, set.Count());
        }

        [Fact]
        public void FillAndEnumerateHalf()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            set.Fill(64);

            for (int i = 0; i < 128; i++)
            {
                XAssert.AreEqual(i < 64, set.Contains(i), "Wrong membership for {0}", i);
            }

            XAssert.AreEqual(64, set.Count());
        }

        [Fact]
        public void FillAndRefill()
        {
            var set = new BitSet(initialCapacity: 128);
            set.SetLength(128);

            set.Fill(128);
            set.Fill(64); // Clears the top bits.

            for (int i = 0; i < 128; i++)
            {
                XAssert.AreEqual(i < 64, set.Contains(i), "Wrong membership for {0}", i);
            }

            XAssert.AreEqual(64, set.Count());
        }

        [Fact]
        public void Big()
        {
            var set = new BitSet(initialCapacity: 64 * 1024);
            set.SetLength(64 * 1024);

            var rng = new Random(99);
            var added = new int[2 * 1024];
            for (int i = 0; i < added.Length; i++)
            {
                added[i] = rng.Next(0, 64 * 1024);
                set.Add(added[i]);
            }

            VerifyContents(set, added);
        }

        [Fact]
        public void RoundToValidBitCount()
        {
            XAssert.AreEqual(64, BitSet.RoundToValidBitCount(64));
            XAssert.AreEqual(64, BitSet.RoundToValidBitCount(1));
            XAssert.AreEqual(0, BitSet.RoundToValidBitCount(0));
            XAssert.AreEqual(128, BitSet.RoundToValidBitCount(65));
            XAssert.AreEqual(128, BitSet.RoundToValidBitCount(128));
        }

        private static void VerifyContents(BitSet set, params int[] expected)
        {
            Array.Sort(expected);

            int i = 0;
            foreach (int actual in set)
            {
                XAssert.IsTrue(i < expected.Length, "Returned {0} which was beyond the tail of the expected set", actual);

                int current = expected[i];
                XAssert.AreEqual(current, actual, "Mismatched entry (ordinal {0})", i);

                // Skip the current thing and any duplicates that follow it.
                while (i < expected.Length && expected[i] == current)
                {
                    i++;
                }
            }

            XAssert.AreEqual(expected.Length, i, "BitSet contained too few items (skipped the tail of the expected set)");

            foreach (int expectedElement in expected)
            {
                XAssert.IsTrue(set.Contains(expectedElement), "Set reported {0} during enumeration but not via Contains", expectedElement);
            }
        }
    }
}
