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

namespace BuildXL.Cache.Monitor.App.Az
{
    internal struct Measurement
    {
        public DateTime TimeStamp { get; }

        public double? Value { get; }

        public Measurement(DateTime timeStamp, double? value)
        {
            TimeStamp = timeStamp;
            Value = value;
        }
    }

    internal struct MetricName
    {
        public string Name { get; }

        public MetricName(string name)
        {
            Name = name;
        }

        public static implicit operator string(MetricName metric) => metric.Name;
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

        private static Dictionary<AggregationType, Func<MetricValue, double?>> ExtractMetric { get; } =
            new Dictionary<AggregationType, Func<MetricValue, double?>>
            {
                // Since we always know which aggregation type we're fetching, it should never be null
                { AggregationType.Average, metricValue => metricValue.Average },
                { AggregationType.Count, metricValue => metricValue.Count },
                { AggregationType.Minimum, metricValue => metricValue.Minimum },
                { AggregationType.Maximum, metricValue => metricValue.Maximum  },
                { AggregationType.Total, metricValue => metricValue.Total },
            };

        private class MetricsClientErrorDetectionStrategy : ITransientErrorDetectionStrategy
        {
            public bool IsTransient(Exception ex)
            {
                // This exception has happened a couple of times. Looks like it gets fixed by retrying.
                return ex is ArgumentOutOfRangeException;
            }
        }

        private static readonly MetricsClientErrorDetectionStrategy ErrorDetectionStrategy = new MetricsClientErrorDetectionStrategy();
        private static readonly ExponentialBackoffRetryStrategy RetryStrategy = new ExponentialBackoffRetryStrategy();
        private static readonly RetryPolicy RetryPolicy = new RetryPolicy(ErrorDetectionStrategy, RetryStrategy);

        public static async Task<List<Measurement>> GetMetricAsync(
            this IMonitorManagementClient monitorManagementClient,
            string resourceUri,
            MetricName metricName,
            DateTime startTimeUtc, DateTime endTimeUtc,
            TimeSpan samplingInterval,
            AggregationType aggregation,
            CancellationToken cancellationToken = default)
        {
            Contract.Requires(aggregation != AggregationType.None);

            // Unfortunately, the Azure Metrics API is pretty bad and lacking documentation, so we do our best here to
            // get things working. However, API may fail anyways if an invalid set of sampling frequency vs time length
            // is input.
            var startTime = startTimeUtc.ToString("o").Replace("+", "%2b");
            var endTime = endTimeUtc.ToString("o").Replace("+", "%2b");

            return await RetryPolicy.ExecuteAsync(async () =>
            {
                var result = await monitorManagementClient.Metrics.ListAsync(
                    resourceUri: resourceUri,
                    metricnames: metricName,
                    timespan: $"{startTime}/{endTime}",
                    interval: samplingInterval,
                    aggregation: aggregation.ToString(),
                    cancellationToken: cancellationToken);

                var metric = result.Value[0];
                var extract = ExtractMetric[aggregation];
                var timeSeries = metric.Timeseries[0];

                return timeSeries.Data.Select(metricValue => new Measurement(metricValue.TimeStamp, extract(metricValue))).ToList();
            }, cancellationToken);
        }
    }
}
