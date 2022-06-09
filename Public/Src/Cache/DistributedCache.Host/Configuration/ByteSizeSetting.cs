// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Globalization;

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Setting for representing timespan in a readable format (i.e. [-][#pb][#tb][#gb][#mb][#kb][#b])
    /// </summary>
    [TypeConverter(typeof(StringConvertibleConverter))]
    public struct ByteSizeSetting : IStringConvertibleSetting
    {
        public const long Petabytes = 1_000 * Terabytes;
        public const long Terabytes = 1_000 * Gigabytes;
        public const long Gigabytes = 1_000 * Megabytes;
        public const long Megabytes = 1_000 * Kilobytes;
        public const long Kilobytes = 1_000;

        public long Value { get; }

        public ByteSizeSetting(long value)
        {
            Value = value;
        }
        
        public static implicit operator long(ByteSizeSetting value)
        {
            return value.Value;
        }

        public static implicit operator ByteSizeSetting(long value)
        {
            return new ByteSizeSetting(value);
        }

        public static implicit operator ByteSizeSetting(string value)
        {
            return Parse(value);
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public string ConvertToString()
        {
            return Value.ToString();
        }

        public static ByteSizeSetting Parse(string value)
        {
            if (!TryParseReadableBytes(value, out var bytes))
            {
                bytes = long.Parse(value);
            }

            return new ByteSizeSetting(bytes);
        }

        public object ConvertFromString(string value)
        {
            return Parse(value);
        }

        /// <summary>
        /// Parses a <see cref="long"/> in readable format
        ///
        /// Format:
        /// [-][#pb][#tb][#gb][#mb][#kb][#b]
        /// where # represents any valid non-negative double. All parts are optional but string must be non-empty.
        /// </summary>
        public static bool TryParseReadableBytes(string value, out long result)
        {
            int start = 0;
            int lastUnitIndex = -1;

            result = 0;
            bool isNegative = false;
            bool succeeded = true;

            // Easier to process if all specifiers are single mutually exclusive characters
            value = value.Trim().ToLowerInvariant()
                .Replace("kb", "k")
                .Replace("mb", "m")
                .Replace("gb", "g")
                .Replace("tb", "t")
                .Replace("pb", "p");

            for (int i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                switch (ch)
                {
                    case '-':
                        succeeded = lastUnitIndex == -1;
                        lastUnitIndex = 0;
                        isNegative = true;
                        break;
                    case 'b':
                        succeeded = process(6, 1, ref result);
                        break;
                    case 'k':
                        succeeded = process(5, Kilobytes, ref result);
                        break;
                    case 'm':
                        succeeded = process(4, Megabytes, ref result);
                        break;
                    case 'g':
                        succeeded = process(3, Gigabytes, ref result);
                        break;
                    case 't':
                        succeeded = process(2, Terabytes, ref result);
                        break;
                    case 'p':
                        succeeded = process(1, Petabytes, ref result);
                        break;
                }

                if (!succeeded)
                {
                    return false;
                }

                bool process(int unitIndex, long unit, ref long result)
                {
                    // No duplicate units allowed and units must appear in decreasing order of magnitude.
                    if (unitIndex > lastUnitIndex)
                    {
                        var factorString = value.Substring(start, i - start).Trim().Replace("_", "");
                        if (double.TryParse(factorString, out var factor))
                        {
                            result += (long)(unit * factor);
                            lastUnitIndex = unitIndex;
                            start = i + 1;
                            return true;
                        }
                    }

                    // Invalidate
                    return false;
                }
            }

            result = result * (isNegative ? -1 : 1);
            return start == value.Length;
        }
    }
}
