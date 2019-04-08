// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    [Trait("Category", "Simulation")]
    public class WriteNeverBuildCacheSimulationTests : BuildCacheSimulationTests
    {
        public WriteNeverBuildCacheSimulationTests()
            : this(StorageOption.Item)
        {
        }

        protected WriteNeverBuildCacheSimulationTests(StorageOption storageOption)
            : base(TestGlobal.Logger, BackingOption.WriteNever, storageOption)
        {
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingBackedEqualToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(false, true, true, true);
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingBackedDifferentToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(false, true, false, true);
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingBackedEqualNonToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(false, true, true, false);
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingBackedDifferentNonToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(false, true, false, false);
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingUnbackedEqualToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(false, false, true, true);
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingUnbackedDifferentToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(false, false, false, true);
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingUnbackedEqualNonToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(false, false, true, false);
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingUnbackedDifferentNonToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(false, false, false, false);
        }

        [Fact]
        public Task AddOrGetWriteThroughWithExistingUnbackedEqualToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(true, false, true, true);
        }

        [Fact]
        public Task AddOrGetWriteThroughWithExistingUnbackedDifferentToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(true, false, false, true);
        }

        [Fact]
        public Task AddOrGetWriteThroughWithExistingUnbackedEqualNonToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(true, false, true, false);
        }

        [Fact]
        public Task AddOrGetWriteThroughWithExistingUnbackedDifferentNonToolValue()
        {
            return TestAddOrGetWithExistingInitialValueAsync(true, false, false, false);
        }

        /// <summary>
        ///     Test that AddOrGet does the right thing given different states of pre-existing values.
        ///     Adds a value then tests a second value at the same StrongFingerprint.
        /// </summary>
        /// <param name="writeThrough">If true, the tested (second) AddOrGet is from a writeThrough client (writeNever client otherwise).</param>
        /// <param name="initiallyBacked">If true, the existing value will be initially backed by written-through content.</param>
        /// <param name="sameValue">If true, the tested (second) AddOrGet will add the same ContentHashList as the existing one.</param>
        /// <param name="toolDeterministic">If true, values will be marked as Tool deterministic (None otherwise).</param>
        private Task TestAddOrGetWithExistingInitialValueAsync(bool writeThrough, bool initiallyBacked, bool sameValue, bool toolDeterministic)
        {
            var context = new Context(Logger);
            return RunWriteThroughWriteNeverTestAsync(
                context,
                async (writeThroughSession, writeNeverSession, cacheId) =>
                {
                    var initialSession = initiallyBacked ? writeThroughSession : writeNeverSession;
                    var determinismToAdd = toolDeterministic ? CacheDeterminism.Tool : CacheDeterminism.None;

                    var strongFingerprintToAdd =
                        await CreateRandomStrongFingerprintAsync(context, true, initialSession).ConfigureAwait(false);
                    var initialContentHashListWithDeterminismToAdd = await CreateRandomContentHashListWithDeterminismAsync(
                        context, true, initialSession, null, determinismToAdd).ConfigureAwait(false);

                    var addOrGetResult = await initialSession.AddOrGetContentHashListAsync(
                        context, strongFingerprintToAdd, initialContentHashListWithDeterminismToAdd, Token).ConfigureAwait(false);
                    addOrGetResult.Succeeded.Should().BeTrue();
                    addOrGetResult.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();

                    if (toolDeterministic)
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.Tool);
                    }
                    else if (initiallyBacked)
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid.Should().Be(cacheId);
                    }
                    else
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.None);
                    }

                    if (writeThrough && !initiallyBacked)
                    {
                        if (sameValue)
                        {
                            await UploadContent(
                                context,
                                strongFingerprintToAdd,
                                initialContentHashListWithDeterminismToAdd.ContentHashList,
                                writeNeverSession,
                                writeThroughSession).ConfigureAwait(false);
                        }
                        else
                        {
                            await UploadContent(
                                context,
                                strongFingerprintToAdd.Selector.ContentHash,
                                writeNeverSession,
                                writeThroughSession).ConfigureAwait(false);
                        }
                    }

                    var testSession = writeThrough ? writeThroughSession : writeNeverSession;
                    var secondContentHashListWithDeterminismToAdd = sameValue
                        ? initialContentHashListWithDeterminismToAdd
                        : await CreateRandomContentHashListWithDeterminismAsync(context, true, testSession, null, determinismToAdd)
                            .ConfigureAwait(false);

                    addOrGetResult = await testSession.AddOrGetContentHashListAsync(
                        context, strongFingerprintToAdd, secondContentHashListWithDeterminismToAdd, Token).ConfigureAwait(false);
                    addOrGetResult.Succeeded.Should().BeTrue();

                    if (sameValue || (writeThrough && !initiallyBacked))
                    {
                        // Add is accepted if it's the same value or the winner
                        addOrGetResult.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
                    }
                    else
                    {
                        // Otherwise the initial winner is returned
                        addOrGetResult.ContentHashListWithDeterminism.ContentHashList.Should()
                            .Be(initialContentHashListWithDeterminismToAdd.ContentHashList);
                    }

                    if (toolDeterministic)
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.Tool);
                    }
                    else if (writeThrough || initiallyBacked)
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid.Should().Be(cacheId);
                    }
                    else
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.None);
                    }
                });
        }

        [Fact]
        public Task AddOrGetWriteNeverWithExistingCompletelyUnbackedDifferentNonToolValue()
        {
            var context = new Context(Logger);
            return RunDisjointWriteNeverTestAsync(
                context,
                async (writeNeverSession1, writeNeverSession2, id) =>
                {
                    var strongFingerprint =
                        await CreateRandomStrongFingerprintAsync(context, true, writeNeverSession1).ConfigureAwait(false);
                    var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                        context, true, writeNeverSession1, null, CacheDeterminism.None).ConfigureAwait(false);

                    var addOrGetResult = await writeNeverSession1.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token).ConfigureAwait(false);
                    addOrGetResult.Succeeded.Should().BeTrue();
                    addOrGetResult.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
                    addOrGetResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.None);

                    await UploadContent(
                        context,
                        strongFingerprint.Selector.ContentHash,
                        writeNeverSession1,
                        writeNeverSession2).ConfigureAwait(false);

                    var differentContentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                        context, true, writeNeverSession2, null, CacheDeterminism.None).ConfigureAwait(false);

                    addOrGetResult = await writeNeverSession2.AddOrGetContentHashListAsync(
                        context, strongFingerprint, differentContentHashListWithDeterminism, Token).ConfigureAwait(false);
                    addOrGetResult.Succeeded.Should().BeTrue();
                    addOrGetResult.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
                    addOrGetResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.None);

                    var getResult = await writeNeverSession2.GetContentHashListAsync(context, strongFingerprint, Token)
                        .ConfigureAwait(false);
                    getResult.Succeeded.Should().BeTrue();
                    getResult.ContentHashListWithDeterminism.Should().Be(differentContentHashListWithDeterminism);
                });
        }

        [Fact]
        public Task GetWriteNeverBackedToolValue()
        {
            return TestGetAsync(false, true, true);
        }

        [Fact]
        public Task GetWriteNeverBackedNonToolValue()
        {
            return TestGetAsync(false, true, false);
        }

        [Fact]
        public Task GetWriteNeverUnbackedToolValue()
        {
            return TestGetAsync(false, false, true);
        }

        [Fact]
        public Task GetWriteNeverUnbackedNonToolValue()
        {
            return TestGetAsync(false, false, false);
        }

        [Fact]
        public Task GetWriteThroughBackedToolValue()
        {
            return TestGetAsync(true, true, true);
        }

        [Fact]
        public Task GetWriteThroughBackedNonToolValue()
        {
            return TestGetAsync(true, true, false);
        }

        [Fact]
        public Task GetWriteThroughUnbackedToolValue()
        {
            return TestGetAsync(true, false, true);
        }

        [Fact]
        public Task GetWriteThroughUnbackedNonToolValue()
        {
            return TestGetAsync(true, false, false);
        }

        /// <summary>
        ///     Test that Get does the right thing given different states of pre-existing values.
        /// </summary>
        /// <param name="writeThrough">If true, the Get is from a writeThrough client (writeNever client otherwise).</param>
        /// <param name="backed">If true, the existing value will be backed by written-through content.</param>
        /// <param name="toolDeterministic">If true, values will be marked as Tool deterministic (None otherwise).</param>
        private Task TestGetAsync(bool writeThrough, bool backed, bool toolDeterministic)
        {
            var context = new Context(Logger);
            return RunWriteThroughWriteNeverTestAsync(
                context,
                async (writeThroughSession, writeNeverSession, cacheId) =>
                {
                    var initialSession = backed ? writeThroughSession : writeNeverSession;
                    var determinismToAdd = toolDeterministic ? CacheDeterminism.Tool : CacheDeterminism.None;

                    var strongFingerprint =
                        await CreateRandomStrongFingerprintAsync(context, true, initialSession).ConfigureAwait(false);
                    var contentHashListWithDeterminism = await CreateRandomContentHashListWithDeterminismAsync(
                        context, true, initialSession, null, determinismToAdd).ConfigureAwait(false);

                    var addOrGetResult = await initialSession.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token).ConfigureAwait(false);
                    addOrGetResult.Succeeded.Should().BeTrue();
                    addOrGetResult.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();

                    if (toolDeterministic)
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.Tool);
                    }
                    else if (backed)
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid.Should().Be(cacheId);
                    }
                    else
                    {
                        addOrGetResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.None);
                    }

                    var testSession = writeThrough ? writeThroughSession : writeNeverSession;

                    var getResult = await testSession.GetContentHashListAsync(context, strongFingerprint, Token).ConfigureAwait(false);
                    getResult.Succeeded.Should().BeTrue();
                    getResult.ContentHashListWithDeterminism.ContentHashList.Should().Be(contentHashListWithDeterminism.ContentHashList);

                    if (toolDeterministic)
                    {
                        getResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.Tool);
                    }
                    else if (backed)
                    {
                        getResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid.Should().Be(cacheId);
                    }
                    else
                    {
                        getResult.ContentHashListWithDeterminism.Determinism.Should().Be(CacheDeterminism.None);
                    }
                });
        }

        private async Task UploadContent(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashList contentHashList,
            IContentSession fromSession,
            IContentSession toSession)
        {
            await UploadContent(context, strongFingerprint.Selector.ContentHash, fromSession, toSession).ConfigureAwait(false);
            foreach (var contentHash in contentHashList.Hashes)
            {
                await UploadContent(context, contentHash, fromSession, toSession).ConfigureAwait(false);
            }
        }

        private async Task UploadContent(
            Context context, ContentHash contentHash, IContentSession fromSession, IContentSession toSession)
        {
            var openStreamResult = await fromSession.OpenStreamAsync(context, contentHash, Token).ConfigureAwait(false);
            openStreamResult.Succeeded.Should().BeTrue();
            var putResult = await toSession.PutStreamAsync(context, contentHash, openStreamResult.Stream, Token).ConfigureAwait(false);
            putResult.Succeeded.Should().BeTrue();
        }

        private delegate Task WriteThroughWriteNeverTestFuncAsync(
            ICacheSession writeThroughSession, ICacheSession writeNeverSession, Guid cacheId);

        /// <summary>
        ///     Run a test against two sessions, one of which writes content through and the other of which writes behind to an arbitrary IContentStore.
        /// </summary>
        private Task RunWriteThroughWriteNeverTestAsync(Context context, WriteThroughWriteNeverTestFuncAsync funcAsync)
        {
            var cacheNamespace = Guid.NewGuid().ToString();
            return RunTestAsync(
                context,
                (writeThroughCache, writeThroughSession, writeThroughTestDirectory) =>
                {
                    return RunTestAsync(
                        context,
                        (writeNeverCache, writeNeverSession, writeNeverTestDirectory) =>
                        {
                            writeNeverCache.Id.Should().Be(writeThroughCache.Id);
                            return funcAsync(writeThroughSession, writeNeverSession, writeNeverCache.Id);
                        },
                        testDirectory => CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, BackingOption.WriteNever, ItemStorageOption));
                },
                testDirectory => CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, BackingOption.WriteThrough, ItemStorageOption));
        }

        private delegate Task DisjointWriteNeverTestFuncAsync(
            ICacheSession writeNeverSession1, ICacheSession writeNeverSession2, Guid cacheId);

        /// <summary>
        ///     Run a test against two sessions, both of which write behind to their own separate IContentStores.
        ///     This is not a recommended scenario, but rather is being used to test the behavior when content
        ///     goes missing from the distributed content store.
        /// </summary>
        private Task RunDisjointWriteNeverTestAsync(Context context, DisjointWriteNeverTestFuncAsync funcAsync)
        {
            var cacheNamespace = Guid.NewGuid().ToString();
            return RunTestAsync(
                context,
                (writeNeverCache1, writeNeverSession1, writeNeverTestDirectory1) =>
                {
                    return RunTestAsync(
                        context,
                        (writeNeverCache2, writeNeverSession2, writeNeverTestDirectory2) =>
                        {
                            writeNeverCache1.Id.Should().Be(writeNeverCache2.Id);
                            return funcAsync(writeNeverSession1, writeNeverSession2, writeNeverCache1.Id);
                        },
                        testDirectory => CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, BackingOption.WriteNever, ItemStorageOption));
                },
                testDirectory => CreateCache(testDirectory, cacheNamespace, FileSystem, Logger, BackingOption.WriteNever, ItemStorageOption));
        }
    }
}
