// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// A non-allocating stopwatch like timer.  Uses the stopwatch ticks
    /// but does not allocated nor provide anthing but TotalMilliseconds
    /// as a double from the time the ElapsedTimer is constructed.
    /// </summary>
    public readonly struct ElapsedTimer : IEquatable<ElapsedTimer>
    {
        /// <summary>
        /// Number of TimeSpan ticks per system clock ticks
        /// </summary>
        private static readonly double s_timespanTicksPerTick = 10000000.0 / Stopwatch.Frequency;

        /// <summary>
        /// Precomputed milliseconds per tick - it is a derived constant from the Stopwatch
        /// </summary>
        private static readonly double s_millisecondsPerTick = 1000.0 / Stopwatch.Frequency;

        /// <summary>
        /// Precomputed seconds per tick - it is a derived constant from the Stopwatch
        /// </summary>
        private static readonly double s_secondsPerTick = 1.0 / Stopwatch.Frequency;

        /// <summary>
        /// The start ticks time - never mutated.
        /// </summary>
        private readonly long m_start;

        /// <summary>
        /// Private constructor that initializes the start time
        /// </summary>
        /// <param name="start">
        /// Start time in Stopwatch ticks.
        /// </param>
        private ElapsedTimer(long start)
        {
            m_start = start;
        }

        /// <summary>
        /// Get and start an ElapsedTimer
        /// </summary>
        /// <returns>
        /// Returns an initialized at this point in time ElapsedTimer
        /// </returns>
        /// <remarks>
        /// This is the only supported way to initialize this struct.
        /// </remarks>
        public static ElapsedTimer StartNew()
        {
            return new ElapsedTimer(Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// The current elapsed time in milliseconds since construction with StartNew
        /// </summary>
        public double TotalMilliseconds => s_millisecondsPerTick * (Stopwatch.GetTimestamp() - m_start);

        /// <summary>
        /// The current elapsed time in seconds since construction with StartNew
        /// </summary>
        public double TotalSeconds => s_secondsPerTick * (Stopwatch.GetTimestamp() - m_start);

        /// <summary>
        /// The current elapsed time as a TimeSpan
        /// </summary>
        public TimeSpan TimeSpan { get { return new TimeSpan((long)(s_timespanTicksPerTick * (Stopwatch.GetTimestamp() - m_start))); } }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_start.GetHashCode();
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return (obj is ElapsedTimer) && Equals((ElapsedTimer)obj);
        }

        /// <nodoc />
        bool IEquatable<ElapsedTimer>.Equals(ElapsedTimer other)
        {
            return m_start == other.m_start;
        }

        /// <nodoc />
        public static bool operator ==(ElapsedTimer left, ElapsedTimer right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ElapsedTimer left, ElapsedTimer right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public bool Equals(ElapsedTimer other)
        {
            return m_start == other.m_start;
        }
    }
}
