// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// Gets the bandwidth lower limit for a bandwidth checker.
    /// </summary>
    internal interface IBandwidthLimitSource
    {
        /// <nodoc />
        Task<double> GetMinimumSpeedInMbPerSecAsync(CancellationToken token);
    }

    /// <nodoc />
    internal class ConstantBandwidthLimit : IBandwidthLimitSource
    {
        private readonly Task<double> _limitTask;

        /// <nodoc />
        public ConstantBandwidthLimit(double limitMbPerSec) => _limitTask = Task.FromResult(limitMbPerSec);

        /// <inheritdoc />
        public Task<double> GetMinimumSpeedInMbPerSecAsync(CancellationToken token) => _limitTask;
    }

    /// <summary>
    /// Gets the bandwidth limit based on historical data of copies.
    /// </summary>
    internal class HistoricalBandwidthLimitSource : IBandwidthLimitSource
    {
        private readonly int _maxRecordsStored;
        private readonly Queue<double> _bandwidthRecords = new Queue<double>();
        private readonly SemaphoreSlim _bandwidthRecordLock = new SemaphoreSlim(1, 1);

        /// <nodoc />
        public HistoricalBandwidthLimitSource(int maxRecordsStored = 64) => _maxRecordsStored = maxRecordsStored;

        /// <summary>
        /// Adds a record for future use.
        /// </summary>
        public async Task AddBandwidthRecordAsync(double value, CancellationToken ct)
        {
            // Do a quick check to protect ourselves against invalid data: NaN's, negative, and zero values.
            if (value <= 0.0)
            {
                return;
            }

            await _bandwidthRecordLock.WaitAsync(ct);
            try
            {
                while (_bandwidthRecords.Count >= _maxRecordsStored)
                {
                    _bandwidthRecords.Dequeue();
                }

                _bandwidthRecords.Enqueue(value);
            }
            finally
            {
                _bandwidthRecordLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<double> GetMinimumSpeedInMbPerSecAsync(CancellationToken token)
        {
            var statistics = await GetBandwidthRecordStatisticsAsync(token);
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

        private async Task<SummaryStatistics> GetBandwidthRecordStatisticsAsync(CancellationToken token)
        {
            var n = 0;
            var mean = 0.0;
            var standardDeviation = 0.0;
            var min = double.MaxValue;
            var max = double.MinValue;
            await _bandwidthRecordLock.WaitAsync(token);
            try
            {
                // This is the standard one-pass algorithm for summary statistic computation.
                // See https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance#Online_algorithm for background.
                foreach (var value in _bandwidthRecords)
                {
                    n++;
                    var delta = value - mean;
                    mean += delta / n;
                    standardDeviation += delta * (value - mean);

                    if (value < min)
                    {
                        min = value;
                    }

                    if (value > max)
                    {
                        max = value;
                    }
                }
            }
            finally
            {
                _bandwidthRecordLock.Release();
            }

            standardDeviation = Math.Sqrt(standardDeviation / (n - 1));

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
