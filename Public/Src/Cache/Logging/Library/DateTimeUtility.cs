// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace BuildXL.Cache.Logging
{
    /// <summary>
     /// Methods provide a high-resolution timestamp from the operating
     /// system.
     /// (note: recommended to use vs. System.DateTime.Now.Ticks due
     /// to slight inaccuracies in the DateTime implementation. Especially
     /// for log timestamps, DateTime.Now is inadequate since it has a
     /// ~15millisecond resolution, whereas this OS method has a
     /// ~1microsecond resolution)
     /// </summary>
    public static class DateTimeUtility
    {
        private static bool FoundError = false;

        [DllImport("kernel32.dll")]
        private static extern void GetSystemTimePreciseAsFileTime(out long filetime);

        /// <summary>
        /// Gets a high resultion DateTime.Now
        /// </summary>
        public static DateTime GetHighResolutionNow()
        {
            if (!FoundError)
            {
                try
                {
                    GetSystemTimePreciseAsFileTime(out long fileTime);
                    return DateTime.FromFileTime(fileTime);
                }
                catch (Exception)
                {
                    FoundError = true;
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }

            // High resolution time isn't available in this OS version, so we return the regular resolution time as a fallback.
            return DateTime.Now;
        }

        /// <summary>
        /// Gets a high resultion DateTime.UtcNow
        /// </summary>
        public static DateTime GetHighResolutionUtcNow()
        {
            if (!FoundError)
            {
                try
                {
                    GetSystemTimePreciseAsFileTime(out long fileTime);
                    return DateTime.FromFileTimeUtc(fileTime);
                }
                catch (Exception)
                {
                    FoundError = true;
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }

            // High resolution time isn't available in this OS version, so we return the regular resolution time as a fallback.
            return DateTime.UtcNow;
        }
    }
}
