// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// This class stores historic performance averages of process pips
    /// </summary>
    public struct PipHistoricPerfData : IEquatable<PipHistoricPerfData>
    {
        // Time to live.
        public const byte DefaultTimeToLive = 140; // 1 week with 20 builds a day.

        private byte m_entryTimeToLive;

        /// <summary>
        /// The average run duration
        /// </summary>
        public readonly uint DurationInMs;

        /// <summary>
        /// Peak memory used (in KB)
        /// </summary>
        public readonly uint PeakMemoryInKB;

        /// <summary>
        /// Processor used in % (150 means one processor fully used and the other half used)
        /// </summary>
        public readonly uint ProcessorsInPercents;

        /// <summary>
        /// The amount of kilobytes read/written
        /// </summary>
        public readonly uint DiskIOInKB;

        /// <summary>
        /// Check if the structure was recently generated
        /// </summary>
        public bool IsFresh
        {
            get { return (m_entryTimeToLive == DefaultTimeToLive); }
        }

        #region Constructors

        public PipHistoricPerfData(byte timeToLive, uint durationInMs, uint peakMemoryInKB = 1, uint processorsInPercents = 1, uint diskIOInKB = 1)
        {
            m_entryTimeToLive = timeToLive;
            DurationInMs = durationInMs;
            PeakMemoryInKB = peakMemoryInKB;
            ProcessorsInPercents = processorsInPercents;
            DiskIOInKB = diskIOInKB;
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Save the data to a file
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(m_entryTimeToLive);
            writer.Write(DurationInMs);
            writer.Write(PeakMemoryInKB);
            writer.Write(ProcessorsInPercents);
            writer.Write(DiskIOInKB);
        }

        /// <summary>
        /// Read the data from a file
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static bool Deserialize(BuildXLReader reader, out PipHistoricPerfData result)
        {
            byte timeToLive = reader.ReadByte();
            if (timeToLive <= 1)
            {
                // Time to go
                result = default(PipHistoricPerfData);
                return false;
            }

            result = new PipHistoricPerfData((byte)(timeToLive - 1), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
            return true;
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
                    (int)DurationInMs,
                    (int)PeakMemoryInKB,
                    (int)ProcessorsInPercents,
                    (int)DiskIOInKB);
            }
        }

        /// <inherit />
        public bool Equals(PipHistoricPerfData other)
        {
            return m_entryTimeToLive == other.m_entryTimeToLive &&
                    DurationInMs == other.DurationInMs &&
                    PeakMemoryInKB == other.PeakMemoryInKB &&
                    ProcessorsInPercents == other.ProcessorsInPercents &&
                    DiskIOInKB == other.DiskIOInKB;
        }

        /// <inherit />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Checks whether two PipHistoricPerfData structures are the same.
        /// </summary>
        public static bool operator ==(PipHistoricPerfData left, PipHistoricPerfData right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks whether two PipHistoricPerfData structures are different.
        /// </summary>
        public static bool operator !=(PipHistoricPerfData left, PipHistoricPerfData right)
        {
            return !left.Equals(right);
        }

        #endregion

        /// <summary>
        /// Merge the old and new results using asymmetric exponential moving average.
        /// See http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
        /// </summary>
        public PipHistoricPerfData Merge(PipHistoricPerfData other)
        {
            return new PipHistoricPerfData(
                DefaultTimeToLive,
                GetMergeResult(DurationInMs, other.DurationInMs),
                GetMergeResult(PeakMemoryInKB, other.PeakMemoryInKB),
                GetMergeResult(ProcessorsInPercents, other.ProcessorsInPercents),
                GetMergeResult(DiskIOInKB, other.DiskIOInKB)
            );
        }

        private static uint GetMergeResult(uint oldData, uint newData)
        {
            // An asymmetric merge that goes up fast but decreases slowly.
            if (newData > oldData)
            {
                return (newData + oldData) / 2;
            }
            else
            {
                return (uint)(oldData * 0.9 + newData * 0.1);
            }
        }
    }
}
