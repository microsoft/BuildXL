// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.PerformanceCollector.Aggregator;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Provides high level breakdown for some internal scheduler performance.
    /// </summary>
    public sealed class SchedulerPerformanceInfo
    {
        /// <nodoc/>
        public long ExecuteProcessDurationMs;

        /// <nodoc/>
        public long MachineMinimumAvailablePhysicalMB;

        /// <nodoc/>
        public long ProcessPipCacheMisses;

        /// <nodoc/>
        public long ProcessPipCacheHits;

        /// <nodoc/>
        public long ProcessPipIncrementalSchedulingPruned;

        /// <nodoc/>
        public long TotalProcessPips;

        /// <nodoc/>
        public long ProcessPipsUncacheable;

        /// <nodoc/>
        public long CriticalPathTableHits;

        /// <nodoc/>
        public long CriticalPathTableMisses;

        /// <nodoc/>
        public long RunProcessFromCacheDurationMs;

        /// <nodoc/>
        public long SandboxedProcessPrepDurationMs;

        /// <nodoc/>
        public FileContentStats FileContentStats;

        /// <nodoc/>
        public long OutputsProduced => FileContentStats.OutputsProduced;

        /// <nodoc/>
        public long OutputsDeployed => FileContentStats.OutputsDeployed;

        /// <nodoc/>
        public long OutputsUpToDate => FileContentStats.OutputsUpToDate;

        /// <nodoc/>
        public CounterCollection<PipExecutionStep> PipExecutionStepCounters;

        /// <nodoc/>
        public int AverageMachineCPU;

        /// <nodoc/>
        public IReadOnlyCollection<DiskStatistics> DiskStatistics;

        /// <summary>
        /// The LowMemory smell is logged when it first happens instead of waiting for the end of the build, since
        /// there is a high likelihood that the machine would be so bogged down from paging that the user would kill
        /// the build before it gets to the end when all perf smells are logged
        /// </summary>
        public bool HitLowMemorySmell;

        /// <nodoc/>
        public PipCountersByTelemetryTag ProcessPipCountersByTelemetryTag;
    }
}
