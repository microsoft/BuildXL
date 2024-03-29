// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Represents a point in time, defined as an approximation of the number of seconds elapsed since 00:00:00 Jan 1 1970.
    /// </summary>
    public readonly struct UnixTime : IEquatable<UnixTime>, IComparable<UnixTime>
    {
        /// <nodoc />
        public static readonly UnixTime Zero = new UnixTime(0);

        /// <nodoc />
        public static UnixTime UtcNow => DateTime.UtcNow.ToUnixTime();

        /// <nodoc />
        public UnixTime(long value) => Value = value;

        /// <nodoc />
        public long Value { get; }

        /// <inheritdoc />
        public bool Equals(UnixTime other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is UnixTime hash && Equals(hash);
        }

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        /// <inheritdoc />
        public int CompareTo(UnixTime other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <nodoc />
        public DateTime ToDateTime() => DateTimeUtilities.FromUnixTime(Value);

        /// <nodoc />
        public static TimeSpan operator -(UnixTime left, UnixTime right) => TimeSpan.FromSeconds(left.Value - right.Value);

        /// <nodoc />
        public static bool operator ==(UnixTime left, UnixTime right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(UnixTime left, UnixTime right) => !left.Equals(right);

        /// <nodoc />
        public static bool operator >(UnixTime left, UnixTime right) => left.Value > right.Value;

        /// <nodoc />
        public static bool operator <(UnixTime left, UnixTime right) => left.Value < right.Value;

        /// <nodoc />
        public static UnixTime Min(UnixTime left, UnixTime right) => Math.Min(left.Value, right.Value) == left.Value ? left : right;

        public static UnixTime Max(UnixTime left, UnixTime right) => Math.Max(left.Value, right.Value) == left.Value ? left : right;

        /// <nodoc />
        public static implicit operator UnixTime(DateTime value)
        {
            return value.ToUnixTime();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToDateTime().ToString();
        }
    }
}
