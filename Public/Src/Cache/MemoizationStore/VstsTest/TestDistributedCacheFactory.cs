// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata;
using BuildXL.Cache.MemoizationStore.Distributed.Sessions;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Distributed.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    public static class TestDistributedCacheFactory
    {
        /// <summary>
        ///     In-memory dictionary for sharing data across multiple clients within a test.
        /// </summary>
        private static readonly ConcurrentDictionary<string, MockRedisDatabase> RedisDatabases =
            new ConcurrentDictionary<string, MockRedisDatabase>();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        internal static ICache CreateCache(
            ILogger logger, ICache innerCache, string redisNamespace, string testClassName, ReadThroughMode readThroughMode)
        {
            var redisDb = RedisDatabases.GetOrAdd(redisNamespace, _ => new MockRedisDatabase(SystemClock.Instance));
            RedisConnectionMultiplexer.TestConnectionMultiplexer = MockRedisDatabaseFactory.CreateConnection(redisDb);
            var tracer = new DistributedCacheSessionTracer(TestGlobal.Logger, testClassName);
            var metadataCache = new RedisMetadataCache(
                new EnvironmentConnectionStringProvider(string.Empty), new RedisSerializer(), redisNamespace, tracer);

            return new DistributedCache(logger, innerCache, metadataCache, tracer, readThroughMode);
        }
    }
}
