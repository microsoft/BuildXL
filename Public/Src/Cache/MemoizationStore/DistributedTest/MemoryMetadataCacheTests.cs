// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata;
using BuildXL.Cache.MemoizationStore.Distributed.Sessions;
using BuildXL.Cache.MemoizationStore.Distributed.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using StackExchange.Redis;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.DistributedTest
{
    public class MemoryMetadataCacheTests
    {
        private const string RedisNameSpace = "test";
        private readonly IRedisSerializer _redisSerializer = new RedisSerializer();

        [Fact]
        public Task TestGetOrAddSelectorsEmptyCache()
        {
            return RunTest(async (context, metadataCache, redisDb) =>
            {
                var selectors = new[] { Selector.Random(), Selector.Random(), };
                var weakFp = Fingerprint.Random();
                await metadataCache.GetOrAddSelectorsAsync(context, weakFp, fp => ToSelectorResult(selectors)).ShouldBeSuccess();

                // Check Redis data
                Assert.Equal(1, redisDb.DbSet.Keys.Count);

                var cacheKey = _redisSerializer.ToRedisKey(weakFp).Prepend(RedisNameSpace);
                Assert.True(redisDb.DbSet.ContainsKey(cacheKey));
                Assert.Equal(2, redisDb.DbSet[cacheKey].Length);
            });
        }

        [Fact]
        public async Task TestGetOrAddSelectorsExisting()
        {
            var selectors = new[] { Selector.Random(), Selector.Random(), };
            var weakFp = Fingerprint.Random();

            var redisValues = new Dictionary<RedisKey, RedisValue[]>
            {
                { _redisSerializer.ToRedisKey(weakFp).Prepend(RedisNameSpace), _redisSerializer.ToRedisValues(selectors) },
            };

            using (var mockDb = new MockRedisDatabase(
                SystemClock.Instance,
                setData: redisValues))
            {
                await RunTest(
                    mockDb,
                    async (context, metadataCache, redisDb) =>
                    {
                        var selectorResult = await metadataCache.GetOrAddSelectorsAsync(
                            context,
                            weakFp,
                            fp =>
                            {
                                throw new InvalidOperationException(
                                    "GetFunc not expected to be called since data is already present in the cache");
                            }).ShouldBeSuccess();

                        // Check result
                        Assert.Equal(2, selectorResult.Value.Length);
                        foreach (Selector result in selectorResult.Value)
                        {
                            Assert.True(selectors.Contains(result));
                        }
                    });
            }
        }

        [Fact]
        public Task TestGetOrAddSelectorsError()
        {
            return RunTest(async (context, metadataCache, redisDb) =>
            {
                var weakFp = Fingerprint.Random();
                var errorResult = new GetSelectorResult("Error");
                Task<Result<Selector[]>> failure = Task.FromResult(Result.FromError<Selector[]>(errorResult));
                var selectorResults = await metadataCache.GetOrAddSelectorsAsync(context, weakFp, fp => failure).ShouldBeError();

                Assert.Equal(errorResult.ErrorMessage, selectorResults.ErrorMessage);

                // Check Redis data
                Assert.Equal(0, redisDb.DbSet.Keys.Count);
            });
        }

        [Fact]
        public Task TestGetOrAddStrongFingerprintAsyncEmptyCache()
        {
            return RunTest(async (context, metadataCache, redisDb) =>
            {
                var strongFp = StrongFingerprint.Random();
                var hashList = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
                var getContentHashListResult = await metadataCache.GetOrAddContentHashListAsync(context, strongFp, fp => ToContentHashListResult(hashList));

                Assert.True(getContentHashListResult.Succeeded);

                // Check Redis data
                Assert.Equal(1, redisDb.GetDbWithExpiry().Keys.Count);

                var cacheKey = _redisSerializer.ToRedisKey(strongFp).Prepend(RedisNameSpace);
                Assert.True(redisDb.GetDbWithExpiry().ContainsKey(cacheKey));
                Assert.Equal(hashList, _redisSerializer.AsContentHashList(redisDb.GetDbWithExpiry()[cacheKey].Value));
            });
        }

        [Fact]
        public async Task TestGetOrAddStrongFingerprintAsyncExisting()
        {
            var strongFp = StrongFingerprint.Random();
            var hashList = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            var initialData = new Dictionary<RedisKey, RedisValue>
            {
                { _redisSerializer.ToRedisKey(strongFp).Prepend(RedisNameSpace), _redisSerializer.ToRedisValue(hashList) },
            };

            using (var mockDb = new MockRedisDatabase(SystemClock.Instance, initialData))
            {
                await RunTest(
                    mockDb,
                    async (context, metadataCache, redisDb) =>
                    {
                        var getContentHashListResult = await metadataCache.GetOrAddContentHashListAsync(
                            context,
                            strongFp,
                            fp =>
                            {
                                throw new InvalidOperationException(
                                    "GetFunc not expected to be called since data is already present in the cache");
                            });

                        // Check result
                        Assert.True(getContentHashListResult.Succeeded);
                        Assert.Equal(hashList, getContentHashListResult.ContentHashListWithDeterminism);
                    });
            }
        }

        [Fact]
        public Task TestGetOrAddStrongFingerprintAsyncError()
        {
            return RunTest(async (context, metadataCache, redisDb) =>
            {
                var strongFp = StrongFingerprint.Random();
                var errorResult = new GetContentHashListResult("Error");
                var getContentHashListResult = await metadataCache.GetOrAddContentHashListAsync(context, strongFp, fp => Task.FromResult(errorResult));

                Assert.False(getContentHashListResult.Succeeded);

                // Check Redis data
                Assert.Equal(0, redisDb.GetDbWithExpiry().Keys.Count);
            });
        }

        [Fact]
        public async Task TestDeleteFingerprintsAsyncExisting()
        {
            var strongFp = StrongFingerprint.Random();

            var initialValues = new Dictionary<RedisKey, RedisValue>
            {
                { _redisSerializer.ToRedisKey(strongFp).Prepend(RedisNameSpace), string.Empty },
            };

            var initialSetValues = new Dictionary<RedisKey, RedisValue[]>
            {
                { _redisSerializer.ToRedisKey(strongFp.WeakFingerprint).Prepend(RedisNameSpace), new RedisValue[] { string.Empty } },
            };

            using (var mockDb = new MockRedisDatabase(SystemClock.Instance, initialValues, setData: initialSetValues))
            {
                await RunTest(
                    mockDb,
                    async (context, metadataCache, redisDb) =>
                    {
                        var result = await metadataCache.DeleteFingerprintAsync(context, strongFp);

                        // Check result
                        Assert.True(result.Succeeded);

                        // Check Redis data
                        Assert.Equal(0, redisDb.GetDbWithExpiry().Keys.Count);
                        Assert.Equal(0, redisDb.DbSet.Keys.Count);
                    });
            }
        }

        private async Task RunTest(Func<Context, IMetadataCache, MockRedisDatabase, Task> test)
        {
            using (var redisDb = new MockRedisDatabase(SystemClock.Instance))
            {
                await RunTest(redisDb, test);
            }
        }

        private async Task RunTest(MockRedisDatabase redisDb, Func<Context, IMetadataCache, MockRedisDatabase, Task> test)
        {
            var context = new Context(TestGlobal.Logger);
            RedisConnectionMultiplexer.TestConnectionMultiplexer = MockRedisDatabaseFactory.CreateConnection(redisDb);
            var tracer = new DistributedCacheSessionTracer(TestGlobal.Logger, nameof(MemoryMetadataCacheTests));
            var metadataCache = new RedisMetadataCache(new EnvironmentConnectionStringProvider(string.Empty), new RedisSerializer(), RedisNameSpace, tracer);
            try
            {
                await metadataCache.StartupAsync(context).ShouldBeSuccess();
                await test(context, metadataCache, redisDb);
                await metadataCache.ShutdownAsync(context).ShouldBeSuccess();
            }
            finally
            {
                RedisConnectionMultiplexer.TestConnectionMultiplexer = null;
            }
        }

        private Task<Result<Selector[]>> ToSelectorResult(IEnumerable<Selector> selectors)
        {
            return Task.FromResult(Result.Success(selectors.Select(s => s).ToArray()));
        }

        private Task<GetContentHashListResult> ToContentHashListResult(ContentHashListWithDeterminism hashListWithDeterminism)
        {
            return Task.FromResult(new GetContentHashListResult(hashListWithDeterminism));
        }
    }
}
