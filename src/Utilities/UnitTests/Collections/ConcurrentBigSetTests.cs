// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for ConcurrentBigSet
    /// </summary>
    public class ConcurrentBigSetTests : XunitBuildXLTest
    {
        public ConcurrentBigSetTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestConcurrentBigSetOperations()
        {
            TestOperationsHelper(parallel: false);
        }

        [Fact]
        public void TestConcurrentBigSetOperationsParallel()
        {
            TestOperationsHelper(parallel: true);
        }

        private static void TestOperationsHelper(bool parallel)
        {
            var set = new ConcurrentBigSet<int>();
            int length = 100000;
            int expectedAddedCount = length;

            var indexedItems = new int[length];

            // Verify that all bits start off with the default value (false in this case)
            For(length, i =>
            {
                XAssert.IsFalse(set.Contains(i));
            }, parallel);

            XAssert.AreEqual(0, set.Count);

            int addedCount = 0;

            // Verify setting bits
            For(length, i =>
            {
                if ((i % 4) == 3)
                {
                    // Introduce some contention for setting the same key.
                    i = i - 1;
                    Interlocked.Decrement(ref expectedAddedCount);
                }

                if ((i % 7) == 3)
                {
                    // Introduce some concurrent read-only operations
                    // in the parallel case
                    set.Contains(i - 2);
                }

                ConcurrentBigSet<int>.GetAddOrUpdateResult result =
                    ((i % 5) != 3) ?
                    set.GetOrAdd(i) :

                    // Test heterogeneous add in set
                    set.GetOrAddItem(new StringIntItem(i.ToString()));

                if (!result.IsFound)
                {
                    Interlocked.Increment(ref addedCount);
                }

                XAssert.AreEqual(i, set[result.Index]);

                // Save where the result claims the index of the item
                // to verify it later.
                indexedItems[result.Index] = i;
            }, parallel);

            XAssert.AreEqual(expectedAddedCount, addedCount);
            XAssert.AreEqual(expectedAddedCount, set.Count);

            For(length, i =>
                {
                    XAssert.AreEqual((i % 4) != 3, set.Contains(i));

                    // Test heterogeneous search in set
                    XAssert.AreEqual((i % 4) != 3, set.ContainsItem(new StringIntItem(i.ToString())));

                    if (i < expectedAddedCount)
                    {
                        // Verify the order of items doesn't change.
                        XAssert.AreEqual(indexedItems[i], set[i]);
                    }
                }, parallel);
        }

        private struct StringIntItem : IPendingSetItem<int>
        {
            private readonly string m_value;

            public StringIntItem(string value)
            {
                m_value = value;
            }

            public int HashCode => int.Parse(m_value).GetHashCode();

            public bool Equals(int other)
            {
                return int.Parse(m_value) == other;
            }

            public int CreateOrUpdateItem(int oldItem, bool hasOldItem, out bool remove)
            {
                remove = false;
                return int.Parse(m_value);
            }
        }

        private static void For(int count, Action<int> action, bool parallel)
        {
            if (parallel)
            {
                Parallel.For(0, count, action);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    action(i);
                }
            }
        }
    }
}
