// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Utility functions concerning the <see cref="TimeSpan" /> structure.
    /// </summary>
    public static class TimeSpanUtilities
    {
        /// <summary>
        /// Turns number of milliseconds into a <code>TimeSpan</code>.
        /// </summary>
        /// <remarks>
        /// This is a fast equivalent of TimeSpan.FromMilliseconds.
        /// </remarks>
        public static TimeSpan MillisecondsToTimeSpan(this uint milliseconds)
        {
            // TimeSpan.TicksPerMillisecond is a long, and it equals 10000.
            // Thus, the computation below yields a long, and never overflows.
            long ticks = TimeSpan.TicksPerMillisecond * milliseconds;
            return new TimeSpan(ticks);
        }

        /// <summary>
        /// Turns number of milliseconds into a <code>TimeSpan</code>.
        /// </summary>
        /// <remarks>
        /// This is a fast equivalent of TimeSpan.FromMilliseconds.
        /// </remarks>
        public static TimeSpan MillisecondsToTimeSpan(this int milliseconds)
        {
            // TimeSpan.TicksPerMillisecond is a long, and it equals 10000.
            // Thus, the computation below yields a long, and never overflows.
            long ticks = TimeSpan.TicksPerMillisecond * milliseconds;
            return new TimeSpan(ticks);
        }

        /// <summary>
        /// Gets the total milliseconds of the given <see cref="TimeSpan"/> as an integral value.
        /// </summary>
        public static long ToMilliseconds(this TimeSpan timespan)
        {
            return (long)timespan.TotalMilliseconds;
        }

        /// <nodoc />
        public static TimeSpan Max(this TimeSpan lhs, TimeSpan rhs)
        {
            if (lhs > rhs)
            {
                return lhs;
            }

            return rhs;
        }

        /// <nodoc />
        public static TimeSpan Min(this TimeSpan lhs, TimeSpan rhs)
        {
            if (lhs > rhs)
            {
                return rhs;
            }

            return lhs;
        }
    }
}
