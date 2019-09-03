// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.ContentStore.InterfacesTest;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation
{
    public abstract class RedisContentLocationStoreTests : TestWithOutput
    {
        private const string DefaultKeySpace = RedisContentLocationStoreFactory.DefaultKeySpace;

        private static readonly AbsolutePath DefaultTempRoot = new AbsolutePath(@"Z:\TempRoot");
        private readonly IContentHasher _contentHasher = HashInfoLookup.Find(HashType.SHA256).CreateContentHasher();
        protected readonly MemoryClock _clock = new MemoryClock();

        protected enum ValidType
        {
            All,
            Partial,
            None
        }

        /// <inheritdoc />
        protected RedisContentLocationStoreTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected abstract ITestRedisDatabase GetRedisDatabase(
            MemoryClock clock,
            IDictionary<RedisKey, RedisValue> initialData = null,
            IDictionary<RedisKey, DateTime> expiryData = null,
            IDictionary<RedisKey, RedisValue[]> setData = null);

        [Fact]
        public Task UpdateBulkNewContent()
        {
            // Setup
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();

            return TestAsync(
                context,
                GetRedisDatabase(_clock),
                GetRedisDatabase(_clock),
                async (store, storeFactory) =>
                {
                    long randomSize = (long)(ThreadSafeRandom.Generator.NextDouble() * long.MaxValue);
                    var contentHashInfo = CreateContentHashWithSizeAndLocations(contentHash, path, randomSize);

                    // Execute
                    var updateResult = await store.UpdateBulkAsync(context, contentHashInfo, CancellationToken.None, UrgencyHint.Nominal, LocationStoreOption.UpdateExpiry);

                    // Verify
                    Assert.Equal(BoolResult.Success, updateResult);
                    var expectedRedisData = GetRedisData(contentHash, path.Length, randomSize, storeFactory.BumpTime);
                    storeFactory.RedisDatabase.GetDbWithExpiry().Should().Equal(expectedRedisData);
                    storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry().Should().Equal(GetMachineRedisData(path));
                });
        }

        [Fact]
        public Task UpdateBulkChangedContentSameReplica()
        {
            // Setup
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize1 = ThreadSafeRandom.Generator.Next();

            var originalRedisData = GetRedisData(contentHash, path.Length, randomSize1, TimeSpan.FromHours(1));
            var originalMachineData = GetMachineRedisData(path);
            return TestAsync(
                context,
                GetRedisDatabase(_clock, new Dictionary<RedisKey, RedisValue>(originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value))),
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    long randomSize2 = (long)(ThreadSafeRandom.Generator.NextDouble() * long.MaxValue);
                    var contentHashInfo = CreateContentHashWithSizeAndLocations(contentHash, path, randomSize2); // Change size in Redis

                    // Execute
                    var updateResult = await store.UpdateBulkAsync(context, contentHashInfo, CancellationToken.None, UrgencyHint.Nominal, LocationStoreOption.UpdateExpiry);

                    // Verify: Size is updated
                    Assert.Equal(BoolResult.Success, updateResult);
                    var updatedRedisData = GetRedisData(contentHash, path.Length, randomSize2, storeFactory.BumpTime);
                    updatedRedisData.Should().Equal(storeFactory.RedisDatabase.GetDbWithExpiry());

                    var expectedMachineRedisData = GetMachineRedisData(path);
                    var foundMachineRedisData = storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry();
                    expectedMachineRedisData.Count.Should().Be(foundMachineRedisData.Count);
                    foreach (var kvp in expectedMachineRedisData)
                    {
                        foundMachineRedisData.TryGetValue(kvp.Key, out MockRedisValueWithExpiry mockRedisValueWithExpiry).Should().BeTrue();

                        // Don't validate expiry here - GetMachineRedisData will always return null expiries
                        kvp.Value.Value.Should().Be(mockRedisValueWithExpiry.Value);
                    }
                });
        }

        [Fact]
        public Task UpdateBulkExistingContentNewReplicaDoesNotUpdateTimespan()
        {
            var paths = CreatePaths(2);

            // Setup
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)(ThreadSafeRandom.Generator.NextDouble() * long.MaxValue);

            var existingRedisData = GetRedisData(contentHash, 1, randomSize, TimeSpan.FromHours(1)); // Only add one replica to Redis

            var existingMachineRedisData = GetMachineRedisData(new[] { paths[0] });

            return TestAsync(
                context,
                GetRedisDatabase(_clock, existingRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value), existingRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value)),
                GetRedisDatabase(_clock, existingMachineRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    // Add new replica to Redis
                    var contentHashInfo = CreateContentHashWithSizeAndLocations(contentHash, paths, randomSize);

                    // Execute
                    var updateResult = await store.UpdateBulkAsync(context, contentHashInfo, CancellationToken.None, UrgencyHint.Nominal, LocationStoreOption.None);

                    // Verify
                    Assert.Equal(BoolResult.Success, updateResult);
                    var expected = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromHours(1));
                    expected.Should().Equal(storeFactory.RedisDatabase.GetDbWithExpiry());

                    var expectedMachineRedisData = GetMachineRedisData(paths);
                    var foundMachineRedisData = storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry();
                    expectedMachineRedisData.Count.Should().Be(foundMachineRedisData.Count);
                    foreach (var kvp in expectedMachineRedisData)
                    {
                        foundMachineRedisData.TryGetValue(kvp.Key, out MockRedisValueWithExpiry mockRedisValueWithExpiry).Should().BeTrue();

                        // Don't validate expiry here - GetMachineRedisData will always return null expiries
                        kvp.Value.Value.Should().Be(mockRedisValueWithExpiry.Value);
                    }
                });
        }

        [Fact]
        public Task UpdateBulkExistingContentSameReplica()
        {
            // Setup
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            var originalRedisData = GetRedisData(contentHash, path.Length, randomSize, TimeSpan.FromHours(1));
            var originalMachineData = GetMachineRedisData(path);
            return TestAsync(
                context,
                GetRedisDatabase(_clock, new Dictionary<RedisKey, RedisValue>(originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value))),
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    var contentHashInfo = CreateContentHashWithSizeAndLocations(contentHash, path, randomSize);

                    // Execute
                    var updateResult = await store.UpdateBulkAsync(context, contentHashInfo, CancellationToken.None, UrgencyHint.Nominal, LocationStoreOption.UpdateExpiry);

                    // Verify
                    Assert.Equal(BoolResult.Success, updateResult);

                    storeFactory.RedisDatabase.GetDbWithExpiry().Should().Equal(originalRedisData);

                    var expectedMachineRedisData = GetMachineRedisData(path);
                    var foundMachineRedisData = storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry();
                    expectedMachineRedisData.Count.Should().Be(foundMachineRedisData.Count);
                    foreach (var kvp in expectedMachineRedisData)
                    {
                        foundMachineRedisData.TryGetValue(kvp.Key, out MockRedisValueWithExpiry mockRedisValueWithExpiry).Should().BeTrue();

                        // Don't validate expiry here - GetMachineRedisData will always return null expiries
                        kvp.Value.Value.Should().Be(mockRedisValueWithExpiry.Value);
                    }
                });
        }

        [Fact]
        public Task TouchBulkExistingRecord()
        {
            // Setup: Location record added to Redis set to expire in 30 minutes.
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;
            var originalRedisData = GetRedisData(contentHash, path.Length, randomSize, TimeSpan.FromMinutes(30));
            var originalMachineData = GetMachineRedisData(path);

            return TestAsync(
                context,
                GetRedisDatabase(_clock, new Dictionary<RedisKey, RedisValue>(originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value))),
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    // Execute: Update the record's expiry with the default bump time - 1hr.
                    var updateResult = await store.TouchBulkAsync(context, new[] { new ContentHashWithSize(contentHash, randomSize) }, CancellationToken.None, UrgencyHint.Nominal);
                    Assert.Equal(BoolResult.Success, updateResult);

                    // Verify: The expiry should be approximately an hour from now.
                    var redisStore = store as RedisContentLocationStore;
                    var expiryResult = await redisStore.GetContentHashExpiryAsync(context, contentHash, CancellationToken.None);
                    Assert.NotNull(expiryResult); // Expiry should be set
                    Assert.InRange(expiryResult.Value, redisStore.ContentHashBumpTime - TimeSpan.FromMinutes(1), redisStore.ContentHashBumpTime); // Expiry should be approximately an hour
                });
        }

        [Fact]
        public Task TouchBulkNewRecord()
        {
            // Setup: Location record is not added to Redis before TouchBulk is called.
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            var randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;
            var hashInfo = CreateContentHashWithSizeAndLocations(contentHash, path, randomSize);

            return TestAsync(
                context,
                GetRedisDatabase(_clock),
                GetRedisDatabase(_clock),
                async (store, storeFactory) =>
                {
                    // Execute: Attempt to touch non-existing content. Create the record's expiry with the default bump time - 1hr.
                    var updateResult = await store.TouchBulkAsync(context, new[] { new ContentHashWithSize(contentHash, randomSize) }, CancellationToken.None, UrgencyHint.Nominal);
                    Assert.Equal(BoolResult.Success, updateResult);

                    // Verify: Location record should now exist in Redis.
                    var bulkResult = await store.GetBulkAsync(context, new[] { contentHash }, CancellationToken.None, UrgencyHint.Nominal);
                    Assert.True(bulkResult.Succeeded);
                    Assert.Equal(1, bulkResult.ContentHashesInfo.Count);

                    var hashResult = bulkResult.ContentHashesInfo.Single();
                    Assert.Equal(contentHash, hashResult.ContentHash);
                    Assert.Equal(1, hashResult.Locations.Count); // Hash is registered at one location

                    var redisStore = store as RedisContentLocationStore;
                    var expiryResult = await redisStore.GetContentHashExpiryAsync(context, contentHash, CancellationToken.None);
                    Assert.NotNull(expiryResult); // Expiry should be set
                    Assert.InRange(expiryResult.Value, redisStore.ContentHashBumpTime - TimeSpan.FromMinutes(1), redisStore.ContentHashBumpTime); // Expiry should be approximately an hour
                });
        }

        [Fact]
        public Task GetDistributedLastAccessTimeWithNoMatchesAndEnoughReplicas()
        {
            /* Setup:
            // Default bump time in 1hr. First hash added to originalRedisData is 2hr.
            // We want to test that the first hash will have greater last use time. */

            var paths = CreatePaths(5); // Default minimum number of replicas is 4
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            var originalRedisData = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromHours(2));
            var originalMachineData = GetMachineRedisData(paths);
            return TestAsync(
                context,
                GetRedisDatabase(_clock, originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value), originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value)),
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    var contentHash2 = ContentHash.Random();
                    var path2 = CreatePaths(1);

                    var contentHashInfo = CreateContentHashWithSizeAndLocations(contentHash2, path2, randomSize);
                    await store.UpdateBulkAsync(context, contentHashInfo, CancellationToken.None, UrgencyHint.Nominal, LocationStoreOption.UpdateExpiry).ShouldBeSuccess();

                    var contentHashInfo1 = Tuple.Create(new ContentHashWithLastAccessTimeAndReplicaCount(contentHash, DateTime.UtcNow), true);
                    var contentHashInfo2 = Tuple.Create(new ContentHashWithLastAccessTimeAndReplicaCount(contentHash2, DateTime.UtcNow), true);

                    // Execute: Provided last-access time is behind the datacenter
                    var result = await store.TrimOrGetLastAccessTimeAsync(
                        context,
                        new[] { contentHashInfo1, contentHashInfo2 },
                        cts: CancellationToken.None,
                        urgencyHint: UrgencyHint.Nominal);

                    var redisStore = store as RedisContentLocationStore;

                    // Verify: Should return remote last-access time because the provided time isn't up-to-date.
                    Assert.True(result.Succeeded);
                    Assert.Equal(2, result.Data.Count);
                    Assert.False(result.Data.First().SafeToEvict);
                    Assert.True(result.Data.First().LastAccessTime > result.Data.Last().LastAccessTime);
                    Assert.Equal(5, result.Data.First().ReplicaCount);

                    var range = _clock.UtcNow + TimeSpan.FromHours(2) - redisStore.ContentHashBumpTime;
                    Assert.InRange(result.Data.First().LastAccessTime, range - TimeSpan.FromSeconds(20), range);

                    range = _clock.UtcNow + TimeSpan.FromHours(1) - redisStore.ContentHashBumpTime;
                    Assert.False(result.Data.Last().SafeToEvict);
                    Assert.InRange(result.Data.Last().LastAccessTime, range - TimeSpan.FromSeconds(20), range);
                    Assert.Equal(1, result.Data.Last().ReplicaCount);
                });
        }

        [Fact]
        public Task GetDistributedLastAccessTimeWithMatchAndEnoughReplicas()
        {
            var paths = CreatePaths(4); // Default minimum number of replicas is 4
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            var originalRedisData = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromMinutes(1));
            var originalMachineData = GetMachineRedisData(paths);
            return TestAsync(
                context,
                GetRedisDatabase(_clock, originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value), originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value)),
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    var contentHashInfo = Tuple.Create(new ContentHashWithLastAccessTimeAndReplicaCount(contentHash, DateTime.UtcNow + TimeSpan.FromMinutes(2)), true);

                    // Execute: Provided last-access time is in-sync remote last-access time and exists at a sufficeint number of replicas.
                    var result = await store.TrimOrGetLastAccessTimeAsync(
                        context,
                        new[] { contentHashInfo },
                        cts: CancellationToken.None,
                        urgencyHint: UrgencyHint.Nominal).ShouldBeSuccess();

                    // Verify
                    Assert.True(result.Succeeded);
                    Assert.Equal(1, result.Data.Count);
                    Assert.True(result.Data.First().SafeToEvict);
                    Assert.Equal(4, result.Data.First().ReplicaCount);
                });
        }

        [Fact]
        public Task GetDistributedLastAccessTimeWithMatchAndNotEnoughReplicas()
        {
            var paths = CreatePaths(1); // Default minimum number of replicas is 2
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            var originalRedisData = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromMinutes(1));
            var originalMachineData = GetMachineRedisData(paths);
            return TestAsync(
                context,
                GetRedisDatabase(_clock, originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value), originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value)),
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    var contentHashInfo = Tuple.Create(new ContentHashWithLastAccessTimeAndReplicaCount(contentHash, DateTime.UtcNow + TimeSpan.FromMinutes(2)), true);

                    // Execute: Provided last-access time is in-sync with remote last-access time but does not exist at enough replicas.
                    var result = await store.TrimOrGetLastAccessTimeAsync(
                        context,
                        new[] { contentHashInfo },
                        cts: CancellationToken.None,
                        urgencyHint: UrgencyHint.Nominal);

                    Assert.True(result.Succeeded);
                    Assert.Equal(1, result.Data.Count);

                    // Verify: Should return the remote last-access time even though the access times are in-sync because the minimum replica count is not met.
                    var redisStore = store as RedisContentLocationStore;
                    var range = _clock.UtcNow + TimeSpan.FromMinutes(1) - redisStore.ContentHashBumpTime;
                    Assert.InRange(result.Data.First().LastAccessTime, range - TimeSpan.FromSeconds(20), range);
                    Assert.Equal(result.Data.First().ReplicaCount, 1);
                });
        }

        [Fact]
        public Task GetDistributedLastAccessTimeWithMatchAndNoReplicaCheck()
        {
            var paths = CreatePaths(1); // Default minimum number of replicas is 2
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            var originalRedisData = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromMinutes(1));
            var originalMachineData = GetMachineRedisData(paths);
            return TestAsync(
                context,
                GetRedisDatabase(_clock, originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value), originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value)),
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    var contentHashInfo = Tuple.Create(new ContentHashWithLastAccessTimeAndReplicaCount(contentHash, DateTime.UtcNow + TimeSpan.FromMinutes(2)), false);

                    // Execute: Provided last-access time is in-sync with remote last-access time but does not exist at enough replicas.
                    var result = await store.TrimOrGetLastAccessTimeAsync(
                        context,
                        new[] { contentHashInfo },
                        cts: CancellationToken.None,
                        urgencyHint: UrgencyHint.Nominal);

                    // Verify
                    Assert.True(result.Succeeded);
                    Assert.Equal(1, result.Data.Count);
                    Assert.True(result.Data.First().SafeToEvict);
                    Assert.Equal(1, result.Data.First().ReplicaCount);
                });
        }

        [Fact]
        public Task GetBulkExistingContentDoesNotUpdateTimespan()
        {
            // Setup
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            // key is available longer than bump threshold.
            var originalRedisData = GetRedisData(contentHash, path.Length, randomSize, TimeSpan.FromHours(8));
            var originalMachineData = GetMachineRedisData(path);
            var mockContentDatabase =
                GetRedisDatabase(
                    _clock,
                    new Dictionary<RedisKey, RedisValue>(originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                    new Dictionary<RedisKey, DateTime>(originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value)));
            return TestAsync(
                context,
                mockContentDatabase,
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    // Execute
                    var getResult = await store.GetBulkAsync(context, new[] { contentHash }, CancellationToken.None, UrgencyHint.Nominal);

                    // Verify
                    Assert.True(getResult.Succeeded);
                    var redisData = storeFactory.RedisDatabase.GetDbWithExpiry();
                    redisData.Should().Equal(originalRedisData);
                    var machineData = storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry();
                    var expectedMachineData = GetMachineRedisData(path);

                    var foundMachineRedisData = storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry();
                    expectedMachineData.Count.Should().Be(machineData.Count);
                    foreach (var kvp in expectedMachineData)
                    {
                        machineData.TryGetValue(kvp.Key, out MockRedisValueWithExpiry mockRedisValueWithExpiry).Should().BeTrue();

                        // Don't validate expiry here - GetMachineRedisData will always return null expiries
                        kvp.Value.Value.Should().Be(mockRedisValueWithExpiry.Value);
                    }
                });
        }

        [Fact]
        public Task GetBulkExistingContentUpdatesTimespanWhenUnderThreshold()
        {
            // Setup
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            // key is available longer than bump threshold.
            var originalRedisData = GetRedisData(contentHash, path.Length, randomSize, TimeSpan.FromHours(1));
            var originalMachineData = GetMachineRedisData(path);
            var mockContentDatabase =
                GetRedisDatabase(
                    _clock,
                    new Dictionary<RedisKey, RedisValue>(originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                    new Dictionary<RedisKey, DateTime>(originalRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value)));
            return TestAsync(
                context,
                mockContentDatabase,
                GetRedisDatabase(_clock, originalMachineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    // Execute
                    var getResult = await store.GetBulkAsync(context, new[] { contentHash }, CancellationToken.None, UrgencyHint.Nominal);

                    // Verify
                    Assert.True(getResult.Succeeded);

                    var expectedRedisKey = GetKey(Convert.ToBase64String(contentHash.ToHashByteArray()));
                    originalRedisData[expectedRedisKey] = new MockRedisValueWithExpiry(
                        originalRedisData[expectedRedisKey].Value,
                        _clock.UtcNow + storeFactory.BumpTime);

                    originalRedisData.Should().Equal(storeFactory.RedisDatabase.GetDbWithExpiry());

                    var expectedMachineRedisData = GetMachineRedisData(path);
                    var foundMachineRedisData = storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry();
                    expectedMachineRedisData.Count.Should().Be(foundMachineRedisData.Count);
                    foreach (var kvp in expectedMachineRedisData)
                    {
                        foundMachineRedisData.TryGetValue(kvp.Key, out MockRedisValueWithExpiry mockRedisValueWithExpiry).Should().BeTrue();

                        // Don't validate expiry here - GetMachineRedisData will always return null expiries
                        kvp.Value.Value.Should().Be(mockRedisValueWithExpiry.Value);
                    }
                });
        }

        [Fact]
        public Task UpdateBulkExistingContentNewReplica()
        {
            var paths = CreatePaths(2);

            // Setup
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            var existingRedisData = GetRedisData(contentHash, 0, randomSize, TimeSpan.FromHours(1)); // Only add one replica to Redis
            var existingMachineRedisData = GetMachineRedisData(new string[0]);

            return TestAsync(
                context,
                GetRedisDatabase(_clock, existingRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                GetRedisDatabase(_clock, existingMachineRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    // Add new replica to Redis
                    var contentHashInfo = CreateContentHashWithSizeAndLocations(contentHash, paths, randomSize);

                    // Execute
                    var updateResult = await store.UpdateBulkAsync(context, contentHashInfo, CancellationToken.None, UrgencyHint.Nominal, LocationStoreOption.UpdateExpiry);

                    // Verify
                    Assert.Equal(BoolResult.Success, updateResult);
                    var expectedRedisData = GetRedisData(contentHash, paths.Length, randomSize, storeFactory.BumpTime);
                    expectedRedisData.Should().Equal(storeFactory.RedisDatabase.GetDbWithExpiry());

                    var expectedMachineRedisData = GetMachineRedisData(paths);
                    var foundMachineRedisData = storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry();
                    expectedMachineRedisData.Count.Should().Be(foundMachineRedisData.Count);
                    foreach (var kvp in expectedMachineRedisData)
                    {
                        foundMachineRedisData.TryGetValue(kvp.Key, out MockRedisValueWithExpiry mockRedisValueWithExpiry).Should().BeTrue();

                        // Don't validate expiry here - GetMachineRedisData will always return null expiries
                        kvp.Value.Value.Should().Be(mockRedisValueWithExpiry.Value);
                    }
                });
        }

        [Fact]
        public async Task GetBulkAllContentFound()
        {
            List<ContentHashWithSizeAndLocations> contentHashInfo = null;
            var getBulkResult = await GetBulkFromReplicas(
                hashesAndLocations =>
                {
                    // Query for existing hash
                    contentHashInfo = hashesAndLocations;
                    return hashesAndLocations.Select(p => p.ContentHash).ToList();
                },
                1);

            // Verify
            var result = getBulkResult.ContentHashesInfo;
            Assert.NotNull(contentHashInfo);
            Assert.Equal(1, result.Count);

            Assert.Equal(result[0].ContentHash, contentHashInfo[0].ContentHash);
            Assert.Equal(contentHashInfo[0].Size, result[0].Size);
            Assert.Equal(1, result[0].Locations.Count);
            Assert.Equal(GetContentPath(contentHashInfo[0]), result[0].Locations.First().ToString());
        }

        [Fact]
        public async Task GetBulkSomeContentFound()
        {
            List<ContentHashWithSizeAndLocations> contentHashInfo = null;
            var getBulkResult = await GetBulkFromReplicas(
                hashesAndLocations =>
                {
                    // Query for a new random hash in addition to existing hash
                    contentHashInfo = hashesAndLocations;
                    var contentHashes = hashesAndLocations.Select(p => p.ContentHash).ToList();
                    contentHashes.Add(ContentHash.Random());
                    return contentHashes;
                },
                1);

            var result = getBulkResult.ContentHashesInfo;
            Assert.NotNull(contentHashInfo);
            Assert.Equal(2, result.Count);

            // Verify existing hash
            Assert.Equal(contentHashInfo[0].ContentHash, result[0].ContentHash);
            Assert.Equal(contentHashInfo[0].Size, result[0].Size);
            Assert.Equal(1, result[0].Locations.Count);
            Assert.Equal(GetContentPath(contentHashInfo[0]), result[0].Locations.First().Path);

            // Verify random hash not found
            Assert.NotNull(result[1]);
            Assert.Null(result[1].Locations);
        }

        [Fact]
        public async Task GetBulkNoContentFound()
        {
            List<ContentHashWithSizeAndLocations> contentHashInfo = null;
            var getBulkResult = await GetBulkFromReplicas(
                hashesAndLocations =>
                {
                    // Query for a random hash
                    contentHashInfo = hashesAndLocations;
                    return new List<ContentHash> { ContentHash.Random() };
                },
                1);

            // Verify
            var result = getBulkResult.ContentHashesInfo;
            Assert.NotNull(contentHashInfo);
            Assert.NotNull(result);
            Assert.Equal(1, result.Count);

            Assert.NotNull(result[0]);
            Assert.Null(result[0].Locations);
        }

        [Fact]
        public async Task GetBulkInvalidLocationNoContentFoundMultipleHash()
        {
            // Two content hash queries: one has invalid location, the other not in Redis
            List<ContentHashWithSizeAndLocations> contentHashInfo = null;
            var getBulkResult = await GetBulkFromReplicas(
                hashesAndLocations =>
                {
                    contentHashInfo = hashesAndLocations;
                    var contentHashes = hashesAndLocations.Select(p => p.ContentHash).ToList();
                    contentHashes.Add(ContentHash.Random());
                    return contentHashes;
                },
                1,
                ValidType.None);

            var result = getBulkResult.ContentHashesInfo;
            Assert.NotNull(contentHashInfo);
            Assert.Equal(2, result.Count);

            // Verify: No locations for content hash
            Assert.NotNull(result[0]);
            Assert.Equal(0, result[0].Locations.Count);

            // Verify: Unable to retrieve info for invalid content hash
            Assert.NotNull(result[1]);
            Assert.Null(result[1].Locations);
        }

        [Fact]
        public async Task GetBulkInvalidLocationNoContentFound()
        {
            // Content hash query with invalid location
            List<ContentHashWithSizeAndLocations> contentHashInfo = null;
            var getBulkResult = await GetBulkFromReplicas(
                hashesAndLocations =>
                {
                    contentHashInfo = hashesAndLocations;
                    return hashesAndLocations.Select(p => p.ContentHash).ToList();
                },
                1,
                ValidType.None);

            var result = getBulkResult.ContentHashesInfo;
            Assert.NotNull(contentHashInfo);
            Assert.NotNull(result);
            Assert.Equal(1, result.Count);

            // Verify: No valid locations for content hash
            Assert.NotNull(result[0]);
            Assert.Equal(0, result[0].Locations.Count);
        }

        [Fact]
        public async Task GetBulkInvalidLocationSomeContentFound()
        {
            // Content hash query has two locations: one invalid, one with valid location
            List<ContentHashWithSizeAndLocations> contentHashInfo = null;
            var getBulkResult = await GetBulkFromReplicas(
                hashesAndLocations =>
                {
                    contentHashInfo = hashesAndLocations;
                    return hashesAndLocations.Select(p => p.ContentHash).ToList();
                },
                2,
                ValidType.Partial);

            // Verify: Only the valid path is returned
            var result = getBulkResult.ContentHashesInfo;
            Assert.NotNull(contentHashInfo);
            Assert.NotNull(result);
            Assert.Equal(1, result.Count);

            Assert.Equal(contentHashInfo[0].ContentHash, result[0].ContentHash);
            Assert.Equal(contentHashInfo[0].Size, result[0].Size);
            Assert.Equal(1, result[0].Locations.Count);
            Assert.Equal(GetContentPath(contentHashInfo[0]), result[0].Locations.First().Path);
        }

        [Fact]
        public async Task GetBulkCheckRandomizedReplicas()
        {
            int numReplicas = 5;
            bool checkRandomized = false;

            // Randomize locations five times to bypass edge case where randomization results in same ordered array
            for (int i = 0; i < 5; i++)
            {
                // Content hash query has five locations, all valid
                List<ContentHashWithSizeAndLocations> contentHashInfo = null;
                var getBulkResult = await GetBulkFromReplicas(
                    hashesAndLocations =>
                    {
                        contentHashInfo = hashesAndLocations;
                        return hashesAndLocations.Select(p => p.ContentHash).ToList();
                    },
                    numReplicas);

                var result = getBulkResult.ContentHashesInfo;
                Assert.NotNull(contentHashInfo);
                Assert.NotNull(result);
                Assert.Equal(1, result.Count);

                Assert.Equal(contentHashInfo[0].ContentHash, result[0].ContentHash);
                Assert.Equal(contentHashInfo[0].Size, result[0].Size);
                Assert.Equal(numReplicas, result[0].Locations.Count);

                Assert.True(CheckSameContent(numReplicas, result, contentHashInfo));
                if (CheckDifferentOrder(numReplicas, result, contentHashInfo))
                {
                    checkRandomized = true;
                    break;
                }
            }

            Assert.True(checkRandomized);
        }

        [Fact]
        public Task UpdateAndGet()
        {
            // Setup
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);

            return TestAsync(
                context,
                GetRedisDatabase(_clock),
                GetRedisDatabase(_clock),
                async storeFactory =>
                {
                    var stores = new List<IContentLocationStore>();
                    for (var i = 0; i < 2; i++)
                    {
                        stores.Add(await storeFactory.CreateAsync());
                    }

                    foreach (var store in stores)
                    {
                        var r = await store.StartupAsync(context);
                        r.ShouldBeSuccess();
                    }

                    long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;
                    var contentHashInfo = CreateContentHashWithSizeAndLocations(ContentHash.Random(), path, randomSize);

                    // Execute
                    await stores[1].UpdateBulkAsync(context, contentHashInfo, CancellationToken.None, UrgencyHint.Nominal, LocationStoreOption.UpdateExpiry).ShouldBeSuccess();
                    var result =
                        await stores[0].GetBulkAsync(context, contentHashInfo.Select(p => p.ContentHash).ToList(), CancellationToken.None, UrgencyHint.Nominal);

                    // Verify
                    Assert.NotNull(result);
                    Assert.True(result.Succeeded);
                    Assert.Equal(1, result.ContentHashesInfo.Count);

                    Assert.Equal(contentHashInfo[0].ContentHash, result.ContentHashesInfo[0].ContentHash);
                    Assert.Equal(result.ContentHashesInfo[0].Size, randomSize);
                    Assert.Equal(1, result.ContentHashesInfo[0].Locations.Count);
                    Assert.Equal(GetContentPath(contentHashInfo[0]), result.ContentHashesInfo[0].Locations.First().Path);

                    foreach (var store in stores)
                    {
                        var r = await store.ShutdownAsync(context);
                        r.ShouldBeSuccess();
                        store.Dispose();
                    }
                });
        }



        [Fact]
        public async Task TestRedisGarbageCollection()
        {
            if (Environment.GetEnvironmentVariable("TestConnectionString") == null)
            {
                return;
            }

            // Setup
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);

            var storeFactory = new RedisContentLocationStoreFactory(
                new EnvironmentConnectionStringProvider("TestConnectionString"),
                new EnvironmentConnectionStringProvider("TestConnectionString2"),
                _clock,
                TimeSpan.FromDays(4),
                "DM_S1CBPrefix", /* NOTE: This value may need to be changed if configured prefix is different for target environment. Find by using slowlog get 10 in redis console and find common prefix of commands */
                //"MW_S9PD", /* NOTE: This value may need to be changed if configured prefix is different for target environment. Find by using slowlog get 10 in redis console and find common prefix of commands */
                new PassThroughFileSystem(TestGlobal.Logger),
                new RedisContentLocationStoreConfiguration()
                {
                    Database =  new RocksDbContentLocationDatabaseConfiguration(new AbsolutePath(@"D:\Dumps\CacheDump2"))
                }
                );

            var r = await storeFactory.StartupAsync(context);
            r.ShouldBeSuccess();

            var store = (RedisContentLocationStore)await storeFactory.CreateAsync(new MachineLocation("TestMachine"));
            r = await store.StartupAsync(context);
            r.ShouldBeSuccess();

            await store.GarbageCollectAsync(new OperationContext(context)).ShouldBeSuccess();

            await store.ShutdownAsync(context).ShouldBeSuccess();
        }

        [Fact]
        public async Task TetGetInfoAsync()
        {
            if (Environment.GetEnvironmentVariable("TestConnectionString") == null)
            {
                return;
            }

            // Setup
            var path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);

            var storeFactory = new RedisContentLocationStoreFactory(
                new EnvironmentConnectionStringProvider("TestConnectionString"),
                new EnvironmentConnectionStringProvider("TestConnectionString2"),
                _clock,
                TimeSpan.FromDays(4),
                "DM_S1CBPrefix", /* NOTE: This value may need to be changed if configured prefix is different for target environment. Find by using slowlog get 10 in redis console and find common prefix of commands */
                //"MW_S9PD", /* NOTE: This value may need to be changed if configured prefix is different for target environment. Find by using slowlog get 10 in redis console and find common prefix of commands */
                new PassThroughFileSystem(TestGlobal.Logger),
                new RedisContentLocationStoreConfiguration()
                {
                    Database =  new RocksDbContentLocationDatabaseConfiguration(new AbsolutePath(@"D:\Dumps\CacheDump2"))
                }
                );

            var r = await storeFactory.StartupAsync(context);
            r.ShouldBeSuccess();

            var store = (RedisContentLocationStore)await storeFactory.CreateAsync(new MachineLocation("TestMachine"));
            r = await store.StartupAsync(context);
            r.ShouldBeSuccess();

            await store.GetRedisInfoAsync(new OperationContext(context)).ShouldBeSuccess();

            await store.ShutdownAsync(context).ShouldBeSuccess();
        }

        [Fact]
        public Task TrimBulk()
        {
            // Setup
            string[] paths = CreatePaths(2);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            var existingRedisData = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromHours(3));
            var machineData = GetMachineRedisData(paths);

            var mockRedisDatabase = GetRedisDatabase(
                _clock,
                existingRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value),
                existingRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value));
            return TestAsync(
                context,
                mockRedisDatabase,
                GetRedisDatabase(_clock, machineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    var absolutePath = paths[1];
                    var contentHashesAndLocations = CreateContentHashesAndLocations(contentHash, new[] { absolutePath });

                    // Execute
                    var trimResult = await store.TrimBulkAsync(context, contentHashesAndLocations, CancellationToken.None, UrgencyHint.Nominal);

                    // Verify
                    Assert.Equal(BoolResult.Success, trimResult);

                    // Change bitmask to remove trimmed location
                    var expectedRedisData = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromHours(3));
                    byte[] expectedBitMask = new byte[9];
                    var sizeByteArray = LongToByteArray(randomSize);
                    Array.Copy(sizeByteArray, expectedBitMask, sizeByteArray.Length);
                    var expectedRedisKey = GetKey(Convert.ToBase64String(contentHash.ToHashByteArray()));
                    expectedBitMask[8] = 0x40;
                    expectedRedisData[expectedRedisKey] = new MockRedisValueWithExpiry(expectedBitMask, expectedRedisData[expectedRedisKey].Expiry);

                    var foundRedisData = storeFactory.RedisDatabase.GetDbWithExpiry();
                    foundRedisData.Count.Should().Be(expectedRedisData.Count);
                    foreach (var kvp in foundRedisData)
                    {
                        expectedRedisData.TryGetValue(kvp.Key, out MockRedisValueWithExpiry expectedMockRedisValueWithExpiry).Should().BeTrue();
                        kvp.Value.Should().Be(expectedMockRedisValueWithExpiry);
                    }

                    var foundMachineRedisData = storeFactory.MachineLocationRedisDatabase.GetDbWithExpiry();
                    var expectedMachineRedisData = GetMachineRedisData(paths);
                    foundMachineRedisData.Count.Should().Be(expectedMachineRedisData.Count);
                    foreach (var kvp in foundMachineRedisData)
                    {
                        expectedMachineRedisData.TryGetValue(kvp.Key, out MockRedisValueWithExpiry expectedMockRedisValueWithExpiry).Should().BeTrue();

                        // Don't validate expiry here - GetMachineRedisData will always return null expiries
                        kvp.Value.Value.Should().Be(expectedMockRedisValueWithExpiry.Value);
                    }
                });
        }

        [Fact]
        public Task TrimBulkLocal()
        {
            // Setup
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            return TestAsync(
                context,
                GetRedisDatabase(_clock),
                GetRedisDatabase(_clock),
                async (store, storeFactory) =>
                {
                    var redisStore = store as RedisContentLocationStore;

                    // Add local location to content tracker
                    var updateResult = await store.UpdateBulkAsync(context, new[] { new ContentHashWithSizeAndLocations(contentHash, randomSize), }, CancellationToken.None, UrgencyHint.Nominal, LocationStoreOption.UpdateExpiry);
                    updateResult.ShouldBeSuccess();

                    // Trim local store's record
                    var trimResult = await redisStore.TrimBulkAsync(context, new[] { contentHash }, CancellationToken.None, UrgencyHint.Nominal);
                    trimResult.ShouldBeSuccess();

                    // Because the content was unique to one location, trimming should result in a deleted key
                    var result = await redisStore.GetContentHashExpiryAsync(context, contentHash, CancellationToken.None); // Returns null if key doesn't exist
                    Assert.Null(result);
                });
        }

        [Fact]
        public Task TrimBulkLeavesZeroLocationsDeletesFromRedis()
        {
            string[] path = CreatePaths(1);
            var context = new Context(TestGlobal.Logger);
            var contentHash = ContentHash.Random();
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            var existingRedisData = GetRedisData(contentHash, path.Length, randomSize, TimeSpan.FromHours(3));
            var machineData = GetMachineRedisData(path);

            var mockRedisDatabase = GetRedisDatabase(
                _clock,
                existingRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value),
                existingRedisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Expiry.Value));

            return TestAsync(
                context,
                mockRedisDatabase,
                GetRedisDatabase(_clock, machineData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
            {
                var redisStore = store as RedisContentLocationStore;
                var result = await redisStore.GetContentHashExpiryAsync(context, contentHash, CancellationToken.None); // Check key exists in store
                Assert.NotNull(result);

                // Content only exists at one location so TrimBulk should remove key from Redis
                var contentHashesAndLocations = CreateContentHashesAndLocations(contentHash, path);
                var trimResult = await store.TrimBulkAsync(context, contentHashesAndLocations, CancellationToken.None, UrgencyHint.Nominal);
                Assert.Equal(BoolResult.Success, trimResult);

                result = await redisStore.GetContentHashExpiryAsync(context, contentHash, CancellationToken.None); // Returns null if key doesn't exist
                Assert.Null(result);
            });
        }

        [Fact]
        public Task IdentityUpdate()
        {
            var context = new Context(TestGlobal.Logger);

            return TestAsync(
                context,
                GetRedisDatabase(_clock),
                GetRedisDatabase(_clock),
                async (store, storeFactory) =>
                {
                    var redisContentLocationStore = (RedisContentLocationStore)store;
                    await redisContentLocationStore.UpdateIdentityAsync(context);

                    var expectedLocalMachineData = storeFactory.PathTransformer.GetLocalMachineLocation(DefaultTempRoot);
                    using (var hasher = HashInfoLookup.Find(HashType.SHA256).CreateContentHasher())
                    {
                        string expectedLocalMachineDataHash =
                            Convert.ToBase64String(hasher.GetContentHash(expectedLocalMachineData).ToHashByteArray());
                        RedisValue redisValue = await storeFactory.MachineLocationRedisDatabase.StringGetAsync(
                                GetKey($"{RedisContentLocationStoreConstants.ContentLocationKeyPrefix}{expectedLocalMachineDataHash}"));

                        redisValue.Should().NotBe(RedisValue.Null);
                        var id = (long)redisValue;
                        id.Should().BeGreaterOrEqualTo(0);

                        redisValue = await storeFactory.MachineLocationRedisDatabase.StringGetAsync(
                            GetKey($"{RedisContentLocationStoreConstants.ContentLocationIdPrefix}{id}"));
                        redisValue.Should().NotBe(RedisValue.Null);

                        byte[] foundMachineLocation = redisValue;
                        foundMachineLocation.Should().Equal(expectedLocalMachineData);
                    }
                });
        }

        private async Task<GetBulkLocationsResult> GetBulkFromReplicas(Func<List<ContentHashWithSizeAndLocations>, List<ContentHash>> hashesToQuery, int numReplicas, ValidType valid = ValidType.All)
        {
            var contentHash = ContentHash.Random();
            var context = new Context(TestGlobal.Logger);
            var paths = CreatePaths(numReplicas);
            long randomSize = (long)ThreadSafeRandom.Generator.NextDouble() * long.MaxValue;

            GetBulkLocationsResult locationsResult = null;
            IDictionary<RedisKey, MockRedisValueWithExpiry> redisData = GetRedisData(contentHash, paths.Length, randomSize, TimeSpan.FromHours(1));
            await TestAsync(
                context,
                GetRedisDatabase(_clock, redisData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                GetRedisDatabase(_clock, GetMachineRedisData(paths, valid).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value)),
                async (store, storeFactory) =>
                {
                    var contentHashInfo = CreateContentHashWithSizeAndLocations(contentHash, paths, randomSize);
                    var contentHashes = hashesToQuery(contentHashInfo);
                    locationsResult =
                        await store.GetBulkAsync(context, contentHashes, CancellationToken.None, UrgencyHint.Nominal);
                });
            locationsResult.Should().NotBeNull();
            locationsResult.ContentHashesInfo.Should().NotBeNull();

            return locationsResult;
        }

        protected string[] CreatePaths(int replicaCount)
        {
            string pathPrefix = @"Z:\Temp";
            string[] paths = new string[replicaCount];

            for (int i = 0; i < replicaCount; i++)
            {
                paths[i] = pathPrefix + i;
            }

            return paths;
        }

        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        protected List<ContentHashAndLocations> CreateContentHashesAndLocations(ContentHash contentHash, string[] paths)
        {
            var absolutePaths = new List<MachineLocation>();
            for (var i = 0; i < paths.Length; i++)
            {
                absolutePaths.Add(new MachineLocation(paths[i]));
            }

            return new List<ContentHashAndLocations>
            {
                new ContentHashAndLocations(contentHash, absolutePaths)
            };
        }

        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        protected List<ContentHashWithSizeAndLocations> CreateContentHashWithSizeAndLocations(ContentHash contentHash, string[] paths, long contentSize)
        {
            var absolutePaths = new List<MachineLocation>();
            for (var i = 0; i < paths.Length; i++)
            {
                absolutePaths.Add(new MachineLocation(paths[i]));
            }

            return new List<ContentHashWithSizeAndLocations>
            {
                new ContentHashWithSizeAndLocations(contentHash, contentSize, absolutePaths)
            };
        }

        protected IDictionary<RedisKey, MockRedisValueWithExpiry> GetRedisData(ContentHash contentHash, int pathsLength, long size, TimeSpan bumpTime)
        {
            var sizeAndLocationBitMask = new byte[9];
            var sizeByteArray = LongToByteArray(size);

            // TODO: Use RedisContentLocationStore to store the data instead of constructing blobs directly. (bug 1365340)
            Array.Copy(sizeByteArray, sizeAndLocationBitMask, sizeByteArray.Length);
            sizeAndLocationBitMask[8] = (byte)(0x80 - (1 << (7 - pathsLength)));

            var redisData = new Dictionary<RedisKey, MockRedisValueWithExpiry>
            {
                { GetKey(Convert.ToBase64String(contentHash.ToHashByteArray())), new MockRedisValueWithExpiry(sizeAndLocationBitMask, _clock.UtcNow + bumpTime) },
            };

            return redisData;
        }

        protected IDictionary<RedisKey, MockRedisValueWithExpiry> GetMachineRedisData(string[] paths, ValidType valid = ValidType.All)
        {
            var redisData = new Dictionary<RedisKey, MockRedisValueWithExpiry>
            {
                { GetKey(RedisContentLocationStoreConstants.MaxContentLocationId), new MockRedisValueWithExpiry(paths.Length, null) },
            };

            // Set to false when testing for only invalid paths
            bool isValid = valid != ValidType.None;
            int id = 1;
            foreach (string path in paths)
            {
                var hashOfPath = _contentHasher.GetContentHash(Encoding.Default.GetBytes(path));
                if (isValid)
                {
                    redisData.Add(GetKey(RedisContentLocationStoreConstants.ContentLocationIdPrefix + id), new MockRedisValueWithExpiry(path, null));
                }

                // Alternates between valid and invalid paths for testing
                if (valid == ValidType.Partial)
                {
                    isValid = !isValid;
                }

                redisData.Add(GetKey(RedisContentLocationStoreConstants.ContentLocationKeyPrefix + Convert.ToBase64String(hashOfPath.ToHashByteArray())), new MockRedisValueWithExpiry(id, null));
                id++;
            }

            return redisData;
        }

        protected static RedisKey GetKey(RedisKey key)
        {
            return key.Prepend(DefaultKeySpace);
        }

        protected string GetContentPath(ContentHashWithSizeAndLocations hashAndLocation, int index = 0)
        {
            return hashAndLocation.Locations[index].Path;
        }

        private bool CheckDifferentOrder(int numReplicas, IReadOnlyList<ContentHashWithSizeAndLocations> result, IList<ContentHashWithSizeAndLocations> contentInfo)
        {
            int numMatch = 0;
            for (int i = 0; i < numReplicas; i++)
            {
                string resultPath = result[0].Locations[i].Path;
                string originalPath = GetContentPath(contentInfo[0], i);
                if (originalPath == resultPath)
                {
                    numMatch++;
                }
            }

            return numMatch != numReplicas;
        }

        private bool CheckSameContent(int numReplicas, IReadOnlyList<ContentHashWithSizeAndLocations> result, IList<ContentHashWithSizeAndLocations> contentInfo)
        {
            // Get absolute paths of original content locations and compare to bulk results
            var originalContentHash = contentInfo[0].ContentHash;
            var originalLocations = contentInfo[0].Locations.Select(item => item.Path).ToList();

            for (var i = 0; i < numReplicas; i++)
            {
                string resultPath = result[0].Locations[i].Path;
                if (!originalLocations.Contains(resultPath))
                {
                    return false;
                }
            }

            return true;
        }

        private byte[] LongToByteArray(long size)
        {
            byte[] bytes = BitConverter.GetBytes(size);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        protected virtual RedisContentLocationStoreConfiguration DefaultConfiguration => null;

        protected Task TestAsync(
            Context context,
            ITestRedisDatabase mockRedisDatabase,
            ITestRedisDatabase mockMachineRedisDatabase,
            Func<IContentLocationStore, MockRedisContentLocationStoreFactory, Task> testFuncAsync,
            RedisContentLocationStoreConfiguration configuration = null)
        {
            return TestAsync(
                context,
                mockRedisDatabase,
                mockMachineRedisDatabase,
                async storeFactory =>
                {
                    using (var store = await storeFactory.CreateAsync())
                    {
                        try
                        {
                            var redisStore = (RedisContentLocationStore)store;

                            // The tests which use GetRedisData don't interact well with something adding to Redis in the background.
                            redisStore.DisableHeartbeat = true;
                            BoolResult result = await store.StartupAsync(context);
                            result.ShouldBeSuccess();

                            await testFuncAsync(store, storeFactory);

                            result = await store.ShutdownAsync(context);
                            result.ShouldBeSuccess();
                        }
                        finally
                        {
                            TestGlobal.Logger.Flush();
                        }
                    }
                },
                configuration: configuration ?? DefaultConfiguration);
        }

        private async Task TestAsync(
            Context context,
            ITestRedisDatabase mockRedisDatabase,
            ITestRedisDatabase mockMachineRedisDatabase,
            Func<MockRedisContentLocationStoreFactory, Task> testFuncAsync,
            RedisContentLocationStoreConfiguration configuration = null)
        {
            configuration = configuration ?? RedisContentLocationStoreConfiguration.Default;

            using (var storeFactory = new MockRedisContentLocationStoreFactory(
                mockRedisDatabase,
                mockMachineRedisDatabase,
                DefaultTempRoot,
                mockClock: _clock,
                configuration: configuration))
            {
                BoolResult result = await storeFactory.StartupAsync(context);
                result.ShouldBeSuccess();

                await testFuncAsync(storeFactory);

                result = await storeFactory.ShutdownAsync(context);
                result.ShouldBeSuccess();
            }
        }
    }
}
