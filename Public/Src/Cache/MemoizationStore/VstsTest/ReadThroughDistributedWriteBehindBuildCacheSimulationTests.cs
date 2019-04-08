// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    [Trait("Category", "Simulation")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class ReadThroughDistributedWriteBehindBuildCacheSimulationTests : WriteBehindBuildCacheSimulationTests
    {
        public ReadThroughDistributedWriteBehindBuildCacheSimulationTests()
            : this(StorageOption.Item)
        {
        }

        protected ReadThroughDistributedWriteBehindBuildCacheSimulationTests(StorageOption storageOption)
            : base(storageOption)
        {
        }

        protected override ICache CreateCache(
            DisposableDirectory testDirectory,
            string cacheNamespace,
            IAbsFileSystem fileSystem,
            ILogger logger,
            BackingOption backingOption,
            StorageOption storageOption,
            TimeSpan? expiryMinimum = null,
            TimeSpan? expiryRange = null)
        {
            return CreateCache(
                testDirectory,
                cacheNamespace,
                fileSystem,
                logger,
                backingOption,
                storageOption,
                expiryMinimum,
                expiryRange,
                readThroughMode: ReadThroughMode.ReadThrough);
        }

        private ICache CreateCache(
            DisposableDirectory testDirectory,
            string cacheNamespace,
            IAbsFileSystem fileSystem,
            ILogger logger,
            BackingOption backingOption,
            StorageOption storageOption,
            TimeSpan? expiryMinimum,
            TimeSpan? expiryRange,
            ReadThroughMode readThroughMode)
        {
            var innerCache = base.CreateCache(testDirectory, cacheNamespace, fileSystem, logger, backingOption, storageOption, expiryMinimum, expiryRange);
            return TestDistributedCacheFactory.CreateCache(
                logger, innerCache, cacheNamespace, nameof(ReadThroughDistributedWriteBehindBuildCacheSimulationTests), readThroughMode);
        }

        [Fact]
        public async Task GetTriggersBackgroundSealingByEndOfSessionVisibleToNonReadThrough()
        {
            var context = new Context(Logger);
            var cacheNamespace = Guid.NewGuid().ToString();

            // Each of the caches created below must use the same testDirectory in order to share local content.
            // And they must not run concurrently because the local content store requires exclusive access.
            // This is just for testing purposes; in production, "local" (not in the backing store) content
            // will live in CASaaS which allows concurrent access and can find and serve content from peers.
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // Add a value and its content to a write-never store. This will not upload the content but the content will still be available locally.
                StrongFingerprint strongFingerprint = StrongFingerprint.Random();
                ContentHashList contentHashList = null;
                await RunTestAsync(
                     context,
                     async (writeNeverCache, writeNeverSession) =>
                     {
                         strongFingerprint = await CreateRandomStrongFingerprintAsync(context, true, writeNeverSession);
                         var initialValue = await CreateRandomContentHashListWithDeterminismAsync(context, true, writeNeverSession);
                         contentHashList = initialValue.ContentHashList;

                         var addOrGetResult = await writeNeverSession.AddOrGetContentHashListAsync(
                             context, strongFingerprint, initialValue, Token).ConfigureAwait(false);
                         addOrGetResult.Succeeded.Should().BeTrue();
                         addOrGetResult.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
                     },
                     () => CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, BackingOption.WriteNever, ItemStorageOption));

                contentHashList.Should().NotBeNull();

                // Get the unbacked value to trigger a background sealing
                await RunTestAsync(
                    context,
                    async (writeBehindCache, writeBehindSession) =>
                    {
                        var getResult =
                            await writeBehindSession.GetContentHashListAsync(context, strongFingerprint, Token).ConfigureAwait(false);
                        getResult.Succeeded.Should().BeTrue();
                        getResult.ContentHashListWithDeterminism.ContentHashList.Should().Be(contentHashList);
                        getResult.ContentHashListWithDeterminism.Determinism.IsDeterministic.Should().BeFalse();
                    },
                    () => CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, BackingOption.WriteBehind, ItemStorageOption));

                // Get the backed to clear the unbacked value from the metadata cache
                await RunTestAsync(
                    context,
                    async (writeBehindCache, writeBehindSession) =>
                    {
                        var getResult =
                            await writeBehindSession.GetContentHashListAsync(context, strongFingerprint, Token).ConfigureAwait(false);
                        getResult.Succeeded.Should().BeTrue();
                        getResult.ContentHashListWithDeterminism.ContentHashList.Should().Be(contentHashList);
                        getResult.ContentHashListWithDeterminism.Determinism.IsDeterministic.Should().BeTrue();
                    },
                    () => CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, BackingOption.WriteBehind, ItemStorageOption));

                // Check that the previous run sealed the value as backed and that even non-readThrough clients can see it
                await RunTestAsync(
                    context,
                    async (writeBehindCache, writeBehindSession) =>
                    {
                        var getResult =
                            await writeBehindSession.GetContentHashListAsync(context, strongFingerprint, Token).ConfigureAwait(false);
                        getResult.Succeeded.Should().BeTrue();
                        getResult.ContentHashListWithDeterminism.ContentHashList.Should().Be(contentHashList);
                        Assert.Equal(writeBehindCache.Id, getResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
                        getResult.ContentHashListWithDeterminism.Determinism.IsDeterministic.Should().BeTrue();
                    },
                    () =>
                        CreateCache(
                            testDirectory,
                            cacheNamespace,
                            FileSystem,
                            Logger,
                            BackingOption.WriteBehind,
                            ItemStorageOption,
                            expiryMinimum: null,
                            expiryRange: null,
                            readThroughMode: ReadThroughMode.None));
            }
        }
    }
}
