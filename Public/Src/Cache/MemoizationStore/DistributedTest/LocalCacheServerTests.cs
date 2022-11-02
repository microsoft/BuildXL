// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Service;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Test.Sessions;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Distributed.Test
{
    public class LocalCacheServerTests : TestBase
    {
        public LocalCacheServerTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task CacheSessionDataIsHibernated()
        {
            using var testDirectory = new DisposableDirectory(FileSystem);
            var cacheDirectory = testDirectory.Path / "Service";
            var cacheName = "theCache";
            var namedCacheRoots = new Dictionary<string, AbsolutePath> { [cacheName] = cacheDirectory / "Root" };
            var grpcPort = PortExtensions.GetNextAvailablePort();
            var serverConfiguration = new LocalServerConfiguration(cacheDirectory, namedCacheRoots, grpcPort, FileSystem)
            {
                GrpcPortFileName = null, // Port is well known at configuration time, no need to expose it.
            };
            var scenario = "Default";

            TaskCompletionSource<BoolResult> tcs = null;
            ICache createBlockingCache(AbsolutePath path)
            {
                var (cache, completionSource) = CreateBlockingPublishingCache(path);
                tcs = completionSource;
                return cache;
            }

            var server = new LocalCacheServer(
                FileSystem,
                TestGlobal.Logger,
                scenario,
                cacheFactory: createBlockingCache,
                serverConfiguration,
                Capabilities.All);

            var context = new OperationContext(new Context(Logger));
            await server.StartupAsync(context).ShouldBeSuccess();

            var pat = Guid.NewGuid().ToString();
            var publishingConfig = new PublishingConfigDummy
            {
                PublishAsynchronously = true,
            };
            using var clientCache = CreateClientCache(publishingConfig, pat, cacheName, grpcPort, scenario);
            await clientCache.StartupAsync(context).ShouldBeSuccess();
            var clientSession = clientCache.CreateSession(context, name: "TheSession", ImplicitPin.None).ShouldBeSuccess().Session;

            await clientSession.StartupAsync(context).ShouldBeSuccess();

            var piecesOfContent = 3;
            var putResults = await Task.WhenAll(
                Enumerable.Range(0, piecesOfContent)
                    .Select(_ => clientSession.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 128, context.Token).ShouldBeSuccess()));

            var contentHashList = new ContentHashList(putResults.Select(r => r.ContentHash).ToArray());
            var determinism = CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.UtcNow.AddDays(1));
            var contentHashListwithDeterminism = new ContentHashListWithDeterminism(contentHashList, determinism);

            var fingerprint = new Fingerprint(ContentHash.Random().ToByteArray());
            var selector = new Selector(ContentHash.Random(), output: new byte[] { 0, 42 });
            var strongFingerprint = new StrongFingerprint(fingerprint, selector);

            var cts = new CancellationTokenSource();
            // Even though publishing is blocking, this should succeed because we're publishing asynchronously.
            await clientSession.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListwithDeterminism, cts.Token).ShouldBeSuccess();

            // Allow for the publishing operation to be registered.
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Simulate a restart.
            await server.ShutdownAsync(context).ShouldBeSuccess();
            server.Dispose();

            server = new LocalCacheServer(
                FileSystem,
                TestGlobal.Logger,
                scenario: scenario,
                cacheFactory: createBlockingCache,
                serverConfiguration,
                Capabilities.All);

            await server.StartupAsync(context).ShouldBeSuccess();

            // Session should have been persisted.
            var sessionsAndDatas = server.GetCurrentSessions();
            sessionsAndDatas.Length.Should().Be(1);
            var serverSession = sessionsAndDatas[0].session;
            var data = sessionsAndDatas[0].data;

            var operation = new PublishingOperation
            {
                ContentHashListWithDeterminism = contentHashListwithDeterminism,
                StrongFingerprint = strongFingerprint
            };
            var operations = new[] { operation };

            data.Name.Should().Be(clientSession.Name);
            data.Pat.Should().Be(pat);
            data.Capabilities.Should().Be(Capabilities.All);
            data.ImplicitPin.Should().Be(ImplicitPin.None);
            data.Pins.Should().BeEquivalentTo(new List<string>());
            data.PublishingConfig.Should().BeEquivalentTo(publishingConfig);
            data.PendingPublishingOperations.Should().BeEquivalentTo(operations);

            var hibernateSession = serverSession as IHibernateCacheSession;
            hibernateSession.Should().NotBeNull();
            var actualPending = hibernateSession.GetPendingPublishingOperations();
            actualPending.Should().BeEquivalentTo(operations);

            // Shutting down the session should not cancel ongoing publishing operations.
            await clientSession.ShutdownAsync(context).ShouldBeSuccess();

            // Session should still be open in the server and operation still pending
            serverSession.ShutdownStarted.Should().BeFalse();
            actualPending = hibernateSession.GetPendingPublishingOperations();
            actualPending.Should().BeEquivalentTo(operations);

            tcs.SetResult(BoolResult.Success);

            // Wait for shutdown to take place
            await Task.Delay(100);
            serverSession.ShutdownStarted.Should().BeTrue();

            await server.ShutdownAsync(context).ShouldBeSuccess();
        }

        private ServiceClientPublishingCache CreateClientCache(PublishingCacheConfiguration publishingConfig, string pat, string cacheName, int grpcPort, string scenario)
        {
            var config = new ServiceClientContentStoreConfiguration(cacheName, new ContentStore.Sessions.ServiceClientRpcConfiguration(grpcPort), scenario);
            return new ServiceClientPublishingCache(Logger, FileSystem, config, publishingConfig, pat);
        }

        private (ICache, TaskCompletionSource<BoolResult>) CreateBlockingPublishingCache(AbsolutePath path)
        {
            var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
            var configurationModel = new ConfigurationModel(configuration);
            var contentStore = new FileSystemContentStore(FileSystem, SystemClock.Instance, path, configurationModel);
            var localCache = new OneLevelCache(
                () => contentStore,
                () => new MemoryMemoizationStore(Logger),
                CacheDeterminism.NewCacheGuid());
            var blockingStore = new BlockingPublishingStore();
            return (new PublishingCacheToContentStore(
                new PublishingCache<OneLevelCache>(localCache, new IPublishingStore[]
                {
                    blockingStore
                }, Guid.NewGuid())),
                blockingStore.TaskCompletionSource);
        }

        internal class PublishingCacheToContentStore : StartupShutdownSlimBase, IContentStore, IPublishingCache
        {
            private readonly IPublishingCache _inner;

            public PublishingCacheToContentStore(IPublishingCache inner)
            {
                _inner = inner;
            }

            public Guid Id => _inner.Id;

            protected override Tracer Tracer { get; } = new Tracer(nameof(PublishingCacheToContentStore));

            public CreateSessionResult<ICacheSession> CreatePublishingSession(Context context, string name, ImplicitPin implicitPin, PublishingCacheConfiguration publishingConfig, string pat) => _inner.CreatePublishingSession(context, name, implicitPin, publishingConfig, pat);
            public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
                => new CreateSessionResult<IReadOnlyContentSession>(_inner.CreateReadOnlySession(context, name, implicitPin).ShouldBeSuccess().Session);

            public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
                => new CreateSessionResult<IContentSession>(_inner.CreateSession(context, name, implicitPin).ShouldBeSuccess().Session);

            public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions)
                => throw new NotImplementedException();

            public override Task<BoolResult> StartupAsync(Context context) => _inner.StartupAsync(context);
            protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context) => _inner.ShutdownAsync(context);

            public void Dispose() => _inner.Dispose();
            public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context) => _inner.EnumerateStrongFingerprints(context);
            public Task<GetStatsResult> GetStatsAsync(Context context) => _inner.GetStatsAsync(context);
            public void PostInitializationCompleted(Context context, BoolResult result) { }
            CreateSessionResult<IReadOnlyCacheSession> ICache.CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin) => _inner.CreateReadOnlySession(context, name, implicitPin);
            CreateSessionResult<ICacheSession> ICache.CreateSession(Context context, string name, ImplicitPin implicitPin) => _inner.CreateSession(context, name, implicitPin);
        }

        private class PublishingConfigDummy : PublishingCacheConfiguration
        {
        }
    }
}
