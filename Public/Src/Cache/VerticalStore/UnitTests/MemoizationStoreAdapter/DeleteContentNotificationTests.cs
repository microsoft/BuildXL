// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.Interfaces;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStoreAdapter;
using Xunit;
using DeleteResultCode = BuildXL.Cache.ContentStore.Interfaces.Results.DeleteResult.ResultCode;
using ICache = BuildXL.Cache.MemoizationStore.Interfaces.Caches.ICache;
using MemoICacheSession = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.ICacheSession;
using StrongFingerprint = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint;

#nullable enable

namespace BuildXL.Cache.MemoizationStoreAdapter.Test
{
    /// <summary>
    /// Tests for the <see cref="MemoizationStoreAdapterCacheCacheSession.DeleteContentAsync"/> notification chain.
    /// Verifies that <see cref="IContentDeletionNotifier.NotifyContentDeletedAsync"/> is called after
    /// successful blob deletion, and NOT called when deletion fails or content is not found.
    /// </summary>
    public class DeleteContentNotificationTests
    {
        private static readonly ILogger Logger = TestGlobal.Logger;

        [Fact]
        public async Task DeleteContentNotifiesOnSuccessfulDeletion()
        {
            var contentHash = ContentHash.Random();
            var mockCacheSession = new MockCacheSessionWithNotifier();
            var mockCache = new MockCacheWithContentStore(
                new MockContentStore(new DeleteResult(DeleteResultCode.Success, contentHash, 100)));

            var session = new MemoizationStoreAdapterCacheCacheSession(
                mockCacheSession,
                mockCache,
                new CacheId("test"),
                Logger,
                enableContentRecoveryOnPlaceFailure: true);

            var casHash = new CasHash(new global::BuildXL.Cache.Interfaces.Hash(contentHash));
            var result = await session.DeleteContentAsync(casHash, CancellationToken.None, Guid.Empty);

            Assert.True(result.Succeeded);
            Assert.Equal(ContentDeleteStatus.Deleted, result.Result);
            Assert.Single(mockCacheSession.NotifiedHashes);
            Assert.Equal(contentHash, mockCacheSession.NotifiedHashes[0]);
        }

        [Fact]
        public async Task DeleteContentDoesNotNotifyOnContentNotFound()
        {
            var contentHash = ContentHash.Random();
            var mockCacheSession = new MockCacheSessionWithNotifier();
            var mockCache = new MockCacheWithContentStore(
                new MockContentStore(new DeleteResult(DeleteResultCode.ContentNotFound, contentHash, 0)));

            var session = new MemoizationStoreAdapterCacheCacheSession(
                mockCacheSession,
                mockCache,
                new CacheId("test"),
                Logger,
                enableContentRecoveryOnPlaceFailure: true);

            var casHash = new CasHash(new global::BuildXL.Cache.Interfaces.Hash(contentHash));
            var result = await session.DeleteContentAsync(casHash, CancellationToken.None, Guid.Empty);

            Assert.True(result.Succeeded);
            Assert.Equal(ContentDeleteStatus.ContentNotFound, result.Result);
            Assert.Empty(mockCacheSession.NotifiedHashes);
        }

        [Fact]
        public async Task DeleteContentDoesNotNotifyOnDeleteFailure()
        {
            var contentHash = ContentHash.Random();
            var mockCacheSession = new MockCacheSessionWithNotifier();
            var mockCache = new MockCacheWithContentStore(
                new MockContentStore(new DeleteResult(DeleteResultCode.Error, "Simulated failure")));

            var session = new MemoizationStoreAdapterCacheCacheSession(
                mockCacheSession,
                mockCache,
                new CacheId("test"),
                Logger,
                enableContentRecoveryOnPlaceFailure: true);

            var casHash = new CasHash(new global::BuildXL.Cache.Interfaces.Hash(contentHash));
            var result = await session.DeleteContentAsync(casHash, CancellationToken.None, Guid.Empty);

            Assert.False(result.Succeeded);
            Assert.Empty(mockCacheSession.NotifiedHashes);
        }

        [Fact]
        public async Task DeleteContentDisabledWhenFeatureFlagOff()
        {
            var contentHash = ContentHash.Random();
            var mockCacheSession = new MockCacheSessionWithNotifier();
            var mockCache = new MockCacheWithContentStore(
                new MockContentStore(new DeleteResult(DeleteResultCode.Success, contentHash, 100)));

            var session = new MemoizationStoreAdapterCacheCacheSession(
                mockCacheSession,
                mockCache,
                new CacheId("test"),
                Logger,
                enableContentRecoveryOnPlaceFailure: false);

            var casHash = new CasHash(new global::BuildXL.Cache.Interfaces.Hash(contentHash));
            var result = await session.DeleteContentAsync(casHash, CancellationToken.None, Guid.Empty);

            Assert.True(result.Succeeded);
            Assert.Equal(ContentDeleteStatus.Disabled, result.Result);
            Assert.Empty(mockCacheSession.NotifiedHashes);
        }

        [Fact]
        public async Task DeleteContentDoesNotNotifyWhenSessionIsNotNotifier()
        {
            var contentHash = ContentHash.Random();
            var mockCacheSession = new MockCacheSessionWithoutNotifier();
            var mockCache = new MockCacheWithContentStore(
                new MockContentStore(new DeleteResult(DeleteResultCode.Success, contentHash, 100)));

            var session = new MemoizationStoreAdapterCacheCacheSession(
                mockCacheSession,
                mockCache,
                new CacheId("test"),
                Logger,
                enableContentRecoveryOnPlaceFailure: true);

            var casHash = new CasHash(new global::BuildXL.Cache.Interfaces.Hash(contentHash));
            var result = await session.DeleteContentAsync(casHash, CancellationToken.None, Guid.Empty);

            Assert.True(result.Succeeded);
            Assert.Equal(ContentDeleteStatus.Deleted, result.Result);
            // No notification since session doesn't implement IContentDeletionNotifier
        }

        #region Mock classes

        /// <summary>
        /// Mock ICache that also implements IContentStore, returning a preconfigured DeleteResult.
        /// </summary>
        private class MockCacheWithContentStore : ICache, IContentStore
        {
            private readonly MockContentStore _contentStore;

            public MockCacheWithContentStore(MockContentStore contentStore)
            {
                _contentStore = contentStore;
            }

            // IContentStore
            public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions = null)
            {
                return _contentStore.DeleteAsync(context, contentHash, deleteOptions);
            }

            CreateSessionResult<IContentSession> IContentStore.CreateSession(Context context, string name, ImplicitPin implicitPin) => throw new NotImplementedException();

            public Task<GetStatsResult> GetStatsAsync(Context context) => Task.FromResult(new GetStatsResult(new ContentStore.UtilitiesCore.CounterSet()));

            public void PostInitializationCompleted(Context context) { }

            // ICache
            public Guid Id => Guid.NewGuid();

            public bool StartupCompleted => true;

            public bool StartupStarted => true;

            public bool ShutdownCompleted => false;

            public bool ShutdownStarted => false;

            CreateSessionResult<MemoICacheSession> ICache.CreateSession(Context context, string name, ImplicitPin implicitPin) => throw new NotImplementedException();

            public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context) => throw new NotImplementedException();

            public Task<BoolResult> ShutdownAsync(Context context) => Task.FromResult(BoolResult.Success);

            public Task<BoolResult> StartupAsync(Context context) => Task.FromResult(BoolResult.Success);

            public void Dispose() { }
        }

        /// <summary>
        /// Mock content store that returns a preconfigured DeleteResult.
        /// </summary>
        private class MockContentStore
        {
            private readonly DeleteResult _deleteResult;

            public MockContentStore(DeleteResult deleteResult)
            {
                _deleteResult = deleteResult;
            }

            public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions = null)
            {
                return Task.FromResult(_deleteResult);
            }
        }

        /// <summary>
        /// Mock ICacheSession that implements IContentDeletionNotifier, tracking notifications.
        /// </summary>
        private class MockCacheSessionWithNotifier : MockCacheSessionBase, IContentDeletionNotifier
        {
            public List<ContentHash> NotifiedHashes { get; } = new();

            public Task NotifyContentDeletedAsync(Context context, ContentHash contentHash)
            {
                NotifiedHashes.Add(contentHash);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Mock ICacheSession that does NOT implement IContentDeletionNotifier.
        /// </summary>
        private class MockCacheSessionWithoutNotifier : MockCacheSessionBase
        {
        }

        /// <summary>
        /// Base mock ICacheSession with no-op implementations.
        /// </summary>
        private abstract class MockCacheSessionBase : MemoICacheSession
        {
            public string Name => "MockCacheSession";

            public bool StartupCompleted => true;

            public bool StartupStarted => true;

            public bool ShutdownCompleted => false;

            public bool ShutdownStarted => false;

            public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<BoolResult> IncorporateStrongFingerprintsAsync(Context context, IEnumerable<Task<StrongFingerprint>> strongFingerprints, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration config) => throw new NotImplementedException();

            public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PlaceFileResult> PlaceFileAsync(Context context, ContentHash contentHash, ContentStore.Interfaces.FileSystem.AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PutResult> PutFileAsync(Context context, HashType hashType, ContentStore.Interfaces.FileSystem.AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PutResult> PutFileAsync(Context context, ContentHash contentHash, ContentStore.Interfaces.FileSystem.AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PutResult> PutStreamAsync(Context context, HashType hashType, System.IO.Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, System.IO.Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new NotImplementedException();

            public Task<BoolResult> ShutdownAsync(Context context) => Task.FromResult(BoolResult.Success);

            public Task<BoolResult> StartupAsync(Context context) => Task.FromResult(BoolResult.Success);

            public void Dispose() { }
        }

        #endregion
    }
}
