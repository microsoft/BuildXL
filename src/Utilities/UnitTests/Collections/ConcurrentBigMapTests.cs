// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
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
    public class ConcurrentBigMapTests : XunitBuildXLTest
    {
        public ConcurrentBigMapTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestConcurrentBigMapOperations()
        {
            ConcurrentBigMap<int, string> map = new ConcurrentBigMap<int, string>();
            XAssert.IsTrue(map.TryAdd(0, "value"));
            XAssert.IsFalse(map.TryAdd(0, "not added value"));
            XAssert.AreEqual("value", map[0]);
            map[0] = "newValue";
            map[1] = "value1";

            var value0 = "newValue";
            XAssert.AreEqual(value0, map[0]);
            XAssert.IsTrue(map.ContainsKey(0));
            XAssert.IsTrue(map.ContainsKey(1));
            XAssert.IsFalse(map.ContainsKey(12));

            XAssert.AreEqual(2, map.Count);

            // Test TryGetValue
            string value1;
            XAssert.IsTrue(map.TryGetValue(1, out value1));
            XAssert.AreEqual("value1", value1);
            string value31;
            XAssert.IsFalse(map.TryGetValue(31, out value31));

            // Test update
            XAssert.IsFalse(map.TryUpdate(1, "notUpdatedValue1", "notActualValue1"));
            XAssert.AreEqual("value1", map[1]);
            XAssert.IsTrue(map.TryUpdate(1, "updatedValue1", "value1"));
            value1 = map[1];
            XAssert.AreEqual("updatedValue1", value1);

            // Test remove
            int beforeFailedRemoveCount = map.Count;
            string value23;
            XAssert.IsFalse(map.TryRemove(23, out value23));
            XAssert.AreEqual(beforeFailedRemoveCount, map.Count);
            map.Add(23, "value23");
            XAssert.AreEqual(beforeFailedRemoveCount + 1, map.Count);
            XAssert.IsTrue(map.TryRemove(23, out value23));
            XAssert.AreEqual("value23", value23);
            XAssert.AreEqual(beforeFailedRemoveCount, map.Count);

            Assert.Equal(new int[] { 0, 1 }, map.Keys.ToArray());
            Assert.Equal(new string[] { value0, value1 }, map.Values.ToArray());

            XAssert.AreEqual(2, map.Count);

            string addedData = "added data";
            string notAddedData = "not added data";
            var result = map.GetOrAdd(2, addedData, (key, data0) => data0);
            XAssert.IsFalse(result.IsFound);
            XAssert.AreEqual(addedData, result.Item.Value);
            XAssert.AreEqual(addedData, map[2]);

            // Ensure entry is not updated for get or add
            result = map.GetOrAdd(2, notAddedData, (key, data0) => data0);
            XAssert.IsTrue(result.IsFound);
            XAssert.AreEqual(addedData, result.Item.Value);
            XAssert.AreEqual(addedData, map[2]);

            Func<int, string, string, string> updateFunction =
                (key, data0, currentValue) => "updated " + currentValue;

            var updatedData = updateFunction(2, notAddedData, addedData);
            result = map.AddOrUpdate(2, notAddedData, (key, data0) => data0, updateFunction);
            XAssert.IsTrue(result.IsFound);
            XAssert.AreEqual(addedData, result.OldItem.Value);
            XAssert.AreEqual(updatedData, result.Item.Value);
            XAssert.AreEqual(updatedData, map[2]);

            result = map.AddOrUpdate(3, addedData, (key, data0) => data0, updateFunction);
            XAssert.IsFalse(result.IsFound);
            XAssert.AreEqual(addedData, result.Item.Value);
            XAssert.AreEqual(addedData, map[3]);

            TestOperationsHelper(parallel: false);
        }

        [Fact]
        public void TestConcurrentBigMapOperationsParallel()
        {
            TestOperationsHelper(parallel: true);
        }

        public void TestOperationsHelper(bool parallel)
        {
            ConcurrentBigMap<int, string> map = new ConcurrentBigMap<int, string>();
            int length = 100000;
            int expectedAddedCount = length;

            string[] expectedValues = new string[length];

            // Verify that all bits start off with the default value (false in this case)
            For(length, i =>
            {
                XAssert.IsFalse(map.ContainsKey(i));
            }, parallel);

            XAssert.AreEqual(0, map.Count);

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
                    map.ContainsKey(i - 2);
                }

                var stringRepresentation = i.ToString();
                if (map.TryAdd(i, stringRepresentation))
                {
                    expectedValues[i] = stringRepresentation;
                    Interlocked.Increment(ref addedCount);
                }

                XAssert.AreEqual(stringRepresentation, map[i]);
            }, parallel);

            XAssert.AreEqual(expectedAddedCount, addedCount);
            XAssert.AreEqual(expectedAddedCount, map.Count);

            For(length, i =>
                {
                    XAssert.AreEqual((i % 4) != 3, map.ContainsKey(i));

                    var expectedValue = expectedValues[i];
                    if (expectedValue != null)
                    {
                        XAssert.AreEqual(expectedValue, map[i]);
                    }
                }, parallel);
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
