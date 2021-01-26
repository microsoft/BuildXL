// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.Monitor.Library.Az;
using Microsoft.Azure.Management.Monitor.Models;

namespace BuildXL.Cache.Monitor.Test
{
    internal class MockAzureMetricsClient : IAzureMetricsClient
    {
        private readonly Queue<Dictionary<MetricName, List<MetricValue>>> _results = new Queue<Dictionary<MetricName, List<MetricValue>>>();

        public void Add(Dictionary<MetricName, List<MetricValue>> metrics)
        {
            _results.Enqueue(metrics);
        }

        public Task<Dictionary<MetricName, List<MetricValue>>> GetMetricsWithDimensionAsync(
            string resourceUri,
            IReadOnlyList<MetricName> metrics,
            string dimension,
            DateTime startTimeUtc,
            DateTime endTimeUtc,
            TimeSpan samplingInterval,
            IReadOnlyList<AggregationType> aggregations,
            CancellationToken cancellationToken = default)
        {
            var result = _results.Peek();
            _results.Dequeue();
            return Task.FromResult(result);
        }
    }
}
