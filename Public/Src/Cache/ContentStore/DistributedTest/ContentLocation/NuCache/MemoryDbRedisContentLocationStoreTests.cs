// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    [Collection("Redis-based tests")]
    [Trait("Category", "LongRunningTest")]
    public class MemoryDbRedisContentLocationStoreTests : LocalRedisRedisContentLocationStoreTests
    {
        protected override RedisContentLocationStoreConfiguration DefaultConfiguration { get; } =
            new RedisContentLocationStoreConfiguration()
            {
                Database = new MemoryContentLocationDatabaseConfiguration()
            };

        public MemoryDbRedisContentLocationStoreTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output)
        {
        }

        [Fact]
        public async Task TestLocalContentLocationDatabaseAsync()
        {
            var contentHash = ContentHash.Random();
            var context = new Context(TestGlobal.Logger);
            const int numReplicas = 1;
            var paths = CreatePaths(numReplicas);
            var valid = ValidType.All;
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            GetBulkLocationsResult locationsResult = null;
            List<ContentHashWithSizeAndLocations> contentHashInfo = null;
            IDictionary<RedisKey, MockRedisValueWithExpiry> redisData = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromHours(1));
            await TestAsync(
                context,
                GetRedisDatabase(_clock, redisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                GetRedisDatabase(_clock, GetMachineRedisData(paths, valid).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (genericStore, storeFactory) =>
                {
                    var store = (RedisContentLocationStore)genericStore;
                    contentHashInfo = CreateContentHashWithSizeAndLocations(contentHash, paths, randomSize);
                    var contentHashes = contentHashInfo.Select(p => p.ContentHash).ToList();
                    locationsResult =
                        await store.GetBulkAsync(context, contentHashes, CancellationToken.None, UrgencyHint.Nominal);

                    // Updating the value with a new location should cause a difference to be reported in the counters
                    await store.UpdateBulkAsync(
                        context,
                        CreateContentHashWithSizeAndLocations(contentHash, CreatePaths(2), randomSize),
                        CancellationToken.None,
                        UrgencyHint.Nominal,
                        LocationStoreOption.None).ShouldBeSuccess();

                    // Get and update operations should not trigger updates on database
                    var counters = store.Counters;
                    counters.GetCounterValue(ContentLocationStoreCounters.DatabaseStoreUpdate).Should().Be(0);
                    counters.GetCounterValue(ContentLocationStoreCounters.DatabaseStoreAdd).Should().Be(0);
                });
        }
    }
}
