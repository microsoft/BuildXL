// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Interop
{
    /// <summary>
    /// Memory counters for process pips, representing all child processes
    /// </summary>
    public readonly struct ProcessMemoryCountersSnapshot 
    {
        /// <summary>
        /// Peak working set (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly int PeakWorkingSetMb;

        /// <summary>
        /// Current working set (in MB) considering all processes.
        /// </summary>
        public readonly int LastWorkingSetMb;

        /// <summary>
        /// Average working set (in MB) considering all processes.
        /// </summary>
        public readonly int AverageWorkingSetMb;

        /// <summary>
        /// Peak commit size (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly int PeakCommitSizeMb;

        /// <summary>
        /// Current commit size (in MB) considering all processes.
        /// </summary>
        public readonly int LastCommitSizeMb;

        /// <nodoc />
        private ProcessMemoryCountersSnapshot(
            int peakWorkingSetMb,
            int lastWorkingSetMb,
            int averageWorkingSetMb,
            int peakCommitSizeMb,
            int lastCommitSizeMb)
        {
            PeakWorkingSetMb = peakWorkingSetMb;
            LastWorkingSetMb = lastWorkingSetMb;
            AverageWorkingSetMb = averageWorkingSetMb;
            PeakCommitSizeMb = peakCommitSizeMb;
            LastCommitSizeMb = lastCommitSizeMb;
        }

        /// <summary>
        /// Create one with memory counters in bytes
        /// </summary>
        public static ProcessMemoryCountersSnapshot CreateFromBytes(
            ulong peakWorkingSet,
            ulong lastWorkingSet,
            ulong averageWorkingSet,
            ulong peakCommitSize,
            ulong lastCommitSize)
        {
            return new ProcessMemoryCountersSnapshot(
                ToMegabytes(peakWorkingSet),
                ToMegabytes(lastWorkingSet),
                ToMegabytes(averageWorkingSet),
                ToMegabytes(peakCommitSize),
                ToMegabytes(lastCommitSize));
        }

        /// <summary>
        /// Create one with memory counters in MB
        /// </summary>
        public static ProcessMemoryCountersSnapshot CreateFromMB(
            int peakWorkingSetMb,
            int lastWorkingSetMb,
            int averageWorkingSetMb,
            int peakCommitSizeMb,
            int lastCommitSizeMb)
        {
            return new ProcessMemoryCountersSnapshot(
                peakWorkingSetMb,
                lastWorkingSetMb,
                averageWorkingSetMb,
                peakCommitSizeMb,
                lastCommitSizeMb);
        }

        private static int ToMegabytes(ulong bytes)
        {
            return (int)(bytes / ((ulong)1024 * 1024));
        }
    }
}
