// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class PerformanceCollectorTests
    {
        [Fact(Skip = "Test is flaky - Bug #1243987")]
        public void NestedCollectorTest()
        {
            using (PerformanceCollector collector = new PerformanceCollector(TimeSpan.FromMilliseconds(10)))
            {
                using (var aggregator = collector.CreateAggregator())
                {
                    // Sleep until we get a sample
                    Stopwatch sw = Stopwatch.StartNew();
                    while (sw.Elapsed.TotalSeconds < 60 && GetMaxAggregatorCount(aggregator) < 2)
                    {
                        Thread.Sleep(10);
                    }

                    XAssert.IsTrue(GetMaxAggregatorCount(aggregator) > 0, "No samples were collected");

                    using (var aggregator2 = collector.CreateAggregator())
                    {
                        Stopwatch sw2 = Stopwatch.StartNew();
                        while (sw2.Elapsed.TotalSeconds < 1 && GetMaxAggregatorCount(aggregator2) < 2)
                        {
                            Thread.Sleep(10);
                        }

                        XAssert.IsTrue(GetMaxAggregatorCount(aggregator) > GetMaxAggregatorCount(aggregator2), "The nested aggregator should have fewer samples than the earlier created aggregator");
                    }
                }
            }
        }

        /// <summary>
        /// Sometimes, certain perf counters are not available on some machines. Try various counters since the intent
        /// of this test is to validate the nesting, not the counters themselves
        /// </summary>
        private static int GetMaxAggregatorCount(PerformanceCollector.Aggregator aggregator)
        {
            return Math.Max(Math.Max(aggregator.MachineAvailablePhysicalMB.Count, aggregator.MachineCpu.Count), aggregator.ProcessCpu.Count);
        }
    }
}
