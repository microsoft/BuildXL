// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Redis
{
    public class RedisInfoTests
    {
        private readonly ITestOutputHelper _output;

        public RedisInfoTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestParseLogic()
        {
            var rawInfo = new[]
                #region Input Data
                          {
                              new[] {"Server", "redis_mode", "cluster"},
                              new[] {"Server", "os", "Windows"},
                              new[] {"Server", "arch_bits", "64"},
                              new[] {"Server", "multiplexing_api", "winsock_IOCP"},
                              new[] {"Server", "run_id", "4e69f04207bcb78d183c548b18cfc63a8a402f76"},
                              new[] {"Server", "uptime_in_seconds", "1823367"},
                              new[] {"Server", "uptime_in_days", "21"},
                              new[] {"Server", "hz", "10"},
                              new[] {"Clients", "connected_clients", "318"},
                              new[] {"Clients", "maxclients", "15000"},
                              new[] {"Clients", "client_longest_output_list", "0"},
                              new[] {"Clients", "client_biggest_input_buf", "0"},
                              new[] {"Clients", "client_total_writes_outstanding", "0"},
                              new[] {"Clients", "client_total_sent_bytes_outstanding", "0"},
                              new[] {"Clients", "blocked_clients", "0"},
                              new[] {"Memory", "used_memory", "2535297272"},
                              new[] {"Memory", "used_memory_human", "2.36G"},
                              new[] {"Memory", "used_memory_rss", "4328046592"},
                              new[] {"Memory", "used_memory_rss_human", "4.03G"},
                              new[] {"Memory", "used_memory_peak", "5429977272"},
                              new[] {"Memory", "used_memory_peak_human", "5.06G"},
                              new[] {"Memory", "used_memory_lua", "71680"},
                              new[] {"Memory", "maxmemory", "13100000000"},
                              new[] {"Memory", "maxmemory_reservation", "99000000"},
                              new[] {"Memory", "maxfragmentationmemory_reservation", "124000000"},
                              new[] {"Memory", "maxmemory_desired_reservation", "99000000"},
                              new[] {"Memory", "maxfragmentationmemory_desired_reservation", "124000000"},
                              new[] {"Memory", "maxmemory_human", "12.20G"},
                              new[] {"Memory", "maxmemory_policy", "volatile-lru"},
                              new[] {"Memory", "mem_allocator", "jemalloc-3.6.0"},
                              new[] {"Stats", "total_connections_received", "6706554"},
                              new[] {"Stats", "total_commands_processed", "13921081759"},
                              new[] {"Stats", "instantaneous_ops_per_sec", "234"},
                              new[] {"Stats", "bytes_received_per_sec", "26915"},
                              new[] {"Stats", "bytes_sent_per_sec", "15009"},
                              new[] {"Stats", "bytes_received_per_sec_human", "26.28K"},
                              new[] {"Stats", "bytes_sent_per_sec_human", "14.66K"},
                              new[] {"Stats", "rejected_connections", "0"},
                              new[] {"Stats", "expired_keys", "16950287"},
                              new[] {"Stats", "evicted_keys", "0"},
                              new[] {"Stats", "keyspace_hits", "4597475110"},
                              new[] {"Stats", "keyspace_misses", "77539312"},
                              new[] {"Stats", "pubsub_channels", "1"},
                              new[] {"Stats", "pubsub_patterns", "0"},
                              new[] {"Stats", "total_oom_messages", "0"},
                              new[] {"Replication", "role", "master"},
                              new[] {"CPU", "used_cpu_sys", "52129.64"},
                              new[] {"CPU", "used_cpu_user", "105675.72"},
                              new[] {"CPU", "used_cpu_avg_ms_per_sec", "2"},
                              new[] {"CPU", "server_load", "0.74"},
                              new[] {"CPU", "event_wait", "48"},
                              new[] {"CPU", "event_no_wait", "113"},
                              new[] {"CPU", "event_wait_count", "56"},
                              new[] {"CPU", "event_no_wait_count", "60"},
                              new[] {"Cluster", "cluster_enabled", "1"},
                              new[] {"Cluster", "cluster_myself_name", "c5030a3471981cfd2e2c51715ceaff915f91168a"},
                              new[] {"Keyspace", "db0", "keys=4599962,expires=4599960,avg_ttl=266217318"},
                          }
                .GroupBy(a => a[0], a => new KeyValuePair<string, string>(a[1], a[2]))
                .ToArray();
#endregion Input Data

            var info = new RedisInfo(rawInfo);
            var stringInfo = info.ToString();
            _output.WriteLine(stringInfo);

            Assert.DoesNotContain("<Unknown>", stringInfo);
            
        }
    }
}
