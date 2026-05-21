// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using Xunit;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    /// <summary>
    /// Integration tests verifying the full content recovery notification chain:
    /// OneLevelCacheSession.NotifyContentDeletedAsync → DatabaseMemoizationSession.NotifyContentDeletedAsync
    /// → MetadataStoreMemoizationDatabase.ContentNotFoundOnPlaceAsync → NotifyAssociatedContentWasNotFoundAsync.
    /// </summary>
    public class ContentRecoveryIntegrationTests
    {
        [Fact]
        public async Task FullChainNotifyContentDeletedDeletesFingerprintViaRealObjects()
        {
            var context = new Context(NullLogger.Instance);
            var operationContext = new OperationContext(context);

            var mockStore = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
            };

            var memoDb = new MetadataStoreMemoizationDatabase(mockStore, config);
            var dbMemoStore = new DatabaseMemoizationStore(memoDb);

            // Start up the memoization store
            var startupResult = await dbMemoStore.StartupAsync(context);
            Assert.True(startupResult.Succeeded, $"DatabaseMemoizationStore startup failed: {startupResult}");

            try
            {
                // Create a DatabaseMemoizationSession (the real object, not a mock)
                var createResult = dbMemoStore.CreateSession(context, "test", contentSession: null!, automaticallyOverwriteContentHashLists: true);
                Assert.True(createResult.Succeeded, $"CreateSession failed: {createResult}");

                var memoSession = createResult.Session!;
                Assert.IsType<DatabaseMemoizationSession>(memoSession);
                var dbSession = (DatabaseMemoizationSession)memoSession;

                _ = await memoSession.StartupAsync(context);

                // Populate the content-to-fingerprint map by calling AssociatedContentNeedsPinning
                var contentHash = ContentHash.Random();
                var strongFingerprint = StrongFingerprint.Random();
                var contentHashList = new ContentHashList(new[] { contentHash });
                var chlWithDeterminism = new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None);
                var chlResult = new ContentHashListResult(chlWithDeterminism, "token", lastContentPinnedTime: null);

                memoDb.AssociatedContentNeedsPinning(operationContext, strongFingerprint, chlResult);

                // Now trigger the notification chain via DatabaseMemoizationSession
                await dbSession.NotifyContentDeletedAsync(context, contentHash);

                // Verify: the mock metadata store received the fingerprint deletion
                Assert.Contains(strongFingerprint, mockStore.ContentNotFoundNotifications);
            }
            finally
            {
                _ = await dbMemoStore.ShutdownAsync(context);
            }
        }

        [Fact]
        public async Task FullChainOneLevelCacheSessionDelegatesToDatabaseMemoizationSession()
        {
            var context = new Context(NullLogger.Instance);
            var operationContext = new OperationContext(context);

            var mockStore = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
            };

            var memoDb = new MetadataStoreMemoizationDatabase(mockStore, config);
            var dbMemoStore = new DatabaseMemoizationStore(memoDb);

            var startupResult = await dbMemoStore.StartupAsync(context);
            Assert.True(startupResult.Succeeded);

            try
            {
                // Create a real DatabaseMemoizationSession
                var createResult = dbMemoStore.CreateSession(context, "test", contentSession: null!, automaticallyOverwriteContentHashLists: true);
                Assert.True(createResult.Succeeded);

                var memoSession = createResult.Session!;
                _ = await memoSession.StartupAsync(context);

                // Wrap in OneLevelCacheSession (the way OneLevelCache does it)
                var contentSession = new MinimalContentSession();
                var oneLevelSession = new OneLevelCacheSession(
                    parent: null, name: "test", implicitPin: ImplicitPin.None,
                    memoizationSession: memoSession, contentSession: contentSession);

                // Populate the map
                var contentHash = ContentHash.Random();
                var strongFingerprint = StrongFingerprint.Random();
                var chlResult = new ContentHashListResult(
                    new ContentHashListWithDeterminism(new ContentHashList(new[] { contentHash }), CacheDeterminism.None),
                    "token", lastContentPinnedTime: null);

                memoDb.AssociatedContentNeedsPinning(operationContext, strongFingerprint, chlResult);

                // Call NotifyContentDeletedAsync through the IContentDeletionNotifier interface
                var notifier = (IContentDeletionNotifier)oneLevelSession;
                await notifier.NotifyContentDeletedAsync(context, contentHash);

                // Verify the full chain executed
                Assert.Contains(strongFingerprint, mockStore.ContentNotFoundNotifications);

                _ = await memoSession.ShutdownAsync(context);
            }
            finally
            {
                _ = await dbMemoStore.ShutdownAsync(context);
            }
        }

        [Fact]
        public async Task FullChainMultipleFingerprintsForSameContentAllDeleted()
        {
            var context = new Context(NullLogger.Instance);
            var operationContext = new OperationContext(context);

            var mockStore = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
            };

            var memoDb = new MetadataStoreMemoizationDatabase(mockStore, config);
            var dbMemoStore = new DatabaseMemoizationStore(memoDb);

            _ = await dbMemoStore.StartupAsync(context);

            try
            {
                var createResult = dbMemoStore.CreateSession(context, "test", contentSession: null!, automaticallyOverwriteContentHashLists: true);
                var dbSession = (DatabaseMemoizationSession)createResult.Session!;
                _ = await dbSession.StartupAsync(context);

                // Same content hash referenced by multiple fingerprints
                var sharedContentHash = ContentHash.Random();
                var fp1 = StrongFingerprint.Random();
                var fp2 = StrongFingerprint.Random();
                var fp3 = StrongFingerprint.Random();

                var chlResult1 = new ContentHashListResult(
                    new ContentHashListWithDeterminism(new ContentHashList(new[] { sharedContentHash }), CacheDeterminism.None),
                    "token1", lastContentPinnedTime: null);
                var chlResult2 = new ContentHashListResult(
                    new ContentHashListWithDeterminism(new ContentHashList(new[] { sharedContentHash }), CacheDeterminism.None),
                    "token2", lastContentPinnedTime: null);
                var chlResult3 = new ContentHashListResult(
                    new ContentHashListWithDeterminism(new ContentHashList(new[] { sharedContentHash }), CacheDeterminism.None),
                    "token3", lastContentPinnedTime: null);

                memoDb.AssociatedContentNeedsPinning(operationContext, fp1, chlResult1);
                memoDb.AssociatedContentNeedsPinning(operationContext, fp2, chlResult2);
                memoDb.AssociatedContentNeedsPinning(operationContext, fp3, chlResult3);

                // Trigger deletion through the session
                await dbSession.NotifyContentDeletedAsync(context, sharedContentHash);

                // All three fingerprints should have been deleted
                Assert.Contains(fp1, mockStore.ContentNotFoundNotifications);
                Assert.Contains(fp2, mockStore.ContentNotFoundNotifications);
                Assert.Contains(fp3, mockStore.ContentNotFoundNotifications);
                Assert.Equal(3, mockStore.ContentNotFoundNotifications.Count);

                _ = await dbSession.ShutdownAsync(context);
            }
            finally
            {
                _ = await dbMemoStore.ShutdownAsync(context);
            }
        }

        [Fact]
        public async Task ContentNotFoundListenerFiresDeletesFingerprintImmediately()
        {
            // This tests the "content not found" path (Path 1):
            // Content session fires "not found" listener → DatabaseMemoizationSession listener
            // → MetadataStoreMemoizationDatabase.ContentNotFoundOnPlaceAsync → metadata store deletes fingerprint.
            // No retries involved — fingerprint is deleted immediately because the blob is genuinely gone.
            var context = new Context(NullLogger.Instance);
            var operationContext = new OperationContext(context);

            var mockStore = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
            };

            var memoDb = new MetadataStoreMemoizationDatabase(mockStore, config);
            var dbMemoStore = new DatabaseMemoizationStore(memoDb);

            _ = await dbMemoStore.StartupAsync(context);

            try
            {
                // Create a content session that supports listener registration
                var contentSession = new ContentSessionWithNotFoundRegistration();

                // Create DatabaseMemoizationSession with the registrable content session —
                // the constructor registers a listener via AddContentNotFoundOnPlaceListener
                var createResult = dbMemoStore.CreateSession(context, "test", contentSession: contentSession, automaticallyOverwriteContentHashLists: true);
                Assert.True(createResult.Succeeded, $"CreateSession failed: {createResult}");

                var memoSession = createResult.Session!;
                _ = await memoSession.StartupAsync(context);

                // Verify the listener was registered
                Assert.Single(contentSession.Listeners);

                // Populate the content-to-fingerprint map
                var contentHash = ContentHash.Random();
                var strongFingerprint = StrongFingerprint.Random();
                var chlResult = new ContentHashListResult(
                    new ContentHashListWithDeterminism(new ContentHashList(new[] { contentHash }), CacheDeterminism.None),
                    "token", lastContentPinnedTime: null);

                memoDb.AssociatedContentNeedsPinning(operationContext, strongFingerprint, chlResult);

                // Simulate: content session detects "not found" and fires the registered listener
                // (this is what AzureBlobStorageContentSession.PlaceFileCoreAsync does)
                await contentSession.SimulateContentNotFound(context, contentHash);

                // Verify: fingerprint was deleted immediately via the listener chain
                Assert.Contains(strongFingerprint, mockStore.ContentNotFoundNotifications);

                _ = await memoSession.ShutdownAsync(context);
            }
            finally
            {
                _ = await dbMemoStore.ShutdownAsync(context);
            }
        }

        #region Minimal mocks

        /// <summary>
        /// Mock metadata store that tracks fingerprint deletion notifications.
        /// </summary>
        private class MockMetadataStoreWithPinNotification : IMetadataStoreWithContentPinNotification
        {
            public List<StrongFingerprint> ContentNotFoundNotifications { get; } = new();
            public List<StrongFingerprint> PinnedFingerprints { get; } = new();

            public string Name => nameof(MockMetadataStoreWithPinNotification);

            public bool StartupCompleted => true;
            public bool StartupStarted => true;
            public bool ShutdownCompleted => false;
            public bool ShutdownStarted => false;

            public Task<BoolResult> StartupAsync(Context context) => Task.FromResult(BoolResult.Success);
            public Task<BoolResult> ShutdownAsync(Context context) => Task.FromResult(BoolResult.Success);

            public Task<Result<bool>> CompareExchangeAsync(OperationContext context, StrongFingerprint strongFingerprint, SerializedMetadataEntry replacement, string expectedReplacementToken)
                => Task.FromResult(Result.Success(true));

            public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
                => Task.FromResult(Result.Success(new LevelSelectors(new List<Selector>(), false)));

            public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
                => Task.FromResult(Result.Success(new SerializedMetadataEntry()));

            public Task<GetStatsResult> GetStatsAsync(Context context)
                => Task.FromResult(new GetStatsResult(new CounterSet()));

            public Task<Result<bool>> NotifyContentWasPinnedAsync(OperationContext context, StrongFingerprint strongFingerprint)
            {
                PinnedFingerprints.Add(strongFingerprint);
                return Task.FromResult(Result.Success(true));
            }

            public Task<Result<bool>> NotifyAssociatedContentWasNotFoundAsync(OperationContext context, StrongFingerprint strongFingerprint)
            {
                ContentNotFoundNotifications.Add(strongFingerprint);
                return Task.FromResult(Result.Success(true));
            }
        }

        /// <summary>
        /// Minimal IContentSession stub for constructing OneLevelCacheSession.
        /// </summary>
        private class MinimalContentSession : IContentSession
        {
            public string Name => "MinimalContentSession";
            public bool StartupCompleted => true;
            public bool StartupStarted => true;
            public bool ShutdownCompleted => false;
            public bool ShutdownStarted => false;

            public Task<BoolResult> StartupAsync(Context context) => Task.FromResult(BoolResult.Success);
            public Task<BoolResult> ShutdownAsync(Context context) => Task.FromResult(BoolResult.Success);
            public void Dispose() { }

            public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
            public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
            public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration config) => throw new System.NotImplementedException();
            public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
            public Task<PlaceFileResult> PlaceFileAsync(Context context, ContentHash contentHash, ContentStore.Interfaces.FileSystem.AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
            public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
            public Task<PutResult> PutFileAsync(Context context, HashType hashType, ContentStore.Interfaces.FileSystem.AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
            public Task<PutResult> PutFileAsync(Context context, ContentHash contentHash, ContentStore.Interfaces.FileSystem.AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
            public Task<PutResult> PutStreamAsync(Context context, HashType hashType, System.IO.Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
            public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, System.IO.Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal) => throw new System.NotImplementedException();
        }

        /// <summary>
        /// Content session that implements IContentNotFoundRegistration, allowing listeners to be registered
        /// and then fired to simulate "content not found" events.
        /// </summary>
        private class ContentSessionWithNotFoundRegistration : IContentSession, IContentNotFoundRegistration
        {
            public List<Func<Context, ContentHash, Task>> Listeners { get; } = new();

            public string Name => "ContentSessionWithNotFoundRegistration";
            public bool StartupCompleted => true;
            public bool StartupStarted => true;
            public bool ShutdownCompleted => false;
            public bool ShutdownStarted => false;

            public void AddContentNotFoundOnPlaceListener(Func<Context, ContentHash, Task> listener)
            {
                Listeners.Add(listener);
            }

            public async Task SimulateContentNotFound(Context context, ContentHash contentHash)
            {
                foreach (var listener in Listeners)
                {
                    await listener(context, contentHash);
                }
            }

            public Task<BoolResult> StartupAsync(Context context) => Task.FromResult(BoolResult.Success);
            public Task<BoolResult> ShutdownAsync(Context context) => Task.FromResult(BoolResult.Success);
            public void Dispose() { }

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
        }

        #endregion
    }
}
