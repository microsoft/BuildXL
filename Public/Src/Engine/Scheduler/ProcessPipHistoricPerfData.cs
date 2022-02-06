// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// This class stores historic performance averages of process pips
    /// </summary>
    public readonly struct ProcessPipHistoricPerfData : IEquatable<ProcessPipHistoricPerfData>
    {
        /// <summary>
        /// Empty historic perf data
        /// </summary>
        public static ProcessPipHistoricPerfData Empty = default(ProcessPipHistoricPerfData);

        /// <summary>
        /// Default time to live
        /// </summary>
        public const byte DefaultTimeToLive = 255; // more than 2 weeks with 20 builds a day.

        private readonly byte m_entryTimeToLive;

        /// <summary>
        /// Processor used in % (150 means one processor fully used and the other half used)
        /// </summary>
        public readonly ushort ProcessorsInPercents;

        /// <summary>
        /// The average exe duration
        /// </summary>
        public readonly uint ExeDurationInMs;

        /// <summary>
        /// The max exe duration observed
        /// </summary>
        public readonly uint MaxExeDurationInMs;

        /// <summary>
        /// The average pip run duration (execute, materializeinput, cachelookup, postprocess, start)
        /// </summary>
        public readonly uint RunDurationInMs;

        /// <summary>
        /// Peak memory usage counters
        /// </summary>
        public readonly ProcessMemoryCounters MemoryCounters;

        /// <summary>
        /// The amount of kilobytes read/written
        /// </summary>
        public readonly uint DiskIOInMB;

        /// <summary>
        /// Check if the structure was recently generated
        /// </summary>
        public bool IsFresh => m_entryTimeToLive == DefaultTimeToLive;

        #region Constructors

        /// <summary>
        /// Construct a new runtime data based on collected performance data
        /// </summary>
        public ProcessPipHistoricPerfData(ProcessPipExecutionPerformance executionPerformance, long runDurationInMs)
        {
            Contract.Requires(executionPerformance.ExecutionLevel == PipExecutionLevel.Executed);

            m_entryTimeToLive = DefaultTimeToLive;
            // Deduct the suspended duration from the process execution time.
            ExeDurationInMs = (uint)Math.Min(uint.MaxValue, Math.Max(1, executionPerformance.ProcessExecutionTime.TotalMilliseconds - executionPerformance.SuspendedDurationMs));
            MaxExeDurationInMs = ExeDurationInMs;
            RunDurationInMs = (uint)Math.Min(uint.MaxValue, Math.Max(1, runDurationInMs));
            // For historical ram usage, we record the peak working set instead of the virtual memory due to the precision.
            MemoryCounters = executionPerformance.MemoryCounters;
            DiskIOInMB = (uint)Math.Min(uint.MaxValue, ByteSizeFormatter.ToMegabytes(executionPerformance.IO.GetAggregateIO().TransferCount));
            ProcessorsInPercents = executionPerformance.ProcessorsInPercents;
        }

        private ProcessPipHistoricPerfData(byte timeToLive, uint exeDurationInMs, uint maxExeDurationInMs, uint runDurationInMs, ProcessMemoryCounters memoryCounters, ushort processorsInPercents, uint diskIOInMB)
        {
            m_entryTimeToLive = timeToLive;
            ExeDurationInMs = exeDurationInMs;
            MaxExeDurationInMs = maxExeDurationInMs;
            RunDurationInMs = runDurationInMs;
            MemoryCounters = memoryCounters;
            ProcessorsInPercents = processorsInPercents;
            DiskIOInMB = diskIOInMB;
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Save the data to a file
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.Write(m_entryTimeToLive);
            writer.Write(ExeDurationInMs);
            writer.Write(MaxExeDurationInMs);
            writer.Write(RunDurationInMs);
            MemoryCounters.Serialize(writer);
            writer.Write(ProcessorsInPercents);
            writer.Write(DiskIOInMB);
        }

        /// <summary>
        /// Read the data from a file
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static bool Deserialize(BuildXLReader reader, out ProcessPipHistoricPerfData result)
        {
            Contract.Requires(reader != null);
            byte timeToLive = reader.ReadByte();
            byte newTimeToLive = (byte)(timeToLive - 1);
            uint exeDurationMs = reader.ReadUInt32();
            uint maxExeDurationMs = reader.ReadUInt32();
            uint runDurationMs = reader.ReadUInt32();
            ProcessMemoryCounters memoryCounters = ProcessMemoryCounters.Deserialize(reader);
            ushort processorsInPercents = reader.ReadUInt16();
            uint diskIOInMB = reader.ReadUInt32();

            result = new ProcessPipHistoricPerfData(
                newTimeToLive,
                exeDurationMs,
                maxExeDurationMs,
                runDurationMs,
                memoryCounters,
                processorsInPercents,
                diskIOInMB);

            return newTimeToLive > 0;
        }

        #endregion

        #region Equality/hash

        /// <inherit />
        public override int GetHashCode()
        {
            unchecked
            {
                return HashCodeHelper.Combine(
                    (int)m_entryTimeToLive,
                    (int)ExeDurationInMs,
                    (int)MaxExeDurationInMs,
                    (int)RunDurationInMs,
                    MemoryCounters.GetHashCode(),
                    (int)ProcessorsInPercents,
                    (int)DiskIOInMB);
            }
        }

        /// <inherit />
        public bool Equals(ProcessPipHistoricPerfData other)
        {
            return m_entryTimeToLive == other.m_entryTimeToLive &&
                    ExeDurationInMs == other.ExeDurationInMs &&
                    MaxExeDurationInMs == other.MaxExeDurationInMs &&
                    RunDurationInMs == other.RunDurationInMs &&
                    MemoryCounters == other.MemoryCounters &&
                    ProcessorsInPercents == other.ProcessorsInPercents &&
                    DiskIOInMB == other.DiskIOInMB;
        }

        /// <inherit />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Checks whether two PipHistoricPerfData structures are the same.
        /// </summary>
        public static bool operator ==(ProcessPipHistoricPerfData left, ProcessPipHistoricPerfData right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks whether two PipHistoricPerfData structures are different.
        /// </summary>
        public static bool operator !=(ProcessPipHistoricPerfData left, ProcessPipHistoricPerfData right)
        {
            return !left.Equals(right);
        }

        #endregion

        /// <summary>
        /// Reset the time-to-live counter to the fresh state.
        /// </summary>
        public ProcessPipHistoricPerfData MakeFresh()
        {
            return new ProcessPipHistoricPerfData(DefaultTimeToLive, ExeDurationInMs, MaxExeDurationInMs, RunDurationInMs, MemoryCounters, ProcessorsInPercents, DiskIOInMB);
        }

        /// <summary>
        /// Merge the old and new results using asymmetric exponential moving average.
        /// See http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
        /// </summary>
        public ProcessPipHistoricPerfData Merge(ProcessPipHistoricPerfData other)
        {
            var exeDurationResult = GetMergeResult(ExeDurationInMs, other.ExeDurationInMs);
            var runDurationResult = GetMergeResult(RunDurationInMs, other.RunDurationInMs);
            var memoryCountersResult = Merge(MemoryCounters, other.MemoryCounters);
            var processorInPercentResult = GetMergeResult(ProcessorsInPercents, other.ProcessorsInPercents);
            var diskIOResult = GetMergeResult(DiskIOInMB, other.DiskIOInMB);
            var maxDurationMs = Math.Max(MaxExeDurationInMs, other.MaxExeDurationInMs);

            return new ProcessPipHistoricPerfData(DefaultTimeToLive, exeDurationResult, maxDurationMs, runDurationResult, memoryCountersResult, (ushort)processorInPercentResult, diskIOResult);
        }

        internal static uint GetMergeResult(uint oldData, uint newData)
        {
            try
            {
                // An asymmetric merge that goes up fast but decreases slowly.
                if (newData > oldData)
                {
                    return (uint)((((ulong)newData) / 2) + (((ulong)oldData) / 2));
                }

                return (uint)((((ulong)oldData) * 9 / 10) + (((ulong)newData) / 10));
            }
            catch (System.OverflowException ex)
            {
                throw new BuildXLException(I($"Failed to merge historic perf data result with old '{oldData} and new {newData}' data values!"), ex);
            }
        }

        private static ProcessMemoryCounters Merge(ProcessMemoryCounters oldData, ProcessMemoryCounters newData)
        {
            return ProcessMemoryCounters.CreateFromMb(
                (int)GetMergeResult((uint)oldData.PeakWorkingSetMb, (uint)newData.PeakWorkingSetMb),
                (int)GetMergeResult((uint)oldData.AverageWorkingSetMb, (uint)newData.AverageWorkingSetMb),
                (int)GetMergeResult((uint)oldData.PeakCommitSizeMb, (uint)newData.PeakCommitSizeMb),
                (int)GetMergeResult((uint)oldData.AverageCommitSizeMb, (uint)newData.AverageCommitSizeMb));
        }
    }
}
