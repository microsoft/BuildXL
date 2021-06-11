// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Globalization;

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Setting for representing timespan in a readable format (i.e. [-][#d][#h][#m][#s][#ms])
    /// </summary>
    [TypeConverter(typeof(StringConvertibleConverter))]
    public struct TimeSpanSetting : IStringConvertibleSetting
    {
        public TimeSpan Value { get; }

        public TimeSpanSetting(TimeSpan value)
        {
            Value = value;
        }

        public static implicit operator TimeSpan(TimeSpanSetting value)
        {
            return value.Value;
        }

        public static implicit operator TimeSpanSetting(TimeSpan value)
        {
            return new TimeSpanSetting(value);
        }

        public static implicit operator TimeSpanSetting(string value)
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

        public static TimeSpanSetting Parse(string value)
        {
            if (!TryParseReadableTimeSpan(value, out var timeSpan))
            {
                timeSpan = TimeSpan.Parse(value);
            }

            return new TimeSpanSetting(timeSpan);
        }

        public object ConvertFromString(string value)
        {
            return Parse(value);
        }

        /// <summary>
        /// Parses a <see cref="TimeSpan"/> in readable format
        ///
        /// Format:
        /// [-][#d][#h][#m][#s][#ms]
        /// where # represents any valid non-negative double. All parts are optional but string must be non-empty.
        /// </summary>
        public static bool TryParseReadableTimeSpan(string value, out TimeSpan result)
        {
            int start = 0;
            int lastUnitIndex = -1;

            result = TimeSpan.Zero;
            bool isNegative = false;
            bool succeeded = true;

            // Easier to process if all specifiers are mutually exclusive characters
            // So replace 'ms' with 'f' so it doesn't conflict with 'm' and 's' specifiers
            value = value.Trim().Replace("ms", "f");

            for (int i = 0; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case ':':
                        // Quickly bypass normal timespans which contain ':' character
                        // that is not allowed in readable timespan format
                        return false;
                    case '-':
                        succeeded = lastUnitIndex == -1;
                        lastUnitIndex = 0;
                        isNegative = true;
                        break;
                    case 'd':
                        succeeded = process(1, TimeSpan.FromDays(1), ref result);
                        break;
                    case 'h':
                        succeeded = process(2, TimeSpan.FromHours(1), ref result);
                        break;
                    case 'm':
                        succeeded = process(3, TimeSpan.FromMinutes(1), ref result);
                        break;
                    case 's':
                        succeeded = process(4, TimeSpan.FromSeconds(1), ref result);
                        break;
                    case 'f':
                        succeeded = process(5, TimeSpan.FromMilliseconds(1), ref result);
                        break;
                }

                if (!succeeded)
                {
                    return false;
                }

                bool process(int unitIndex, TimeSpan unit, ref TimeSpan result)
                {
                    // No duplicate units allowed and units must appear in decreasing order of magnitude.
                    if (unitIndex > lastUnitIndex)
                    {
                        var factorString = value.Substring(start, i - start).Trim();
                        if (double.TryParse(factorString, out var factor))
                        {
                            result += Multiply(unit, factor);
                            lastUnitIndex = unitIndex;
                            start = i + 1;
                            return true;
                        }
                    }

                    // Invalidate
                    return false;
                }
            }

            result = Multiply(result, isNegative ? -1 : 1);
            return start == value.Length;
        }

        /// <nodoc />
        private static TimeSpan Multiply(TimeSpan timespan, double factor)
        {
            return TimeSpan.FromTicks((long)(timespan.Ticks * factor));
        }
    }
}
