// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        /// <summary>
        /// Converts a given <paramref name="timeString"/> to <see cref="DateTime"/>.
        /// </summary>
        public static bool TryParseReadableTimestamp(string timeString, out DateTime time)
        {
            var result = FromReadableTimestamp(timeString);
            time = result.HasValue ? result.Value : default;
            return result.HasValue;
        }

        /// <nodoc />
        public static bool IsRecent(this DateTime lastAccessTime, DateTime now, TimeSpan recencyInterval)
        {
            if (recencyInterval == Timeout.InfiniteTimeSpan)
            {
                return true;
            }

            return lastAccessTime + recencyInterval >= now;
        }

        public static bool IsStale(this DateTime lastAcccessTime, DateTime now, TimeSpan frequency)
        {
            return lastAcccessTime + frequency <= now;
        }

        /// <nodoc />
        public static TimeSpan Multiply(this TimeSpan timespan, double factor)
        {
            return TimeSpan.FromTicks((long)(timespan.Ticks * factor));
        }

        public static DateTime Max(this DateTime lhs, DateTime rhs)
        {
            if (lhs > rhs)
            {
                return lhs;
            }

            return rhs;
        }

        public static DateTime Min(this DateTime lhs, DateTime rhs)
        {
            if (lhs > rhs)
            {
                return rhs;
            }

            return lhs;
        }

        /// <summary>
        /// The Epoch for Unix time.
        /// </summary>
        public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static readonly DateTime CompactTimeEpoch = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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

        public static uint ToCompactTimeMinutes(this DateTime preciseDateTime)
        {
            if (preciseDateTime == DateTime.MinValue)
            {
                return 0;
            }

            if (preciseDateTime.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("preciseDateTime must be UTC");
            }

            return (uint)(preciseDateTime - CompactTimeEpoch).TotalMinutes;
        }

        public static CompactTime ToCompactTime(this DateTime preciseDateTime)
        {
            return new CompactTime(preciseDateTime.ToCompactTimeMinutes());
        }

        public static DateTime FromCompactTime(uint compactTimeMinutes)
        {
            if (compactTimeMinutes == 0)
            {
                return DateTime.MinValue;
            }

            return CompactTimeEpoch.AddMinutes(compactTimeMinutes);
        }
    }
}
