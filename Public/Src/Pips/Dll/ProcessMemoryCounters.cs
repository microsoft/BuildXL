// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Memory counters for process pips, representing all child processes
    /// </summary>
    public readonly struct ProcessMemoryCounters
    {
        /// <summary>
        /// Peak working set (in bytes) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly ulong PeakWorkingSet;

        /// <summary>
        /// <see cref="PeakWorkingSet"/> in megabytes
        /// </summary>
        public int PeakWorkingSetMb => (int)(PeakWorkingSet / (1024 * 1024));

        /// <summary>
        /// Peak working set (in bytes) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly ulong PeakPagefileUsage;

        /// <summary>
        /// <see cref="PeakPagefileUsage"/> in megabytes
        /// </summary>
        public int PeakPagefileUsageMb => (int)(PeakPagefileUsage / (1024 * 1024));

        /// <summary>
        /// Peak memory usage (in bytes) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly ulong PeakVirtualMemoryUsage;

        /// <summary>
        /// <see cref="PeakVirtualMemoryUsage"/> in megabytes
        /// </summary>
        public int PeakVirtualMemoryUsageMb => (int)(PeakVirtualMemoryUsage / (1024 * 1024));

        /// <nodoc />
        public ProcessMemoryCounters(
            ulong peakVirtualMemoryUsage,
            ulong peakWorkingSet,
            ulong peakPagefileUsage)
        {
            PeakVirtualMemoryUsage = peakVirtualMemoryUsage;
            PeakWorkingSet = peakWorkingSet;
            PeakPagefileUsage = peakPagefileUsage;
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(PeakVirtualMemoryUsage);
            writer.Write(PeakWorkingSet);
            writer.Write(PeakPagefileUsage);
        }

        /// <nodoc />
        public static ProcessMemoryCounters Deserialize(BuildXLReader reader)
        {
            ulong peakVirtualMemoryUsage = reader.ReadUInt64();
            ulong peakWorkingSet = reader.ReadUInt64();
            ulong peakPagefileUsage = reader.ReadUInt64();

            return new ProcessMemoryCounters(peakVirtualMemoryUsage, peakWorkingSet, peakPagefileUsage);
        }
    }
}
