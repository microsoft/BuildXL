// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Vsts;
using BuildXL.Cache.MemoizationStore.Vsts.Adapters;
using BuildXL.Cache.MemoizationStore.Vsts.Http;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
using Xunit;

// ReSharper disable ConvertClosureToMethodGroup
namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    public abstract class BuildCacheSimulationTests : CacheTests
    {
        public enum BackingOption
        {
            WriteThrough,
            WriteBehind,
            WriteNever
        }

        protected enum StorageOption
        {
            Item = 0,

            Blob = 1,
        }

        private readonly BackingOption _backingOption;
        protected readonly StorageOption ItemStorageOption;

        protected BuildCacheSimulationTests(ILogger logger, BackingOption backingOption, StorageOption itemStorageOption)
            : base(() => new PassThroughFileSystem(logger), logger)
        {
            _backingOption = backingOption;
            ItemStorageOption = itemStorageOption;
        }

        protected override HashType PreferredHashType => HashType.Vso0;

        protected override bool RespectsToolDeterminism => false;

        protected override bool SupportsMultipleHashTypes => false;

        protected override bool ImplementsEnumerateStrongFingerprints => false;

        protected override AuthorityLevel Authority
            => _backingOption == BackingOption.WriteThrough ? AuthorityLevel.Immediate : AuthorityLevel.Potential;

        protected virtual ICache CreateCache(
            DisposableDirectory testDirectory, string cacheNamespace, IAbsFileSystem fileSystem, ILogger logger, BackingOption backingOption, StorageOption storageOption, TimeSpan? expiryMinimum = null, TimeSpan? expiryRange = null)
        {
            return CreateBareBuildCache(testDirectory, cacheNamespace, fileSystem, logger, backingOption, storageOption, expiryMinimum, expiryRange);
        }

        private ICache CreateBareBuildCache(
            DisposableDirectory testDirectory,
            string cacheNamespace,
            IAbsFileSystem fileSystem,
            ILogger logger,
            BackingOption backingOption,
            StorageOption storageOption,
            TimeSpan? expiryMinimum = null,
            TimeSpan? expiryRange = null)
        {
            var vssCredentialsFactory = new VssCredentialsFactory(new VsoCredentialHelper());
            IBuildCacheHttpClientFactory buildCacheHttpClientFactory =
                new BuildCacheHttpClientFactory(new Uri(@"http://localhost:22085"), vssCredentialsFactory, TimeSpan.FromMinutes(BuildCacheServiceConfiguration.DefaultHttpSendTimeoutMinutes), false);
            IArtifactHttpClientFactory backingContentStoreHttpClientFactory =
                new BackingContentStoreHttpClientFactory(new Uri(@"http://localhost:22084"), vssCredentialsFactory, TimeSpan.FromMinutes(BuildCacheServiceConfiguration.DefaultHttpSendTimeoutMinutes), false);

            // Using a consistent path in the test directory allows tests to share content between
            // multiple callers.  Using FileSystemContentStore *will* require the callers to be serialized
            // because of its need for exclusive access (and the DirectoryLock enforcing it).
            var writeThroughContentStoreFunc = backingOption == BackingOption.WriteThrough
                ? (Func<IContentStore>)null
                : () => new FileSystemContentStore(
                    fileSystem,
                    SystemClock.Instance,
                    testDirectory.Path / "_writeThroughStore",
                    new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota("100MB"))));

            return new BuildCacheCache(
                fileSystem,
                cacheNamespace,
                buildCacheHttpClientFactory,
                backingContentStoreHttpClientFactory,
                BuildCacheServiceConfiguration.DefaultMaxFingerprintSelectorsToFetch,
                TimeSpan.FromDays(BuildCacheServiceConfiguration.DefaultDaysToKeepUnreferencedContent),
                TimeSpan.FromMinutes(BuildCacheServiceConfiguration.DefaultPinInlineThresholdMinutes),
                TimeSpan.FromMinutes(BuildCacheServiceConfiguration.DefaultIgnorePinThresholdHours),
                expiryMinimum.GetValueOrDefault(TimeSpan.FromDays(BuildCacheServiceConfiguration.DefaultDaysToKeepContentBags)),
                expiryRange.GetValueOrDefault(TimeSpan.FromDays(BuildCacheServiceConfiguration.DefaultRangeOfDaysToKeepContentBags)),
                logger,
                true,
                5,
                20,
                writeThroughContentStoreFunc,
                backingOption == BackingOption.WriteBehind,
                storageOption == StorageOption.Blob);
        }

        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            return CreateCache(testDirectory, Guid.NewGuid().ToString(), FileSystem, Logger, _backingOption, ItemStorageOption);
        }

        protected ICache CreateCache(DisposableDirectory testDirectory, string cacheNamespace, TimeSpan hashListLifetime, TimeSpan additionalHashListLifetime)
        {
            return CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, _backingOption, ItemStorageOption, hashListLifetime, additionalHashListLifetime);
        }

        private async Task IncorporateSucceedsAsyncRunner(Func<Context, ICacheSession, StrongFingerprint, ContentHashListWithDeterminism, Task<bool>> markForIncorporationFunc)
        {
            if (_backingOption != BackingOption.WriteThrough && ItemStorageOption != StorageOption.Blob)
            {
                // The item-store implementation only exposes expiration to its client when the values are backed
                // (i.e. Written-Through to BlobStore).
                // Since the goal is to move everyone towards the Blob implementation,
                // it's not worth investing in this gap for the Item implementation at this time.
                return;
            }

            TimeSpan expiryMinimum = TimeSpan.FromDays(7);
            TimeSpan expiryRange = TimeSpan.FromDays(2);

            var context = new Context(Logger);

            string cacheNamespace = Guid.NewGuid().ToString();

            var startTime = DateTime.UtcNow;
            StrongFingerprint strongFingerprint = await PublishValueForRandomStrongFingerprintAsync(
                context, cacheNamespace, _backingOption, expiryMinimum, expiryRange);

            await AssertExpirationInRangeAsync(
                context, cacheNamespace, strongFingerprint, startTime + expiryMinimum, startTime + expiryMinimum + expiryRange);

            TimeSpan longerExpiryMinimum = TimeSpan.FromDays(11);

            Func<DisposableDirectory, ICache> createCacheWithLongerHashLifetimeFunc =
                (dir) => CreateCache(dir, cacheNamespace, longerExpiryMinimum, expiryRange);
            Func<ICacheSession, Task> incorporateOnSessionShutdownFunc = async (ICacheSession incorporateSession) =>
            {
                ContentHashListWithDeterminism newValue = await CreateRandomContentHashListWithDeterminismAsync(context, true, incorporateSession);
                var incorporateSucceeded = await markForIncorporationFunc(context, incorporateSession, strongFingerprint, newValue).ConfigureAwait(false);
                Assert.True(incorporateSucceeded);
            };
            await RunTestAsync(context, incorporateOnSessionShutdownFunc, createCacheWithLongerHashLifetimeFunc);

            await AssertExpirationInRangeAsync(
                context, cacheNamespace, strongFingerprint, startTime + longerExpiryMinimum, startTime + longerExpiryMinimum + expiryRange);
        }

        [Fact]
        public async Task IncorporateSucceedsAsync()
        {
            await IncorporateSucceedsAsyncRunner(async (context, incorporateSession, strongFingerprint, contentHashListWithDeterminism) =>
            {
                var strongFingerprints = new List<Task<StrongFingerprint>>() { Task.FromResult(strongFingerprint) };
                BoolResult incorporateResult = await incorporateSession.IncorporateStrongFingerprintsAsync(context, strongFingerprints, Token).ConfigureAwait(false);
                return incorporateResult.Succeeded;
            });

            await IncorporateSucceedsAsyncRunner(async (context, incorporateSession, strongFingerprint, contentHashListWithDeterminism) =>
            {
                var addOrGetResult = await incorporateSession.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListWithDeterminism, Token).ConfigureAwait(false);
                return addOrGetResult.Succeeded;
            });

            await IncorporateSucceedsAsyncRunner(async (context, incorporateSession, strongFingerprint, contentHashListWithDeterminism) =>
            {
                var getResult = await incorporateSession.GetContentHashListAsync(context, strongFingerprint, Token).ConfigureAwait(false);
                return getResult.Succeeded && (getResult.ContentHashListWithDeterminism.ContentHashList != null);
            });
        }

        [Theory]
        [InlineData(BackingOption.WriteNever)]
        [InlineData(BackingOption.WriteThrough)]
        public async Task IncorporateOnlyStaleFingerprints(BackingOption initialBackingOption)
        {
            if (initialBackingOption != BackingOption.WriteThrough && ItemStorageOption != StorageOption.Blob)
            {
                // The item-store implementation only exposes expiration to its client when the values are backed
                // (i.e. Written-Through to BlobStore).
                // Since the goal is to move everyone towards the Blob implementation,
                // it's not worth investing in this gap for the Item implementation at this time.
                return;
            }

            TimeSpan staleExpiryMinimum = TimeSpan.FromDays(1);
            TimeSpan freshExpiryMinimum = TimeSpan.FromDays(7);
            TimeSpan expiryRange = TimeSpan.FromDays(2);

            // Setup
            // Publish 2 (sets of) fingerprints, one with an expiration of 1-3 days and one with an expiration of 7-9 days
            var context = new Context(Logger);

            string cacheNamespace = Guid.NewGuid().ToString();

            StrongFingerprint staleFingerprint = await PublishValueForRandomStrongFingerprintAsync(
                context, cacheNamespace, initialBackingOption, staleExpiryMinimum, expiryRange);
            StrongFingerprint freshFingerprint = await PublishValueForRandomStrongFingerprintAsync(
                context, cacheNamespace, initialBackingOption, freshExpiryMinimum, expiryRange);

            IEnumerable<StrongFingerprint> fingerprints = new[] {staleFingerprint, freshFingerprint};

            TimeSpan testExpiryMinimum = TimeSpan.FromDays(5);
            TimeSpan testExpiryRange = TimeSpan.FromDays(7);
            var startTime = DateTime.UtcNow;

            // Reference the fingerprints from a client configured to extend stale (younger than 5 days) fingerprints to 5-12 days.
            await ReferenceStrongFingerprintsAsync(
                    context, cacheNamespace, fingerprints, testExpiryMinimum, testExpiryRange);

            // Assert that the stale fingerprint has 5-12 days to live
            await AssertExpirationInRangeAsync(
                context, cacheNamespace, staleFingerprint, startTime + testExpiryMinimum, startTime + testExpiryMinimum + testExpiryRange);

            // Assert that the fresh fingerprint has 7-9 days to live
            await AssertExpirationInRangeAsync(
                context, cacheNamespace, freshFingerprint, startTime + freshExpiryMinimum, startTime + freshExpiryMinimum + expiryRange);
        }

        [Fact]
        public async Task ExpirationIsVisibleToClient()
        {
            if (_backingOption != BackingOption.WriteThrough && ItemStorageOption != StorageOption.Blob)
            {
                // The item-store implementation only exposes expiration to its client when the values are backed
                // (i.e. Written-Through to BlobStore).
                // Since the goal is to move everyone towards the Blob implementation,
                // it's not worth investing in this gap for the Item implementation at this time.
                return;
            }

            TimeSpan expiryMinimum = TimeSpan.FromDays(7);
            TimeSpan expiryRange = TimeSpan.FromDays(2);

            var context = new Context(Logger);
            string cacheNamespace = Guid.NewGuid().ToString();
            var startTime = DateTime.UtcNow;

            // Add a random value with an expiration of 7 + 2 days (the default minimum and range).
            StrongFingerprint publishedFingerprint = await PublishValueForRandomStrongFingerprintAsync(
                context, cacheNamespace, _backingOption, expiryMinimum, expiryRange);

            // Assert that the raw expiration is 7-9 days from now.
            await AssertExpirationInRangeAsync(
                context, cacheNamespace, publishedFingerprint, startTime + expiryMinimum, startTime + expiryMinimum + expiryRange);
        }

        private async Task<StrongFingerprint> PublishValueForRandomStrongFingerprintAsync(
            Context context,
            string cacheNamespace,
            BackingOption backingOption,
            TimeSpan expiryMinimum,
            TimeSpan expiryRange)
        {
            StrongFingerprint publishedFingerprint = await CreateBackedRandomStrongFingerprintAsync(context, cacheNamespace);

            Func<DisposableDirectory, ICache> createCache = dir => CreateBareBuildCache(
                dir, cacheNamespace, FileSystem, Logger, backingOption, ItemStorageOption, expiryMinimum, expiryRange);

            Func<ICacheSession, Task> publishAsync = async (ICacheSession session) =>
            {
                ContentHashListWithDeterminism valueToPublish =
                    await CreateRandomContentHashListWithDeterminismAsync(context, true, session);
                AddOrGetContentHashListResult addOrGetResult = await session.AddOrGetContentHashListAsync(
                    context, publishedFingerprint, valueToPublish, Token);

                // Ensure the new value was successfully published and that it was the winning value for that key
                Assert.True(addOrGetResult.Succeeded);
                Assert.Null(addOrGetResult.ContentHashListWithDeterminism.ContentHashList);
            };

            await RunTestAsync(context, publishAsync, createCache);

            return publishedFingerprint;
        }

        private async Task<StrongFingerprint> CreateBackedRandomStrongFingerprintAsync(
            Context context, string cacheNamespace)
        {
            Func<DisposableDirectory, ICache> createFingerprintCache = dir => CreateBareBuildCache(
                dir, cacheNamespace, FileSystem, Logger, BackingOption.WriteThrough, ItemStorageOption);

            StrongFingerprint dummyFingerprint = StrongFingerprint.Random();
            StrongFingerprint publishedFingerprint = dummyFingerprint;
            Func<ICacheSession, Task> publishSelectorHashAsync = async (ICacheSession session) =>
            {
                publishedFingerprint = await CreateRandomStrongFingerprintAsync(context, true, session);
            };

            await RunTestAsync(context, publishSelectorHashAsync, createFingerprintCache);
            Assert.NotEqual(dummyFingerprint, publishedFingerprint);

            return publishedFingerprint;
        }

        private Task ReferenceStrongFingerprintsAsync(
            Context context,
            string cacheNamespace,
            IEnumerable<StrongFingerprint> strongFingerprints,
            TimeSpan expiryMinimum,
            TimeSpan expiryRange)
        {
            Func<DisposableDirectory, ICache> createTestCache =
                dir => CreateCache(dir, cacheNamespace, expiryMinimum, expiryRange);

            Func<ICacheSession, Task> referenceFingerprintsAsync = async (ICacheSession session) =>
            {
                foreach (StrongFingerprint strongFingerprint in strongFingerprints)
                {
                    var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                    Assert.True(getResult.Succeeded);
                    Assert.NotNull(getResult.ContentHashListWithDeterminism.ContentHashList);
                }
            };

            return RunTestAsync(context, referenceFingerprintsAsync, createTestCache);
        }

        private async Task AssertExpirationInRangeAsync(Context context, string cacheNamespace, StrongFingerprint strongFingerprint, DateTime expectedLow, DateTime expectedHigh)
        {
            // Create a bare BuildCache client so that the value read is not thwarted by some intermediate cache layer (like Redis)
            Func<DisposableDirectory, ICache> createCheckerCacheFunc =
                dir => CreateBareBuildCache(dir, cacheNamespace, FileSystem, Logger, BackingOption.WriteThrough, ItemStorageOption);

            Func<ICacheSession, Task> checkFunc = async (ICacheSession checkSession) =>
            {
                // Bare BuildCache clients should only produce BuildCacheSessions
                BuildCacheSession buildCacheSession = checkSession as BuildCacheSession;
                Assert.NotNull(buildCacheSession);

                // Raw expiration is only visible to the adapter, not through the ICacheSession APIs.
                // This also has the nice (non-)side-effect of not updating the expiration as part of *this* read.
                IContentHashListAdapter buildCacheHttpClientAdapter = buildCacheSession.ContentHashListAdapter;
                ObjectResult<ContentHashListWithCacheMetadata> getResult = await buildCacheHttpClientAdapter.GetContentHashListAsync(context, cacheNamespace, strongFingerprint);
                Assert.True(getResult.Succeeded);
                Assert.NotNull(getResult.Data);
                DateTime? rawExpiration = getResult.Data.GetRawExpirationTimeUtc();
                Assert.NotNull(rawExpiration);

                // Verify that the raw expiration (i.e. the value's TTL w/o consideration for content existence or whether the value might be replaced) is visible and correct
                TimeSpan assertionTolerance = TimeSpan.FromHours(1); // The test time + the simulator's clock skew hopefully won't exceed 1 hour.
                Assert.InRange(rawExpiration.Value, expectedLow - assertionTolerance, expectedHigh + assertionTolerance);
            };

            await RunTestAsync(context, checkFunc, createCheckerCacheFunc);
        }
    }
}
