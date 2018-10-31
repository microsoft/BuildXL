// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
            Contract.Ensures(Contract.Result<TimeSpan>().TotalMilliseconds == milliseconds);

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
            Contract.Ensures(Contract.Result<TimeSpan>().TotalMilliseconds == milliseconds);

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
    }
}
