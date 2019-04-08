// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Collects samples of data while the scheduler is executing pips
    /// </summary>
    public sealed class ExecutionSampler
    {
        private readonly long m_maxProcessPips;
        private DateTime m_lastSnapshotUtc = DateTime.MinValue;
        private readonly bool m_isDistributed;

        // Used to prevent a caller from fetching data while it is being updated
        private readonly object m_lock = new object();

        /// <summary>
        /// Creates an ExecutionSampler
        /// </summary>
        public ExecutionSampler(bool isDistributed, long maxProcessPips)
        {
            m_isDistributed = isDistributed;
            m_maxProcessPips = maxProcessPips;
        }

        /// <summary>
        /// Updates the limiting resource based on observed state
        /// </summary>
        /// <param name="aggregator">Performance Aggregator</param>
        /// <param name="readyProcessPips">Process pips whose graph dependencies have been satisfied but are not currently executing</param>
        /// <param name="executinProcessPips">Number of process pips that are currently executing</param>
        /// <param name="lastLimitingResource">The most recent limiting worker resource</param>
        internal LimitingResource OnPerfSample(PerformanceCollector.Aggregator aggregator, long readyProcessPips, long executinProcessPips, WorkerResource? lastLimitingResource)
        {
            if (m_lastSnapshotUtc == DateTime.MinValue)
            {
                // We don't have a window, so don't collect this sample and just remember when the next window starts
                m_lastSnapshotUtc = DateTime.UtcNow;
                return LimitingResource.Other;
            }

            LimitingResource limitingResource = DetermineLimitingResource(aggregator, readyProcessPips, executinProcessPips, lastLimitingResource);
            UpdateAggregations(limitingResource);

            return limitingResource;
        }

        /// <summary>
        /// Determines what build execution is being limited by for the sample period
        /// </summary>
        private LimitingResource DetermineLimitingResource(PerformanceCollector.Aggregator aggregator, long readyProcessPips, long executingProcessPips, WorkerResource? workerResource)
        {
            // Determining the heuristic on distributed builds requires some more thought. For now just bucket them as Other
            // to keep from showing possibly incorrect data
            if (m_isDistributed)
            {
                return LimitingResource.Other;
            }

            // High CPU trumps all other factors
            if (aggregator.MachineCpu.Latest > 95)
            {
                return LimitingResource.CPU;
            }

            // Next up is low available memory. Getting too low will cause memory paging to disk which is very bad, but
            // it will also cause more cycles to be spent in the GC and limit the effectiveness of filesystem caching.
            // Hence the number is set to a few hundred MB instead of zero
            if (aggregator.MachineAvailablePhysicalMB.Latest < 300)
            {
                return LimitingResource.Memory;
            }

            // The scheduler has backed off on executing additional process pips because of projected memory usage,
            // even though the graph and concurrency configuration would allow it
            if (workerResource.HasValue && workerResource.Value == WorkerResource.AvailableMemoryMb)
            {
                return LimitingResource.ProjectedMemory;
            }

            // Some other user configured semaphore is preventing the scheduler from launching additional processes.
            if (workerResource.HasValue &&
                workerResource.Value != WorkerResource.AvailableMemoryMb &&
                workerResource.Value != WorkerResource.AvailableProcessSlots &&
                workerResource.Value != WorkerResource.ResourcesAvailable &&
                workerResource.Value != WorkerResource.Status &&
                workerResource.Value != WorkerResource.TotalProcessSlots)
            {
                return LimitingResource.Semaphore;
            }

            // Next we look for any disk with a relatively high percentage of active time
            foreach (var disk in aggregator.DiskStats)
            {
                if (disk.CalculateActiveTime(lastOnly: true) > 95)
                {
                    return LimitingResource.Disk;
                }
            }

            // Then we look for low-ish available ready pips. This isn't zero because we are sampling and we might
            // just hit a sample where the queue wasn't completely drained. The number 3 isn't very scientific
            if (readyProcessPips < 3)
            {
                return LimitingResource.GraphShape;
            }

            // If the number of running pips is what the queue maximum is, and no machine resources are constrained, the
            // pips are probably contending with themselves. There may be headroom to add more pips
            if (((1.0 * executingProcessPips) / m_maxProcessPips) > .95)
            {
                return LimitingResource.ConcurrencyLimit;
            }

            // We really don't expect to fall through to this case. But track it separately so we know if the heuristic
            // needs to be updated.
            // DEBUGGING ONLY
            // Console.WriteLine("CPU:{0}, AvailableMB:{1}, ReadyPips:{2}, RunningPips:{3}", aggregator.MachineCpu.Latest, aggregator.MachineAvailablePhysicalMB.Latest, readyPips, runningPips);
            // Console.WriteLine();
            return LimitingResource.Other;
        }

        private void UpdateAggregations(LimitingResource limitingResource)
        {
            lock (m_lock)
            {
                int time = (int)(DateTime.UtcNow - m_lastSnapshotUtc).TotalMilliseconds;
                m_lastSnapshotUtc = DateTime.UtcNow;

                switch (limitingResource)
                {
                    case LimitingResource.GraphShape:
                        m_blockedOnGraphMs += time;
                        break;
                    case LimitingResource.CPU:
                        m_blockedOnCpuMs += time;
                        break;
                    case LimitingResource.Disk:
                        m_blockedOnDiskMs += time;
                        break;
                    case LimitingResource.Memory:
                        m_blockedOnMemoryMs += time;
                        break;
                    case LimitingResource.ProjectedMemory:
                        m_blockedOnProjectedMemoryMs += time;
                        break;
                    case LimitingResource.Semaphore:
                        m_blockedOnSemaphoreMs += time;
                        break;
                    case LimitingResource.ConcurrencyLimit:
                        m_blockedOnPipSynchronization += time;
                        break;
                    case LimitingResource.Other:
                        m_blockedOnUnknownMs += time;
                        break;
                    default:
                        Contract.Assert(false, "Unexpected LimitingResource:" + limitingResource.ToString());
                        throw new NotImplementedException("Unexpected Limiting Resource:" + limitingResource.ToString());
                }
            }
        }

        /// <summary>
        /// Resource limiting execution, based on heuristic
        /// </summary>
        public enum LimitingResource
        {
            /// <summary>
            /// Not enough concurrency in the build to run more pips
            /// </summary>
            GraphShape,

            /// <summary>
            /// Not enough CPU cores to run more pips
            /// </summary>
            CPU,

            /// <summary>
            /// Disk access appears to be slowing pips down
            /// </summary>
            Disk,

            /// <summary>
            /// Available memory is low
            /// </summary>
            Memory,

            /// <summary>
            /// CPU and Disk are not maxed out even though many pips are being run concurrently. The pips may be
            /// synchronizing internally
            /// </summary>
            ConcurrencyLimit,

            /// <summary>
            /// The scheduler throttling because it projects that launching an additional process would exhaust
            /// available RAM
            /// </summary>
            ProjectedMemory,

            /// <summary>
            /// A user configured semaphore is limiting concurrency
            /// </summary>
            Semaphore,

            /// <summary>
            /// Don't know what the limiting factor is
            /// </summary>
            Other,
        }

        /// <nodoc/>
        public sealed class LimitingResourcePercentages
        {
            /// <nodoc/>
            public int GraphShape;

            /// <nodoc/>
            public int CPU;

            /// <nodoc/>
            public int Disk;

            /// <nodoc/>
            public int Memory;

            /// <nodoc/>
            public int ConcurrencyLimit;

            /// <nodoc/>
            public int ProjectedMemory;

            /// <nodoc/>
            public int Semaphore;

            /// <nodoc/>
            public int Other;
        }

        private int m_blockedOnGraphMs = 0;
        private int m_blockedOnCpuMs = 0;
        private int m_blockedOnDiskMs = 0;
        private int m_blockedOnMemoryMs = 0;
        private int m_blockedOnPipSynchronization = 0;
        private int m_blockedOnProjectedMemoryMs = 0;
        private int m_blockedOnSemaphoreMs = 0;
        private int m_blockedOnUnknownMs = 0;

        private int GetPercentage(int numerator)
        {
            int totalTime = m_blockedOnGraphMs + m_blockedOnCpuMs + m_blockedOnDiskMs + m_blockedOnMemoryMs + m_blockedOnPipSynchronization + m_blockedOnProjectedMemoryMs + m_blockedOnSemaphoreMs + m_blockedOnUnknownMs;
            if (totalTime > 0)
            {
                // We intentionally return the floor to make sure we don't add up to more than 100
                return (int)((double)numerator / totalTime * 100);
            }

            return 0;
        }

        /// <summary>
        /// Gets the percentage of time blocked on various resources
        /// </summary>
        public LimitingResourcePercentages GetLimitingResourcePercentages()
        {
            lock (m_lock)
            {
                var result = new LimitingResourcePercentages()
                {
                    GraphShape = GetPercentage(m_blockedOnGraphMs),
                    CPU = GetPercentage(m_blockedOnCpuMs),
                    Disk = GetPercentage(m_blockedOnDiskMs),
                    Memory = GetPercentage(m_blockedOnMemoryMs),
                    ConcurrencyLimit = GetPercentage(m_blockedOnPipSynchronization),
                };

                // It's possible these percentages don't add up to 100%. So we'll round everything down
                // and use "Other" as our fudge factor to make sure we add up to 100.
                result.Other = 100 - result.GraphShape - result.CPU - result.Disk - result.Memory - result.ConcurrencyLimit;
                return result;
            }
        }
    }
}
