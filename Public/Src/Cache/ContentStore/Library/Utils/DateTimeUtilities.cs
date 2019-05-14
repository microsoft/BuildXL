// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <nodoc />
    public static class DateTimeUtilities
    {
        private const string LastAccessedFormatString = "yyyyMMdd.HHmmss";

        /// <summary>
        /// Gets a readable string representation of a given <paramref name="time"/>.
        /// </summary>
        public static string ToReadableString(this DateTime time)
        {
            return time.ToString(LastAccessedFormatString, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts a given <paramref name="timeString"/> to <see cref="DateTime"/>.
        /// </summary>
        /// <returns>null if a given string is null or not valid.</returns>
        public static DateTime? FromReadableTimestamp(string timeString)
        {
            if (timeString == null)
            {
                return null;
            }

            if (DateTime.TryParseExact(timeString, LastAccessedFormatString, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }

            return null;
        }

        /// <nodoc />
        public static bool IsRecent(this DateTime lastTouch, DateTime now, TimeSpan recencyInterval)
        {
            if (recencyInterval == Timeout.InfiniteTimeSpan)
            {
                return true;
            }

            return lastTouch + recencyInterval >= now;
        }

        /// <nodoc />
        public static TimeSpan Multiply(this TimeSpan timespan, double factor)
        {
            return TimeSpan.FromTicks((long)(timespan.Ticks * factor));
        }
    }
}
