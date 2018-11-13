// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class IntervalTest : XunitBuildXLTest
    {
        public IntervalTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(1, 4, 3, true)]
        [InlineData(1, 4, 1, false)]
        [InlineData(1, 4, 0, false)]
        [InlineData(1, 4, 4, false)]
        [InlineData(1, 4, 5, false)]
        public void TestOpenIntervals(int lowerBound, int upperBound, int element, bool isContained)
        {
            var interval = Interval<int>.CreateInterval(lowerBound, upperBound, IntervalBoundType.Open, IntervalBoundType.Open);
            XAssert.AreEqual(isContained, interval.Contains(element));
        }

        [Theory]
        [InlineData(1, 4, 3, true)]
        [InlineData(1, 4, 1, true)]
        [InlineData(1, 4, 0, false)]
        [InlineData(1, 4, 4, true)]
        [InlineData(1, 4, 5, false)]
        public void TestClosedIntervals(int lowerBound, int upperBound, int element, bool isContained)
        {
            var interval = Interval<int>.CreateInterval(lowerBound, upperBound, IntervalBoundType.Closed, IntervalBoundType.Closed);
            XAssert.AreEqual(isContained, interval.Contains(element));
        }

        [Fact]
        public void TestLowerUnboundedInterval()
        {
            var interval = Interval<int>.CreateIntervalWithNoLowerBound(10);
            XAssert.IsFalse(interval.Contains(11));
            XAssert.IsTrue(interval.Contains(10));
            XAssert.IsTrue(interval.Contains(int.MinValue));
        }

        [Fact]
        public void TestUpperUnboundedInterval()
        {
            var interval = Interval<int>.CreateIntervalWithNoUpperBound(10);
            XAssert.IsFalse(interval.Contains(9));
            XAssert.IsTrue(interval.Contains(10));
            XAssert.IsTrue(interval.Contains(int.MaxValue));
        }
    }
}
