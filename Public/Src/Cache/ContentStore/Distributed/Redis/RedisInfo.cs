// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    internal static class ToStringExtensions
    {
        public static string ToStringHuman(this long? value)
        {
            if (value == null)
            {
                return "<Uknown>";
            }

            var inGb = (double)value / (1 << 30);
            if (inGb < 0)
            {
                return ((double)value / (1 <<20)).ToString("#.##M");
            }

            return inGb.ToString("#.##G");
        }
    }
    /// <summary>
    /// Processed representation of Redis Info command.
    /// </summary>
    public sealed class RedisInfo
    {
        private static readonly Dictionary<(string group, string key), Action<string, RedisInfo>> Mapper = CreateMapper();

        private static Dictionary<(string group, string key), Action<string, RedisInfo>> CreateMapper()
        {
            var result = new Dictionary<(string group, string key), Action<string, RedisInfo>>
            {
                [(group: "Server", key: "uptime_in_seconds")] = (input, redisInfo) =>
                                                {
                                                    var uptimeInSeconds = TryParse(input);
                                                    redisInfo.Uptime = uptimeInSeconds != null ? (TimeSpan?)TimeSpan.FromSeconds(uptimeInSeconds.Value) : null;
                                                },
                [(group: "Memory", key: "used_memory")] = (input, redisInfo) =>
                                                {
                                                    redisInfo.UsedMemory = TryParse(input);
                                                },

                [(group: "Memory", key: "used_memory_rss")] = (input, redisInfo) =>
                                                {
                                                    redisInfo.UsedMemoryRss = TryParse(input);
                                                },

                [(group: "Memory", key: "maxmemory")] = (input, redisInfo) =>
                                                {
                                                    redisInfo.MaxMemory = TryParse(input);
                                                },

                [(group: "Stats", key: "expired_keys")] = (input, redisInfo) =>
                                                {
                                                    redisInfo.ExpiredKeys = TryParse(input);
                                                },

                [(group: "Stats", key: "evicted_keys")] = (input, redisInfo) =>
                                                {
                                                    redisInfo.EvictedKeys = TryParse(input);
                                                },

                [(group: "Stats", key: "keyspace_hits")] = (input, redisInfo) =>
                                                {
                                                    redisInfo.KeySpaceHits = TryParse(input);
                                                },

                [(group: "Stats", key: "keyspace_misses")] = (input, redisInfo) =>
                                                {
                                                    redisInfo.KeySpaceMisses = TryParse(input);
                                                },

                [(group: "CPU", key: "used_cpu_avg_ms_per_sec")] = (input, redisInfo) =>
                                                {
                                                    redisInfo.UsedCpuAveragePersentage = (int?)TryParse(input)/10;
                                                },

                [(group: "Keyspace", key: "db0")] = (input, redisInfo) =>
                                                    {
                                                        // This should be in a form of 'keys=123,expires=456,avg_ttl=789'.
                                                        var parts = input.Split(new []{','}, StringSplitOptions.RemoveEmptyEntries);
                                                        if (parts.Length == 3)
                                                        {
                                                            redisInfo.KeyCount = tryParseValue(parts[0]);
                                                            redisInfo.ExpirableKeyCount = tryParseValue(parts[1]);
                                                        }

                                                        static long? tryParseValue(string s)
                                                        {
                                                            var idx = s.IndexOf("=", StringComparison.InvariantCulture);
                                                            return idx != -1 ? TryParse(s.Substring(idx + 1)) : null;
                                                        }
                                                },
            };

            return result;
        }

        private static long? TryParse(string s)
        {
            if (long.TryParse(s, out var result))
            {
                return result;
            }

            return null;
        }

        /// <nodoc />
        public long? UsedMemory { get; private set; }
        /// <nodoc />
        public string UsedMemoryHuman => UsedMemory.ToStringHuman();

        /// <nodoc />
        public long? UsedMemoryRss { get; private set; }
        /// <nodoc />
        public string UsedMemoryRssHuman => UsedMemoryRss.ToStringHuman();

        /// <nodoc />
        public long? MaxMemory { get; private set; }
        /// <nodoc />
        public string MaxMemoryHuman => MaxMemory.ToStringHuman();

        /// <nodoc />
        public long? ExpiredKeys { get; private set; }

        /// <nodoc />
        public long? EvictedKeys { get; private set; }

        /// <nodoc />
        public long? KeySpaceHits { get; private set; }

        /// <nodoc />
        public long? KeySpaceMisses { get; private set; }

        /// <nodoc />
        public int? UsedCpuAveragePersentage { get; private set; }

        /// <nodoc />
        public TimeSpan? Uptime { get; private set; }

        /// <nodoc />
        public long? KeyCount { get; private set; }

        /// <nodoc />
        public long? ExpirableKeyCount { get; private set; }

        /// <nodoc />
        public IGrouping<string, KeyValuePair<string, string>>[] Result { get; }

        /// <nodoc />
        public RedisInfo(IGrouping<string, KeyValuePair<string, string>>[] result)
        {
            Result = result;

            foreach (var g in result)
            {
                foreach (var kvp in g)
                {
                    if (Mapper.TryGetValue((g.Key, kvp.Key), out var processFunc))
                    {
                        processFunc(kvp.Value, this);
                    }
                }
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"Key count: {KeyCount?.ToString() ?? "<Unknown>"}")
                .Append($", Expirable key count: {ExpirableKeyCount?.ToString() ?? "<Unknown>"}")
                .Append($", Expired keys: {ExpiredKeys?.ToString() ?? "<Unknown>"}")
                .Append($", Evicted keys: {EvictedKeys?.ToString() ?? "<Unknown>"}")
                .Append($", Key space hits: {KeySpaceHits?.ToString() ?? "<Unknown>"}")
                .Append($", Key space misses: {KeySpaceMisses?.ToString() ?? "<Unknown>"}")
                .Append($", Uptime: {Uptime?.ToString() ?? "<Unknown>"}")
                .Append($", CPU (%): {UsedCpuAveragePersentage?.ToString() ?? "<Unknown>"}")
                .Append($", Used memory: {UsedMemoryHuman}")
                .Append($", Used memory RSS: {UsedMemoryRssHuman}")
                .Append($", Max memory: {MaxMemoryHuman}");
            return sb.ToString();
        }
    }
}
