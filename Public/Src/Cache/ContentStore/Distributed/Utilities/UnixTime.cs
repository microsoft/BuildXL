// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Represents a point in time, defined as an approximation of the number of seconds elapsed since 00:00:00 Jan 1 1970.
    /// </summary>
    public readonly struct UnixTime : IEquatable<UnixTime>, IComparer<UnixTime>
    {
        /// <nodoc />
        public static readonly UnixTime Zero = new UnixTime(0);

        /// <nodoc />
        public UnixTime(long value) => Value = value;

        /// <nodoc />
        public long Value { get; }

        /// <inheritdoc />
        public bool Equals(UnixTime other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is UnixTime hash && Equals(hash);
        }

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        /// <inheritdoc />
        public int Compare(UnixTime x, UnixTime y)
        {
            return x.Value.CompareTo(y.Value);
        }

        /// <nodoc />
        public DateTime ToDateTime() => DateTimeUtilities.FromUnixTime(Value);

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
