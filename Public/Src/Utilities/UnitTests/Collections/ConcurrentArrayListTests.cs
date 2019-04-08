// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable UseObjectOrCollectionInitializer
namespace Test.BuildXL.Utilities
{
    public class ConcurrentArrayListTests : XunitBuildXLTest
    {
        public ConcurrentArrayListTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Create()
        {
            var list = new ConcurrentArrayList<TestValue>(1, false);
            list[0] = new TestValue(1);
            XAssert.IsNotNull(list[0]);
            XAssert.AreEqual(1, list[0].Value);
        }

        [Fact]
        public void PreventResize()
        {
            var list = new ConcurrentArrayList<TestValue>(1, false);
            list[0] = new TestValue(0);
            XAssert.IsNotNull(list[0]);
            XAssert.AreEqual(0, list[0].Value);

            Exception expectedException = null;
            try
            {
                list[1] = new TestValue(1);
            }
            catch (Exception e)
            {
                expectedException = e;
            }

            XAssert.IsNotNull(expectedException as ArgumentException);
            XAssert.AreEqual("index", ((ArgumentException)expectedException).ParamName);
        }

        [Fact]
        public void AllowResize()
        {
            var list = new ConcurrentArrayList<TestValue>(1, true);
            list[0] = new TestValue(0);
            XAssert.IsNotNull(list[0]);
            XAssert.AreEqual(0, list[0].Value);

            list[1] = new TestValue(1);
            XAssert.IsNotNull(list[1]);
            XAssert.AreEqual(1, list[1].Value);
        }

        [Fact]
        public void GetOrSet()
        {
            var list = new ConcurrentArrayList<TestValue>(1, true);
            list[5] = new TestValue(5);
            XAssert.IsNotNull(list[5]);
            XAssert.AreEqual(5, list[5].Value);

            bool callBackCalled = false;
            TestValue val = list.GetOrSet(
                5,
                () =>
                {
                    callBackCalled = true;
                    return new TestValue(10);
                });

            XAssert.IsNotNull(val);
            XAssert.AreEqual(5, val.Value);

            // also test index
            XAssert.IsNotNull(list[5]);
            XAssert.AreEqual(5, list[5].Value);

            XAssert.IsFalse(callBackCalled);
        }

        [Fact]
        public void IndexStress()
        {
            var list = new ConcurrentArrayList<TestValue>(1, true);
            var rnd = new Random();
            int maxIndex = 100000;
            IOrderedEnumerable<int> indices = Enumerable.Range(0, maxIndex).ToArray().OrderBy(x => rnd.Next(maxIndex));

            Parallel.ForEach(
                indices,
                index => list[index] = new TestValue(index));

            Parallel.ForEach(
                indices,
                index =>
                {
                    TestValue value = list[index];
                    XAssert.IsNotNull(value);
                    XAssert.AreEqual(index, value.Value);
                });
        }

        [Fact]
        public void GetOrSetStress()
        {
            // Generate a bunch of operations that work on a set of fields that trigger resizes, updates and reads.
            var list = new ConcurrentArrayList<TestValue>(1, true);
            var rnd = new Random();
            int maxIndex = 2000;
            int maxOperationsFactor = 1000;

            int val0 = 0xfff0000;
            int val1 = 0x000ff000;
            int val2 = 0x00000fff;

            var operations = Enumerable
                .Range(0, maxIndex * maxOperationsFactor)
                .Select(
                    index =>
                    {
                        int value;
                        int valueToSet = rnd.Next(3);
                        switch (valueToSet)
                        {
                            case 0:
                                value = val0;
                                break;
                            case 1:
                                value = val1;
                                break;
                            case 2:
                                value = val2;
                                break;
                            default:
                                value = 0;
                                XAssert.Fail("sdfsd");
                                break;
                        }

                        return new
                            {
                                    Index = index / maxOperationsFactor,
                                    Value = value
                            };
                    })
                .ToArray()
                .OrderBy(x => rnd.Next(maxIndex));

            Parallel.ForEach(
                operations,
                operation =>
                {
                    // Make sure it is one of the legal value.
                    TestValue res = list.GetOrSet(operation.Index, () => new TestValue(operation.Value));
                    XAssert.IsTrue(res.Value == val0 || res.Value == val1 || res.Value == val2);

                    // Also inspect random location. Make sure it is a legal value (or null when it is not set yet)
                    TestValue other = list[rnd.Next(maxIndex)];
                    XAssert.IsTrue(other == null || other.Value == val0 || other.Value == val1 || other.Value == val2);
                });
        }
    }

    /// <summary>
    /// Test helper
    /// </summary>
    public class TestValue
    {
        /// <summary>
        /// Test value
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        public TestValue(int value)
        {
            Value = value;
        }
    }
}
