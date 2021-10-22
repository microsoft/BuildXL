// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>Memory counters for process pips.</summary>
    public readonly struct MemoryCounters
    {
        /// <summary>Average commit size (in MB) considering all processes.</summary>
        public readonly int AverageCommitSizeMb;

        /// <summary>Average working set (in MB) considering all processes.</summary>
        public readonly int AverageWorkingSetMb;

        /// <summary>Peak commit size (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).</summary>
        public readonly int PeakCommitSizeMb;

        /// <summary>Peak working set (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).</summary>
        public readonly int PeakWorkingSetMb;

        /// <summary>Construct IOCounters.</summary>
        public MemoryCounters(
            int avgCommit,
            int avgWorking,
            int peakCommit,
            int peakWorking)
        {
            AverageCommitSizeMb = avgCommit;
            AverageWorkingSetMb = avgWorking;
            PeakCommitSizeMb = peakCommit;
            PeakWorkingSetMb = peakWorking;
        }
    }
}
