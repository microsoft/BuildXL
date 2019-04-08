// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using BuildXL.Pips;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Tests for <see cref="ArtificialCacheMissOptions" />
    /// </summary>
    public class ArtificialCacheMissOptionsTests
    {
        [Fact]
        public void ParseJustRate()
        {
            ExpectParseSuccess("0.1", 0.1, inverted: false);
            ExpectParseSuccess("0.0", 0.0, inverted: false);
            ExpectParseSuccess("1.0", 1.0, inverted: false);
        }

        [Fact]
        public void ParseRateAndSeed()
        {
            ExpectParseSuccess("0.1#123", 0.1, inverted: false, seed: 123);
        }

        [Fact]
        public void ParseInvertedRateAndSeed()
        {
            ExpectParseSuccess("~0.1#123", 0.1, inverted: true, seed: 123);
        }

        [Fact]
        public void ParseInvertedRate()
        {
            ExpectParseSuccess("~0.3", 0.3, inverted: true);
        }

        private static void ExpectParseSuccess(string value, double rate, bool inverted, int? seed = null)
        {
            ArtificialCacheMissOptions options = ArtificialCacheMissOptions.TryParse(value, CultureInfo.InvariantCulture);
            XAssert.IsNotNull(options, "Expected parse success for {0}", value);

            XAssert.AreEqual(rate, options.Rate, "Incorrect rate");
            XAssert.AreEqual(inverted, options.IsInverted, "Incorrect inversion");

            if (seed.HasValue)
            {
                XAssert.AreEqual(seed.Value, options.Seed, "Incorrect seed");
            }
        }

        [Fact]
        public void ParseRateOutOfRangeFails()
        {
            ExpectParseFailure("-0.5");
            ExpectParseFailure("1.01");
            ExpectParseFailure("1.01");
        }

        [Fact]
        public void ParseWithNothingAfterHashFails()
        {
            ExpectParseFailure("0.5#");
        }

        [Fact]
        public void ParseWithMetacharactersOnlyFails()
        {
            ExpectParseFailure("~#");
        }

        [Fact]
        public void ParseWithInvalidSeedFails()
        {
            ExpectParseFailure("0.5#abc");
        }

        private static void ExpectParseFailure(string value)
        {
            ArtificialCacheMissOptions options = ArtificialCacheMissOptions.TryParse(value, CultureInfo.InvariantCulture);
            if (options != null)
            {
                XAssert.Fail("Expected parse failure for {0}; got options {1}", value, options.ToString());
            }
        }

        [Fact]
        public void TestMissRate50Percent()
        {
            ExpectMissRate(new ArtificialCacheMissOptions(0.5, invert: false, seed: 984502), samples: 4000);
        }

        [Fact]
        public void TestMissRate90PercentInverted()
        {
            ExpectMissRate(new ArtificialCacheMissOptions(0.9, invert: true, seed: 12032), samples: 4000);
        }

        private static void ExpectMissRate(ArtificialCacheMissOptions options, int samples)
        {
            var rng = new Random(909090);
            int misses = 0;
            for (int i = 0; i < samples; i++)
            {
                ulong high = unchecked(((ulong)rng.Next()) << 32);
                ulong low = unchecked((ulong)rng.Next());
                long val = unchecked((long)(high | low));
                if (options.ShouldHaveArtificialMiss(val))
                {
                    misses++;
                }
            }

            double actualRate = (double)misses / samples;
            double deltaOfPercentages = Math.Abs(actualRate - options.EffectiveMissRate);
            double error = deltaOfPercentages / options.EffectiveMissRate;

            if (double.IsInfinity(error) || error > 0.1)
            {
                XAssert.Fail(
                    "Options {0} gave {1} misses; expected {2} ; error {3:P}",
                    options.ToString(),
                    misses,
                    options.EffectiveMissRate * samples,
                    error);
            }
        }
    }
}
