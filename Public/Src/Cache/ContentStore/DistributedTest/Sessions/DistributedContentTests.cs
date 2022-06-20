// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.Host.Service.Internal;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    public abstract class DistributedContentTests<TStore, TSession> : TestBase
        where TSession : IContentSession
        where TStore : IStartupShutdown
    {
        private static readonly Tracer _tracer = new Tracer(nameof(DistributedContentTests<TStore, TSession>));

        protected readonly string UniqueTestId = Guid.NewGuid().ToString();

        // It is very important to use "cancellable" cancellation token instance.
        // This fact can be used by the system and change the behavior based on it.
        protected static readonly CancellationToken Token = new CancellationTokenSource().Token;

        protected static readonly ContentStoreConfiguration Config = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(50);
        protected bool UseGrpcServer;

        public MemoryClock TestClock { get; } = new MemoryClock();

        protected abstract CreateSessionResult<TSession> CreateSession(TStore store, Context context, string name, ImplicitPin implicitPin);

        protected abstract Task<GetStatsResult> GetStatsAsync(TStore store, Context context);

        protected abstract IContentStore UnwrapRootContentStore(TStore store);

        protected virtual IContentSession UnwrapRootContentSession(TSession session) => session;

        protected abstract TStore CreateFromArguments(DistributedCacheServiceArguments arguments);

        protected virtual DistributedCacheServiceArguments ModifyArguments(DistributedCacheServiceArguments arguments) => arguments;

        protected abstract TestServerProvider CreateStore(
            Context context,
            IRemoteFileCopier fileCopier,
            DisposableDirectory testDirectory,
            int index,
            int iteration,
            uint grpcPort);

        public class TestServerProvider
        {
            private readonly Func<TStore> _getStore;
            public IStartupShutdownSlim Server { get; }
            public TStore Store => _getStore();

            public TestServerProvider(IStartupShutdownSlim server, TStore store)
                : this(server ?? store, () => store)
            {
            }

            public TestServerProvider(IStartupShutdownSlim server, Func<TStore> getStore)
            {
                Assert.NotNull(server);
                Assert.NotNull(getStore);

                Server = server;
                _getStore = getStore;
            }

            public static implicit operator TestServerProvider((TStore store, IStartupShutdownSlim server) t)
            {
                return new TestServerProvider(t.server, t.store);
            }
        }

        public class TestContext
        {
            private readonly DistributedContentTests<TStore, TSession> _testInstance;
            private readonly bool _traceStoreStatistics;
            public readonly Context Context;
            public readonly OperationContext[] StoreContexts;
            public readonly TestFileCopier TestFileCopier;
            public readonly IRemoteFileCopier FileCopier;
            public readonly IList<DisposableDirectory> Directories;
            public IList<TSession> Sessions { get; protected set; }
            public IReadOnlyList<TestServerProvider> ServerProviders;
            public readonly IReadOnlyList<TStore> Stores;
            public readonly IList<IStartupShutdownSlim> Servers;
            public readonly int[] Ports;
            public readonly int Iteration;
            public ImplicitPin ImplicitPin { get; private set; }

            public virtual bool ShouldCreateContentSessions => true;

            public TestContext(TestContext other)
                : this(
                      other._testInstance,
                      other.Context,
                      other.FileCopier,
                      other.Directories,
                      other.ServerProviders,
                      other.Iteration,
                      other.Ports,
                      other._traceStoreStatistics)
            {
            }

            public TestContext(
                DistributedContentTests<TStore, TSession> testInstance,
                Context context,
                IRemoteFileCopier fileCopier,
                IList<DisposableDirectory> directories,
                IReadOnlyList<TestServerProvider> serverProviders,
                int iteration,
                int[] ports,
                bool traceStoreStatistics = false)
            {
                _testInstance = testInstance;
                _traceStoreStatistics = traceStoreStatistics;
                Context = context;
                StoreContexts = serverProviders.Select((s, index) => new OperationContext(CreateContext(index, iteration))).ToArray();
                TestFileCopier = fileCopier as TestFileCopier;
                FileCopier = fileCopier;
                Directories = directories;
                ServerProviders = serverProviders;
                Stores = serverProviders.SelectList(s => s.Store);
                Servers = serverProviders.Select(s => s.Server).ToList();
                Iteration = iteration;
                Ports = ports;
            }

            private Context CreateContext(int index, int iteration)
            {
                var idBytes = Enumerable.Repeat(byte.MaxValue, 16).ToArray();

                idBytes[0] = (byte)index;
                idBytes[5] = (byte)iteration;

                var nestedContextId = $"{Context.TraceId}.Idx_{index}.Iteration_{iteration}";
                return new Context(Context, nestedContextId, componentName: _tracer.Name);
            }

            public virtual async Task StartupAsync(ImplicitPin implicitPin, int? storeToStartupLast, string buildId = null, int? insideRingBuilderCount = null)
            {
                ImplicitPin = implicitPin;
                var startupResults = await TaskUtilities.SafeWhenAll(Servers.Select(async (server, index) =>
                {
                    if (index == storeToStartupLast)
                    {
                        return BoolResult.Success;
                    }

                    var result = await StartupServerAsync(index);
                    return result;

                }));

                Assert.True(startupResults.All(x => x.Succeeded), $"Failed to startup: {string.Join(Environment.NewLine, startupResults.Where(s => !s))}");

                if (storeToStartupLast.HasValue)
                {
                    var finalStartup = await StartupServerAsync(storeToStartupLast.Value).ShouldBeSuccess();
                }

                if (ShouldCreateContentSessions)
                {
                    Sessions = Stores.Select((store, id) => _testInstance.CreateSession(store, Context, GetSessionName(id, (insideRingBuilderCount == null || id < insideRingBuilderCount.Value) ? buildId : null), implicitPin).Session).ToList();
                    await TaskUtilities.SafeWhenAll(Sessions.Select(async (session, index) => await session.StartupAsync(StoreContexts[index])));
                }
            }

            private async Task<BoolResult> StartupServerAsync(int index)
            {
                var server = Servers[index];
                var result = await server.StartupAsync(StoreContexts[index]);
                if (result.Succeeded && TestFileCopier != null && Servers.Count > 1)
                {
                    var distributedStore = (DistributedContentStore)GetDistributedStore(index);
                    lock (TestFileCopier)
                    {
                        TestFileCopier.CopyHandlersByLocation[distributedStore.LocalMachineLocation] = distributedStore;
                        TestFileCopier.PushHandlersByLocation[distributedStore.LocalMachineLocation] = distributedStore;
                        TestFileCopier.DeleteHandlersByLocation[distributedStore.LocalMachineLocation] = distributedStore;
                        TestFileCopier.StreamStoresByLocation[distributedStore.LocalMachineLocation] = distributedStore;
                    }
                }

                return result;
            }

            protected static string GetSessionName(int id, string buildId) =>
                buildId == null || id == 0 // Master should not be part of the build.
                        ? $"store{id}"
                        : $"store{id}{Constants.BuildIdPrefix}{buildId}";

            public virtual async Task ShutdownAsync()
            {
                await TaskUtilities.SafeWhenAll(
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
                await TaskUtilities.SafeWhenAll(
                    Servers.Select(
                        async (server, index) =>
                        {
                            if (!server.ShutdownCompleted)
                            {
                                await server.ShutdownAsync(StoreContexts[index]).ThrowIfFailure();
                            }
                        }));

                foreach (var server in Servers.OfType<IDisposable>())
                {
                    server.Dispose();
                }
            }

            protected async Task LogStatsAsync()
            {
                for (int storeId = 0; storeId < Stores.Count; storeId++)
                {
                    var store = Stores[storeId];
                    var stats = await _testInstance.GetStatsAsync(store, StoreContexts[storeId]);
                    if (stats.Succeeded)
                    {
                        foreach (var counter in stats.CounterSet.ToDictionaryIntegral())
                        {
                            _tracer.Debug(StoreContexts[storeId], $"Stat: Store{storeId}.{counter.Key}=[{counter.Value}]");
                        }
                    }
                }
            }

            public static implicit operator Context(TestContext context) => context.Context;

            public static implicit operator OperationContext(TestContext context) => new OperationContext(context);

            public virtual TSession GetSession(int idx)
            {
                return Sessions[idx];
            }

            public virtual DistributedContentSession GetDistributedSession(int idx, bool primary = true)
            {
                return GetTypedSession<DistributedContentSession>(idx, primary);
            }

            public DistributedContentSession[] GetDistributedSessions()
            {
                return EnumerateWorkersIndices().Select(i => GetDistributedSession(i)).ToArray();
            }

            public FileSystemContentSession GetFileSystemSession(int idx, bool primary = true)
            {
                return GetTypedSession<FileSystemContentSession>(idx, primary);
            }

            private TTypedSession GetTypedSession<TTypedSession>(int idx, bool primary)
            {
                var session = _testInstance.UnwrapRootContentSession(GetSession(idx));
                while (!(session is TTypedSession))
                {
                    var nextSession = UnwrapSession(session, primary);
                    if (nextSession == session)
                    {
                        break;
                    }

                    session = nextSession;
                }

                return (TTypedSession)session;
            }

            private static IContentSession UnwrapSession(IContentSession session, bool primary)
            {
                if (session is MultiplexedContentSession multiplexSession)
                {
                    var primarySession = multiplexSession.PreferredContentSession;
                    session = (IContentSession)(primary ? primarySession : multiplexSession.SessionsByCacheRoot.Values.Where(s => s != primarySession).First());
                }
                else if (session is DistributedContentSession distributedSession)
                {
                    session = distributedSession.Inner;
                }

                return session;
            }

            public ContentLocationStoreServices GetServices(int? idx = null)
            {
                return (GetDistributedStore(idx ?? GetMasterIndex()).ContentLocationStoreFactory as ContentLocationStoreFactory)?.Services;
            }

            public ResilientGlobalCacheService GetContentMetadataService(int? idx = null)
            {
                return GetServices(idx ?? GetMasterIndex()).Dependencies.GlobalCacheService.InstanceOrDefault() as ResilientGlobalCacheService;
            }

            public LocalLocationStore GetLocalLocationStore(int idx)
            {
                return GetServices(idx).LocalLocationStore.Instance;
            }

            internal RedisGlobalStore GetRedisGlobalStore(int idx)
            {
                return GetServices(idx).RedisGlobalStore.Instance;
            }

            internal BlobContentLocationRegistry GetBlobContentLocationRegistry(int idx)
            {
                return GetServices(idx).BlobContentLocationRegistry.GetRequiredInstance();
            }

            internal TransitioningContentLocationStore GetLocationStore(int idx) =>
                ((TransitioningContentLocationStore)GetDistributedSession(idx).ContentLocationStore);

            public virtual DistributedContentStore GetDistributedStore(int idx, bool primary = true)
            {
                return GetTypedStore<DistributedContentStore>(idx, primary);
            }

            internal FileSystemContentStore GetFileSystemStore(int idx, bool primary = true)
            {
                return GetTypedStore<FileSystemContentStore>(idx, primary);
            }

            private TTypedStore GetTypedStore<TTypedStore>(int idx, bool primary)
            {
                var store = _testInstance.UnwrapRootContentStore(Stores[idx]);
                return GetTypedStore<TTypedStore>(store, primary);
            }

            public static TTypedStore GetTypedStore<TTypedStore>(IContentStore store, bool primary = true)
            {
                while (!(store is TTypedStore))
                {
                    var nextStore = UnwrapStore(store, primary);
                    if (nextStore == store)
                    {
                        break;
                    }

                    store = nextStore;
                }

                return (TTypedStore)store;
            }

            private static IContentStore UnwrapStore(IContentStore store, bool primary)
            {
                if (store is MultiplexedContentStore multiplexStore)
                {
                    var primaryStore = multiplexStore.PreferredContentStore;
                    store = primary ? primaryStore : multiplexStore.DrivesWithContentStore.Values.Where(s => s != primaryStore).First();
                }
                else if (store is DistributedContentStore distributedStore)
                {
                    store = distributedStore.InnerContentStore;
                }

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

            internal IContentLocationStore GetContentLocationStore(DistributedContentSession session)
            {
                return session.ContentLocationStore;
            }

            internal Task SyncAsync(int idx)
            {
                var store = GetFileSystemStore(idx);
                return store.Store.SyncAsync(this);
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
            await RunTestAsync(3, async context =>
            {
                var sessions = context.Sessions;

                // Insert random file in session 0
                var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                var putResult1 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token);
                Assert.True(putResult1.Succeeded);

                // Insert random file in session 1
                var randomBytes2 = ThreadSafeRandom.GetBytes(0x40);
                var putResult2 = await sessions[0].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes2), Token);
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
            return RunTestAsync(2, async context =>
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
            return RunTestAsync(2, async context =>
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
        public Task SomeLocalContentStoresCorrupt()
        {
            return RunTestAsync(3, async context =>
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

                // Query for file from session 2
                await OpenStreamReturnsExpectedFile(sessions[2], context.Context, putResult0.ContentHash, randomBytes);
            });
        }

        [Fact]
        public Task PutWithWrongHash()
        {
            return RunTestAsync(2, async testContext =>
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
                1,
                async context =>
                {
                    var session = context.GetSession(0);
                    var store = context.GetLocationStore(0);

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
                1,
                async context =>
                {
                    var session = context.GetSession(0);
                    var locationStore = context.GetLocationStore(0);

                    var locationsResult = await locationStore.GetBulkAsync(
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
                1,
                async context =>
                {
                    using (var directory = new DisposableDirectory(FileSystem))
                    {
                        var sessions = context.Sessions;

                        var openStreamResult = await context.GetSession(0).OpenStreamAsync(context, VsoHashInfo.Instance.EmptyHash, Token);

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
                1,
                async context =>
                {
                    using (var directory = new DisposableDirectory(FileSystem))
                    {
                        var sessions = context.Sessions;

                        await context.GetSession(0).PinAsync(context, VsoHashInfo.Instance.EmptyHash, Token).ShouldBeSuccess();

                        Assert.Equal(0, context.TestFileCopier.FilesCopied.Count);
                    }
                });
        }

        [Fact]
        public Task PlaceEmptyFileWithoutCopying()
        {
            return RunTestAsync(
                2,
                async context =>
                {
                    using (var directory = new DisposableDirectory(FileSystem))
                    {
                        var sessions = context.Sessions;

                        var placeFileResult = (await context.GetSession(0).PlaceFileAsync(
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

        protected virtual void InitializeTestRun(int storeCount)
        {
        }

        protected virtual Task CleanupTestRunAsync(TestContext context)
        {
            return BoolResult.SuccessTask;
        }

        public async Task RunTestAsync(
            int storeCount,
            Func<TestContext, Task> testFunc,
            ImplicitPin implicitPin = ImplicitPin.PutAndGet,
            int iterations = 1,
            TestContext outerContext = null,
            bool ensureLiveness = true,
            int? storeToStartupLast = null,
            TestFileCopier testCopier = null,
            string buildId = null,
            int? insideRingBuilderCount = null)
        {

            var context = new Context(Logger);
            var startIndex = outerContext?.Stores.Count ?? 0;
            var indexedDirectories = Enumerable.Range(0, storeCount)
                .Select(i => new { Index = i, Directory = new DisposableDirectory(FileSystem, TestRootDirectoryPath / (i + startIndex).ToString()) })
                .ToList();

            InitializeTestRun(storeCount);

            try
            {
                var ports = UseGrpcServer ? Enumerable.Range(0, storeCount).Select(n => PortExtensions.GetNextAvailablePort()).ToArray() : new int[storeCount];

                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    TestOutputPrefix = $"[{iteration}] ";

                    if (testCopier != null)
                    {
                        testCopier.WorkingDirectory = indexedDirectories[0].Directory.Path;
                    }

                    var testFileCopier = testCopier ?? outerContext?.TestFileCopier ?? new TestFileCopier(FileSystem)
                    {
                        WorkingDirectory = indexedDirectories[0].Directory.Path
                    };

                    _tracer.Always(context, $"Starting test iteration {iteration}");

                    IRemoteFileCopier[] testFileCopiers;
                    if (UseGrpcServer && storeCount > 1)
                    {
                        var grpcCopyClientCacheConfiguration = new GrpcCopyClientCacheConfiguration()
                        {
                            ResourcePoolVersion = GrpcCopyClientCacheConfiguration.PoolVersion.V2,
                            ResourcePoolConfiguration = new ResourcePoolConfiguration()
                            {
                                MaximumAge = TimeSpan.FromMinutes(1),
                                MaximumResourceCount = 1,
                            },
                            GrpcCopyClientConfiguration = new()
                            {
                                ConnectOnStartup = true
                            }
                        };

                        testFileCopiers = Enumerable.Range(0, storeCount).Select(i =>
                        {
                            var grpcFileCopierConfiguration = new GrpcFileCopierConfiguration()
                            {
                                GrpcPort = ports[i],
                                GrpcCopyClientCacheConfiguration = grpcCopyClientCacheConfiguration,
                                GrpcCopyClientInvalidationPolicy = GrpcFileCopierConfiguration.ClientInvalidationPolicy.OnEveryError,
                                UseUniversalLocations = true,
                            };

                            return new GrpcFileCopier(context, grpcFileCopierConfiguration);
                        }).ToArray();
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

                    var testContext = ConfigureTestContext(new TestContext(this, context, testFileCopier, indexedDirectories.Select(p => p.Directory).ToList(), stores, iteration, ports));

                    await testContext.StartupAsync(implicitPin, storeToStartupLast, buildId, insideRingBuilderCount);

                    // This mode is meant to make sure that all machines are alive and ready to go
                    if (ensureLiveness)
                    {
                        for (int i = 0; i < testContext.Sessions.Count; i++)
                        {
                            var localStore = testContext.GetLocationStore(i);

                            var state = (await localStore.LocalLocationStore.SetOrGetMachineStateAsync(testContext, MachineState.Unknown).ShouldBeSuccess()).Value;
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

                    await CleanupTestRunAsync(testContext);

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
            _tracer.Debug(context, $"Validating stream for content hash {hash} returned result {openResult.Code} with diagnostics {openResult} with ErrorMessage {openResult.ErrorMessage} diagnostics {openResult.Diagnostics}");
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
