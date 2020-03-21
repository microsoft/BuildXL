// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit;

namespace ContentStoreTest.Distributed.Sessions
{
    public partial class LocalLocationStoreDistributedContentTests : DistributedContentTests
    {
        protected void ConfigureWithOneMasterAndSmallBlobs(Action<TestDistributedContentSettings> overrideDistributed = null, Action<RedisContentLocationStoreConfiguration> overrideRedis = null)
        {
            ConfigureWithOneMaster(s =>
            {
                s.BlobExpiryTimeMinutes = 10;
                overrideDistributed?.Invoke(s);
            },
            overrideRedis);
        }

        [Fact]
        public Task BigBlobIsNotPutIntoRedis()
        {
            ConfigureWithOneMasterAndSmallBlobs();

            return RunTestAsync(
                new Context(Logger),
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);
                    var redisStore = context.GetRedisGlobalStore(0);

                    await session.PutRandomAsync(context, HashType.Vso0, false, redisStore.Configuration.MaxBlobSize + 1, CancellationToken.None).ShouldBeSuccess();

                    Assert.Equal(0, redisStore.Counters[GlobalStoreCounters.PutBlob].Value);
                });
        }

        [Fact]
        public Task SmallPutStreamIsPutIntoRedis()
        {
            ConfigureWithOneMasterAndSmallBlobs();

            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = context.GetRedisGlobalStore(0);

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = context.GetRedisGlobalStore(1);

                    var putResult = await session0.PutRandomAsync(context, HashType.Vso0, false, 10, CancellationToken.None).ShouldBeSuccess();
                    Assert.Equal(1, redisStore0.Counters[GlobalStoreCounters.PutBlob].Value);

                    await session1.OpenStreamAsync(context, putResult.ContentHash, CancellationToken.None).ShouldBeSuccess();
                    Assert.Equal(1, redisStore1.Counters[GlobalStoreCounters.GetBlob].Value);
                });
        }

        [Fact]
        public Task SmallPutFileIsPutIntoRedis()
        {
            ConfigureWithOneMasterAndSmallBlobs();

            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = context.GetRedisGlobalStore(0);

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = context.GetRedisGlobalStore(1);

                    var putResult = await session0.PutRandomFileAsync(context, FileSystem, HashType.Vso0, false, 10, CancellationToken.None).ShouldBeSuccess();
                    Assert.Equal(1, redisStore0.Counters[GlobalStoreCounters.PutBlob].Value);
                    
                    await session1.OpenStreamAsync(context, putResult.ContentHash, CancellationToken.None).ShouldBeSuccess();
                    Assert.Equal(1, redisStore1.Counters[GlobalStoreCounters.GetBlob].Value);
                });
        }

        [Fact]
        public Task SmallCopyIsPutIntoRedis()
        {
            ConfigureWithOneMasterAndSmallBlobs(s =>
            {
                if (s.TestMachineIndex == 0)
                {
                    // Disable small files in Redis for session 0
                    s.BlobExpiryTimeMinutes = 0;
                }
            });

            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = context.GetRedisGlobalStore(0);

                    // Put a random file when small files in Redis feature is disabled.
                    var putResult = await session0.PutRandomFileAsync(context, FileSystem, HashType.Vso0, false, 10, CancellationToken.None).ShouldBeSuccess();
                    Assert.Equal(0, redisStore0.Counters[GlobalStoreCounters.PutBlob].Value);
                    var contentHash = putResult.ContentHash;

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = context.GetRedisGlobalStore(1);

                    // Getting the file when small files in Redis feature is enabled.
                    // This should copy the file from another "machine" and place blob into redis.
                    await session1.OpenStreamAsync(context, contentHash, CancellationToken.None).ShouldBeSuccess();
                    var counters1 = redisStore1.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, redisStore1.Counters[GlobalStoreCounters.GetBlob].Value);
                    Assert.Equal(1, redisStore1.Counters[GlobalStoreCounters.PutBlob].Value);
                });
        }

        [Fact]
        public Task RepeatedBlobIsSkipped()
        {
            ConfigureWithOneMasterAndSmallBlobs();

            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var sessions = context.Sessions;

                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = context.GetRedisGlobalStore(0);

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = context.GetRedisGlobalStore(1);

                    var file = ThreadSafeRandom.GetBytes(10);
                    var fileString = Encoding.Default.GetString(file);

                    await session0.PutContentAsync(context, fileString).ShouldBeSuccess();
                    Assert.Equal(1, redisStore0.Counters[GlobalStoreCounters.PutBlob].Value);

                    await session1.PutContentAsync(context, fileString).ShouldBeSuccess();
                    Assert.Equal(1, redisStore1.Counters[GlobalStoreCounters.PutBlob].Value);
                    Assert.Equal(1, redisStore1.BlobAdapter.Counters[RedisBlobAdapter.RedisBlobAdapterCounters.SkippedBlobs].Value);
                });
        }

        [Fact]
        public Task SmallBlobsInRedisAfterCopy()
        {
            ConfigureWithOneMasterAndSmallBlobs();

            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var sessions = context.Sessions;

                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = context.GetRedisGlobalStore(0);

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = context.GetRedisGlobalStore(1);

                    var putResult = await session0.PutRandomAsync(context, HashType.Vso0, false, 10, CancellationToken.None).ShouldBeSuccess();
                    Assert.Equal(1, redisStore0.Counters[GlobalStoreCounters.PutBlob].Value);

                    // Simulate that the blob has expired.
                    var blobKey = RedisBlobAdapter.GetBlobKey(putResult.ContentHash);
                    var deleted = await PrimaryGlobalStoreDatabase.KeyDeleteAsync($"{redisStore0.Configuration.Keyspace}{blobKey}");
                    Assert.True(deleted, $"Could not delete {blobKey} because it does not exist.");

                    var openStreamResult = await session1.OpenStreamAsync(context, putResult.ContentHash, CancellationToken.None).ShouldBeSuccess();
                    Assert.Equal(0, redisStore1.BlobAdapter.Counters[RedisBlobAdapter.RedisBlobAdapterCounters.DownloadedBlobs].Value);
                    Assert.Equal(1, redisStore1.Counters[GlobalStoreCounters.PutBlob].Value);
                });
        }
    }
}
