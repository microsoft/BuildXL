// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using Xunit;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Test.Stores
{
    public class MetadataStoreMemoizationDatabaseTests
    {
        public MetadataStoreMemoizationDatabaseTests()
        {
        }

        private static OperationContext CreateContext()
        {
            return new OperationContext(new Context(NullLogger.Instance));
        }

        private static ContentHashListResult CreateContentHashListResult(
            ContentHash[] hashes,
            DateTime? lastContentPinnedTime = null)
        {
            var contentHashList = new ContentHashList(hashes);
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None);
            return new ContentHashListResult(contentHashListWithDeterminism, "token", lastContentPinnedTime);
        }

        [Fact]
        public async Task RecoveryEnabledContentNotFoundDeletesFingerprintEntry()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var strongFingerprint = StrongFingerprint.Random();
            var contentHash = ContentHash.Random();
            var chlResult = CreateContentHashListResult(new[] { contentHash }, lastContentPinnedTime: DateTime.UtcNow);

            // Trigger map population (pin is needed since no retention policy, returns true)
            db.AssociatedContentNeedsPinning(context, strongFingerprint, chlResult);

            // Simulate content not found on place
            await db.ContentNotFoundOnPlaceAsync(context, contentHash);

            // Verify the store was notified
            Assert.Contains(strongFingerprint, store.ContentNotFoundNotifications);
        }

        [Fact]
        public async Task RecoveryDisabledRetentionPolicySetPinElidedNotifiesOnContentNotFound()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = false,
                RetentionPolicy = TimeSpan.FromDays(30),
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var strongFingerprint = StrongFingerprint.Random();
            var contentHash = ContentHash.Random();
            // Content was pinned very recently -> pin will be elided (result = false)
            var chlResult = CreateContentHashListResult(new[] { contentHash }, lastContentPinnedTime: DateTime.UtcNow);

            // Pin should be elided since content was pinned very recently
            bool needsPinning = db.AssociatedContentNeedsPinning(context, strongFingerprint, chlResult);
            Assert.False(needsPinning);

            // Simulate content not found on place
            await db.ContentNotFoundOnPlaceAsync(context, contentHash);

            // Verify notification was sent
            Assert.Contains(strongFingerprint, store.ContentNotFoundNotifications);
        }

        [Fact]
        public async Task RecoveryDisabledRetentionPolicySetPinNotElidedNoAction()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = false,
                RetentionPolicy = TimeSpan.FromDays(1),
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var strongFingerprint = StrongFingerprint.Random();
            var contentHash = ContentHash.Random();
            // Content was pinned long ago -> pin will NOT be elided (result = true)
            var chlResult = CreateContentHashListResult(new[] { contentHash }, lastContentPinnedTime: DateTime.UtcNow - TimeSpan.FromDays(30));

            // Pin should NOT be elided since content is old
            bool needsPinning = db.AssociatedContentNeedsPinning(context, strongFingerprint, chlResult);
            Assert.True(needsPinning);

            // Simulate content not found on place
            await db.ContentNotFoundOnPlaceAsync(context, contentHash);

            // Verify no action was taken (pin was not elided so entry was not tracked, and recovery is off)
            Assert.Empty(store.ContentNotFoundNotifications);
            Assert.Empty(store.ContentNotFoundNotifications);
        }

        [Fact]
        public async Task RecoveryDisabledNoRetentionPolicyNoAction()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = false,
                RetentionPolicy = null,
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var strongFingerprint = StrongFingerprint.Random();
            var contentHash = ContentHash.Random();
            var chlResult = CreateContentHashListResult(new[] { contentHash });

            db.AssociatedContentNeedsPinning(context, strongFingerprint, chlResult);

            // Simulate content not found on place
            await db.ContentNotFoundOnPlaceAsync(context, contentHash);

            // Verify no action since both flags are off
            Assert.Empty(store.ContentNotFoundNotifications);
            Assert.Empty(store.ContentNotFoundNotifications);
        }

        [Fact]
        public async Task BothFlagsEnabledPinElidedDeletesFingerprintEntry()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
                RetentionPolicy = TimeSpan.FromDays(30),
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var strongFingerprint = StrongFingerprint.Random();
            var contentHash = ContentHash.Random();
            // Content was pinned very recently -> pin will be elided
            var chlResult = CreateContentHashListResult(new[] { contentHash }, lastContentPinnedTime: DateTime.UtcNow);

            bool needsPinning = db.AssociatedContentNeedsPinning(context, strongFingerprint, chlResult);
            Assert.False(needsPinning);

            // Simulate content not found on place
            await db.ContentNotFoundOnPlaceAsync(context, contentHash);

            // Verify store was notified when recovery is enabled
            Assert.Contains(strongFingerprint, store.ContentNotFoundNotifications);
        }

        [Fact]
        public async Task RecoveryEnabledPinNotElidedStillDeletesFingerprintEntry()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
                RetentionPolicy = TimeSpan.FromDays(1),
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var strongFingerprint = StrongFingerprint.Random();
            var contentHash = ContentHash.Random();
            // Content was pinned long ago -> pin will NOT be elided
            var chlResult = CreateContentHashListResult(new[] { contentHash }, lastContentPinnedTime: DateTime.UtcNow - TimeSpan.FromDays(30));

            bool needsPinning = db.AssociatedContentNeedsPinning(context, strongFingerprint, chlResult);
            Assert.True(needsPinning);

            // Simulate content not found on place
            await db.ContentNotFoundOnPlaceAsync(context, contentHash);

            // Even though pin was not elided, recovery is enabled so fingerprint should still be deleted
            Assert.Contains(strongFingerprint, store.ContentNotFoundNotifications);
        }

        [Fact]
        public async Task ContentNotInMapNoAction()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            // Never called AssociatedContentNeedsPinning, so map is empty
            var unknownHash = ContentHash.Random();
            await db.ContentNotFoundOnPlaceAsync(context, unknownHash);

            Assert.Empty(store.ContentNotFoundNotifications);
            Assert.Empty(store.ContentNotFoundNotifications);
        }

        [Fact]
        public async Task DisablePreventivePinningRecoveryEnabledStillDeletesFingerprint()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
                DisablePreventivePinning = true,
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var strongFingerprint = StrongFingerprint.Random();
            var contentHash = ContentHash.Random();
            var chlResult = CreateContentHashListResult(new[] { contentHash });

            // DisablePreventivePinning returns false, but tracking should still happen
            bool needsPinning = db.AssociatedContentNeedsPinning(context, strongFingerprint, chlResult);
            Assert.False(needsPinning);

            await db.ContentNotFoundOnPlaceAsync(context, contentHash);

            Assert.Contains(strongFingerprint, store.ContentNotFoundNotifications);
        }

        [Fact]
        public async Task MultipleFingerprintsSameContentHashAllDeleted()
        {
            var store = new MockMetadataStoreWithPinNotification();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var sharedContentHash = ContentHash.Random();
            var fp1 = StrongFingerprint.Random();
            var fp2 = StrongFingerprint.Random();

            var chlResult1 = CreateContentHashListResult(new[] { sharedContentHash });
            var chlResult2 = CreateContentHashListResult(new[] { sharedContentHash });

            db.AssociatedContentNeedsPinning(context, fp1, chlResult1);
            db.AssociatedContentNeedsPinning(context, fp2, chlResult2);

            await db.ContentNotFoundOnPlaceAsync(context, sharedContentHash);

            Assert.Contains(fp1, store.ContentNotFoundNotifications);
            Assert.Contains(fp2, store.ContentNotFoundNotifications);
        }

        [Fact]
        public Task StoreWithoutPinNotificationNoAction()
        {
            // Use a store that does NOT implement IMetadataStoreWithContentPinNotification
            var store = new MockMetadataStoreBasic();
            var config = new MetadataStoreMemoizationDatabaseConfiguration
            {
                EnableContentRecoveryOnPlaceFailure = true,
            };

            var db = new MetadataStoreMemoizationDatabase(store, config);
            var context = CreateContext();

            var strongFingerprint = StrongFingerprint.Random();
            var contentHash = ContentHash.Random();
            var chlResult = CreateContentHashListResult(new[] { contentHash });

            db.AssociatedContentNeedsPinning(context, strongFingerprint, chlResult);

            // Should not throw — just a no-op since store doesn't support pin notification
            return db.ContentNotFoundOnPlaceAsync(context, contentHash);
        }

        /// <summary>
        /// Mock implementation of IMetadataStoreWithContentPinNotification for testing.
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
            {
                return Task.FromResult(Result.Success(true));
            }

            public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
            {
                return Task.FromResult(Result.Success(new LevelSelectors(new List<Selector>(), false)));
            }

            public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
            {
                return Task.FromResult(Result.Success(new SerializedMetadataEntry()));
            }

            public Task<GetStatsResult> GetStatsAsync(Context context)
            {
                return Task.FromResult(new GetStatsResult(new ContentStore.UtilitiesCore.CounterSet()));
            }

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
        /// Mock implementation of IMetadataStore that does NOT implement IMetadataStoreWithContentPinNotification.
        /// </summary>
        private class MockMetadataStoreBasic : IMetadataStore
        {
            public string Name => nameof(MockMetadataStoreBasic);

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
                => Task.FromResult(new GetStatsResult(new ContentStore.UtilitiesCore.CounterSet()));
        }
    }
}
