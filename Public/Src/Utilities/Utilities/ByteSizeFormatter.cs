// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Formats a byte count in typical (non-SI) units (e.g. 1024 -> "1 KB").
    /// </summary>
    public static class ByteSizeFormatter
    {
        private const long KB = 1024;
        private const long MB = 1024 * KB;
        private const long GB = 1024 * MB;
        private const long TB = 1024 * GB;

        /// <nodoc />
        public static long ToMegabytes(long bytes)
        {
            return bytes / MB;
        }

        /// <nodoc />
        public static string Format(long bytes)
        {
            if (bytes >= TB)
            {
                return FormatInternal(bytes, TB, "TB");
            }
            else if (bytes >= GB)
            {
                return FormatInternal(bytes, GB, "GB");
            }
            else if (bytes >= MB)
            {
                return FormatInternal(bytes, MB, "MB");
            }
            else if (bytes >= KB)
            {
                return FormatInternal(bytes, KB, "KB");
            }
            else
            {
                return FormatInternal(bytes, 1, "B");
            }
        }

        private static string FormatInternal(long bytes, long scale, string suffix)
        {
            if (scale != 1)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:N2} {1}",
                    (double)bytes / scale,
                    suffix);
            }
            else
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:N0} {1}",
                    (double)bytes / scale,
                    suffix);
            }
        }
    }
}
