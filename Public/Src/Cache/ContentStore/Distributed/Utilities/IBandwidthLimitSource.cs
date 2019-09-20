// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Gets the bandwidth lower limit for a bandwidth checker.
    /// </summary>
    internal interface IBandwidthLimitSource
    {
        /// <nodoc />
        double GetMinimumSpeedInMbPerSec();
    }

    /// <nodoc />
    internal class ConstantBandwidthLimit : IBandwidthLimitSource
    {
        private readonly double _limit;

        /// <nodoc />
        public ConstantBandwidthLimit(double limitMbPerSec) => _limit = limitMbPerSec;

        /// <inheritdoc />
        public double GetMinimumSpeedInMbPerSec() => _limit;
    }

    /// <summary>
    /// Gets the bandwidth limit based on historical data of copies.
    /// </summary>
    internal class HistoricalBandwidthLimitSource : IBandwidthLimitSource
    {
        private readonly int _maxRecordsStored;
        private readonly double[] _bandwidthRecords;
        private long _currentIndex = 0;

        /// <nodoc />
        public HistoricalBandwidthLimitSource(int maxRecordsStored = 64) {
            _maxRecordsStored = maxRecordsStored;
            _bandwidthRecords = new double[maxRecordsStored];
        }

        /// <summary>
        /// Adds a record for future use.
        /// </summary>
        public void AddBandwidthRecord(double value)
        {
            // Do a quick check to protect ourselves against invalid data: NaN's, negative, and zero values.
            if (value <= 0.0)
            {
                return;
            }

            var index = Interlocked.Increment(ref _currentIndex);

            _bandwidthRecords[index % _maxRecordsStored] = value;
        }

        /// <inheritdoc />
        public double GetMinimumSpeedInMbPerSec()
        {
            var statistics = GetBandwidthRecordStatistics();
            if (statistics.Count < _maxRecordsStored / 2)
            {
                // If not enough records, don't impose a limit.
                return (0.0);
            }
            else
            {
                // Set the fiducial limit to 2.27 SD below the mean, or about the bottom 1% for normally distributed values.
                // Use the minimum recorded speed as a lower bound to make sure we don't get negative or otherwise
                // ridiculous limit values for unusual data patterns.
                // Then allow copies to be up to twice that slow, so we don't time out even 1% of successful transfers.
                var limit = 0.5 * Math.Max(statistics.Mean - 2.27 * statistics.StandardDeviation, statistics.Minimum);
                return (limit);
            }
        }

        private SummaryStatistics GetBandwidthRecordStatistics()
        {
            var n = 0;
            var mean = 0.0;
            var sumOfSquaredDelta = 0.0;
            var min = double.MaxValue;
            var max = double.MinValue;

            foreach (var value in _bandwidthRecords.Where(record => record != 0))
            {
                n++;
                var delta = value - mean;
                mean += delta / n;
                sumOfSquaredDelta += delta * (value - mean);

                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }
            }

            var standardDeviation = Math.Sqrt(sumOfSquaredDelta / (n - 1));

            return new SummaryStatistics { Count = n, Mean = mean, StandardDeviation = standardDeviation, Minimum = min, Maximum = max };
        }

        private struct SummaryStatistics
        {
            public int Count;

            public double Mean;

            public double StandardDeviation;

            public double Minimum;

            public double Maximum;
        }
    }
}
