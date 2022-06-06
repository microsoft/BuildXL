// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Represents a point in time, defined as an approximation of the number of minutes elapsed since 00:00:00 Jan 1 2010.
    /// This represents the time as a 32 bit integer vs 64 bit used by <see cref="DateTime"/> and <see cref="UnixTime"/>
    /// </summary>
    public readonly struct CompactTime : IEquatable<CompactTime>, IComparable<CompactTime>
    {
        /// <nodoc />
        public static readonly CompactTime Zero = new CompactTime(0);

        /// <nodoc />
        public CompactTime(uint value) => Value = value;

        /// <nodoc />
        public uint Value { get; }

        /// <inheritdoc />
        public bool Equals(CompactTime other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is CompactTime hash && Equals(hash);
        }

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        /// <inheritdoc />
        public int CompareTo(CompactTime other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <nodoc />
        public DateTime ToDateTime() => DateTimeUtilities.FromCompactTime(Value);

        /// <nodoc />
        public static TimeSpan operator -(CompactTime left, CompactTime right) => TimeSpan.FromMinutes(left.Value - right.Value);

        /// <nodoc />
        public static bool operator ==(CompactTime left, CompactTime right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(CompactTime left, CompactTime right) => !left.Equals(right);

        /// <nodoc />
        public static bool operator >(CompactTime left, CompactTime right) => left.Value > right.Value;

        /// <nodoc />
        public static bool operator <(CompactTime left, CompactTime right) => left.Value < right.Value;

        /// <nodoc />
        public static CompactTime Min(CompactTime left, CompactTime right) => Math.Min(left.Value, right.Value) == left.Value ? left : right;

        public static CompactTime Max(CompactTime left, CompactTime right) => Math.Max(left.Value, right.Value) == left.Value ? left : right;

        /// <nodoc />
        public static implicit operator CompactTime(DateTime value)
        {
            return value.ToCompactTime();
        }

        /// <nodoc />
        public static implicit operator CompactTime(UnixTime value)
        {
            return value.ToDateTime().ToCompactTime();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToDateTime().ToString();
        }
    }
}
