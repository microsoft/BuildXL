// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.ContractsLight;

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

    internal static class AzureMetricsExtensions
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
    }
}
