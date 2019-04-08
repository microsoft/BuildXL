// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    [Trait("Category", "Simulation")]
    public class WriteBehindBuildCacheSimulationTests : BuildCacheSimulationTests
    {
        public WriteBehindBuildCacheSimulationTests()
            : this(StorageOption.Item)
        {
        }

        protected WriteBehindBuildCacheSimulationTests(StorageOption storageOption)
            : base(TestGlobal.Logger, BackingOption.WriteBehind, storageOption)
        {
        }

        [Theory]
        [InlineData(DeterminismNone)]
        [InlineData(DeterminismCache1)]
        [InlineData(DeterminismCache2)]
        [InlineData(DeterminismCache1Expired)]
        [InlineData(DeterminismCache2Expired)]
        public Task IgnoresGivenNonToolDeterminismAndConvergesWithBackedValueByEndOfSession(int determinism)
        {
            var context = new Context(Logger);

            return RunTestAsync(context, async cache =>
            {
                StrongFingerprint initialStrongFingerprint = StrongFingerprint.Random();
                StrongFingerprint strongFingerprint = initialStrongFingerprint;
                await RunTestAsync(context, cache, async session =>
                {
                    strongFingerprint = await CreateRandomStrongFingerprintAsync(context, true, session).ConfigureAwait(false);
                    var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                        context, true, session, determinism: Determinism[determinism]).ConfigureAwait(false);

                    // Add new
                    var addResult = await session.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token).ConfigureAwait(false);
                    Assert.Equal(CacheDeterminism.None.EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
                }).ConfigureAwait(false);

                Assert.NotEqual(initialStrongFingerprint, strongFingerprint);

                await RunTestAsync(context, cache, async session =>
                {
                    var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token).ConfigureAwait(false);
                    Assert.Equal(cache.Id, getResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
                }).ConfigureAwait(false);
            });
        }

        [Fact]
        public async Task GetTriggersBackgroundSealingByEndOfSession()
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

                // Check that the previous run sealed the value as backed
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
                    () => CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, BackingOption.WriteBehind, ItemStorageOption));
            }
        }
    }
}
