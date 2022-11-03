// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
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
        int? CommitTotalMb,
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
        public string ToTracingString()
        {
            using var pooledHandle = Pools.StringBuilderPool.GetInstance();
            var sb = pooledHandle.Instance;
            CollectMetrics((metricName, value) => appendMetric(metricName, value));

            return sb.ToString();

            void appendMetric(string metricName, long value)
            {
                var prefix = sb.Length == 0 ? string.Empty : " ";
                sb.Append($"{prefix}{metricName}=[{value}]");
            }
        }

        public delegate void AddMetric(string metricName, long value);

        public void CollectMetrics(AddMetric addMetric)
        {
            addMetric(nameof(CpuQueueLength), CpuQueueLength);
            addMetric(nameof(CpuUsagePercentage), CpuUsagePercentage);
            addMetric(nameof(CpuWMIUsagePercentage), CpuWMIUsagePercentage);
            addMetric(nameof(ContextSwitchesPerSec), ContextSwitchesPerSec);
            addMetric(nameof(ProcessCpuPercentage), ProcessCpuPercentage);

            invokeAddMetric(nameof(TotalRamMb), TotalRamMb);
            invokeAddMetric(nameof(AvailableRamMb), AvailableRamMb);
            invokeAddMetric(nameof(EffectiveAvailableRamMb), EffectiveAvailableRamMb);
            invokeAddMetric(nameof(CommitTotalMb), CommitTotalMb);
            addMetric(nameof(ProcessWorkingSetMb), ProcessWorkingSetMb);
            addMetric(nameof(GCTotalMemoryMb), GCTotalMemoryMb);
            invokeAddMetric(nameof(GCTotalAvailableMemoryMb), GCTotalAvailableMemoryMb);

            addMetric(nameof(ProcessThreadCount), ProcessThreadCount);
            addMetric(nameof(ThreadPoolWorkerThreads), ThreadPoolWorkerThreads);
            addMetric(nameof(ThreadPoolCompletionPortThreads), ThreadPoolCompletionPortThreads);

            addMetric(nameof(MachineKbitsPerSecReceived), MachineKbitsPerSecReceived);
            addMetric(nameof(MachineKbitsPerSecSent), MachineKbitsPerSecSent);

            // We're interested only in two drives: D and K, and K drive is optional.
            addDriveStats(DriveD);
            addDriveStats(DriveK);

            void invokeAddMetric(string name, long? value)
            {
                if (value != null)
                {
                    addMetric(name, value.Value);
                }
            }

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
                CommitTotalMb = info.CommitUsedMb,
                ProcessWorkingSetMb = info.ProcessWorkingSetMB,
                GCTotalMemoryMb = (long)Math.Ceiling(GC.GetTotalMemory(forceFullCollection: false) / 1e6),
                GCTotalAvailableMemoryMb = gcTotalAvailableMemoryMb,

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
}
