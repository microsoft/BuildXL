// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;

namespace ContentStoreTest.Stores
{
    public sealed class ElasticFileSystemContentStoreInternalPutStreamTests : FileSystemContentStoreInternalPutStreamTests
    {
        [Fact]
        public Task PutStreamContentLargerThanCacheSucceeds()
        {
            return TestStore(Context, Clock, async store =>
            {
                var data = ThreadSafeRandom.GetBytes(MaxSizeHard + 1);
                using (var dataStream = new MemoryStream(data))
                {
                    await store.PutStreamAsync(Context, dataStream, ContentHashType, null).ShouldBeSuccess();
                }
            });
        }

        [Fact]
        public Task SuccessivePutStreamsFullPinnedCacheSucceed()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (var pinContext = store.CreatePinContext())
                {
                    var pinRequest = new PinRequest(pinContext);
                    var r = await store.PutRandomAsync(Context, MaxSizeHard / 3).ShouldBeSuccess();
                    Clock.Increment();
                    Assert.True(await store.ContainsAsync(Context, r.ContentHash, pinRequest));
                    Clock.Increment();

                    r = await store.PutRandomAsync(Context, MaxSizeHard / 3);
                    Clock.Increment();
                    Assert.True(await store.ContainsAsync(Context, r.ContentHash, pinRequest));
                    Clock.Increment();

                    var data = ThreadSafeRandom.GetBytes(MaxSizeHard / 2);
                    using (var dataStream = new MemoryStream(data))
                    {
                        await store.PutStreamAsync(Context, dataStream, ContentHashType, null).ShouldBeSuccess();
                    }

                    await store.SyncAsync(Context);
                }
            });
        }

        protected override TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock, NagleQueue<ContentHash> nagleBlock = null)
        {
            return CreateElastic(rootPath, clock, nagleBlock, new MaxSizeQuota(MaxSizeHard));
        }
    }
}
