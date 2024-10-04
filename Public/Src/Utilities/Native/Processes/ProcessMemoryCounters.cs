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
        /// Peak working set (in bytes) considering all processes (highest point-in-time sum of the memory usage of the process tree).
        /// </summary>
        public readonly ulong PeakWorkingSet;

        /// <summary>
        /// Average working set (in bytes) considering all processes.
        /// </summary>
        public readonly ulong AverageWorkingSet;

        /// <nodoc />
        private ProcessMemoryCounters(
            ulong peakWorkingSet,
            ulong averageWorkingSet)
        {
            PeakWorkingSet = peakWorkingSet;
            AverageWorkingSet = averageWorkingSet;
        }

        /// <summary>
        /// Create one with memory counters in bytes
        /// </summary>
        public static ProcessMemoryCounters CreateFromBytes(
            ulong peakWorkingSet,
            ulong averageWorkingSet) => new(peakWorkingSet, averageWorkingSet);

        /// <summary>
        /// Create one with memory counters in megabytes
        /// </summary>
        public static ProcessMemoryCounters CreateFromMb(
            int peakWorkingSetMb,
            int averageWorkingSetMb) => new(
                ByteSizeFormatter.MegabytesToBytes((ulong)peakWorkingSetMb),
                ByteSizeFormatter.MegabytesToBytes((ulong)averageWorkingSetMb));

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(PeakWorkingSet);
            writer.Write(AverageWorkingSet);
        }

        /// <nodoc />
        public static ProcessMemoryCounters Deserialize(BuildXLReader reader)
        {
            ulong peakWorkingSet = reader.ReadUInt64();
            ulong averageWorkingSet = reader.ReadUInt64();

            return new ProcessMemoryCounters(peakWorkingSet, averageWorkingSet);
        }


        /// <inherit />
        public override int GetHashCode()
        {
            unchecked
            {
                return (int)HashCodeHelper.Combine(
                    (long)PeakWorkingSet,
                    (long)AverageWorkingSet);
            }
        }

        /// <inherit />
        public bool Equals(ProcessMemoryCounters other) =>
            PeakWorkingSet == other.PeakWorkingSet
            && AverageWorkingSet == other.AverageWorkingSet;

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
