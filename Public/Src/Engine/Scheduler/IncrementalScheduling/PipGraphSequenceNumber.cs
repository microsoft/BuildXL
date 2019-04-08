// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Sequence number that increases whenever pip graph changes.
    /// </summary>
    /// <remarks>
    /// This sequence number is used to denote, at which version of pip graph, a pip or a source file is marked clean.
    /// </remarks>
    internal readonly struct PipGraphSequenceNumber : IEquatable<PipGraphSequenceNumber>, IComparable<PipGraphSequenceNumber>
    {
        /// <summary>
        /// Internal number that is monotonically increasing.
        /// </summary>
        private readonly int m_value;

        /// <summary>
        /// Zero sequence number.
        /// </summary>
        public static readonly PipGraphSequenceNumber Zero = new PipGraphSequenceNumber(0);

        /// <nodoc />
        public PipGraphSequenceNumber(int value) => m_value = value;

        /// <summary>
        /// Checks if this number is the beginning of the sequence.
        /// </summary>
        public bool IsZero => m_value == 0;

        /// <nodoc />
        public bool Equals(PipGraphSequenceNumber other) => m_value == other.m_value;

        /// <nodoc />
        public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

        /// <nodoc />
        public override int GetHashCode() => m_value.GetHashCode();

        /// <nodoc />
        public static bool operator ==(PipGraphSequenceNumber left, PipGraphSequenceNumber right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(PipGraphSequenceNumber left, PipGraphSequenceNumber right) => !left.Equals(right);

        /// <nodoc />
        public static bool operator <(PipGraphSequenceNumber left, PipGraphSequenceNumber right) => left.m_value < right.m_value;

        /// <nodoc />
        public static bool operator >(PipGraphSequenceNumber left, PipGraphSequenceNumber right) => left.m_value > right.m_value;

        /// <nodoc />
        public static bool operator <=(PipGraphSequenceNumber left, PipGraphSequenceNumber right) => left.m_value <= right.m_value;

        /// <nodoc />
        public static bool operator >=(PipGraphSequenceNumber left, PipGraphSequenceNumber right) => left.m_value >= right.m_value;

        /// <nodoc />
        public int CompareTo(PipGraphSequenceNumber other) => m_value.CompareTo(other.m_value);

        /// <inheritdoc />
        public override string ToString() => m_value.ToString();

        /// <nodoc />
        public PipGraphSequenceNumber Increment() => new PipGraphSequenceNumber(m_value + 1);

        /// <nodoc />
        public void Serialize(BinaryWriter writer) => writer.Write(m_value);

        /// <nodoc />
        public static PipGraphSequenceNumber Deserialize(BinaryReader reader) => new PipGraphSequenceNumber(reader.ReadInt32());
    }
}
