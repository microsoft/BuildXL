// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    public abstract class DistributedContentTests : TestBase
    {
        protected static readonly CancellationToken Token = CancellationToken.None;
        protected static readonly ContentStoreConfiguration Config = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(50);

        protected MemoryClock TestClock { get; } = new MemoryClock();

        protected ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee ContentAvailabilityGuarantee { get; set; } = ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee.FileRecordsExist;

        protected abstract IContentStore CreateStore(
            Context context,
            TestFileCopier fileCopier,
            DisposableDirectory testDirectory,
            int index,
            bool enableDistributedEviction,
            int? replicaCreditInMinutes,
            bool enableRepairHandling,
            bool emptyFileHashShortcutEnabled);

        protected class TestContext
        {
            public readonly Context Context;
            public readonly TestFileCopier FileCopier;
            public readonly IList<DisposableDirectory> Directories;
            public readonly IList<IContentSession> Sessions;
            public readonly IList<IContentStore> Stores;
            public readonly int Iteration;

            public TestContext(Context context, TestFileCopier fileCopier, IList<DisposableDirectory> directories, IList<IContentSession> sessions, IList<IContentStore> stores, int iteration)
            {
                Context = context;
                FileCopier = fileCopier;
                Directories = directories;
                Sessions = sessions;
                Stores = stores;
                Iteration = iteration;
            }

            public static implicit operator Context(TestContext context) => context.Context;

            public static implicit operator OperationContext(TestContext context) => new OperationContext(context);

            public DistributedContentSession<AbsolutePath> GetDistributedSession(int idx) => (DistributedContentSession<AbsolutePath>)Sessions[idx];

            public LocalLocationStore GetLocalLocationStore(int idx) =>
                ((TransitioningContentLocationStore)GetDistributedSession(idx).ContentLocationStore).LocalLocationStore;

            internal RedisContentLocationStore GetRedisLocationStore(int idx) =>
                ((TransitioningContentLocationStore)GetDistributedSession(idx).ContentLocationStore).RedisContentLocationStore;

            internal TransitioningContentLocationStore GetLocationStore(int idx) =>
                ((TransitioningContentLocationStore)GetDistributedSession(idx).ContentLocationStore);

            internal int GetMasterIndex()
            {
                for (int i = 0; i < Sessions.Count; i++)
                {
                    var localStore = GetLocationStore(i);
                    if (localStore.LocalLocationStore.CurrentRole == Role.Master)
                    {
                        return i;
                    }
                }

                throw new InvalidOperationException($"Unable to find Master instance.");
            }

            internal TransitioningContentLocationStore GetMaster()
            {
                return GetLocationStore(GetMasterIndex());
            }

            internal RedisContentLocationStore GetRedisStore(DistributedContentSession<AbsolutePath> session)
            {
                var cls = session.ContentLocationStore;
                if (cls is TransitioningContentLocationStore transitionStore)
                {
                    return transitionStore.RedisContentLocationStore;
                }
                else
                {
                    return (RedisContentLocationStore)cls;
                }
            }

            internal Task SyncAsync(int idx)
            {
                var store = (DistributedContentStore<AbsolutePath>)Stores[idx];
                var localContentStore = (FileSystemContentStore)store.InnerContentStore;
                return localContentStore.Store.SyncAsync(this);
            }

            internal int GetFirstWorkerIndex()
            {
                for (int i = 0; i < Sessions.Count; i++)
                {
                    var localStore = GetLocationStore(i);
                    if (localStore.LocalLocationStore.CurrentRole == Role.Worker)
                    {
                        return i;
                    }
                }

                throw new InvalidOperationException($"Unable to find Worker instance.");
            }

            internal IEnumerable<int> EnumerateWorkersIndices()
            {
                for (int i = 0; i < Sessions.Count; i++)
                {
                    var localStore = GetLocationStore(i);
                    if (localStore.LocalLocationStore.CurrentRole == Role.Worker)
                    {
                        yield return i;
                    }
                }
            }

            internal IEnumerable<TransitioningContentLocationStore> EnumerateWorkers()
            {
                return EnumerateWorkersIndices().Select(i => GetLocationStore(i));
            }

            internal TransitioningContentLocationStore GetFirstWorker()
            {
                return GetLocationStore(GetFirstWorkerIndex());
            }
        }

        protected DistributedContentTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task GetFilesFromDifferentReplicas()
        {
            await RunTestAsync(new Context(Logger), 3, async context =>
            {
                var sessions = context.Sessions;

                // Insert random file in session 0
                var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                var putResult1 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token);
                Assert.True(putResult1.Succeeded);

                // Insert random file in session 1
                var randomBytes2 = ThreadSafeRandom.GetBytes(0x40);
                var putResult2 = await sessions[1].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes2), Token);
                Assert.True(putResult2.Succeeded);

                // Ensure both files are downloaded to session 2
                await OpenStreamReturnsExpectedFile(sessions[2], context, putResult1.ContentHash, randomBytes1);
                await OpenStreamReturnsExpectedFile(sessions[2], context, putResult2.ContentHash, randomBytes2);

                // Query for other random file returns nothing
                var randomHash = ContentHash.Random();
                var openStreamResult = await sessions[2].OpenStreamAsync(context, randomHash, Token);

                Assert.Equal(OpenStreamResult.ResultCode.ContentNotFound, openStreamResult.Code);
                Assert.Null(openStreamResult.Stream);
            });

            Assert.True(true);
        }

        [Fact]
        public async Task PinWithRedundantRecordAvailability()
        {
            ContentAvailabilityGuarantee = ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee.RedundantFileRecordsOrCheckFileExistence;

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;

                    // Insert random file in session 0
                    var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                    var putResult1 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token);
                    var localContentPath1 = PathUtilities.GetContentPath(context.Directories[0].Path / "Root", putResult1.ContentHash);
                    Assert.True(putResult1.Succeeded);

                    // Case 1: Underreplicated file exists
                    context.FileCopier.SetNextFileExistenceResult(localContentPath1, FileExistenceResult.ResultCode.FileExists);
                    var pinResult = await sessions[2].PinAsync(context, putResult1.ContentHash, Token);
                    Assert.Equal(PinResult.ResultCode.Success, pinResult.Code);

                    // Case 2: Underreplicated file does not exist
                    context.FileCopier.SetNextFileExistenceResult(localContentPath1, FileExistenceResult.ResultCode.FileNotFound);
                    pinResult = await sessions[2].PinAsync(context, putResult1.ContentHash, Token);
                    Assert.Equal(PinResult.ResultCode.ContentNotFound, pinResult.Code);

                    // Now insert the content into session 1 to ensure it is sufficiently replicated
                    var putResult2 = await sessions[1].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token);
                    var localContentPath2 = PathUtilities.GetContentPath(context.Directories[1].Path / "Root", putResult2.ContentHash);
                    Assert.True(putResult2.Succeeded);

                    // Ensure both files don't exist from file copier's point of view to verify that existence isn't checked
                    var priorExistenceCheckCount1 = context.FileCopier.GetExistenceCheckCount(localContentPath1);

                    // Should have checked existence already for content in session0 for Case1 and Case 2
                    Assert.Equal(2, priorExistenceCheckCount1);

                    var priorExistenceCheckCount2 = context.FileCopier.GetExistenceCheckCount(localContentPath2);
                    Assert.Equal(0, priorExistenceCheckCount2);

                    pinResult = await sessions[2].PinAsync(context, putResult1.ContentHash, Token);
                    Assert.Equal(PinResult.ResultCode.Success, pinResult.Code);
                });
        }

        [Fact]
        public Task RemoteFileAddedToLocalBeforePlace()
        {
            return RunTestAsync(new Context(Logger), 2, async context =>
            {
                var sessions = context.Sessions;

                // Insert random file in session 0
                var putResult = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, 0x40, Token);
                Assert.True(putResult.Succeeded);

                // Place file from session 1
                var filePath = context.Directories[1].Path / "Temp" / "file.txt";
                var placeResult = await sessions[1].PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    filePath,
                    FileAccessMode.Write,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Copy,
                    Token);
                Assert.True(placeResult.IsPlaced());
                FileSystem.FileExists(filePath).Should().BeTrue();

                // Ensure file is added to local content store for session 1
                var expectedLocalContentPath = PathUtilities.GetContentPath(context.Directories[1].Path / "Root", putResult.ContentHash);
                Assert.True(File.Exists(expectedLocalContentPath.Path));
            });
        }

        [Fact]
        public Task NoRetryOfCopyWhenLocalIsFull()
        {
            return RunTestAsync(new Context(Logger), 2, async context =>
            {
                var sessions = context.Sessions;

                // Fill session 0 with content
                var putResult = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, Config.MaxSizeQuota.Hard, Token).ShouldBeSuccess();

                // Insert random file into session 1
                putResult = await sessions[1].PutRandomAsync(context, HashType.Vso0, false, Config.MaxSizeQuota.Hard / 2, Token).ShouldBeSuccess();

                var hash = putResult.ContentHash;
                var placeResult = await sessions[0].PlaceFileAsync(
                    context,
                    hash,
                    context.Directories[0].Path / "blah",
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.FailIfExists,
                    FileRealizationMode.Any,
                    Token);

                placeResult.Code.Should().Be(PlaceFileResult.ResultCode.Error, placeResult.ToString());
            });
        }

        [Fact]
        public async Task RemoveFromTrackerWipesLocalLocation()
        {
            var contentHash = ContentHash.Random();
            var loggingContext = new Context(Logger);

            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);
                    var store = (IRepairStore)context.Stores[0];

                    // Add random file to empty cache and update the content tracker
                    var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, 0x40, Token).ShouldBeSuccess();
                    contentHash = putResult.ContentHash;

                    // Wipe all hashes registered at the local machine from the content tracker
                    var removeFromTrackerResult = await store.RemoveFromTrackerAsync(context);
                    removeFromTrackerResult.ShouldBeSuccess();
                },
                enableRepairHandling: true);

            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);
                    var contentLocationStore = session.ContentLocationStore;

                    // Because the file is unique, trimming should remove the hash from the content tracker
                    var getResult = await contentLocationStore.GetBulkAsync(context, new[] { contentHash }, CancellationToken.None, UrgencyHint.Nominal);
                    getResult.ShouldBeSuccess();
                    Assert.Null(getResult.ContentHashesInfo.First().Locations);
                });
        }

        [Fact]
        public Task SomeLocalContentStoresCorrupt()
        {
            return RunTestAsync(new Context(Logger), 3, async context =>
            {
                var sessions = context.Sessions;

                // Insert random file in session 0
                var randomBytes = ThreadSafeRandom.GetBytes(0x40);
                var putResult0 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes), Token).ShouldBeSuccess();

                // Insert same file in session 1
                var putResult1 = await sessions[1].PutStreamAsync(context.Context, HashType.Vso0, new MemoryStream(randomBytes), Token).ShouldBeSuccess();

                putResult0.ContentHash.Should().Be(putResult1.ContentHash, "both puts of the same content should have the same hash.");

                // Delete file from local content store 0
                var localContentPath = PathUtilities.GetContentPath(context.Directories[0].Path / "Root", putResult0.ContentHash);
                File.Delete(localContentPath.Path);
                context.FileCopier.FilesCopied.TryAdd(localContentPath, localContentPath);
                context.FileCopier.FileExistenceByReturnCode.GetOrAdd(
                    localContentPath,
                    (_) =>
                    {
                        var queue = new ConcurrentQueue<FileExistenceResult.ResultCode>();
                        queue.Enqueue(FileExistenceResult.ResultCode.FileNotFound);
                        return queue;
                    });

                // Query for file from session 2
                await OpenStreamReturnsExpectedFile(sessions[2], context.Context, putResult0.ContentHash, randomBytes);
            });
        }

        [Fact]
        public Task PutWithWrongHash()
        {
            return RunTestAsync(new Context(Logger), 2, async testContext =>
            {
                var sessions = testContext.Sessions;

                var randomBytesForHash = ThreadSafeRandom.GetBytes(0x40);
                var putResult0 = await sessions[0].PutStreamAsync(testContext.Context, HashType.Vso0, new MemoryStream(randomBytesForHash), Token).ShouldBeSuccess();
                File.Exists(PathUtilities.GetContentPath(testContext.Directories[0].Path / "Root", putResult0.ContentHash).Path).Should().BeTrue();

                var randomBytesForPut = ThreadSafeRandom.GetBytes(0x40);
                var putResult1 = await sessions[1].PutStreamAsync(testContext.Context, putResult0.ContentHash, new MemoryStream(randomBytesForPut), Token).ShouldBeError();
                File.Exists(PathUtilities.GetContentPath(testContext.Directories[1].Path / "Root", putResult0.ContentHash).Path).Should().BeFalse();
            });
        }

        [Fact]
        public Task PinLargeSetsSucceeds()
        {
            return RunTestAsync(new Context(Logger), 3, async context =>
            {
                var sessions = context.Sessions;

                // Insert random file in session 0
                List<ContentHash> contentHashes = new List<ContentHash>();

                for (int i = 0; i < 250; i++)
                {
                    var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                    var putResult1 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token).ShouldBeSuccess();
                    contentHashes.Add(putResult1.ContentHash);
                }

                // Insert same file in session 1
                IEnumerable<Task<Indexed<PinResult>>> pinResultTasks = await sessions[1].PinAsync(context, contentHashes, Token);

                foreach (var pinResultTask in pinResultTasks)
                {
                    Assert.True((await pinResultTask).Item.Succeeded);
                }
            });
        }

        [Fact]
        public async Task EvictionCausesRedisGarbageCollection()
        {
            // Use the same context in two sessions when checking for file existence
            var loggingContext = new Context(Logger);

            // Set content hash for random file #1 because content hash cannot be null
            var randomHash = ContentHash.Random();
            ContentHash contentHash = randomHash;

            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);

                    // Insert random file #1 into session
                    var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, Config.MaxSizeQuota.Hard, Token);
                    Assert.True(putResult.Succeeded, putResult.ErrorMessage + " " + putResult.Diagnostics);

                    contentHash = putResult.ContentHash;

                    var result = await session.ContentLocationStore.GetBulkAsync(
                        context,
                        new[] { putResult.ContentHash },
                        Token,
                        UrgencyHint.Nominal);

                    Assert.True(result.Succeeded, result.ErrorMessage + " " + result.Diagnostics);
                    result.ContentHashesInfo.Count.Should().Be(1);
                    result.ContentHashesInfo[0].ContentHash.Should().Be(putResult.ContentHash);
                    result.ContentHashesInfo[0].Size.Should().Be(putResult.ContentSize);
                    result.ContentHashesInfo[0].Locations.Count.Should().Be(1);

                    // Put large file #2 that will evict random file #1
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, Config.MaxSizeQuota.Hard, Token);
                    Assert.True(putResult.Succeeded, putResult.ErrorMessage + " " + putResult.Diagnostics);
                },
                implicitPin: ImplicitPin.None,
                enableDistributedEviction: true);

            // Content hash should be set to random file #1 from first session
            contentHash.Should().NotBe(randomHash);

            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);

                    var locationsResult = await session.ContentLocationStore.GetBulkAsync(
                        context,
                        new[] { contentHash },
                        Token,
                        UrgencyHint.Nominal);

                    // Random file #1 should not be found
                    Assert.True(locationsResult.Succeeded, locationsResult.ErrorMessage + " " + locationsResult.Diagnostics);
                    locationsResult.ContentHashesInfo.Count.Should().Be(1);
                    locationsResult.ContentHashesInfo[0].Should().NotBeNull();
                    locationsResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();
                });
        }

        [Fact]
        public async Task EvictContentBasedOnLastAccessTime()
        {
            // Use the same context in two sessions when checking for file existence
            var loggingContext = new Context(Logger);

            var contentHashes = new List<ContentHash>();

            // HACK: Existing purge code removes an extra file. Testing with this in mind.
            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = (DistributedContentSession<AbsolutePath>)context.Sessions[0];

                    var store = context.GetRedisStore(session);

                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    // Insert random file #1 into session
                    store.ContentHashBumpTime = TimeSpan.FromHours(1);
                    var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #2 into session.
                    store.ContentHashBumpTime = TimeSpan.FromMinutes(30);
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #3 into session.
                    store.ContentHashBumpTime = TimeSpan.FromMinutes(15);
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #4 into session that will evict file #2 and #3.
                    // Changing the ContentHashBumpTime tricks the store into believing that #1 was accessed the latest.
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);
                },
                implicitPin: ImplicitPin.None,
                enableDistributedEviction: true);

            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = (DistributedContentSession<AbsolutePath>)context.Sessions[0];

                    var locationsResult = await session.ContentLocationStore.GetBulkAsync(
                        context,
                        contentHashes,
                        Token,
                        UrgencyHint.Nominal);

                    // Random file #2 and 3 should not be found
                    Assert.True(locationsResult.Succeeded, locationsResult.ErrorMessage + " " + locationsResult.Diagnostics);
                    locationsResult.ContentHashesInfo.Count.Should().Be(4);

                    locationsResult.ContentHashesInfo[0].Locations.Count.Should().Be(1);
                    locationsResult.ContentHashesInfo[2].Locations.Should().BeNullOrEmpty();
                    locationsResult.ContentHashesInfo[3].Locations.Count.Should().Be(1);
                });
        }

        [Fact(Skip="Flaky and becoming obselete with LLS.")]
        public async Task EvictContentBasedOnLastAccessTimeWithPriorityQueue()
        {
            // Use the same context in two sessions when checking for file existence
            var loggingContext = new Context(Logger);

            var contentHashes = new List<ContentHash>();

            // HACK: Existing purge code removes an extra file. Testing with this in mind.
            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);
                    var store = context.GetRedisStore(session);

                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    // Insert random file #1 into session
                    store.ContentHashBumpTime = TimeSpan.FromHours(1);
                    var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #2 into session.
                    store.ContentHashBumpTime = TimeSpan.FromMinutes(30);
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #3 into session.
                    store.ContentHashBumpTime = TimeSpan.FromMinutes(15);
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #4 into session that will evict file #2 and #3.
                    // Changing the ContentHashBumpTime tricks the store into believing that #1 was accessed the latest.
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();

                    contentHashes.Add(putResult.ContentHash);
                },
                implicitPin: ImplicitPin.None,
                enableDistributedEviction: true,
                replicaCreditInMinutes: 10);

            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);

                    var locationsResult = await session.ContentLocationStore.GetBulkAsync(
                        context,
                        contentHashes,
                        Token,
                        UrgencyHint.Nominal);

                    // Random file #2 and 3 should not be found
                    Assert.True(locationsResult.Succeeded, locationsResult.ErrorMessage + " " + locationsResult.Diagnostics);
                    locationsResult.ContentHashesInfo.Count.Should().Be(4);

                    locationsResult.ContentHashesInfo[0].Locations.Count.Should().Be(1);
                    locationsResult.ContentHashesInfo[1].Locations.Should().BeNullOrEmpty();
                    locationsResult.ContentHashesInfo[2].Locations.Should().BeNullOrEmpty();
                    locationsResult.ContentHashesInfo[3].Locations.Count.Should().Be(1);
                });
        }

        [Fact(Skip = "Flaky test")]
        public async Task EvictContentBasedOnLastAccessTimeWithPinnedFiles()
        {
            // Use the same context in two sessions when checking for file existence
            var loggingContext = new Context(Logger);

            var contentHashes = new List<ContentHash>();

            // HACK: Existing purge code removes an extra file. Testing with this in mind.
            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);
                    var store = context.GetRedisStore(session);

                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    // Insert random file #1 into session
                    store.ContentHashBumpTime = TimeSpan.FromHours(1);
                    var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();

                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #2 into session.
                    store.ContentHashBumpTime = TimeSpan.FromMinutes(30);
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();

                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #3 into session.
                    store.ContentHashBumpTime = TimeSpan.FromMinutes(15);
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    await session.PinAsync(loggingContext, putResult.ContentHash, CancellationToken.None, UrgencyHint.Nominal).ShouldBeSuccess();

                    // Put random large file #4 into session that will evict file #1 and #2
                    // Even though #3 is considered the least recently used, it's pinned so we can't evict it
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();

                    contentHashes.Add(putResult.ContentHash);
                },
                implicitPin: ImplicitPin.None,
                enableDistributedEviction: true);

            await RunTestAsync(
                loggingContext,
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);

                    var locationsResult = await session.ContentLocationStore.GetBulkAsync(
                        context,
                        contentHashes,
                        Token,
                        UrgencyHint.Nominal);

                    // Random file #1 and 2 should not be found
                    Assert.True(locationsResult.Succeeded, locationsResult.ErrorMessage + " " + locationsResult.Diagnostics);
                    locationsResult.ContentHashesInfo.Count.Should().Be(4);

                    locationsResult.ContentHashesInfo[0].Locations.Count.Should().Be(0);
                    locationsResult.ContentHashesInfo[1].Locations.Count.Should().Be(0);
                    locationsResult.ContentHashesInfo[2].Locations.Count.Should().Be(1);
                    locationsResult.ContentHashesInfo[3].Locations.Count.Should().Be(1);
                });
        }

        [Fact]
        public Task PinUpdatesExpiryAndSizeWhenFoundLocal()
        {
            return RunTestAsync(new Context(Logger), 1, async context =>
            {
                var sessions = context.Sessions;

                var session = context.GetDistributedSession(0);
                var redisStore = context.GetRedisStore(session);

                // Insert random file in session 0 with expiry of 30 minutes
                redisStore.ContentHashBumpTime = TimeSpan.FromMinutes(30);
                var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                var putResult1 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token);
                Assert.True(putResult1.Succeeded);

                // Insert same file in same session and update expiry to 1 hour
                redisStore.ContentHashBumpTime = TimeSpan.FromHours(1);
                IEnumerable<Task<Indexed<PinResult>>> pinResultTasks = await sessions[0].PinAsync(context, new[] { putResult1.ContentHash }, Token);
                Assert.True((await pinResultTasks.First()).Item.Succeeded);

                var expiry = await redisStore.GetContentHashExpiryAsync(new Context(Logger), putResult1.ContentHash, CancellationToken.None);
                Assert.True(expiry.HasValue);
                Assert.InRange(expiry.Value, redisStore.ContentHashBumpTime - TimeSpan.FromSeconds(15), redisStore.ContentHashBumpTime + TimeSpan.FromSeconds(15));

                var result = await redisStore.GetBulkAsync(new Context(Logger), new[] { putResult1.ContentHash }, CancellationToken.None, UrgencyHint.Nominal);
                Assert.True(result.Succeeded);
                Assert.Equal(putResult1.ContentSize, result.ContentHashesInfo[0].Size);
            });
        }

        [Fact]
        public async Task PinUpdatesExpiryWhenFoundRemote()
        {
            await RunTestAsync(new Context(Logger), 2, async context =>
            {
                var sessions = context.Sessions;

                var session = context.GetDistributedSession(0);
                var redisStore = context.GetRedisStore(session);

                // Insert random file in session 0 with expiry of 30 minutes
                redisStore.ContentHashBumpTime = TimeSpan.FromMinutes(30);
                var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                var putResult1 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token);
                Assert.True(putResult1.Succeeded);

                // Insert same file in session 1 and update expiry to 1 hour
                redisStore.ContentHashBumpTime = TimeSpan.FromHours(1);
                IEnumerable<Task<Indexed<PinResult>>> pinResultTasks = await sessions[1].PinAsync(context, new[] { putResult1.ContentHash }, Token);
                Assert.True((await pinResultTasks.First()).Item.Succeeded);

                var expiry = await redisStore.GetContentHashExpiryAsync(new Context(Logger), putResult1.ContentHash, CancellationToken.None);
                Assert.True(expiry.HasValue);
                Assert.InRange(expiry.Value, redisStore.ContentHashBumpTime - TimeSpan.FromSeconds(15), redisStore.ContentHashBumpTime + TimeSpan.FromSeconds(15));
            });
        }

        [Fact]
        public Task PlaceUpdatesExpiryWhenFoundLocal()
        {
            return RunTestAsync(
                new Context(Logger),
                1,
                async context =>
                {
                    using (var directory = new DisposableDirectory(FileSystem))
                    {
                        var sessions = context.Sessions;

                        var session = context.GetDistributedSession(0);
                        var redisStore = context.GetRedisStore(session);

                        // Insert random file in session 0 with expiry of 30 minutes
                        redisStore.ContentHashBumpTime = TimeSpan.FromMinutes(30);
                        var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                        var putResult1 = await sessions[0].PutStreamAsync(
                            context,
                            HashType.Vso0,
                            new MemoryStream(randomBytes1),
                            Token);
                        Assert.True(putResult1.Succeeded);

                        // Insert same file in same session and update expiry to 1 hour
                        redisStore.ContentHashBumpTime = TimeSpan.FromHours(1);
                        var result = await session.PlaceFileAsync(
                            new Context(Logger),
                            new[] { new ContentHashWithPath(putResult1.ContentHash, directory.CreateRandomFileName()) },
                            FileAccessMode.Write,
                            FileReplacementMode.FailIfExists,
                            FileRealizationMode.Any,
                            Token);

                        Assert.True((await result.First()).Item.Succeeded);
                        var expiry = await redisStore.GetContentHashExpiryAsync(new Context(Logger), putResult1.ContentHash, CancellationToken.None);
                        Assert.True(expiry.HasValue);
                        Assert.InRange(
                            expiry.Value,
                            redisStore.ContentHashBumpTime - TimeSpan.FromSeconds(15),
                            redisStore.ContentHashBumpTime + TimeSpan.FromSeconds(15));
                    }
                });
        }

        [Fact]
        public Task PlaceUpdatesExpiryWhenFoundRemote()
        {
            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    using (var directory = new DisposableDirectory(FileSystem))
                    {
                        var sessions = context.Sessions;

                        var session = context.GetDistributedSession(0);
                        var redisStore = context.GetRedisStore(session);

                        // Insert random file in session 0 with expiry of 30 minutes
                        redisStore.ContentHashBumpTime = TimeSpan.FromMinutes(30);
                        var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                        var putResult1 = await sessions[0].PutStreamAsync(
                            context,
                            HashType.Vso0,
                            new MemoryStream(randomBytes1),
                            Token);
                        Assert.True(putResult1.Succeeded);

                        session = (DistributedContentSession<AbsolutePath>)sessions[1];
                        redisStore = context.GetRedisStore(session);

                        // Insert same file in session 1 and update expiry to 1 hour
                        redisStore.ContentHashBumpTime = TimeSpan.FromHours(1);
                        var result = await session.PlaceFileAsync(
                            new Context(Logger),
                            new[] { new ContentHashWithPath(putResult1.ContentHash, directory.CreateRandomFileName()) },
                            FileAccessMode.Write,
                            FileReplacementMode.FailIfExists,
                            FileRealizationMode.Any,
                            Token);

                        var first = (await result.First()).Item;
                        Assert.True(first.Succeeded, first.ErrorMessage);
                        var expiry = await redisStore.GetContentHashExpiryAsync(new Context(Logger), putResult1.ContentHash, CancellationToken.None);
                        Assert.True(expiry.HasValue, "Expiry should have value.");
                        Assert.InRange(
                            expiry.Value,
                            redisStore.ContentHashBumpTime - TimeSpan.FromSeconds(15),
                            redisStore.ContentHashBumpTime + TimeSpan.FromSeconds(15));
                    }
                });
        }

        [Fact]
        public Task StreamEmptyFileWithoutCopying()
        {
            return RunTestAsync(
                new Context(Logger),
                1,
                async context =>
                {
                    using (var directory = new DisposableDirectory(FileSystem))
                    {
                        var sessions = context.Sessions;

                        var openStreamResult = await context.GetDistributedSession(0).OpenStreamAsync(context, VsoHashInfo.Instance.EmptyHash, Token);

                        openStreamResult.ShouldBeSuccess();
                        Assert.Equal(0, openStreamResult.Stream.Length);
                        Assert.Equal(0, context.FileCopier.FilesCopied.Count);
                    }
                },
                emptyFileHashShortcutEnabled: true);
        }

        [Fact]
        public Task PinEmptyFileWithoutCopying()
        {
            return RunTestAsync(
                new Context(Logger),
                1,
                async context =>
                {
                    using (var directory = new DisposableDirectory(FileSystem))
                    {
                        var sessions = context.Sessions;

                        await context.GetDistributedSession(0).PinAsync(context, VsoHashInfo.Instance.EmptyHash, Token).ShouldBeSuccess();

                        Assert.Equal(0, context.FileCopier.FilesCopied.Count);
                    }
                },
                emptyFileHashShortcutEnabled: true);
        }

        [Fact]
        public Task PlaceEmptyFileWithoutCopying()
        {
            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    using (var directory = new DisposableDirectory(FileSystem))
                    {
                        var sessions = context.Sessions;

                        var placeFileResult = (await context.GetDistributedSession(0).PlaceFileAsync(
                            context,
                            new[] { new ContentHashWithPath(VsoHashInfo.Instance.EmptyHash, directory.CreateRandomFileName()) },
                            FileAccessMode.Write,
                            FileReplacementMode.FailIfExists,
                            FileRealizationMode.Any,
                            Token)).First().Result.Item;

                        placeFileResult.ShouldBeSuccess();
                        Assert.Equal(0, placeFileResult.FileSize);
                        Assert.Equal(0, context.FileCopier.FilesCopied.Count);
                    }
                },
                emptyFileHashShortcutEnabled: true);
        }

        protected async Task RunTestAsync(
            Context context,
            int storeCount,
            Func<TestContext, Task> testFunc,
            ImplicitPin implicitPin = ImplicitPin.PutAndGet,
            bool enableDistributedEviction = false,
            int? replicaCreditInMinutes = null,
            bool enableRepairHandling = false,
            bool emptyFileHashShortcutEnabled = false,
            int iterations = 1)
        {
            var indexedDirectories = Enumerable.Range(0, storeCount)
                .Select(i => new { Index = i, Directory = new DisposableDirectory(FileSystem, TestRootDirectoryPath / i.ToString()) })
                .ToList();
            var testFileCopier = new TestFileCopier();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                var stores = indexedDirectories.Select(
                    directory =>
                        CreateStore(
                            context,
                            testFileCopier,
                            directory.Directory,
                            directory.Index,
                            enableDistributedEviction,
                            replicaCreditInMinutes,
                            enableRepairHandling,
                            emptyFileHashShortcutEnabled)).ToList();

                var startupResults = await TaskSafetyHelpers.WhenAll(stores.Select(async store => await store.StartupAsync(context)));

                Assert.True(startupResults.All(x => x.Succeeded), $"Failed to startup: {string.Join(Environment.NewLine, startupResults.Where(s => !s))}");

                var id = 0;
                var sessions = stores.Select(store => store.CreateSession(context, "store" + id++, implicitPin).Session).ToList();
                await TaskSafetyHelpers.WhenAll(sessions.Select(async session => await session.StartupAsync(context)));

                var testContext = new TestContext(context, testFileCopier, indexedDirectories.Select(p => p.Directory).ToList(), sessions, stores, iteration);
                await testFunc(testContext);

                await TaskSafetyHelpers.WhenAll(
                    sessions.Select(async session =>
                     {
                         if (!session.ShutdownCompleted)
                         {
                             await session.ShutdownAsync(context).ThrowIfFailure();
                         }
                     }));
                sessions.ForEach(session => session.Dispose());

                await TaskSafetyHelpers.WhenAll(Enumerable.Range(0, storeCount).Select(storeId => LogStats(testContext, storeId)));

                await TaskSafetyHelpers.WhenAll(stores.Select(async store => await store.ShutdownAsync(context)));
                stores.ForEach(store => store.Dispose());
            }

            indexedDirectories.ForEach(directory => directory.Directory.Dispose());
        }

        protected async Task LogStats(TestContext context, int storeId)
        {
            var store = context.Stores[storeId];
            var stats = await store.GetStatsAsync(context);
            if (stats.Succeeded)
            {
                foreach (var counter in stats.CounterSet.ToDictionaryIntegral())
                {
                    context.Context.Debug($"Stat: Store{storeId}.{counter.Key}=[{counter.Value}]");
                }
            }
        }

        protected async Task OpenStreamReturnsExpectedFile(
            IReadOnlyContentSession session, Context context, ContentHash hash, byte[] expected)
        {
            OpenStreamResult openResult = await session.OpenStreamAsync(context, hash, Token);
            context.Debug($"Validating stream for content hash {hash} returned result {openResult.Code} with diagnostics {openResult} with ErrorMessage {openResult.ErrorMessage} diagnostics {openResult.Diagnostics}");
            Assert.Equal<OpenStreamResult.ResultCode>(OpenStreamResult.ResultCode.Success, openResult.Code);
            Assert.True(openResult.Succeeded, $"OpenStream did not succeed for content hash {hash}");
            Assert.NotNull(openResult.Stream);

            using (openResult.Stream)
            {
                var actualBytes = await openResult.Stream.GetBytes(false);
                actualBytes.Should().Equal(expected);
            }
        }
    }
}
