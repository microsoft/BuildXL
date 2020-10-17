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
        /// Average working set (in MB) considering all processes.
        /// </summary>
        public readonly int AverageWorkingSetMb;

        /// <summary>
        /// Peak commit size (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly int PeakCommitSizeMb;

        /// <summary>
        /// Average commit size (in MB) considering all processes.
        /// </summary>
        public readonly int AverageCommitSizeMb;

        /// <nodoc />
        private ProcessMemoryCounters(
            int peakWorkingSetMb,
            int averageWorkingSetMb,
            int peakCommitSizeMb,
            int averageCommitSizeMb)
        {
            PeakWorkingSetMb = peakWorkingSetMb;
            AverageWorkingSetMb = averageWorkingSetMb;
            PeakCommitSizeMb = peakCommitSizeMb;
            AverageCommitSizeMb = averageCommitSizeMb;
        }

        /// <summary>
        /// Create one with memory counters in bytes
        /// </summary>
        public static ProcessMemoryCounters CreateFromBytes(
            ulong peakWorkingSet,
            ulong averageWorkingSet,
            ulong peakCommitSize,
            ulong averageCommitSize)
        {
            return new ProcessMemoryCounters(
                (int)ByteSizeFormatter.ToMegabytes(peakWorkingSet),
                (int)ByteSizeFormatter.ToMegabytes(averageWorkingSet),
                (int)ByteSizeFormatter.ToMegabytes(peakCommitSize),
                (int)ByteSizeFormatter.ToMegabytes(averageCommitSize));
        }

        /// <summary>
        /// Create one with memory counters in megabytes
        /// </summary>
        public static ProcessMemoryCounters CreateFromMb(
            int peakWorkingSetMb,
            int averageWorkingSetMb,
            int peakCommitSizeMb,
            int averageCommitSizeMb)
        {
            return new ProcessMemoryCounters(
                peakWorkingSetMb,
                averageWorkingSetMb,
                peakCommitSizeMb,
                averageCommitSizeMb);
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(PeakWorkingSetMb);
            writer.Write(AverageWorkingSetMb);
            writer.Write(PeakCommitSizeMb);
            writer.Write(AverageCommitSizeMb);
        }

        /// <nodoc />
        public static ProcessMemoryCounters Deserialize(BuildXLReader reader)
        {
            int peakWorkingSetMb = reader.ReadInt32();
            int averageWorkingSetMb = reader.ReadInt32();
            int peakCommitSizeMb = reader.ReadInt32();
            int averageCommitSizeMb = reader.ReadInt32();

            return new ProcessMemoryCounters(peakWorkingSetMb, averageWorkingSetMb, peakCommitSizeMb, averageCommitSizeMb);
        }


        /// <inherit />
        public override int GetHashCode()
        {
            unchecked
            {
                return HashCodeHelper.Combine(
                    PeakWorkingSetMb,
                    AverageWorkingSetMb,
                    PeakCommitSizeMb,
                    AverageCommitSizeMb);
            }
        }

        /// <inherit />
        public bool Equals(ProcessMemoryCounters other)
        {
            return PeakWorkingSetMb == other.PeakWorkingSetMb &&
                    AverageWorkingSetMb == other.AverageWorkingSetMb &&
                    PeakCommitSizeMb == other.PeakCommitSizeMb &&
                    AverageCommitSizeMb == other.AverageCommitSizeMb;
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
