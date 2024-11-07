// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Utilities for performing type convert.
    /// </summary>
    public static class ConvertUtilities
    {
        /// <summary>
        /// Used to parse time duration options
        /// </summary>
        private static readonly (string, int)[] s_durationFactorBySuffix =
        [
                ("ms",  1),         // Order matters so we try ms before s
                ("s",   1000),
                ("m",   1000 * 60),
                ("min", 1000 * 60),
                ("h",   1000 * 60 * 60)
        ];

        /// <summary>
        /// Parse an option that represents a time duration: the allowed suffixes are 'ms', 's', 'm', 'h'
        /// If no suffix is specified the amount is interpreted in milliseconds
        /// </summary>
        /// <returns>
        /// Return time in mmilliseconds if succeeded
        /// Return a Failure with string content describe the error if failed.
        /// </returns>
        public static Possible<int, Failure<string>> TryParseDurationOptionToMilliseconds(string timeDuration, string name, int minValue, int maxValue)
        {
            if (string.IsNullOrEmpty(timeDuration))
            {
                return new Failure<string>($"The {name} argument requires a value.");
            }

            var input = timeDuration;
            if (double.TryParse(input, out double doubleValue))
            {
                return ranged(doubleValue);
            }

            // We'll do a very naive parsing, but good enough, this is called at most a couple of times
            foreach (var (suffix, multiplier) in s_durationFactorBySuffix)
            {
                if (input.EndsWith(suffix))
                {
                    var numberPart = input.Substring(0, input.Length - suffix.Length);
                    if (!double.TryParse(numberPart, out doubleValue))
                    {
                        // An incorrect suffix was provided
                        break;
                    }

                    var valueInMs = doubleValue * multiplier;
                    return ranged(valueInMs);
                }
            }

            return new Failure<string>($"The value provided for the {name} argument is invalid, expecting a numeric expression representing a time period (ending with 'ms', 's', 'm', 'h')");

            Possible<int, Failure<string>> ranged(double x)
            {
                if (x < minValue || x > maxValue)
                {
                    return new Failure<string>($"The value provided for the {name} argument is invalid, expecting a duration falling in the range {minValue}ms..{maxValue}ms but got '{timeDuration} = {x}ms'.");
                }

                return (int)x;
            }
        }
    }
}
