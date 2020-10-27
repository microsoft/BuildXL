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

namespace BuildXL.Cache.Monitor.Library.Az
{
    internal class MetricName
    {
        public string Name { get; }

        public Dictionary<string, string>? Group { get; }

        public MetricName(string name, Dictionary<string, string>? group = null)
        {
            Contract.RequiresNotNullOrEmpty(name);
            Name = name;
            Group = group;
        }

        public static implicit operator string(MetricName metric) => metric.ToString();

        public override string ToString()
        {
            if (Group != null && Group.Count > 0)
            {
                var groupAsString = string.Join(
                    ", ",
                    Group.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                return $"{Name}({groupAsString})";
            }

            return $"{Name}";
        }
    }

    internal enum AzureRedisClusterMetric
    {
        CacheLatency,
        Errors,
    }

    /// <summary>
    /// See: https://docs.microsoft.com/en-us/azure/azure-cache-for-redis/cache-how-to-monitor#available-metrics-and-reporting-intervals
    /// </summary>
    internal enum AzureRedisShardMetric
    {
        ConnectedClients,
        TotalCommandsProcessed,
        CacheHits,
        CacheMisses,
        CacheMissRate,
        GetCommands,
        SetCommands,
        OperationsPerSecond,
        EvictedKeys,
        TotalKeys,
        ExpiredKeys,
        UsedMemory,
        UsedMemoryPercentage,
        UsedMemoryRss,
        ServerLoad,
        CacheWrite,
        CacheRead,
        PercentProcessorTime,
    }

    internal static class AzureMetricsClientExtensions
    {
        public static MetricName ToMetricName(this AzureRedisClusterMetric metric)
        {
            switch (metric)
            {
                case AzureRedisClusterMetric.CacheLatency:
                    return new MetricName("cacheLatency");
                case AzureRedisClusterMetric.Errors:
                    return new MetricName("errors");
            }

            throw new NotImplementedException($"Missing name translation for {metric}");
        }

        private static Dictionary<AzureRedisShardMetric, string> AzureRedisShardMetricsMap { get; } =
            new Dictionary<AzureRedisShardMetric, string>
            {
                { AzureRedisShardMetric.ConnectedClients, "connectedclients"},
                { AzureRedisShardMetric.TotalCommandsProcessed, "totalcommandsprocessed"},
                { AzureRedisShardMetric.CacheHits, "cachehits"},
                { AzureRedisShardMetric.CacheMisses, "cachemisses"},
                { AzureRedisShardMetric.CacheMissRate, "cachemissrate"},
                { AzureRedisShardMetric.GetCommands, "getcommands"},
                { AzureRedisShardMetric.SetCommands, "setcommands"},
                { AzureRedisShardMetric.OperationsPerSecond, "operationsPerSecond"},
                { AzureRedisShardMetric.EvictedKeys, "evictedkeys"},
                { AzureRedisShardMetric.TotalKeys, "totalkeys"},
                { AzureRedisShardMetric.ExpiredKeys, "expiredkeys"},
                { AzureRedisShardMetric.UsedMemory, "usedmemory"},
                { AzureRedisShardMetric.UsedMemoryPercentage, "usedmemorypercentage"},
                { AzureRedisShardMetric.UsedMemoryRss, "usedmemoryRss"},
                { AzureRedisShardMetric.ServerLoad, "serverLoad"},
                { AzureRedisShardMetric.CacheWrite, "cacheWrite"},
                { AzureRedisShardMetric.CacheRead, "cacheRead"},
                { AzureRedisShardMetric.PercentProcessorTime, "percentProcessorTime"},
            };

        public static MetricName ToMetricName(this AzureRedisShardMetric metric, int? shard = null)
        {
            if (shard == null)
            {
                return new MetricName(AzureRedisShardMetricsMap[metric]);
            }

            return new MetricName($"{AzureRedisShardMetricsMap[metric]}{shard}");
        }

        private class MetricsClientErrorDetectionStrategy : ITransientErrorDetectionStrategy
        {
            public bool IsTransient(Exception ex) => ex is AzureMetricsClientValidationException;
        }

        public class AzureMetricsClientValidationException : Exception
        {
            public AzureMetricsClientValidationException(string message) : base(message)
            {
            }

            public AzureMetricsClientValidationException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }

        private static readonly ExponentialBackoffRetryStrategy RetryStrategy = new ExponentialBackoffRetryStrategy();
        private static readonly MetricsClientErrorDetectionStrategy ErrorDetectionStrategyV2 = new MetricsClientErrorDetectionStrategy();
        private static readonly RetryPolicy RetryPolicy = new RetryPolicy(ErrorDetectionStrategyV2, RetryStrategy);

        public static async Task<Dictionary<MetricName, List<MetricValue>>> GetMetricsWithDimensionAsync(
            this IMonitorManagementClient monitorManagementClient,
            string resourceUri,
            IReadOnlyList<MetricName> metrics,
            string dimension,
            DateTime startTimeUtc, DateTime endTimeUtc,
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
                var result = await monitorManagementClient.Metrics.ListAsync(
                    resourceUri: resourceUri,
                    metricnames: metricNames,
                    odataQuery: odataQuery,
                    timespan: interval,
                    interval: samplingInterval,
                    aggregation: aggregation,
                    cancellationToken: cancellationToken);
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
    }
}
