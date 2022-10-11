// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Performance statistics in a strongly typed form.
    /// </summary>
    internal readonly record struct PerformanceStatistics(
        int CpuQueueLength,
        int CpuUsagePercentage,
        int CpuWMIUsagePercentage,
        int ContextSwitchesPerSec,
        int ProcessCpuPercentage,

        int? TotalRamMb,
        int? AvailableRamMb,
        int? EffectiveAvailableRamMb,
        int CommitTotalMb,
        int ProcessWorkingSetMb,
        long GCTotalMemoryMb,
        long? GCTotalAvailableMemoryMb,

        int ProcessThreadCount,
        int ThreadPoolWorkerThreads,
        int ThreadPoolCompletionPortThreads,

        PerformanceCollector.Aggregator.DiskStatistics? DriveD,
        PerformanceCollector.Aggregator.DiskStatistics? DriveK,

        long MachineKbitsPerSecReceived,
        long MachineKbitsPerSecSent
        )
    {

        /// <summary>
        /// Gets a string representation suitable for tracing.
        /// </summary>
        /// <returns></returns>
        public string ToTracingString()
        {
            List<string> parts = new List<string>();
            CollectMetrics((metricName, value) => parts.Add($"{metricName}=[{value}]"));
            return string.Join(" ", parts);
        }

        public delegate void AddMetric(string metricName, long value);

        public void CollectMetrics(AddMetric addMetric)
        {
            addMetric(nameof(CpuQueueLength), CpuQueueLength);
            addMetric(nameof(CpuUsagePercentage), CpuUsagePercentage);
            addMetric(nameof(CpuWMIUsagePercentage), CpuWMIUsagePercentage);
            addMetric(nameof(ContextSwitchesPerSec), ContextSwitchesPerSec);
            addMetric(nameof(ProcessCpuPercentage), ProcessCpuPercentage);

            addMetric(nameof(TotalRamMb), TotalRamMb ?? -1);
            addMetric(nameof(AvailableRamMb), AvailableRamMb ?? -1);
            addMetric(nameof(EffectiveAvailableRamMb), EffectiveAvailableRamMb ?? -1);
            addMetric(nameof(CommitTotalMb), CommitTotalMb);
            addMetric(nameof(ProcessWorkingSetMb), ProcessWorkingSetMb);
            addMetric(nameof(GCTotalMemoryMb), GCTotalMemoryMb);
            addMetric(nameof(GCTotalAvailableMemoryMb), GCTotalAvailableMemoryMb ?? -1);

            addMetric(nameof(ProcessThreadCount), ProcessThreadCount);
            addMetric(nameof(ThreadPoolWorkerThreads), ThreadPoolWorkerThreads);
            addMetric(nameof(ThreadPoolCompletionPortThreads), ThreadPoolCompletionPortThreads);

            addMetric(nameof(MachineKbitsPerSecReceived), MachineKbitsPerSecReceived);
            addMetric(nameof(MachineKbitsPerSecSent), MachineKbitsPerSecSent);

            // We're interested only in two drives: D and K, and K drive is optional.
            addDriveStats(DriveD);
            addDriveStats(DriveK);

            void addDriveStats(PerformanceCollector.Aggregator.DiskStatistics? stats)
            {
                if (stats != null)
                {
                    var prefix = stats.Drive + "_";
                    addMetric(prefix + "QueueDepth", (long)stats.QueueDepth.Latest);
                    addMetric(prefix + "AvailableSpaceGb", (long)stats.AvailableSpaceGb.Latest);
                }
            }
        }
    }

    internal class MachinePerformanceCollector : IDisposable
    {
        private readonly PerformanceCollector _collector;
        private readonly PerformanceCollector.Aggregator _perfStatsAggregator;

        public MachinePerformanceCollector(TimeSpan collectionFrequency, bool logWmiCounters)
        {
            _collector = new PerformanceCollector(collectionFrequency, logWmiCounters);
            _perfStatsAggregator = _collector.CreateAggregator();
        }

        public void Dispose()
        {
            _collector.Dispose();
        }

        /// <summary>
        /// Get machine performance statistics.
        /// </summary>
        public PerformanceStatistics GetMachinePerformanceStatistics()
        {
            var info = _perfStatsAggregator.ComputeMachinePerfInfo(ensureSample: true);
            ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);

            long? gcTotalAvailableMemoryMb = null;
#if NET6_0
            var gcInfo = GC.GetGCMemoryInfo();
            gcTotalAvailableMemoryMb = (long)Math.Ceiling(gcInfo.TotalAvailableMemoryBytes / 1e6);
#endif

            return new PerformanceStatistics()
            {
                // CPU
                CpuQueueLength = info.CpuQueueLength,
                CpuUsagePercentage = info.CpuUsagePercentage,
                CpuWMIUsagePercentage = info.CpuWMIUsagePercentage,
                ContextSwitchesPerSec = info.ContextSwitchesPerSec,
                ProcessCpuPercentage = info.ProcessCpuPercentage,

                // Memory usage
                TotalRamMb = info.TotalRamMb,
                AvailableRamMb = info.AvailableRamMb,
                EffectiveAvailableRamMb = info.EffectiveAvailableRamMb,
                CommitTotalMb = info.CommitUsedMb ?? -1,
                ProcessWorkingSetMb = info.ProcessWorkingSetMB,
                GCTotalMemoryMb = (long)Math.Ceiling(GC.GetTotalMemory(forceFullCollection: false) / 1e6),
                GCTotalAvailableMemoryMb = gcTotalAvailableMemoryMb ?? -1,


                // Threads
                ProcessThreadCount = (int)_perfStatsAggregator.ProcessThreadCount.Latest,
                ThreadPoolWorkerThreads = workerThreads,
                ThreadPoolCompletionPortThreads = completionPortThreads,

                // IO devices
                // We're interested only in two drives: D and K, and K drive is optional.
                DriveD = _perfStatsAggregator.DiskStats.FirstOrDefault(s => s.Drive == "D"),
                DriveK = _perfStatsAggregator.DiskStats.FirstOrDefault(s => s.Drive == "K"),

                // Networking
                MachineKbitsPerSecReceived = (long)info.MachineKbitsPerSecReceived,
                MachineKbitsPerSecSent = (long)info.MachineKbitsPerSecSent,
            };
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
