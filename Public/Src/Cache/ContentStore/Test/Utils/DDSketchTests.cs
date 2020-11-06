// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.UtilitiesCore.Sketching;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class DDSketchTests : TestWithOutput
    {
        public DDSketchTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestSingleItem()
        {
            var quantiles = GenerateTestQuantiles(0.001).ToArray();

            var sketch = new DDSketch();
            CompareAgainstExactQuantiles(sketch,
                dataset: new double[] { 1 },
                quantiles);
        }

        // This test computes quantiles from samples out of a uniform distribution. We first test with increasing
        // sample sizes, and then with increasing ranges.
        [Theory]
        [InlineData(0, 10000)]
        [InlineData(1, 10000)]
        [InlineData(2, 100000)]
        [InlineData(3, 1000000)]
        [InlineData(4, 10000000)]
        [InlineData(5, 10000, 100000)]
        [InlineData(6, 10000, 1000000)]
        [InlineData(7, 10000, 10000000)]
        public void TestUniformlyDistributed(
            int seed,
            int size,
            double range = 5.0,
            double increment = 0.001)
        {
            var quantiles = GenerateTestQuantiles(increment).ToArray();

            var rng = new Random(seed);
            var dataset = Enumerable.Range(0, size).Select(_ => (rng.NextDouble() - 0.5) * range).ToList();

            var sketch = new DDSketch();
            CompareAgainstExactQuantiles(
                sketch,
                dataset,
                quantiles);
        }

        public void CompareAgainstExactQuantiles(
            DDSketch sketch,
            IReadOnlyList<double> dataset,
            double[] quantiles)
        {
            Contract.Requires(sketch.Count == 0);

            foreach (var element in dataset)
            {
                sketch.Insert(element);
            }

            // Computation of all of these is done in the same order, so results should be the same.
            sketch.Count.Should().Be((ulong)dataset.Count);
            sketch.Sum.Should().Be(dataset.Sum());
            sketch.Average.Should().Be(dataset.Average());
            sketch.Max.Should().Be(dataset.Max());
            sketch.Min.Should().Be(dataset.Min());

            var sortedSequence = dataset.ToList();
            sortedSequence.Sort();

            foreach (var quantile in quantiles)
            {
                // NOTE(jubayard): this is floating point code, with relatively sensitive behavior with respect to
                // operations. Be careful :)

                var (minExact, maxExact) = ExactQuantile(sortedSequence, quantile);
                var estimate = sketch.Quantile(quantile);

                // 1e-12 is the acceptable error due to floating point approximation
                var minExpected = minExact * (1 - sketch.Alpha) - 1e12;
                var maxExpected = maxExact * (1 + sketch.Alpha) + 1e12;
                Assert.True(estimate >= minExpected && estimate <= maxExpected, $"Min=[{minExpected}] Max=[{maxExpected}] Estimate=[{estimate}]");
            }
        }

        private IEnumerable<double> GenerateTestQuantiles(double increment)
        {
            for (double i = 0; i <= 1.0; i += increment)
            {
                yield return i;
            }
        }

        public enum ExactQuantileVariant
        {
            // Linear interpolation of the approximate medians for order statistics.
            LinearInterpolation = 0,
            // Approximately unbiased for the expected order statistics if x is normally distributed.
            NormalInterpolation = 1,
            Paper = 2,
        }

        private (double, double) ExactQuantile(
            IReadOnlyList<double> sortedSequence,
            double quantile,
            ExactQuantileVariant variant = ExactQuantileVariant.Paper,
            double tolerance = 1e-12)
        {
            int size = sortedSequence.Count;

            if (Math.Abs(quantile - 1) < tolerance)
            {
                var q = sortedSequence[size - 1];
                return (q, q);
            }

            if (Math.Abs(quantile) < tolerance)
            {
                var q = sortedSequence[0];
                return (q, q);
            }

            if (size == 1)
            {
                var q = sortedSequence[0];
                return (q, q);
            }

            // See: https://en.wikipedia.org/wiki/Quantile#Estimating_quantiles_from_a_sample
            switch (variant)
            {
                case ExactQuantileVariant.LinearInterpolation:
                {
                    double hNormal = ((size - 1.0) * quantile) + 1.0;
                    int h = (int)Math.Floor(hNormal);
                    Contract.Assert(h >= 0 && h < size - 1, $"`{h}` (`{hNormal}`) is an invalid index in an array of size `{size}`");

                    var q = sortedSequence[h] + (hNormal - h) * (sortedSequence[h + 1] - sortedSequence[h]);
                    return (q, q);
                }
                case ExactQuantileVariant.NormalInterpolation:
                {
                    double hNormal = ((size + 0.25) * quantile) + (3.0 / 8.0);
                    int h = (int)Math.Floor(hNormal);
                    Contract.Assert(h >= 0 && h < size - 1, $"`{h}` (`{hNormal}`) is an invalid index in an array of size `{size}`");

                    var q = sortedSequence[h] + (hNormal - h) * (sortedSequence[h + 1] - sortedSequence[h]);
                    return (q, q);
                }
                case ExactQuantileVariant.Paper:
                {
                    int lowIdx = (int)Math.Floor(quantile * (size - 1));
                    int highIdx = (int)Math.Ceiling(quantile * (size - 1));
                    return (sortedSequence[lowIdx], sortedSequence[highIdx]);
                }
            }

            throw new NotImplementedException();
        }
    }
}
