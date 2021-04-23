// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public abstract class PublishingCacheTests : CacheTests
    {
        // In this case, we need to skip this test. Since the inner cache has AuthorityLevle.None, it will succeed even when content is not backed, returning
        // the already existing option. The PublishingCache will then attempt to publish the existing CHL, which is the expected behavior.
        protected override bool SkipAuthoritativeCachesGiveErrorOnAddOrGetBeforeContentIsAddedEvenWhenDifferentValueExists => true;

        // Weird things happen in these tests because we only call the inner cache version of EnumerateStrongFingerprints.
        protected override bool ImplementsEnumerateStrongFingerprints => false;

        protected override AuthorityLevel Authority => AuthorityLevel.Potential;

        protected PublishingCacheTests(ITestOutputHelper output = null)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            var contentStore = CreateInnerCache(testDirectory);
            return new PublishingCacheWrapper(contentStore, CreatePublishingStore(new CacheToContentStore(contentStore)), Guid.NewGuid(), () => CreateConfiguration(publishAsynchronously: false));
        }

        protected abstract IPublishingStore CreatePublishingStore(IContentStore contentStore);

        private ICache CreateInnerCache(DisposableDirectory testDirectory)
        {
            var otherTest = new LocalCacheWithSingleCasTests();
            return otherTest.PublicCreateCache(testDirectory);
        }

        protected abstract PublishingCacheConfiguration CreateConfiguration(bool publishAsynchronously);

        [Fact]
        public async Task AsynchronousPublishingDoesNotBlock()
        {
            var context = new Context(Logger);
            using var testDirectory = new DisposableDirectory(FileSystem);
            var blockingStore = new BlockingPublishingStore();
            var publishingCache = new PublishingCache(CreateInnerCache(testDirectory), blockingStore, Guid.NewGuid());
            await publishingCache.StartupAsync(context).ShouldBeSuccess();

            var sessionResult = publishingCache.CreatePublishingSession(
                context,
                name: "Default",
                ImplicitPin.None,
                CreateConfiguration(publishAsynchronously: true),
                pat: Guid.NewGuid().ToString()).ShouldBeSuccess();

            var session = sessionResult.Session;
            await session.StartupAsync(context).ShouldBeSuccess();

            var amountOfFiles = 10;

            var putResults = await Task.WhenAll(Enumerable.Range(0, amountOfFiles + 2)
                .Select(n => session.PutRandomAsync(context, HashType.Vso0, provideHash: false, size: 1024, Token).ShouldBeSuccess()));

            var hashes = putResults.Select(r => r.ContentHash);
            var contentHashList = new ContentHashListWithDeterminism(
                new ContentHashList(hashes.Take(amountOfFiles).ToArray()),
                CacheDeterminism.None);
            var strongFingerprint = new StrongFingerprint(
                new Fingerprint(hashes.Skip(amountOfFiles).First().ToByteArray()),
                new Selector(hashes.Skip(amountOfFiles + 1).First()));

            await session.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashList, Token).ShouldBeSuccess();

            Assert.False(blockingStore.TaskCompletionSource.Task.IsCompleted);
            blockingStore.TaskCompletionSource.SetResult(new BoolResult(new Exception()));
            await blockingStore.TaskCompletionSource.Task.ShouldBeError();
        }
    }

    /// <summary>
    /// Used to be able to test all session features as if publishing has been specified.
    /// </summary>
    internal class PublishingCacheWrapper : PublishingCache
    {
        private Func<PublishingCacheConfiguration> _configFactory;

        /// <nodoc />
        public PublishingCacheWrapper(ICache local, IPublishingStore remote, Guid id, Func<PublishingCacheConfiguration> configFactory) : base(local, remote, id)
        {
            _configFactory = configFactory;
        }

        /// <inheritdoc />
        public override CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            var sessionResult = base.CreatePublishingSession(context, name, implicitPin, _configFactory(), pat: Guid.NewGuid().ToString());

            if (!sessionResult.Succeeded)
            {
                return new CreateSessionResult<IReadOnlyCacheSession>(sessionResult);
            }

            return new CreateSessionResult<IReadOnlyCacheSession>(sessionResult.Session);
        }

        /// <inheritdoc />
        public override CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
            => base.CreatePublishingSession(context, name, implicitPin, _configFactory(), pat: Guid.NewGuid().ToString());
    }

    internal class BlockingPublishingStore : StartupShutdownSlimBase, IPublishingStore
    {
        public TaskCompletionSource<BoolResult> TaskCompletionSource { get; set; } = new TaskCompletionSource<BoolResult>();

        protected override Tracer Tracer { get; } = new Tracer(nameof(BlockingPublishingStore));

        public Result<IPublishingSession> CreateSession(Context context, PublishingCacheConfiguration config, string pat)
            => new Result<IPublishingSession>(new BlockingPublishingSession(this));

        internal class BlockingPublishingSession : IPublishingSession
        {
            private readonly BlockingPublishingStore _store;

            public BlockingPublishingSession(BlockingPublishingStore store)
            {
                _store = store;
            }

            public async Task<BoolResult> PublishContentHashListAsync(Context context, StrongFingerprint fingerprint, ContentHashListWithDeterminism contentHashList, CancellationToken token)
            {
                var winningTask = await Task.WhenAny(_store.TaskCompletionSource.Task, Task.Delay(Timeout.InfiniteTimeSpan, token));

                if (winningTask == _store.TaskCompletionSource.Task)
                {
                    return await _store.TaskCompletionSource.Task;
                }
                else
                {
                    return new BoolResult(new TaskCanceledException());
                }
            }
        }
    }

    internal class CacheToContentStore : StartupShutdownSlimBase, IContentStore, ICache
    {
        private readonly ICache _inner;

        public CacheToContentStore(ICache inner)
        {
            _inner = inner;
        }

        public Guid Id => _inner.Id;

        protected override Tracer Tracer { get; } = new Tracer(nameof(CacheToContentStore));

        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
            => new CreateSessionResult<IReadOnlyContentSession>(_inner.CreateReadOnlySession(context, name, implicitPin).ShouldBeSuccess().Session);

        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
            => new CreateSessionResult<IContentSession>(_inner.CreateSession(context, name, implicitPin).ShouldBeSuccess().Session);

        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions)
            => throw new NotImplementedException();

        public void Dispose() => _inner.Dispose();
        public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context) => _inner.EnumerateStrongFingerprints(context);
        public Task<GetStatsResult> GetStatsAsync(Context context) => _inner.GetStatsAsync(context);
        public void PostInitializationCompleted(Context context, BoolResult result) { }
        CreateSessionResult<IReadOnlyCacheSession> ICache.CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin) => _inner.CreateReadOnlySession(context, name, implicitPin);
        CreateSessionResult<ICacheSession> ICache.CreateSession(Context context, string name, ImplicitPin implicitPin) => _inner.CreateSession(context, name, implicitPin);
    }
}
