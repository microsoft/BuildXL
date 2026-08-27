// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class ApproximateCacheCountersTests : XunitBuildXLTest
    {
        public ApproximateCacheCountersTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void NewCountersAreZero()
        {
            var counters = new ApproximateCacheCounters();

            XAssert.AreEqual(0, counters.Hits);
            XAssert.AreEqual(0, counters.Misses);
        }

        [Fact]
        public void SingleThreadedUpdatesAreExactAndIndependent()
        {
            var counters = new ApproximateCacheCounters();

            for (int i = 0; i < 17; i++)
            {
                counters.RecordHit();
            }

            for (int i = 0; i < 11; i++)
            {
                counters.RecordMiss();
            }

            XAssert.AreEqual(17, counters.Hits);
            XAssert.AreEqual(11, counters.Misses);
        }

        [Fact]
        public void ConcurrentUpdatesStayWithinExpectedBounds()
        {
            const int UpdateCount = 100_000;
            var counters = new ApproximateCacheCounters();

            Parallel.For(
                0,
                UpdateCount,
                _ =>
                {
                    counters.RecordHit();
                    counters.RecordMiss();
                });

            XAssert.IsTrue(counters.Hits > 0);
            XAssert.IsTrue(counters.Hits <= UpdateCount);
            XAssert.IsTrue(counters.Misses > 0);
            XAssert.IsTrue(counters.Misses <= UpdateCount);
        }

        [Fact]
        public void AggregationSaturatesOnOverflow()
        {
            XAssert.AreEqual(30, ApproximateCacheCounters.AddForAggregation(10, 20));
            XAssert.AreEqual(long.MaxValue, ApproximateCacheCounters.AddForAggregation(long.MaxValue - 1, 2));
            XAssert.AreEqual(long.MaxValue, ApproximateCacheCounters.AddForAggregation(10, long.MinValue));
        }
    }
}
