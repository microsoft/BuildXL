// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;

namespace ContentStoreTest.Stores
{
    public sealed class ElasticFileSystemContentStoreInternalCalibrateTests : FileSystemContentStoreInternalTestBase
    {
        private const int InitialMaxSizeHard = 100;
        private const int WindowSize = 2;
        private readonly MemoryClock _clock;

        public ElasticFileSystemContentStoreInternalCalibrateTests()
            : base(() => new MemoryFileSystem(new MemoryClock(), Drives), TestGlobal.Logger)
        {
            _clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public Task TestQuotaCalibration()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                int totalSize = 0;

                // Add big content should trigger calibration.
                using (var pinContext = store.CreatePinContext())
                {
                    int size = 60;
                    using (var dataStream1 = RandomStream(size))
                    using (var dataStream2 = RandomStream(size))
                    {
                        await store.PutStreamAsync(context, dataStream1, ContentHashType, new PinRequest(pinContext)).ShouldBeSuccess();
                        _clock.Increment();

                        await store.PutStreamAsync(context, dataStream2, ContentHashType, new PinRequest(pinContext)).ShouldBeSuccess();
                        _clock.Increment();
                    }

                    totalSize += 2 * size;
                }

                MaxSizeQuota currentQuota = null;

                // Initial max = 100, total size = 120. Calibrate up.
                await VerifyQuota(context, store, quota =>
                {
                    currentQuota = quota;
                    Assert.True(quota.Hard > totalSize);
                    Assert.True(quota.Soft > totalSize);
                });

                // Add small content does not change quota.
                using (var pinContext = store.CreatePinContext())
                {
                    int size = 1;
                    using (var dataStream = RandomStream(size))
                    {
                        await store.PutStreamAsync(context, dataStream, ContentHashType, new PinRequest(pinContext)).ShouldBeSuccess();
                        _clock.Increment();
                    }

                    totalSize += size;
                }

                await VerifyQuota(context, store, quota =>
                {
                    Assert.Equal(currentQuota.Hard, quota.Hard);
                    Assert.Equal(currentQuota.Soft, quota.Soft);
                });

                // Add small content, but window is small. Calibrate down such that in the next reservation purging can run.
                using (var pinContext = store.CreatePinContext())
                {
                    int size = 1;
                    using (var dataStream = RandomStream(size))
                    {
                        await store.PutStreamAsync(context, dataStream, ContentHashType, new PinRequest(pinContext)).ShouldBeSuccess();
                        _clock.Increment();
                    }

                    totalSize += size;
                }

                await VerifyQuota(context, store, quota =>
                {
                    Assert.True(currentQuota.Hard > quota.Hard);
                    Assert.True(currentQuota.Soft > quota.Soft);
                    Assert.True(totalSize > quota.Soft && totalSize < quota.Hard);
                });
            });
        }

        protected override TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock, NagleQueue<ContentHash> nagleBlock = null)
        {
            return CreateElastic(rootPath, clock, nagleBlock, new MaxSizeQuota(InitialMaxSizeHard), WindowSize);
        }

        private static MemoryStream RandomStream(long size)
        {
            return new MemoryStream(ThreadSafeRandom.GetBytes((int)size));
        }

        private async Task VerifyQuota(Context context, TestFileSystemContentStoreInternal store, Action<MaxSizeQuota> verify)
        {
            // Sync to allow calibration to occur.
            await store.SyncAsync(context);
            var currentQuota = await LoadElasticQuotaAsync(store.RootPathForTest);
            Assert.NotNull(currentQuota);
            verify(currentQuota.Quota);
        }
    }
}
