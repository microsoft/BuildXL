// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Core.Tasks;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    using Counter = FileSystemContentStoreInternal.Counter;

    public abstract class FileSystemContentStoreInternalPutStreamTests : FileSystemContentStoreInternalTestBase
    {
        protected readonly Context Context;
        protected readonly MemoryClock Clock;

        protected FileSystemContentStoreInternalPutStreamTests()
            : base(() => new MemoryFileSystem(new MemoryClock(), Drives), TestGlobal.Logger)
        {
            Context = new Context(Logger);
            Clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public Task PutStreamFastPath()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (var dataStream = new MemoryStream(ThreadSafeRandom.GetBytes(MaxSizeHard / 3)))
                {
                    var put = await store.PutStreamAsync(Context, dataStream, ContentHashType, pinRequest: null).ShouldBeSuccess();
                    var hashFromPut = put.ContentHash;
                    store.Counters[Counter.PutFileFast].Value.Should().Be(0);

                    dataStream.Position = 0;
                    put = await store.PutStreamAsync(Context, dataStream, ContentHashType, pinRequest: null).ShouldBeSuccess();
                    store.Counters[Counter.PutFileFast].Value.Should().Be(1, "Non-pinning put should use fast path if content is already in cache");

                    dataStream.Position = 0;
                    put = await store.PutStreamAsync(Context, dataStream, hashFromPut, pinRequest: null).ShouldBeSuccess();
                    store.Counters[Counter.PutFileFast].Value.Should().Be(2, "Non-pinning put (with hash) should use fast path if content is already in cache");

                    using (var pinContext = store.CreatePinContext())
                    {
                        // Put the content into the store w/ hard link

                        dataStream.Position = 0;
                        put = await store.PutStreamAsync(Context, dataStream, ContentHashType, pinRequest: new PinRequest(pinContext)).ShouldBeSuccess();
                        store.Counters[Counter.PutFileFast].Value.Should().Be(2, "Pinning put should NOT use fast path if it needs to pin content");
                        Assert.True(pinContext.Contains(hashFromPut));
                        await store.EnsureContentIsPinned(Context, Clock, hashFromPut);
                        Clock.Increment();

                        dataStream.Position = 0;
                        put = await store.PutStreamAsync(Context, dataStream, ContentHashType, pinRequest: new PinRequest(pinContext)).ShouldBeSuccess();
                        store.Counters[Counter.PutFileFast].Value.Should().Be(3, "Pinning put should use fast path if it needs to pin content");

                        dataStream.Position = 0;
                        put = await store.PutStreamAsync(Context, dataStream, hashFromPut, pinRequest: new PinRequest(pinContext)).ShouldBeSuccess();
                        store.Counters[Counter.PutFileFast].Value.Should().Be(4, "Pinning put (with hash) should use fast path if content is already in cache");
                    }

                    await store.EnsureContentIsNotPinned(Context, Clock, hashFromPut);
                }
            });
        }

        [Fact]
        public Task PutStreamWritesToExpectedLocation()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (var memoryStream = new MemoryStream(ThreadSafeRandom.GetBytes(ValueSize)))
                {
                    await store.PutStreamAsync(Context, memoryStream, ContentHashType, null).ShouldBeSuccess();
                }

                var cacheRoot = store.RootPathForTest;
                var contentHashRoot = cacheRoot / "Shared" / ContentHashType.ToString();
                FileSystem.DirectoryExists(contentHashRoot).Should().BeTrue();
            });
        }

        [Fact]
        public Task PutStreamIsCancelledIfCalledIfCalledAfterShutdown()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (var memoryStream = new MemoryStream(ThreadSafeRandom.GetBytes(ValueSize)))
                {
                    await store.ShutdownAsync(Context).ShouldBeSuccess();
                    var result = await store.PutStreamAsync(Context, memoryStream, ContentHashType, null);
                    Assert.True(result.IsCancelled, $"Result is {result}");
                }
            });
        }

        [Fact]
        public Task PutStreamPins()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (var dataStream = new MemoryStream(ThreadSafeRandom.GetBytes(MaxSizeHard / 3)))
                {
                    ContentHash hashFromPut;
                    using (var pinContext = store.CreatePinContext())
                    {
                        var r = await store.PutStreamAsync(Context, dataStream, ContentHashType, new PinRequest(pinContext)).ShouldBeSuccess();
                        hashFromPut = r.ContentHash;
                        Clock.Increment();
                        await store.EnsureContentIsPinned(Context, Clock, hashFromPut);
                        Assert.True(pinContext.Contains(hashFromPut));
                    }

                    await store.EnsureContentIsNotPinned(Context, Clock, hashFromPut);
                }
            });
        }

        [Fact]
        public Task PutStreamPinningToDisposedContextThrows()
        {
            return TestStore(Context, Clock, async store =>
            {
                var pinContext = store.CreatePinContext();
                const int size = MaxSizeHard / 3;

                await store.PutRandomAsync(Context, size, ContentHashType, new PinRequest(pinContext)).ShouldBeSuccess();
                await pinContext.DisposeAsync();

                using (var stream = new MemoryStream(ThreadSafeRandom.GetBytes(size)))
                {
                    var r = await store.PutStreamAsync(
                        Context, stream, ContentHashType, new PinRequest(pinContext));
                    r.ShouldBeError();
                }
            });
        }

        [Fact(Skip = "Skip until pending PR to fix flakiness.")]
        public Task PutStreamParallelAdds()
        {
            const int PutSize = 30;

            return TestStore(Context, Clock, async store =>
             {
                 var putTasks = Enumerable.Range(0, 20).Select(async i =>
                 {
                     ContentHash hashFromPut;
                     using (var dataStream = new MemoryStream(ThreadSafeRandom.GetBytes(PutSize)))
                     {
                         var r = await store.PutStreamAsync(Context, dataStream, ContentHashType, null).ShouldBeSuccess();
                         hashFromPut = r.ContentHash;
                         Clock.Increment();
                     }
                     return hashFromPut;
                 }).ToArray();

                 ContentHash[] puthashes = await TaskUtilities.SafeWhenAll(putTasks);

                 await store.SyncAsync(Context);

                 var filesStillInCache = 0;
                 foreach (var hash in puthashes)
                 {
                     filesStillInCache += (await store.ContainsAsync(Context, hash, null)) ? 1 : 0;
                 }

                 Assert.Equal(20, filesStillInCache);
             });
        }

        [Fact]
        public Task PutStreamRecoversFromBlobForNonexistentContentDirectoryEntry()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var bytes = await store.CorruptWithBlobForNonexistentContentDirectoryEntry(Context, tempDirectory);
                    var contentHash = bytes.CalculateHash(ContentHashType);

                    using (var contentStream = new MemoryStream(bytes))
                    {
                        // Ensure that put succeeds despite the extraneous blob.
                        Func<Task> putFunc = async () => await store.PutStreamAsync(Context, contentStream, ContentHashType, null);
                        putFunc.Should().NotThrow();

                        // Ensure that the content directory now has exactly one entry.
                        (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(1);

                        await store.EnsureHasContent(Context, contentHash, tempDirectory);
                    }
                }
            });
        }

        [Fact]
        public async Task PutStreamPurgeAnnouncesRemove()
        {
            var mockAnnouncer = new TestContentChangeAnnouncer();

            await TestStore(Context, Clock, mockAnnouncer, async store =>
            {
                store.Announcer.Should().NotBeNull();

                var cas = store as FileSystemContentStoreInternal;
                var blobSize = BlobSizeToStartSoftPurging(2);

                using (var stream1 = new MemoryStream(ThreadSafeRandom.GetBytes(blobSize)))
                using (var stream2 = new MemoryStream(ThreadSafeRandom.GetBytes(blobSize)))
                {
                    await cas.PutStreamAsync(Context, stream1, ContentHashType).ShouldBeSuccess();
                    Clock.Increment();
                    await cas.PutStreamAsync(Context, stream2, ContentHashType).ShouldBeSuccess();
                    Clock.Increment();
                    await store.SyncAsync(Context);
                }
            });

            mockAnnouncer.ContentEvictedCalled.Should().BeTrue();
        }

        private class TestContentChangeAnnouncer : IContentChangeAnnouncer
        {
            public bool ContentEvictedCalled { get; private set; } = false;

            public Task ContentAdded(Context context, ContentHashWithSize item)
            {
                return Task.FromResult(0);
            }

            public Task ContentEvicted(Context context, ContentHashWithSize item)
            {
                ContentEvictedCalled = true;
                return Task.FromResult(0);
            }
        }
    }
}
