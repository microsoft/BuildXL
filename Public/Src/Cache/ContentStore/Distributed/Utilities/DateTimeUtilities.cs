// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{

    internal static class DateTimeUtilities
    {
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
