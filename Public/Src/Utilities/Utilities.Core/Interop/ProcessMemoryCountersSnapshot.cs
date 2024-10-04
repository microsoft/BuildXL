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

        /// <nodoc />
        private ProcessMemoryCountersSnapshot(
            int peakWorkingSetMb,
            int lastWorkingSetMb,
            int averageWorkingSetMb)
        {
            PeakWorkingSetMb = peakWorkingSetMb;
            LastWorkingSetMb = lastWorkingSetMb;
            AverageWorkingSetMb = averageWorkingSetMb;
        }

        /// <summary>
        /// Create one with memory counters in bytes
        /// </summary>
        public static ProcessMemoryCountersSnapshot CreateFromBytes(
            ulong peakWorkingSet,
            ulong lastWorkingSet,
            ulong averageWorkingSet)
        {
            return new ProcessMemoryCountersSnapshot(
                ToMegabytes(peakWorkingSet),
                ToMegabytes(lastWorkingSet),
                ToMegabytes(averageWorkingSet));
        }

        /// <summary>
        /// Create one with memory counters in MB
        /// </summary>
        public static ProcessMemoryCountersSnapshot CreateFromMB(
            int peakWorkingSetMb,
            int lastWorkingSetMb,
            int averageWorkingSetMb)
        {
            return new ProcessMemoryCountersSnapshot(
                peakWorkingSetMb,
                lastWorkingSetMb,
                averageWorkingSetMb);
        }

        private static int ToMegabytes(ulong bytes)
        {
            return (int)(bytes / ((ulong)1024 * 1024));
        }
    }
}
