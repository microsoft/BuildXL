// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using ContentStoreTest.Extensions;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using System.Diagnostics.ContractsLight;

namespace ContentStoreTest.Distributed.Sessions
{
    public abstract class DistributedContentTests : TestBase
    {
        protected static readonly CancellationToken Token = CancellationToken.None;
        protected static readonly ContentStoreConfiguration Config = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(50);
        protected bool UseGrpcServer;

        public MemoryClock TestClock { get; } = new MemoryClock();

        protected abstract (IContentStore store, IStartupShutdown server) CreateStore(
            Context context,
            IAbsolutePathFileCopier fileCopier,
            DisposableDirectory testDirectory,
            int index,
            int iteration,
            uint grpcPort);

        public class TestContext
        {
            private readonly bool _traceStoreStatistics;
            public readonly Context Context;
            public readonly Context[] StoreContexts;
            public readonly TestFileCopier TestFileCopier;
            public readonly IAbsolutePathFileCopier FileCopier;
            public readonly IList<DisposableDirectory> Directories;
            public IList<IContentSession> Sessions { get; protected set; }
            public readonly IList<IContentStore> Stores;
            public readonly IList<IStartupShutdown> Servers;
            public readonly int Iteration;

            public TestContext(TestContext other)
                : this(other.Context, other.FileCopier, other.Directories, other.Stores.Select((store, i) => (store, other.Servers[i])).ToList(), other.Iteration, other._traceStoreStatistics)
            {
            }

            public TestContext(Context context, IAbsolutePathFileCopier fileCopier, IList<DisposableDirectory> directories, IList<(IContentStore store, IStartupShutdown server)> stores, int iteration, bool traceStoreStatistics = false)
            {
                _traceStoreStatistics = traceStoreStatistics;
                Context = context;
                StoreContexts = stores.Select((s, index) => CreateContext(index, iteration)).ToArray();
                TestFileCopier = fileCopier as TestFileCopier;
                FileCopier = fileCopier;
                Directories = directories;
                Stores = stores.Select(s => s.store).ToList();
                Servers = stores.Select(s => s.server ?? s.store).ToList();
                Iteration = iteration;

                if (TestFileCopier != null)
                {
                    for (int i = 0; i < Stores.Count; i++)
                    {
                        var distributedStore = (DistributedContentStore<AbsolutePath>)GetDistributedStore(i);
                        TestFileCopier.CopyHandlersByLocation[distributedStore.LocalMachineLocation] = distributedStore;
                        TestFileCopier.PushHandlersByLocation[distributedStore.LocalMachineLocation] = distributedStore;
                        TestFileCopier.DeleteHandlersByLocation[distributedStore.LocalMachineLocation] = distributedStore;
                    }

                }
            }

            private Context CreateContext(int index, int iteration)
            {
                var idBytes = Enumerable.Repeat(byte.MaxValue, 16).ToArray();
                
                idBytes[0] = (byte)index;
                idBytes[5] = (byte)iteration;

                return new Context(Context, new Guid(idBytes));
            }

            public virtual async Task StartupAsync(ImplicitPin implicitPin)
            {
                var startupResults = await TaskSafetyHelpers.WhenAll(Servers.Select(async (server, index) => await server.StartupAsync(StoreContexts[index])));

                Assert.True(startupResults.All(x => x.Succeeded), $"Failed to startup: {string.Join(Environment.NewLine, startupResults.Where(s => !s))}");

                Sessions = Stores.Select((store, id) => store.CreateSession(Context, "store" + id, implicitPin).Session).ToList();
                await TaskSafetyHelpers.WhenAll(Sessions.Select(async (session, index) => await session.StartupAsync(StoreContexts[index])));
            }

            public virtual async Task ShutdownAsync()
            {
                await TaskSafetyHelpers.WhenAll(
                    Sessions.Select(async (session, index) =>
                    {
                        if (!session.ShutdownCompleted)
                        {
                            await session.ShutdownAsync(StoreContexts[index]).ThrowIfFailure();
                        }
                    }));

                foreach (var session in Sessions)
                {
                    session.Dispose();
                }

                if (_traceStoreStatistics)
                {
                    await LogStatsAsync();
                }

                await ShutdownStoresAsync();
            }

            protected virtual async Task ShutdownStoresAsync()
            {
                await TaskSafetyHelpers.WhenAll(Servers.Select(async (server, index) => await server.ShutdownAsync(StoreContexts[index])));

                foreach (var server in Servers)
                {
                    server.Dispose();
                }
            }

            protected async Task LogStatsAsync()
            {
                for (int storeId = 0; storeId < Stores.Count; storeId++)
                {
                    var store = Stores[storeId];
                    var stats = await store.GetStatsAsync(StoreContexts[storeId]);
                    if (stats.Succeeded)
                    {
                        foreach (var counter in stats.CounterSet.ToDictionaryIntegral())
                        {
                            StoreContexts[storeId].Debug($"Stat: Store{storeId}.{counter.Key}=[{counter.Value}]");
                        }
                    }
                }
            }

            public static implicit operator Context(TestContext context) => context.Context;

            public static implicit operator OperationContext(TestContext context) => new OperationContext(context);

            public virtual DistributedContentSession<AbsolutePath> GetDistributedSession(int idx)
            {
                var session = Sessions[idx];
                return (DistributedContentSession<AbsolutePath>)session;
            }

            public LocalLocationStore GetLocalLocationStore(int idx) =>
                ((TransitioningContentLocationStore)GetDistributedSession(idx).ContentLocationStore).LocalLocationStore;

            internal RedisGlobalStore GetRedisGlobalStore(int idx) =>
                (RedisGlobalStore)GetLocalLocationStore(idx).GlobalStore;

            internal TransitioningContentLocationStore GetLocationStore(int idx) =>
                ((TransitioningContentLocationStore)GetDistributedSession(idx).ContentLocationStore);

            internal IContentStore GetDistributedStore(int idx)
            {
                var store = Stores[idx];
                return store;
            }

            public int GetMasterIndex()
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

            internal void SendEventToMaster(ContentLocationEventData eventData)
            {
                GetMaster().LocalLocationStore.EventStore.DispatchAsync(this, eventData).GetAwaiter().GetResult();
            }

            internal TransitioningContentLocationStore GetMaster()
            {
                return GetLocationStore(GetMasterIndex());
            }

            internal IContentLocationStore GetContentLocationStore(DistributedContentSession<AbsolutePath> session)
            {
                return session.ContentLocationStore;
            }

            internal Task SyncAsync(int idx)
            {
                var store = (DistributedContentStore<AbsolutePath>)Stores[idx];
                var localContentStore = (FileSystemContentStore)store.InnerContentStore;
                return localContentStore.Store.SyncAsync(this);
            }

            public int GetFirstWorkerIndex()
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

            public IEnumerable<int> EnumerateWorkersIndices()
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

        [Fact(Skip = "Fails without old redis")]
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

        [Fact(Skip = "Fails without old redis")]
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
                });

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
                context.TestFileCopier.FilesCopied.TryAdd(localContentPath, localContentPath);
                context.TestFileCopier.FileExistenceByReturnCode.GetOrAdd(
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
                    var store = context.GetContentLocationStore(session);

                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    // Insert random file #1 into session
                    //store.ContentHashBumpTime = TimeSpan.FromHours(1);
                    var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();

                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #2 into session.
                    //store.ContentHashBumpTime = TimeSpan.FromMinutes(30);
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();

                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #3 into session.
                    //store.ContentHashBumpTime = TimeSpan.FromMinutes(15);
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    await session.PinAsync(loggingContext, putResult.ContentHash, CancellationToken.None, UrgencyHint.Nominal).ShouldBeSuccess();

                    // Put random large file #4 into session that will evict file #1 and #2
                    // Even though #3 is considered the least recently used, it's pinned so we can't evict it
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();

                    contentHashes.Add(putResult.ContentHash);
                },
                implicitPin: ImplicitPin.None);

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

        //[Fact]
        //public Task PinUpdatesExpiryAndSizeWhenFoundLocal()
        //{
        //    return RunTestAsync(new Context(Logger), 1, async context =>
        //    {
        //        var sessions = context.Sessions;

        //        var session = context.GetDistributedSession(0);
        //        var redisStore = context.GetRedisStore(session);

        //        // Insert random file in session 0 with expiry of 30 minutes
        //        //redisStore.ContentHashBumpTime = TimeSpan.FromMinutes(30);
        //        var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
        //        var putResult1 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token);
        //        Assert.True(putResult1.Succeeded);

        //        // Insert same file in same session and update expiry to 1 hour
        //        redisStore.ContentHashBumpTime = TimeSpan.FromHours(1);
        //        IEnumerable<Task<Indexed<PinResult>>> pinResultTasks = await sessions[0].PinAsync(context, new[] { putResult1.ContentHash }, Token);
        //        Assert.True((await pinResultTasks.First()).Item.Succeeded);

        //        var expiry = await redisStore.GetContentHashExpiryAsync(new Context(Logger), putResult1.ContentHash, CancellationToken.None);
        //        Assert.True(expiry.HasValue);
        //        Assert.InRange(expiry.Value, redisStore.ContentHashBumpTime - TimeSpan.FromSeconds(15), redisStore.ContentHashBumpTime + TimeSpan.FromSeconds(15));

        //        var result = await redisStore.GetBulkAsync(new Context(Logger), new[] { putResult1.ContentHash }, CancellationToken.None, UrgencyHint.Nominal);
        //        Assert.True(result.Succeeded);
        //        Assert.Equal(putResult1.ContentSize, result.ContentHashesInfo[0].Size);
        //    });
        //}

        //[Fact]
        //public Task PlaceUpdatesExpiryWhenFoundLocal()
        //{
        //    return RunTestAsync(
        //        new Context(Logger),
        //        1,
        //        async context =>
        //        {
        //            using (var directory = new DisposableDirectory(FileSystem))
        //            {
        //                var sessions = context.Sessions;

        //                var session = context.GetDistributedSession(0);
        //                var redisStore = context.GetRedisStore(session);

        //                // Insert random file in session 0 with expiry of 30 minutes
        //                //redisStore.ContentHashBumpTime = TimeSpan.FromMinutes(30);
        //                var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
        //                var putResult1 = await sessions[0].PutStreamAsync(
        //                    context,
        //                    HashType.Vso0,
        //                    new MemoryStream(randomBytes1),
        //                    Token);
        //                Assert.True(putResult1.Succeeded);

        //                // Insert same file in same session and update expiry to 1 hour
        //                //redisStore.ContentHashBumpTime = TimeSpan.FromHours(1);
        //                var result = await session.PlaceFileAsync(
        //                    new Context(Logger),
        //                    new[] { new ContentHashWithPath(putResult1.ContentHash, directory.CreateRandomFileName()) },
        //                    FileAccessMode.Write,
        //                    FileReplacementMode.FailIfExists,
        //                    FileRealizationMode.Any,
        //                    Token);

        //                Assert.True((await result.First()).Item.Succeeded);
        //                var expiry = await redisStore.GetContentHashExpiryAsync(new Context(Logger), putResult1.ContentHash, CancellationToken.None);
        //                Assert.True(expiry.HasValue);
        //                Assert.InRange(
        //                    expiry.Value,
        //                    redisStore.ContentHashBumpTime - TimeSpan.FromSeconds(15),
        //                    redisStore.ContentHashBumpTime + TimeSpan.FromSeconds(15));
        //            }
        //        });
        //}

        //[Fact]
        //public Task PlaceUpdatesExpiryWhenFoundRemote()
        //{
        //    return RunTestAsync(
        //        new Context(Logger),
        //        2,
        //        async context =>
        //        {
        //            using (var directory = new DisposableDirectory(FileSystem))
        //            {
        //                var sessions = context.Sessions;

        //                var session = context.GetDistributedSession(0);
        //                var redisStore = context.GetRedisStore(session);

        //                // Insert random file in session 0 with expiry of 30 minutes
        //                redisStore.ContentHashBumpTime = TimeSpan.FromMinutes(30);
        //                var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
        //                var putResult1 = await sessions[0].PutStreamAsync(
        //                    context,
        //                    HashType.Vso0,
        //                    new MemoryStream(randomBytes1),
        //                    Token);
        //                Assert.True(putResult1.Succeeded);

        //                session = session = context.GetDistributedSession(1);
        //                redisStore = context.GetRedisStore(session);

        //                // Insert same file in session 1 and update expiry to 1 hour
        //                redisStore.ContentHashBumpTime = TimeSpan.FromHours(1);
        //                var result = await session.PlaceFileAsync(
        //                    new Context(Logger),
        //                    new[] { new ContentHashWithPath(putResult1.ContentHash, directory.CreateRandomFileName()) },
        //                    FileAccessMode.Write,
        //                    FileReplacementMode.FailIfExists,
        //                    FileRealizationMode.Any,
        //                    Token);

        //                var first = (await result.First()).Item;
        //                Assert.True(first.Succeeded, first.ErrorMessage);
        //                var expiry = await redisStore.GetContentHashExpiryAsync(new Context(Logger), putResult1.ContentHash, CancellationToken.None);
        //                Assert.True(expiry.HasValue, "Expiry should have value.");
        //                Assert.InRange(
        //                    expiry.Value,
        //                    redisStore.ContentHashBumpTime - TimeSpan.FromSeconds(15),
        //                    redisStore.ContentHashBumpTime + TimeSpan.FromSeconds(15));
        //            }
        //        });
        //}

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
                        Assert.Equal(0, context.TestFileCopier.FilesCopied.Count);
                    }
                });
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

                        Assert.Equal(0, context.TestFileCopier.FilesCopied.Count);
                    }
                });
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
                        Assert.Equal(0, context.TestFileCopier.FilesCopied.Count);
                    }
                });
        }

        public async Task RunTestAsync(
            Context context,
            int storeCount,
            Func<TestContext, Task> testFunc,
            ImplicitPin implicitPin = ImplicitPin.PutAndGet,
            int iterations = 1,
            TestContext outerContext = null,
            bool ensureLiveness = true)
        {
            var startIndex = outerContext?.Stores.Count ?? 0;
            var indexedDirectories = Enumerable.Range(0, storeCount)
                .Select(i => new { Index = i, Directory = new DisposableDirectory(FileSystem, TestRootDirectoryPath / (i + startIndex).ToString()) })
                .ToList();

            try
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    var testFileCopier = outerContext?.TestFileCopier ?? new TestFileCopier()
                    {
                        WorkingDirectory = indexedDirectories[0].Directory.Path
                    };

                    context.Always($"Starting test iteration {iteration}");

                    var ports = UseGrpcServer ? Enumerable.Range(0, storeCount).Select(n => PortExtensions.GetNextAvailablePort()).ToArray() : new int[storeCount];
                    IAbsolutePathFileCopier[] testFileCopiers;
                    if (UseGrpcServer)
                    {
                        Contract.Assert(storeCount == 2, "Currently we can only handle two stores while using gRPC, because of copiers.");
                        testFileCopiers = Enumerable.Range(0, 2).Select(i => new GrpcFileCopier(context, ports[i == 1 ? 0 : 1], maxGrpcClientCount: 1, maxGrpcClientAgeMinutes: 1)).ToArray();
                    }
                    else
                    {
                        testFileCopiers = Enumerable.Range(0, storeCount).Select(i => testFileCopier).ToArray();
                    }

                    var stores = indexedDirectories.Select(
                        directory =>
                            CreateStore(
                                context,
                                testFileCopiers[directory.Index],
                                directory.Directory,
                                directory.Index,
                                iteration: iteration,
                                grpcPort: (uint)ports[directory.Index])).ToList();

                    var testContext = ConfigureTestContext(new TestContext(context, testFileCopier, indexedDirectories.Select(p => p.Directory).ToList(), stores, iteration));

                    await testContext.StartupAsync(implicitPin);

                    // This mode is meant to make sure that all machines are alive and ready to go
                    if (ensureLiveness)
                    {
                        for (int i = 0; i < testContext.Sessions.Count; i++)
                        {
                            var localStore = testContext.GetLocationStore(i);

                            var globalStore = localStore.LocalLocationStore.GlobalStore;
                            var state = (await globalStore.SetLocalMachineStateAsync(testContext, MachineState.Unknown).ShouldBeSuccess()).Value;
                            if (state == MachineState.Closed)
                            {
                                await localStore.ReconcileAsync(testContext, force: true).ShouldBeSuccess();
                                await localStore.LocalLocationStore.HeartbeatAsync(testContext, inline: true).ShouldBeSuccess();
                            }
                        }

                        for (int i = 0; i < testContext.Sessions.Count; i++)
                        {
                            var localStore = testContext.GetLocationStore(i);
                            await localStore.LocalLocationStore.HeartbeatAsync(testContext, inline: true).ShouldBeSuccess();
                        }
                    }

                    await testFunc(testContext);

                    await testContext.ShutdownAsync();
                }
            }
            finally
            {
                foreach (var directory in indexedDirectories.Select(i => i.Directory))
                {
                    directory.Dispose();
                }
            }
        }

        protected virtual TestContext ConfigureTestContext(TestContext context)
        {
            return context;
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
