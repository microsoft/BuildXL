// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;

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
        public readonly int PeakWorkingSetMb => unchecked((int)ByteSizeFormatter.BytesToMegabytes(PeakWorkingSet));

        /// <summary>
        /// Average working set (in MB) considering all processes.
        /// </summary>
        public readonly int AverageWorkingSetMb => unchecked((int)ByteSizeFormatter.BytesToMegabytes(AverageWorkingSet));

        /// <summary>
        /// Peak commit size (in MB) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly int PeakCommitSizeMb => unchecked((int)ByteSizeFormatter.BytesToMegabytes(PeakCommitSize));

        /// <summary>
        /// Average commit size (in MB) considering all processes.
        /// </summary>
        public readonly int AverageCommitSizeMb => unchecked((int)ByteSizeFormatter.BytesToMegabytes(AverageCommitSize));

        /// <summary>
        /// Peak working set (in bytes) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly ulong PeakWorkingSet;

        /// <summary>
        /// Average working set (in bytes) considering all processes.
        /// </summary>
        public readonly ulong AverageWorkingSet;

        /// <summary>
        /// Peak commit size (in bytes) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly ulong PeakCommitSize;

        /// <summary>
        /// Average commit size (in bytes) considering all processes.
        /// </summary>
        public readonly ulong AverageCommitSize;

        /// <nodoc />
        private ProcessMemoryCounters(
            ulong peakWorkingSet,
            ulong averageWorkingSet,
            ulong peakCommitSize,
            ulong averageCommitSize)
        {
            PeakWorkingSet = peakWorkingSet;
            AverageWorkingSet = averageWorkingSet;
            PeakCommitSize = peakCommitSize;
            AverageCommitSize = averageCommitSize;
        }

        /// <summary>
        /// Create one with memory counters in bytes
        /// </summary>
        public static ProcessMemoryCounters CreateFromBytes(
            ulong peakWorkingSet,
            ulong averageWorkingSet,
            ulong peakCommitSize,
            ulong averageCommitSize) => new(peakWorkingSet, averageWorkingSet, peakCommitSize, averageCommitSize);

        /// <summary>
        /// Create one with memory counters in megabytes
        /// </summary>
        public static ProcessMemoryCounters CreateFromMb(
            int peakWorkingSetMb,
            int averageWorkingSetMb,
            int peakCommitSizeMb,
            int averageCommitSizeMb) => new(
                ByteSizeFormatter.MegabytesToBytes((ulong)peakWorkingSetMb),
                ByteSizeFormatter.MegabytesToBytes((ulong)averageWorkingSetMb),
                ByteSizeFormatter.MegabytesToBytes((ulong)peakCommitSizeMb),
                ByteSizeFormatter.MegabytesToBytes((ulong)averageCommitSizeMb));

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(PeakWorkingSet);
            writer.Write(AverageWorkingSet);
            writer.Write(PeakCommitSize);
            writer.Write(AverageCommitSize);
        }

        /// <nodoc />
        public static ProcessMemoryCounters Deserialize(BuildXLReader reader)
        {
            ulong peakWorkingSet = reader.ReadUInt64();
            ulong averageWorkingSet = reader.ReadUInt64();
            ulong peakCommitSize = reader.ReadUInt64();
            ulong averageCommitSize = reader.ReadUInt64();

            return new ProcessMemoryCounters(peakWorkingSet, averageWorkingSet, peakCommitSize, averageCommitSize);
        }


        /// <inherit />
        public override int GetHashCode()
        {
            unchecked
            {
                return (int)HashCodeHelper.Combine(
                    (long)PeakWorkingSet,
                    (long)AverageWorkingSet,
                    (long)PeakCommitSize,
                    (long)AverageCommitSize);
            }
        }

        /// <inherit />
        public bool Equals(ProcessMemoryCounters other) =>
            PeakWorkingSet == other.PeakWorkingSet
            && AverageWorkingSet == other.AverageWorkingSet
            && PeakCommitSize == other.PeakCommitSize
            && AverageCommitSize == other.AverageCommitSize;

        /// <inherit />
        public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

        /// <summary>
        /// Checks whether two PipHistoricPerfData structures are the same.
        /// </summary>
        public static bool operator ==(ProcessMemoryCounters left, ProcessMemoryCounters right) => left.Equals(right);

        /// <summary>
        /// Checks whether two PipHistoricPerfData structures are different.
        /// </summary>
        public static bool operator !=(ProcessMemoryCounters left, ProcessMemoryCounters right) => !left.Equals(right);
    }
}
