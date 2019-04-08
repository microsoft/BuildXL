// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    ///     Helper class for converting number of bytes to/from a friendly byte string.
    /// </summary>
    public static class ByteCountUtilities
    {
        private const string AlternateByteSuffix = "B";
        private static readonly string[] ByteSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB" };

        /// <summary>
        ///     Converts byte count expressions of the form '4096', '4096 bytes', '4096B', or '4KB', etc., into the number of bytes
        ///     it represents.
        /// </summary>
        /// <param name="expression">Byte count expression to convert.</param>
        /// <returns>The count of bytes represented.</returns>
        public static long ToSize(this string expression)
        {
            if (expression == null)
            {
                return 0;
            }

            long numBytes;
            const string exceptionMessage = "Invalid byte count expression: ";
            for (int i = 0; i < ByteSuffixes.Length; i++)
            {
                var byteSuffix = ByteSuffixes[i];
                if (expression.EndsWith(byteSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(expression.Substring(0, expression.Length - byteSuffix.Length), out numBytes))
                    {
                        numBytes *= (long)Math.Pow(1024, i);
                        return numBytes;
                    }

                    throw new ArgumentException(exceptionMessage + expression, nameof(expression));
                }
            }

            if (expression.EndsWith(AlternateByteSuffix, StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(expression.Substring(0, expression.Length - AlternateByteSuffix.Length), out numBytes))
            {
                return numBytes;
            }

            if (long.TryParse(expression, out numBytes))
            {
                return numBytes;
            }

            throw new ArgumentException(exceptionMessage + expression);
        }

        /// <summary>
        ///     Converts the provided count of bytes into a friendly, human-readable byte count expression.
        /// </summary>
        /// <param name="count">Count of bytes to represent.</param>
        /// <param name="displayExactBytes">
        ///     If true, represents the full, exact number of bytes instead of condensing into KB, MB,
        ///     GB, etc.
        /// </param>
        /// <returns>A friendly byte count expression.</returns>
        public static string ToSizeExpression(this long count, bool displayExactBytes = false)
        {
            Contract.Requires(count >= 0);

            if (displayExactBytes)
            {
                return count.ToString("N0", CultureInfo.InvariantCulture) + " " + ByteSuffixes[0];
            }

            var index = count == 0 ? 0 : (int)Math.Min(Math.Floor(Math.Log(count, 1024)), ByteSuffixes.Length - 1);

            if (index < 0)
            {
                index = 0;
            }

            return (count / Math.Pow(1024, index)).ToString("#,0.##", CultureInfo.InvariantCulture) + " " + ByteSuffixes[index];
        }
    }
}
