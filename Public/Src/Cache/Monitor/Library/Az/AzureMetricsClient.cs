// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Monitor;
using Microsoft.Azure.Management.Monitor.Models;
using System.Diagnostics.ContractsLight;
using System.Threading;
using Microsoft.Rest.TransientFaultHandling;
using Microsoft.Rest.Azure.OData;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;

#nullable enable

namespace BuildXL.Cache.Monitor.Library.Az
{
    internal class AzureMetricsClient : IAzureMetricsClient
    {
        private readonly IMonitorManagementClient _monitorManagementClient;

        private static readonly ExponentialBackoffRetryStrategy RetryStrategy = new ExponentialBackoffRetryStrategy();
        private static readonly MetricsClientErrorDetectionStrategy ErrorDetectionStrategy = new MetricsClientErrorDetectionStrategy();
        private static readonly RetryPolicy RetryPolicy = new RetryPolicy(ErrorDetectionStrategy, RetryStrategy);

        /// <summary>
        /// We allow few concurrent Azure Metrics requests. This is because the API seems to basically fail when doing
        /// too many requests at once, and it sometimes happens that the monitor launches too many of these rules in
        /// parallel.
        /// </summary>
        private static readonly SemaphoreSlim AzureMetricsRequestLimiter = new SemaphoreSlim(initialCount: 4);

        public AzureMetricsClient(IMonitorManagementClient monitorManagementClient)
        {
            _monitorManagementClient = monitorManagementClient;
        }

        public async Task<Dictionary<MetricName, List<MetricValue>>> GetMetricsWithDimensionAsync(
            string resourceUri,
            IReadOnlyList<MetricName> metrics,
            string dimension,
            DateTime startTimeUtc,
            DateTime endTimeUtc,
            TimeSpan samplingInterval,
            IReadOnlyList<AggregationType> aggregations,
            CancellationToken cancellationToken = default)
        {
            Contract.Requires(startTimeUtc < endTimeUtc);

            // HERE BE DRAGONS. The Azure Monitor Metrics API is basically nondeterministic. It may fail at random, for
            // unknown periods of time, and for unknown reasons. This function is an attempt to make a sane wrapper
            // over it.

            // We remove the seconds in an attempt to play nicely with the Azure Metrics API. Format is not strictly
            // ISO, as they claim is supported, but what we have found to work by trial and error and looking at
            // examples.
            var startTime = startTimeUtc.ToString("yyyy-MM-ddTHH:mm:00Z");
            var endTime = endTimeUtc.ToString("yyyy-MM-ddTHH:mm:00Z");
            var interval = $"{startTime}/{endTime}";

            return await RetryPolicy.ExecuteAsync(async () =>
            {
                var metricNames = string.Join(",", metrics.Select(name => name.Name.ToLower()));
                var odataQuery = new ODataQuery<MetadataValue>(odataExpression: $"{dimension} eq '*'");
                var aggregation = string.Join(
                    ",",
                    aggregations.Select(
                        agg =>
                        {
                            Contract.Assert(agg != AggregationType.None); return agg.ToString().ToLower();
                        }));

                Response result;
                using (var waitToken = await AzureMetricsRequestLimiter.WaitTokenAsync())
                {
                    result = await _monitorManagementClient.Metrics.ListAsync(
                        resourceUri: resourceUri,
                        metricnames: metricNames,
                        odataQuery: odataQuery,
                        timespan: interval,
                        interval: samplingInterval,
                        aggregation: aggregation,
                        cancellationToken: cancellationToken);
                }
                result.Validate();

                if (result.Value.Count == 0)
                {
                    // Sometimes, for unknown reasons, this can be empty. This is an error, but can go away when
                    // retried. That's exactly what we do here (look at the retry policy's detection strategy).
                    throw new AzureMetricsClientValidationException("Invalid `result.Value` returned");
                }

                var output = new Dictionary<MetricName, List<MetricValue>>();
                foreach (var metric in result.Value)
                {
                    if (metric.Timeseries.Count == 0)
                    {
                        // Sometimes, there may be no timeseries. Reasons seem to be varied, but here's the predominant
                        // example that would brick us in production:
                        //  - We'd attempt to fetch operationsPerSecond going back several weeks for all possible 10
                        //    shards.
                        //  - The current number of shards is less than 10, say 1
                        //  - Shard 0 would have a result. Shards 1-9 wouldn't.
                        // WARNING: this continue means that we won't report the result for this metric. Usages must
                        // account for arbitrary metric disappearance.
                        continue;
                    }

                    var metricNameFromApi = metric.Name.Value;
                    if (string.IsNullOrEmpty(metricNameFromApi))
                    {
                        throw new AzureMetricsClientValidationException("Received empty metric name");
                    }

                    foreach (var timeseries in metric.Timeseries)
                    {
                        var metricGroupFromApi = timeseries.Metadatavalues.ToDictionary(v => v.Name.Value, v => v.Value);

                        var timeSeriesFromApi = timeseries.Data;
                        if (timeSeriesFromApi == null || timeSeriesFromApi.Count < 1)
                        {
                            throw new AzureMetricsClientValidationException($"Received invalid time series for metric `{metricNameFromApi}`. Size=[{timeSeriesFromApi?.Count ?? -1}]");
                        }

                        var metricName = new MetricName(metricNameFromApi, metricGroupFromApi);
                        output[metricName] = timeSeriesFromApi
                            .OrderBy(m => m.TimeStamp)
                            .ToList();
                    }
                }

                return output;
            }, cancellationToken);
        }

        private class MetricsClientErrorDetectionStrategy : ITransientErrorDetectionStrategy
        {
            public bool IsTransient(Exception ex) => ex is AzureMetricsClientValidationException;
        }

        private class AzureMetricsClientValidationException : Exception
        {
            public AzureMetricsClientValidationException(string message) : base(message)
            {
            }

            public AzureMetricsClientValidationException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }
    }
}
