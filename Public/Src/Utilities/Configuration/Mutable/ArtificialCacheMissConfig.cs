// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class ArtificialCacheMissConfig : IArtificialCacheMissConfig
    {
        /// <nodoc />
        public ArtificialCacheMissConfig()
        {
            // Create an "empty" configuration
            Seed = 0;
            Rate = 0;
            IsInverted = false;
        }

        /// <nodoc />
        public ArtificialCacheMissConfig(IArtificialCacheMissConfig template)
        {
            Contract.Assume(template != null);

            Seed = template.Seed;
            Rate = template.Rate;
            IsInverted = template.IsInverted;
        }

        /// <inheritdoc />
        public int Seed { get; set; }

        /// <inheritdoc />
        public ushort Rate { get; set; }

        /// <inheritdoc />
        public bool IsInverted { get; set; }

        /// <summary>
        /// Tries to parse a string representation miss options, with number formatting based on <paramref name="formatProvider" />.
        /// 0.1 indicates "Miss rate 0.1; random seed"
        /// ~0.1 indicates "Miss rate 0.1; inverted; random seed"
        /// ~0.1#123 indicates "Miss rate 0.1 (10%); seed 123; inverted"
        /// </summary>
        public static ArtificialCacheMissConfig TryParse(string value, IFormatProvider formatProvider)
        {
            Contract.Requires(value != null);

            int pos = 0;
            if (value.Length == 0)
            {
                return null;
            }

            bool negate = false;
            if (value[pos] == '~')
            {
                negate = true;
                pos++;
            }

            if (pos >= value.Length)
            {
                return null;
            }

            int hashPos = value.IndexOf('#', pos);
            Contract.Assume(hashPos < 0 || hashPos >= pos);
            bool hasHash = hashPos >= 0;
            int rateStopPos = hasHash ? hashPos : value.Length;
            Contract.Assert(rateStopPos >= pos);

            string rateStr = value.Substring(pos, rateStopPos - pos);
            double rate;
            if (!double.TryParse(rateStr, NumberStyles.Float, formatProvider, out rate))
            {
                return null;
            }

            if (rate < 0.0 || rate > 1.0)
            {
                return null;
            }

            if (hasHash)
            {
                if (hashPos + 1 >= value.Length)
                {
                    return null;
                }

                int seed;
                string seedStr = value.Substring(hashPos + 1);
                if (!int.TryParse(seedStr, NumberStyles.Integer, formatProvider, out seed))
                {
                    return null;
                }

                return new()
                {
                    Rate = (ushort)(rate * ushort.MaxValue),
                    IsInverted = negate,
                    Seed = seed
                };
            }
            else
            {
                return new()
                {
                    Rate = (ushort)(rate * ushort.MaxValue),
                    IsInverted = negate
                };
            }
        }
    }
}
