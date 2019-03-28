// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    internal static class DateTimeUtilities
    {
        /// <summary>
        /// The Epoch for Unix time.
        /// </summary>
        public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private const string LastAccessedFormatString = "yyyyMMdd.HHmmss";

        public static string ToReadableString(this DateTime time)
        {
            return time.ToString(LastAccessedFormatString, CultureInfo.InvariantCulture);
        }

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

        public static long ToUnixTimeSeconds(this DateTime preciseDateTime)
        {
            if (preciseDateTime == DateTime.MinValue)
            {
                return 0;
            }

            if (preciseDateTime.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("preciseDateTime must be UTC");
            }

            return (long)(preciseDateTime - UnixEpoch).TotalSeconds;
        }

        public static UnixTime ToUnixTime(this DateTime preciseDateTime)
        {
            return new UnixTime(preciseDateTime.ToUnixTimeSeconds());
        }

        public static DateTime FromUnixTime(long unixTimeSeconds)
        {
            if (unixTimeSeconds <= 0)
            {
                return DateTime.MinValue;
            }

            return UnixEpoch.AddSeconds(unixTimeSeconds);
        }

        public static bool IsRecent(this DateTime lastTouch, DateTime now, TimeSpan recencyInterval)
        {
            if (recencyInterval == Timeout.InfiniteTimeSpan)
            {
                return true;
            }

            return lastTouch + recencyInterval >= now;
        }

        public static TimeSpan Multiply(this TimeSpan timespan, double factor)
        {
            return TimeSpan.FromTicks((long)(timespan.Ticks * factor));
        }
    }
}
