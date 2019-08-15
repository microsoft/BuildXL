// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities;

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

        /// <nodoc />
        public CounterSet GetPerformanceStats()
        {
            var set = new CounterSet();
            var perfInfo = _perfStatsAggregator.ComputeMachinePerfInfo(ensureSample: true);

            set.AddMetric("MachineAvailableRamMb", perfInfo.AvailableRamMb ?? -1);
            set.AddMetric("CommitLimitMb", perfInfo.CommitLimitMb ?? -1);
            set.AddMetric("CommitTotalMb", perfInfo.CommitTotalMb ?? -1);
            set.AddMetric("CommitUsagePercentage", perfInfo.CommitUsagePercentage ?? -1);
            set.AddMetric("CpuUsagePercentage", perfInfo.CpuUsagePercentage);
            set.AddMetric("MachineBandwidth", perfInfo.MachineBandwidth);
            set.AddMetric("MachineKbitsPerSecReceived", (long)perfInfo.MachineKbitsPerSecReceived);
            set.AddMetric("MachineKbitsPerSecSent", (long)perfInfo.MachineKbitsPerSecSent);
            set.AddMetric("ProcessCpuPercentage", perfInfo.ProcessCpuPercentage);
            set.AddMetric("ProcessWorkingSetMB", perfInfo.ProcessWorkingSetMB);
            set.AddMetric("RamUsagePercentage", perfInfo.RamUsagePercentage ?? -1);
            set.AddMetric("TotalRamMb", perfInfo.TotalRamMb ?? -1);

            set.AddMetric("ProcessThreadCount", (long)_perfStatsAggregator.ProcessThreadCount.Latest);

            foreach (var diskStat in _perfStatsAggregator.DiskStats)
            {
                var diskSet = new CounterSet();

                diskSet.Add("IdleTime", (long)diskStat.IdleTime.Latest);
                diskSet.Add("QueueDepth", (long)diskStat.QueueDepth.Latest);
                diskSet.Add("ReadTime", (long)diskStat.ReadTime.Latest);
                diskSet.Add("WriteTime", (long)diskStat.WriteTime.Latest);

                set.Merge(diskSet, $"{diskStat.Drive}.");
            }

            return set;
        }
    }
}
