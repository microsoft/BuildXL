// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Utilities.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public abstract class FileSystemContentStoreInternalPurgeTests : FileSystemContentStoreInternalTestBase
    {
        protected readonly Context Context;
        protected readonly ITestClock Clock;

        protected FileSystemContentStoreInternalPurgeTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : base(createFileSystemFunc, logger, output)
        {
            Context = new Context(Logger);

            Clock = FileSystem is MemoryFileSystem fileSystem ? fileSystem.Clock : new TestSystemClock();
        }

        protected abstract int ContentSizeToStartSoftPurging(int numberOfBlobs);

        protected abstract int ContentSizeToStartHardPurging(int numberOfBlobs);

        protected virtual bool SucceedsEvenIfFull => false;

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutIsNotBlockedByPurgeAsync(bool useFile)
        {
            var tcs = TaskSourceSlim.Create<bool>();

            return FillSoNextPutTriggersSoftPurgeThenRunTest(
                async list =>
                {
                    await tcs.Task;
                    return list;
                },
                async (store, hash1, hash2, stream) =>
                {
                    await Task.Run(async () =>
                    {
                        await PutAsync(store, stream, useFile);
                        tcs.SetResult(true);
                    });
                });
        }

        [Fact]
        public Task PutPurgesOldest()
        {
            return FillSoNextPutTriggersSoftPurgeThenRunTest(null, async (store, hash1, hash2, stream) =>
            {
                var hash3 = await PutAsync(store, stream, sync: true);
                await AssertContainsHash(store, hash1);
                await AssertDoesNotContain(store, hash2);
                await AssertContainsHash(store, hash3);
            });
        }

        [Fact]
        public async Task PutAcrossRunsPurgesOldest()
        {
            var contentSize = ContentSizeToStartSoftPurging(3);

            using (MemoryStream
                stream1 = RandomStream(contentSize),
                stream2 = RandomStream(contentSize),
                stream3 = RandomStream(contentSize))
            {
                using (var directory = new DisposableDirectory(FileSystem))
                {
                    var hash1 = new ContentHash(ContentHashType);
                    var hash2 = new ContentHash(ContentHashType);

                    await TestStore(Context, Clock, directory, async store =>
                    {
                        hash1 = await PutAsync(store, stream1);
                        Clock.Increment();
                        hash2 = await PutAsync(store, stream2);
                    });

                    Clock.Increment();

                    await TestStore(Context, Clock, directory, async store =>
                    {
                        var hash3 = await PutAsync(store, stream3, sync: true);
                        await AssertDoesNotContain(store, hash1);
                        await AssertContainsHash(store, hash2);
                        await AssertContainsHash(store, hash3);
                    });
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task PutAcrossRunsWithDifferentHashesPurgesOldest()
        {
            var contentSize = ContentSizeToStartSoftPurging(3);

            using (MemoryStream
                stream1 = RandomStream(contentSize),
                stream2 = RandomStream(contentSize),
                stream3 = RandomStream(contentSize))
            {
                using (var directory = new DisposableDirectory(FileSystem))
                {
                    var hash1 = new ContentHash(ContentHashType);
                    var hash2 = new ContentHash(ContentHashType);

                    using (var sha1Hasher = HashInfoLookup.Find(HashType.SHA1).CreateContentHasher())
                    {
                        await TestStore(Context, Clock, directory, async store =>
                        {
                            hash1 = await PutAsync(store, stream1, hashType: HashType.SHA1);
                            hash2 = await PutAsync(store, stream2, hashType: HashType.SHA1);
                        });

                        hash1.HashType.Should().Be(sha1Hasher.Info.HashType);
                        hash2.HashType.Should().Be(sha1Hasher.Info.HashType);
                    }

                    using (var sha256Hasher = HashInfoLookup.Find(ContentHashType).CreateContentHasher())
                    {
                        await TestStore(Context, Clock, directory, async store =>
                        {
                            var hash3 = await PutAsync(store, stream3, sync: true);
                            hash3.HashType.Should().Be(sha256Hasher.Info.HashType);
                            hash3.HashType.Should().NotBe(hash1.HashType);
                            await AssertDoesNotContain(store, hash1);
                            await AssertContainsHash(store, hash2);
                            await AssertContainsHash(store, hash3);
                        });
                    }
                }
            }
        }

        protected async Task PutRandomAndPinAsync(FileSystemContentStoreInternal store, int contentSize, PinContext pinContext)
        {
            PutResult putResult = await store.PutRandomAsync(Context, contentSize);
            putResult.ShouldBeSuccess();

            PinResult pinResult = await store.PinAsync(Context, putResult.ContentHash, pinContext);
            pinResult.ShouldBeSuccess();
        }

        [Fact]
        public Task PurgeIsCanceledForShutdown()
        {
            var tcsBlockEnumeration = TaskSourceSlim.Create<bool>();

            return FillSoNextPutTriggersSoftPurgeThenRunTest(
                async list =>
                {
                    Output.WriteLine("Awaiting tsc...");
                    await tcsBlockEnumeration.Task;
                    Output.WriteLine("Tsc is done");
                    return list;
                },
                async (store, hash1, hash2, stream) =>
                {
                    var hash = await PutAsync(store, stream);

                    Task shutdownTask = Task.Run(() => store.ShutdownAsync(Context));

                    while (!store.ShutdownStarted)
                    {
                        await Task.Yield();
                    }

                    tcsBlockEnumeration.SetResult(true);

                    await shutdownTask;

                    Assert.True(FileSystem.FileExists(store.GetReplicaPathForTest(hash1, 0)));
                    Assert.True(FileSystem.FileExists(store.GetReplicaPathForTest(hash2, 0)));
                    Assert.True(FileSystem.FileExists(store.GetReplicaPathForTest(hash, 0)));
                });
        }

        [Fact]
        public Task PurgeCounterIncremented()
        {
            return FillSoNextPutTriggersSoftPurgeThenRunTest(null, async (store, hash1, hash2, stream) =>
            {
                await PutAsync(store, stream, sync: true);
                var r = await store.GetStatsAsync(Context);
                r.CounterSet.GetIntegralWithNameLike("PurgeCall").Should().BeGreaterOrEqualTo(1);
            });
        }

        [Fact]
        public Task AddImmediatelyDelete()
        {
            return TestStore(Context, Clock, async (store) =>
            {
                store.QuotaKeeperSize().Should().Be(0);

                int contentSize = 10;
                PutResult putResult = await store.PutRandomAsync(Context, contentSize);
                putResult.ShouldBeSuccess();

                DeleteResult deleteResult = await store.DeleteAsync(Context, putResult.ContentHash);
                deleteResult.ShouldBeSuccess();
                deleteResult.EvictedSize.Should().Be(contentSize);
                deleteResult.PinnedSize.Should().Be(0);

                store.IsPinned(putResult.ContentHash).Should().BeFalse();
            });
        }

        private async Task FillSoNextPutTriggersSoftPurgeThenRunTest(
            Func<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>, Task<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>>> onLruEnumerationWithTime,
            Func<TestFileSystemContentStoreInternal, ContentHash, ContentHash, MemoryStream, Task> func)
        {
            var contentSize = ContentSizeToStartSoftPurging(3);

            using (var directory = new DisposableDirectory(FileSystem))
            {
                using (var store = Create(directory.Path, Clock))
                {
                    try
                    {
                        await store.StartupAsync(Context).ShouldBeSuccess();

                        if (onLruEnumerationWithTime != null)
                        {
                            store.OnLruEnumerationWithTime = onLruEnumerationWithTime;
                        }

                        using (MemoryStream
                            stream1 = RandomStream(contentSize),
                            stream2 = RandomStream(contentSize),
                            stream3 = RandomStream(contentSize))
                        {
                            var hash1 = await PutAsync(store, stream1);
                            var hash2 = await PutAsync(store, stream2, sync: true);

                            Assert.True(await ContainsAsync(store, hash2));
                            Assert.True(await ContainsAsync(store, hash1));

                            await func(store, hash1, hash2, stream3);
                        }
                    }
                    finally
                    {
                        if (!store.ShutdownStarted)
                        {
                            await store.ShutdownAsync(Context).ShouldBeSuccess();
                        }
                    }
                }
            }
        }

        protected static MemoryStream RandomStream(long size)
        {
            return new MemoryStream(ThreadSafeRandom.GetBytes((int)size));
        }

        protected async Task AssertContainsHash(IContentStoreInternal store, ContentHash contentHash)
        {
            var contains = await store.ContainsAsync(Context, contentHash);
            contains.Should().BeTrue($"Expected hash={contentHash.ToShortString()} to be present but was not");
        }

        protected async Task AssertDoesNotContain(IContentStoreInternal store, ContentHash contentHash)
        {
            var contains = await store.ContainsAsync(Context, contentHash);
            contains.Should().BeFalse($"Expected hash={contentHash.ToShortString()} to not be present but was");
        }

        protected async Task<bool> ContainsAsync(IContentStoreInternal store, ContentHash contentHash)
        {
            var r = await store.ContainsAsync(Context, contentHash);

            Clock.Increment();

            return r;
        }

        protected async Task<PutResult> PutStreamAsync(IContentStoreInternal store, MemoryStream content)
        {
            var r = await store.PutStreamAsync(Context, content, ContentHashType);
            Clock.Increment();
            return r;
        }

        protected async Task<ContentHash> PutAsync
            (
            IContentStoreInternal store,
            MemoryStream content,
            bool useFile = false,
            HashType hashType = ContentHashType,
            bool sync = false
            )
        {
            ContentHash contentHash;

            if (useFile)
            {
                using (var directory = new DisposableDirectory(FileSystem))
                {
                    var path = directory.CreateRandomFileName();
                    using (var stream = await FileSystem.OpenAsync(
                        path, FileAccess.Write, FileMode.CreateNew, FileShare.Delete))
                    {
                        content.WriteTo(stream);
                    }

                    var r = await store.PutFileAsync(Context, path, FileRealizationMode.Any, hashType).ShouldBeSuccess();
                    contentHash = r.ContentHash;
                }
            }
            else
            {
                var r = await store.PutStreamAsync(Context, content, hashType).ShouldBeSuccess();
                contentHash = r.ContentHash;
            }

            Clock.Increment();

            if (sync)
            {
                await store.SyncAsync(Context);
            }

            return contentHash;
        }

        protected async Task Replicate(IContentStoreInternal store, ContentHash contentHash, DisposableDirectory directory, int numberOfFiles = 1500)
        {
            // ReSharper disable once UnusedVariable
            foreach (var x in Enumerable.Range(0, numberOfFiles))
            {
                var result = await store.PlaceFileAsync(
                    Context,
                    contentHash,
                    directory.CreateRandomFileName(),
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.FailIfExists,
                    FileRealizationMode.HardLink);

                result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);

                Clock.Increment();
            }
        }

        [Fact]
        public async Task PlaceFileRequiringNewReplicaCloseToHardLimitDoesNotHang()
        {
            var context = new Context(Logger);
            using (DisposableDirectory testDirectory = new DisposableDirectory(FileSystem))
            {
#pragma warning disable AsyncFixer04 // A disposable object used in a fire & forget async call
                Task testTask = TestStore(context, Clock, testDirectory, async store =>
#pragma warning restore AsyncFixer04 // A disposable object used in a fire & forget async call
                {
                    // Make a file which will overflow the cache size with just 2 copies.
                    PutResult putResult = await store.PutRandomAsync(context, ContentSizeToStartHardPurging(2));
                    ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);
                    ContentHash hash = putResult.ContentHash;

                    // Hardlink the file out 1024 times. Since the limit is 1024 total, and we already have 1 in the CAS,
                    // this will overflow the links and cause the CAS to create a new replica for it. This will cause
                    // the purger to consider that hash for eviction *while making room for that hash*, which is the
                    // trigger for the previous deadlock that this test will now guard against.
                    for (int i = 0; i < 1024; i++)
                    {
                        PlaceFileResult placeResult = await store.PlaceFileAsync(
                            context,
                            hash,
                            testDirectory.Path / $"hardlink{i}.txt",
                            FileAccessMode.ReadOnly,
                            FileReplacementMode.FailIfExists,
                            FileRealizationMode.HardLink,
                            null);

                        // The checks below are just to make sure that the calls completed as expected.
                        // The most important part is that they complete *at all*, which is enforced by
                        // racing against the Task.Delay in the outer scope.
                        if (i < 1023 || SucceedsEvenIfFull)
                        {
                            // The first 1023 links should succeed (bringing it up to the limit of 1024)
                            // And *all* of the calls should succeed if the cache takes new content even when overflowed.
                            Assert.True(placeResult.Succeeded);
                        }
                        else
                        {
                            // If the implementation rejects overflowing content, then the last call should fail.
                            Assert.False(placeResult.Succeeded);
                            Assert.Contains("Failed to reserve space", placeResult.ErrorMessage);
                        }
                    }
                });

                // Race between the test and a 2-minute timer. This can be increased if the test ends up flaky.
                Task firstCompletedTask = await Task.WhenAny(testTask, Task.Delay(TimeSpan.FromMinutes(2)));

                // The test should finish first, long before a minute passes, but it won't if it deadlocks.
                Assert.True(firstCompletedTask == testTask);
                await firstCompletedTask;
            }
        }

        [Fact]
        public async Task PutFileRequiringNewReplicaCloseToHardLimitDoesNotHang()
        {
            var context = new Context(Logger);
            using (DisposableDirectory testDirectory = new DisposableDirectory(FileSystem))
            {
#pragma warning disable AsyncFixer04 // A disposable object used in a fire & forget async call
                Task testTask = TestStore(context, Clock, testDirectory, async store =>
#pragma warning restore AsyncFixer04 // A disposable object used in a fire & forget async call
                {
                    // Make a file which will overflow the cache size with just 2 copies.
                    PutResult putResult = await store.PutRandomAsync(context, ContentSizeToStartHardPurging(2));
                    ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);
                    ContentHash hash = putResult.ContentHash;
                    AbsolutePath primaryPath = testDirectory.Path / "hardlinkPrimary.txt";

                    // Hardlink the file out once.
                    PlaceFileResult placeResult = await store.PlaceFileAsync(
                        context,
                        hash,
                        primaryPath,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.HardLink,
                        null);
                    Assert.True(placeResult.Succeeded);

                    // Hardlink the file out 1023 more times. Since the limit is 1024 total, and we already had 2,
                    // this will overflow the links and cause the CAS to create a new replica for it. This will cause
                    // the purger to consider that hash for eviction *while making room for that hash*, which is the
                    // trigger for the previous deadlock that this test will now guard against.
                    for (int i = 0; i < 1023; i++)
                    {
                        AbsolutePath newPath = testDirectory.Path / $"hardlink{i}.txt";
                        await FileSystem.CopyFileAsync(primaryPath, newPath, false);

                        putResult = await store.PutFileAsync(
                            context,
                            newPath,
                            FileRealizationMode.HardLink,
                            hash,
                            null);

                        // The checks below are just to make sure that the calls completed as expected.
                        // The most important part is that they complete *at all*, which is enforced by
                        // racing against the Task.Delay in the outer scope.
                        if (i < 1022 || SucceedsEvenIfFull)
                        {
                            // The first 1022 links should succeed (bringing it up to the limit of 1024)
                            // And *all* of the calls should succeed if the cache takes new content even when overflowed.
                            Assert.True(putResult.Succeeded);
                        }
                        else
                        {
                            // If the implementation rejects overflowing content, then the last call should fail.
                            Assert.False(putResult.Succeeded);
                            Assert.Contains("Failed to reserve space", putResult.ErrorMessage);
                        }
                    }
                });

                // Race between the test and a 1-minute timer. This can be increased if the test ends up flaky.
                Task firstCompletedTask = await Task.WhenAny(testTask, Task.Delay(TimeSpan.FromMinutes(1)));

                // The test should finish first, long before a minute passes, but it won't if it deadlocks.
                Assert.True(firstCompletedTask == testTask);
                await firstCompletedTask;
            }
        }
    }
}
