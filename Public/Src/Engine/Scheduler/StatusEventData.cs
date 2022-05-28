// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Status event data to periodically log machine resource state in the status.csv file
    /// </summary>
    public struct StatusEventData
    {
        /// <summary>
        /// Time of the usage snapshot
        /// </summary>
        public DateTime Time;

        /// <summary>
        /// Cpu usage percent
        /// </summary>
        public int CpuPercent;

        /// <summary>
        /// Disk usage percents
        /// </summary>
        public int[] DiskPercents;

        /// <summary>
        /// Disk queue depths
        /// </summary>
        public int[] DiskQueueDepths;

        /// <summary>
        /// Available Disk space in Gigabyte
        /// </summary>
        public int[] DiskAvailableSpaceGb;

        /// <summary>
        /// Ram usage percent
        /// </summary>
        public int RamPercent;

        /// <nodoc />
        public int AfterRamPercent;

        /// <summary>
        /// Ram utilization in MB
        /// </summary>
        public int RamUsedMb;

        /// <summary>
        /// Available Ram in MB
        /// </summary>
        public int RamFreeMb;

        /// <nodoc />
        public int AfterRamFreeMb;

        /// <summary>
        /// Percentage of available commit used. Note if the machine has an expandable page file, this is based on the
        /// current size not necessarily the maximum size. So even if this hits 100%, the machine may still be able to
        /// commit more as the page file expands.
        /// </summary>
        public int CommitPercent;

        /// <summary>
        /// The machine's total commit in MB
        /// </summary>
        public int CommitUsedMb;

        /// <summary>
        /// Available Commit in MB
        /// </summary>
        public int CommitFreeMb;

        /// <summary>
        /// CPU utilization of the current process
        /// </summary>
        public int ProcessCpuPercent;

        /// <summary>
        /// Working set in MB of the current process
        /// </summary>
        public int ProcessWorkingSetMB;

        /// <summary>
        /// Number of waiting items in the CPU dispatcher
        /// </summary>
        public int CpuWaiting;

        /// <summary>
        /// Number of running items in the CPU dispatcher
        /// </summary>
        public int CpuRunning;

        /// <summary>
        /// Number of running pips in the CPU dispatcher
        /// </summary>
        public int CpuRunningPips;

        /// <summary>
        /// Concurrency limit in the IP dispatcher
        /// </summary>
        public int IoCurrentMax;

        /// <summary>
        /// Number of waiting items in the IO dispatcher
        /// </summary>
        public int IoWaiting;

        /// <summary>
        /// Number of running items in the IO dispatcher
        /// </summary>
        public int IoRunning;

        /// <summary>
        /// Number of waiting items in the CacheLookup dispatcher
        /// </summary>
        public int LookupWaiting;

        /// <summary>
        /// Number of running items in the CacheLookup dispatcher
        /// </summary>
        public int LookupRunning;

        /// <summary>
        /// Number of processes running under PipExecutor
        /// </summary>
        public int RunningPipExecutorProcesses;

        /// <summary>
        /// Number of processes that are currently running in remote agents.
        /// </summary>
        public int RunningRemotelyPipExecutorProcesses;

        /// <summary>
        /// Number of processes that are currently running in locally in the presence of remoting capability.
        /// </summary>
        public int RunningLocallyPipExecutorProcesses;

        /// <summary>
        /// Number of processes that have run in remote agents.
        /// </summary>
        public int TotalRunRemotelyProcesses;

        /// <summary>
        /// Number of processes that have run locally in the presence of remoting capability.
        /// </summary>
        public int TotalRunLocallyProcesses;

        /// <summary>
        /// Number of OS processes physically running (doesn't include children processes, just the main pip process).
        /// </summary>
        public int RunningProcesses;

        /// <summary>
        /// Number of pips succeeded for each type
        /// </summary>
        public long[] PipsSucceededAllTypes;

        /// <summary>
        /// LimitingResource heuristic during the sample
        /// </summary>
        public ExecutionSampler.LimitingResource LimitingResource;

        /// <summary>
        /// Factor of how much less frequently the status update time gets compared to what is expected. A value of 1 means
        /// it is fired exactly at the expected rate. 2 means it is trigged twice as slowly as expected. Etc.
        /// </summary>
        public int UnresponsivenessFactor;

        /// <summary>
        /// Number of process pips that have not completed yet
        /// </summary>
        public long ProcessPipsPending;

        /// <summary>
        /// Number of process pips allocated a slot on workers (including localworker)
        /// </summary>
        public long ProcessPipsAllocatedSlots;
    }
}
