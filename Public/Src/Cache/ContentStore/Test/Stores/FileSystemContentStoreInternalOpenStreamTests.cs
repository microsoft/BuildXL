// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public sealed class FileSystemContentStoreInternalOpenStreamTests : FileSystemContentStoreInternalTestBase
    {
        private const int NumParallelTasks = 20;
        private readonly MemoryClock _clock;

        public FileSystemContentStoreInternalOpenStreamTests(ITestOutputHelper outputHelper)
            : base(() => new MemoryFileSystem(new MemoryClock()), TestGlobal.Logger, outputHelper)
        {
            _clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public Task OpenStreamPinContextPins()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                var r = await store.PutRandomAsync(context, MaxSizeHard / 3);
                _clock.Increment();
                using (var pinContext = store.CreatePinContext())
                {
                    var result = await store.OpenStreamAsync(context, r.ContentHash, new PinRequest(pinContext));
                    using (var streamFromGet = result.Stream)
                    {
                        Assert.NotNull(streamFromGet);
                        _clock.Increment();
                    }

                    await store.EnsureContentIsPinned(context, _clock, r.ContentHash);
                    Assert.True(pinContext.Contains(r.ContentHash));
                }

                await store.EnsureContentIsNotPinned(context, _clock, r.ContentHash);
            });
        }

        [Fact]
        public Task OpenStreamRecoversFromContentDirectoryEntryForNonexistentBlob()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(0);

                // Add a content directory entry for which there is no blob on disk.
                var nonexistentHash = ContentHash.Random();
                await store.ContentDirectoryForTest.UpdateAsync(nonexistentHash, true, _clock, fileInfo =>
                    Task.FromResult(new ContentFileInfo(_clock, 1, 100)));
                (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(1);

                // Ensure that GetStream treats the missing blob as a miss despite the bad content directory entry.
                var result = await store.OpenStreamAsync(context, nonexistentHash, null);

                Assert.Null(result.Stream);

                // Ensure the cache has removed the bad content directory entry.
                (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(0);
            });
        }

        [Fact]
        public Task OpenStreamParallelPinsToSinglePinContext()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                ContentHash contentHash;
                using (var pinContext = store.CreatePinContext())
                {
                    // Pin some new content.
                    const int size = MaxSizeHard / 3;
                    var r = await store.PutRandomAsync(context, size, ContentHashType, new PinRequest(pinContext));
                    contentHash = r.ContentHash;
                    store.PinMapForTest.ContainsKey(contentHash).Should().BeTrue();
                    store.PinMapForTest[contentHash].Count.Should().Be(1);

                    // Open multiple streams to same content pinning to the same pin context.
                    var streams = Enumerable.Repeat<Stream>(null, NumParallelTasks).ToList();
                    var tasks = Enumerable.Range(0, NumParallelTasks).Select(i => Task.Run(async () => streams[i] =
                        (await store.OpenStreamAsync(context, contentHash, new PinRequest(pinContext))).Stream));
                    await TaskSafetyHelpers.WhenAll(tasks);
                    store.PinMapForTest[contentHash].Count.Should().Be(NumParallelTasks + 1);

                    // Disposing the streams do not unpin the content.
                    streams.ForEach(stream => stream.Dispose());
                    store.PinMapForTest[contentHash].Count.Should().Be(NumParallelTasks + 1);

                    await pinContext.DisposeAsync();
                }

                // After pin context disposed, content is no longer pinned.
                Pin pin;
                store.PinMapForTest.TryGetValue(contentHash, out pin).Should().Be(false);
            });
        }

        [Fact]
        public Task OpenStreamParallelPinsToMultiplePinContexts()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                ContentHash contentHash;
                using (var pinContext = store.CreatePinContext())
                {
                    // Pin some new content.
                    const int size = MaxSizeHard / 3;
                    var r = await store.PutRandomAsync(context, size, ContentHashType, new PinRequest(pinContext));
                    contentHash = r.ContentHash;
                    store.PinMapForTest.ContainsKey(contentHash).Should().BeTrue();
                    store.PinMapForTest[contentHash].Count.Should().Be(1);

                    // Open multiple streams to same content, each pinning to a separate pin context.
                    var streams = Enumerable.Repeat<Stream>(null, NumParallelTasks).ToList();
                    var contexts = Enumerable.Range(0, NumParallelTasks).Select(i => store.CreatePinContext()).ToList();
                    var tasks = Enumerable.Range(0, NumParallelTasks).Select(i => Task.Run(async () => streams[i] =
                        (await store.OpenStreamAsync(context, contentHash, new PinRequest(contexts[i]))).Stream));
                    await TaskSafetyHelpers.WhenAll(tasks);
                    store.PinMapForTest[contentHash].Count.Should().Be(NumParallelTasks + 1);

                    // Disposing the streams to not unpin the content.
                    streams.ForEach(stream => stream.Dispose());
                    store.PinMapForTest[contentHash].Count.Should().Be(NumParallelTasks + 1);

                    // Disposing the separate pin contexts still does not unpin the content.
                    contexts.ForEach(c => c.Dispose());
                    store.PinMapForTest[contentHash].Count.Should().Be(1);

                    await pinContext.DisposeAsync();
                }

                // After all pin contexts disposed, content is no longer pinned.
                Pin pin;
                store.PinMapForTest.TryGetValue(contentHash, out pin).Should().Be(false);
            });
        }

        [Fact]
        public async Task LinkCanBeDeletedWhileOpenStreamHasAnotherLinkOpen()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);
                await TestStore(context, _clock, async store =>
                {
                    // Put some content.
                    var r1 = await store.PutRandomAsync(context, 10);
                    ResultTestExtensions.ShouldBeSuccess((BoolResult) r1);

                    // Hardlink it somewhere outside the cache.
                    var path = directory.CreateRandomFileName();
                    var r2 = await store.PlaceFileAsync(
                        context,
                        r1.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.HardLink,
                        null);
                    r2.IsPlaced().Should().BeTrue();

                    // Open a stream to the cache link.
                    var r3 = await store.OpenStreamAsync(context, r1.ContentHash, null);
                    r3.ShouldBeSuccess();

                    using (r3.Stream)
                    {
                        // Verify the external link can be deleted
                        FileSystem.DeleteFile(path);
                    }
                });
            }
        }
    }
}
