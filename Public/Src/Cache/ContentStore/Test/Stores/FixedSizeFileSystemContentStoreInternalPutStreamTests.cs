// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using Xunit;

namespace ContentStoreTest.Stores
{
    public sealed class FixedSizeFileSystemContentStoreInternalPutStreamTests : FileSystemContentStoreInternalPutStreamTests
    {
        [Fact]
        public Task PutStreamContentLargerThanCacheGivesError()
        {
            return TestStore(Context, Clock, async store =>
            {
                var data = ThreadSafeRandom.GetBytes(MaxSizeHard + 1);
                using (var dataStream = new MemoryStream(data))
                {
                    var result = await store.PutStreamAsync(Context, dataStream, ContentHashType, null);
                    Assert.False(result.Succeeded);
                }
            });
        }

        [Fact]
        public Task PutStreamFullPinnedCacheThrows()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (var pinContext = store.CreatePinContext())
                {
                    var pinRequest = new PinRequest(pinContext);
                    var r = await store.PutRandomAsync(Context, MaxSizeHard / 3);
                    ResultTestExtensions.ShouldBeSuccess((BoolResult) r);
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
                        var result = await store.PutStreamAsync(Context, dataStream, ContentHashType, null);
                        Assert.False(result.Succeeded);
                    }

                    await store.SyncAsync(Context);
                }
            });
        }
    }
}
