// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// Represents a history of table size statistics (<see cref="HistoricDataPoint"/>) in chronological order.
    ///
    /// The first element in this list (i.e., at position 0) is the oldest entry; the last element is the most recent entry.
    /// </summary>
    public sealed class HistoricTableSizes : IReadOnlyList<HistoricDataPoint>
    {
        [NotNull]
        private readonly HistoricDataPoint[] m_historicData;

        private HistoricTableSizes([NotNull] HistoricDataPoint[] historicData)
        {
            m_historicData = historicData;
        }

        /// <nodoc/>
        public HistoricTableSizes([NotNull] IEnumerable<HistoricDataPoint> historicData)
            : this(historicData.ToArray()) { }

        /// <inheritdoc/>
        public int Count => m_historicData.Length;

        /// <inheritdoc/>
        public HistoricDataPoint this[int index] => m_historicData[index];

        /// <inheritdoc/>
        public IEnumerator<HistoricDataPoint> GetEnumerator()
        {
            return ((IReadOnlyList<HistoricDataPoint>)m_historicData).GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Creates a new instance of <see cref="HistoricTableSizes"/> by concatenating
        /// <paramref name="newDataPoint"/> to historic data points in this instance.
        /// </summary>
        public HistoricTableSizes ConcatDataPoint(in HistoricDataPoint newDataPoint)
        {
            var newHistoricData = new HistoricDataPoint[m_historicData.Length + 1];
            Array.Copy(m_historicData, newHistoricData, m_historicData.Length);
            newHistoricData[m_historicData.Length] = newDataPoint;
            return new HistoricTableSizes(newHistoricData);
        }

        /// <summary>
        /// Serializes this object to a given writer.
        /// <seealso cref="HistoricDataPoint.Serialize(BuildXLWriter)"/>.
        /// </summary>
        public void Serialize([NotNull] BuildXLWriter writer)
        {
            writer.Write(m_historicData, (w, item) => item.Serialize(w));
        }

        /// <summary>
        /// Deserializes an object of this type from a given reader.
        /// <seealso cref="HistoricDataPoint.Deserialize(BuildXLReader)"/>.
        /// </summary>
        public static HistoricTableSizes Deserialize([NotNull] BuildXLReader reader)
        {
            return new HistoricTableSizes(reader.ReadArray(HistoricDataPoint.Deserialize));
        }
    }

    /// <summary>
    /// Struct to keep together historic statistics about various table.
    /// Currently, contains statistics for <see cref="PathTable"/>, <see cref="SymbolTable"/>, and <see cref="StringTable"/>.
    /// </summary>
    public readonly struct HistoricDataPoint : IEquatable<HistoricDataPoint>
    {
        /// <nodoc/>
        public TableStats PathTableStats { get; }

        /// <nodoc/>
        public TableStats SymbolTableStats { get; }

        /// <nodoc/>
        public TableStats StringTableStats { get; }

        /// <summary>
        /// All <see cref="TableStats"/> objects contained in this historic data point.
        /// </summary>
        public IReadOnlyCollection<TableStats> AllTableStats => new[] { PathTableStats, StringTableStats, SymbolTableStats };

        /// <summary>
        /// Sum of all table sizes in bytes.
        /// </summary>
        public long TotalSizeInBytes() => AllTableStats.Sum(s => s.SizeInBytes);

        /// <nodoc/>
        public HistoricDataPoint(TableStats pathTableStats, TableStats symbolTableStats, TableStats stringTableStats)
        {
            PathTableStats = pathTableStats;
            SymbolTableStats = symbolTableStats;
            StringTableStats = stringTableStats;
        }

        /// <nodoc/>
        public bool Equals(HistoricDataPoint other)
        {
            return AllTableStats.SequenceEqual(other.AllTableStats);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is HistoricDataPoint && Equals((HistoricDataPoint)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(AllTableStats.Select(t => t.GetHashCode()).ToArray());
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return I($"{{PathTable: {PathTableStats}, SymbolTable: {SymbolTableStats}, StringTable: {StringTableStats}}}");
        }

        /// <nodoc/>
        public static bool operator ==(HistoricDataPoint left, HistoricDataPoint right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(HistoricDataPoint left, HistoricDataPoint right)
        {
            return !left.Equals(right);
        }

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            PathTableStats.Serialize(writer);
            SymbolTableStats.Serialize(writer);
            StringTableStats.Serialize(writer);
        }

        /// <nodoc/>
        public static HistoricDataPoint Deserialize(BuildXLReader reader)
        {
            return new HistoricDataPoint(
                pathTableStats: TableStats.Deserialize(reader),
                symbolTableStats: TableStats.Deserialize(reader),
                stringTableStats: TableStats.Deserialize(reader));
        }
    }

    /// <summary>
    /// Simple struct for keeping a count and a size of a table.
    /// </summary>
    public readonly struct TableStats : IEquatable<TableStats>
    {
        /// <summary>Number of entries in the table. </summary>
        public int Count { get; }

        /// <summary>Size of the table in bytes. </summary>
        public long SizeInBytes { get; }

        /// <nodoc/>
        public TableStats(int count, long sizeInBytes)
        {
            Count = count;
            SizeInBytes = sizeInBytes;
        }

        /// <nodoc/>
        public bool Equals(TableStats other)
        {
            return Count == other.Count && SizeInBytes == other.SizeInBytes;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is TableStats && Equals((TableStats)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Count, HashCodeHelper.GetHashCode(SizeInBytes));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return I($"{{Count: {Count}, SizeInBytes: {SizeInBytes}}}");
        }

        /// <nodoc/>
        public static bool operator ==(TableStats left, TableStats right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(TableStats left, TableStats right)
        {
            return !left.Equals(right);
        }

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(Count);
            writer.WriteCompact(SizeInBytes);
        }

        /// <nodoc/>
        public static TableStats Deserialize(BuildXLReader reader)
        {
            return new TableStats(
                count: reader.ReadInt32Compact(),
                sizeInBytes: reader.ReadInt32Compact());
        }
    }
}
