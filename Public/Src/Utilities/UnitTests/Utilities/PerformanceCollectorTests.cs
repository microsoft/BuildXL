// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using BuildXL.Utilities;
using Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.DataCollection.Interfaces;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class PerformanceCollectorTests
    {
        [Fact]
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
        /// This test checks whether GetMachineActiveTcpConnections method returns active tcp connections established on the machine.
        /// </summary>
        [Fact]
        public void VerifyMachineActiveTcpConnectionsCount()
        {
             XAssert.IsTrue(PerformanceCollector.GetMachineActiveTcpConnections() > 0, "GetMachineActiveTcpConnections method has failed to return a valid, non-negative TCP connection count.");
        }

        /// <summary>
        /// This test checks whether the GetMachineOpenFileDescriptors methods returns the file descriptors currently open on the machine. 
        /// </summary>
        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void VerifyMachineOpenFileDescriptorsCount()
        {
            XAssert.IsTrue(PerformanceCollector.GetMachineOpenFileDescriptors() > 0, "GetMachineOpenFileDescriptors method has failed to return a valid, non-negative open file descriptors count.");
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
