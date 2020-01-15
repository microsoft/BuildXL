// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Memory counters for process pips, representing all child processes
    /// </summary>
    public readonly struct ProcessMemoryCounters : IEquatable<ProcessMemoryCounters>
    {
        /// <summary>
        /// Peak working set (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly int PeakWorkingSetMb;

        /// <summary>
        /// Peak commit usage (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly int PeakCommitUsageMb;

        /// <summary>
        /// Peak virtual memory usage (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly int PeakVirtualMemoryUsageMb;

        /// <nodoc />
        private ProcessMemoryCounters(
            int peakVirtualMemoryUsageMb,
            int peakWorkingSetMb,
            int peakCommitUsageMb)
        {
            PeakVirtualMemoryUsageMb = peakVirtualMemoryUsageMb;
            PeakWorkingSetMb = peakWorkingSetMb;
            PeakCommitUsageMb = peakCommitUsageMb;
        }

        /// <summary>
        /// Create one with memory counters in bytes
        /// </summary>
        public static ProcessMemoryCounters CreateFromBytes(
            ulong peakVirtualMemoryUsage,
            ulong peakWorkingSet,
            ulong peakCommitUsage)
        {
            return new ProcessMemoryCounters(
                (int)ByteSizeFormatter.ToMegabytes(peakVirtualMemoryUsage),
                (int)ByteSizeFormatter.ToMegabytes(peakWorkingSet),
                (int)ByteSizeFormatter.ToMegabytes(peakCommitUsage));
        }

        /// <summary>
        /// Create one with memory counters in megabytes
        /// </summary>
        public static ProcessMemoryCounters CreateFromMb(
            int peakVirtualMemoryUsageMb,
            int peakWorkingSetMb,
            int peakCommitUsageMb)
        {
            return new ProcessMemoryCounters(
                peakVirtualMemoryUsageMb,
                peakWorkingSetMb,
                peakCommitUsageMb);
        }
        
        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(PeakVirtualMemoryUsageMb);
            writer.Write(PeakWorkingSetMb);
            writer.Write(PeakCommitUsageMb);
        }

        /// <nodoc />
        public static ProcessMemoryCounters Deserialize(BuildXLReader reader)
        {
            int peakVirtualMemoryUsageMb = reader.ReadInt32();
            int peakWorkingSetMb = reader.ReadInt32();
            int peakCommitUsageMb = reader.ReadInt32();

            return new ProcessMemoryCounters(peakVirtualMemoryUsageMb, peakWorkingSetMb, peakCommitUsageMb);
        }


        /// <inherit />
        public override int GetHashCode()
        {
            unchecked
            {
                return HashCodeHelper.Combine(
                    PeakVirtualMemoryUsageMb,
                    PeakWorkingSetMb,
                    PeakCommitUsageMb);
            }
        }

        /// <inherit />
        public bool Equals(ProcessMemoryCounters other)
        {
            return PeakVirtualMemoryUsageMb == other.PeakVirtualMemoryUsageMb &&
                    PeakWorkingSetMb == other.PeakWorkingSetMb &&
                    PeakCommitUsageMb == other.PeakCommitUsageMb;
        }

        /// <inherit />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Checks whether two PipHistoricPerfData structures are the same.
        /// </summary>
        public static bool operator ==(ProcessMemoryCounters left, ProcessMemoryCounters right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks whether two PipHistoricPerfData structures are different.
        /// </summary>
        public static bool operator !=(ProcessMemoryCounters left, ProcessMemoryCounters right)
        {
            return !left.Equals(right);
        }
    }
}
