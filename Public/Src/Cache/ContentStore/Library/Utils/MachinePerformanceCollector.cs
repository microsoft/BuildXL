// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities;
#nullable enable

namespace BuildXL.Cache.ContentStore.Utils
{
    internal class MachinePerformanceCollector
    {
        private readonly PerformanceCollector _collector = new PerformanceCollector(collectionFrequency: TimeSpan.FromMinutes(1));
        private readonly PerformanceCollector.Aggregator _perfStatsAggregator;

        public MachinePerformanceCollector()
        {
            _perfStatsAggregator = _collector.CreateAggregator();
        }

        /// <summary>
        /// Get machine performance statistics as string for tracing.
        /// </summary>
        public string GetMachinePerformanceStatistics()
        {
            var perfInfo = _perfStatsAggregator.ComputeMachinePerfInfo(ensureSample: true);
            var set = new StringBuilder();
            set.AddMetric("CommitTotalMb", perfInfo.CommitUsedMb ?? -1, first: true);
            set.AddMetric("CpuUsagePercentage", perfInfo.CpuUsagePercentage);
            set.AddMetric("MachineKbitsPerSecReceived", (long)perfInfo.MachineKbitsPerSecReceived);
            set.AddMetric("MachineKbitsPerSecSent", (long)perfInfo.MachineKbitsPerSecSent);
            set.AddMetric("ProcessCpuPercentage", perfInfo.ProcessCpuPercentage);
            set.AddMetric("ProcessWorkingSetMB", perfInfo.ProcessWorkingSetMB);
            set.AddMetric("GCTotalMemoryMB", (long)Math.Ceiling(GC.GetTotalMemory(forceFullCollection: false) / 1e6));
            set.AddMetric("ProcessThreadCount", (long)_perfStatsAggregator.ProcessThreadCount.Latest);

            ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
            set.AddMetric("ThreadPoolWorkerThreads", workerThreads);
            set.AddMetric("ThreadPoolCompletionPortThreads", completionPortThreads);

            // We're interested only in two drives: D and K, and K drive is optional.
            var driveD = _perfStatsAggregator.DiskStats.FirstOrDefault(s => s.Drive == "D");
            var driveK = _perfStatsAggregator.DiskStats.FirstOrDefault(s => s.Drive == "K");

            addDriveStats(driveD);
            addDriveStats(driveK);

            return set.ToString();

            void addDriveStats(PerformanceCollector.Aggregator.DiskStatistics? stats)
            {
                if (stats != null)
                {
                    var prefix = stats.Drive + "_";
                    set.AddMetric(prefix + "QueueDepth", (long)stats.QueueDepth.Latest);
                    set.AddMetric(prefix + "AvailableSpaceGb", (long)stats.AvailableSpaceGb.Latest);
                }
            }
        }
    }

    /// <summary>
    /// A set of extension methods to keep the implementation of <see cref="MachinePerformanceCollector"/> simpler.
    /// </summary>
    internal static class StringBuilderExtensions
    {
        /// <nodoc />
        public static StringBuilder AddMetric(this StringBuilder sb, string metricName, long value, bool first = false)
        {
            sb.Append($"{(!first ? ", " : "")}{metricName}: {value}");
            return sb;
        }
    }

}
