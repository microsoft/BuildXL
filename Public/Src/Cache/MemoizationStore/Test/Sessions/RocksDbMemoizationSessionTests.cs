// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;
using Xunit;
using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using FluentAssertions;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class RocksDbMemoizationSessionTests : MemoizationSessionTests
    {
        private readonly MemoryClock _clock = new MemoryClock();

        public RocksDbMemoizationSessionTests(ITestOutputHelper helper = null)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, helper)
        {
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            return CreateStore(testDirectory, configMutator: null);
        }

        protected IMemoizationStore CreateStore(DisposableDirectory testDirectory, Action<RocksDbContentLocationDatabaseConfiguration> configMutator = null)
        {
            var memoConfig = new RocksDbMemoizationStoreConfiguration()
            {
                Database = new RocksDbContentLocationDatabaseConfiguration(testDirectory.Path)
            };

            configMutator?.Invoke(memoConfig.Database);

            return new RocksDbMemoizationStore(_clock, memoConfig);
        }

        [Fact]
        public Task GetSelectorsGivesSelectorsInReverseLruOrderAfterAdd()
        {
            var context = new Context(Logger);
            var weakFingerprint = Fingerprint.Random();
            var selector1 = Selector.Random();
            var selector2 = Selector.Random();
            var strongFingerprint1 = new StrongFingerprint(weakFingerprint, selector1);
            var strongFingerprint2 = new StrongFingerprint(weakFingerprint, selector2);
            var contentHashListWithDeterminism1 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            var contentHashListWithDeterminism2 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTestAsync(context, async session =>
            {
                await session.AddOrGetContentHashListAsync(context, strongFingerprint1, contentHashListWithDeterminism1, Token).ShouldBeSuccess();
                _clock.Increment();
                await session.AddOrGetContentHashListAsync(context, strongFingerprint2, contentHashListWithDeterminism2, Token).ShouldBeSuccess();
                _clock.Increment();

                List<GetSelectorResult> getSelectorResults = await session.GetSelectors(context, weakFingerprint, Token).ToListAsync(CancellationToken.None);
                Assert.Equal(2, getSelectorResults.Count);

                GetSelectorResult r1 = getSelectorResults[0];
                Assert.True(r1.Succeeded);
                Assert.True(r1.Selector == selector2);

                GetSelectorResult r2 = getSelectorResults[1];
                Assert.True(r2.Succeeded);
                Assert.True(r2.Selector == selector1);
            });
        }

        [Fact]
        public Task GetSelectorsGivesSelectorsInReverseLruOrderAfterGet()
        {
            var context = new Context(Logger);
            var weakFingerprint = Fingerprint.Random();
            var selector1 = Selector.Random();
            var selector2 = Selector.Random();
            var strongFingerprint1 = new StrongFingerprint(weakFingerprint, selector1);
            var strongFingerprint2 = new StrongFingerprint(weakFingerprint, selector2);
            var contentHashListWithDeterminism1 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            var contentHashListWithDeterminism2 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTestAsync(context, async (store, session) =>
            {
                await session.AddOrGetContentHashListAsync(context, strongFingerprint1, contentHashListWithDeterminism1, Token).ShouldBeSuccess();
                _clock.Increment();
                await session.AddOrGetContentHashListAsync(context, strongFingerprint2, contentHashListWithDeterminism2, Token).ShouldBeSuccess();
                _clock.Increment();
                await session.GetContentHashListAsync(context, strongFingerprint1, Token).ShouldBeSuccess();
                _clock.Increment();

                List<GetSelectorResult> getSelectorResults = await session.GetSelectors(context, weakFingerprint, Token).ToListAsync();
                Assert.Equal(2, getSelectorResults.Count);

                GetSelectorResult r1 = getSelectorResults[0];
                Assert.True(r1.Succeeded);
                Assert.True(r1.Selector == selector1);

                GetSelectorResult r2 = getSelectorResults[1];
                Assert.True(r2.Succeeded);
                Assert.True(r2.Selector == selector2);
            });
        }

        [Fact]
        public Task GarbageCollectionKeepsLastAdded()
        {
            var context = new Context(Logger);
            var weakFingerprint = Fingerprint.Random();
            var selector1 = Selector.Random();
            var selector2 = Selector.Random();
            var strongFingerprint1 = new StrongFingerprint(weakFingerprint, selector1);
            var strongFingerprint2 = new StrongFingerprint(weakFingerprint, selector2);
            var contentHashListWithDeterminism1 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            var contentHashListWithDeterminism2 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTestAsync(context,
                funcAsync: async (store, session) =>
                {
                    await session.AddOrGetContentHashListAsync(context, strongFingerprint1, contentHashListWithDeterminism1, Token).ShouldBeSuccess();
                    _clock.Increment();

                    // Notice we don't increment the clock here
                    await session.AddOrGetContentHashListAsync(context, strongFingerprint2, contentHashListWithDeterminism2, Token).ShouldBeSuccess();

                    RocksDbContentLocationDatabase database = (store as RocksDbMemoizationStore)?.RocksDbDatabase;
                    Contract.Assert(database != null);

                    var ctx = new OperationContext(context);
                    await database.GarbageCollectAsync(ctx).ShouldBeSuccess();

                    var r1 = database.GetContentHashList(ctx, strongFingerprint1).ShouldBeSuccess().ContentHashListWithDeterminism;
                    r1.ContentHashList.Should().BeNull();
                    r1.Determinism.Should().Be(CacheDeterminism.None);

                    var r2 = database.GetContentHashList(ctx, strongFingerprint2).ShouldBeSuccess().ContentHashListWithDeterminism;
                    r2.Should().BeEquivalentTo(contentHashListWithDeterminism2);

                    database.Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesRemoved].Value.Should().Be(1);
                    database.Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesScanned].Value.Should().Be(2);
                },
                createStoreFunc: createStoreInternal);

            // This is needed because type errors arise if you inline
            IMemoizationStore createStoreInternal(DisposableDirectory disposableDirectory)
            {
                return CreateStore(testDirectory: disposableDirectory, configMutator: (configuration) =>
                {
                    // Disables automatic GC
                    configuration.GarbageCollectionInterval = Timeout.InfiniteTimeSpan;
                    configuration.MetadataGarbageCollectionMaximumSizeMb = 0.0001;
                });
            }
        }

        [Fact]
        public Task GarbageCollectionDeletesInLruOrder()
        {
            var context = new Context(Logger);
            var weakFingerprint = Fingerprint.Random();

            var selector1 = Selector.Random();
            var strongFingerprint1 = new StrongFingerprint(weakFingerprint, selector1);
            var contentHashListWithDeterminism1 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            var selector2 = Selector.Random();
            var strongFingerprint2 = new StrongFingerprint(weakFingerprint, selector2);
            var contentHashListWithDeterminism2 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTestAsync(context,
                funcAsync: async (store, session) =>
                {
                    await session.AddOrGetContentHashListAsync(context, strongFingerprint1, contentHashListWithDeterminism1, Token).ShouldBeSuccess();
                    _clock.Increment();

                    await session.AddOrGetContentHashListAsync(context, strongFingerprint2, contentHashListWithDeterminism2, Token).ShouldBeSuccess();
                    _clock.Increment();

                    // Force update the last access time of the first fingerprint
                    await session.GetContentHashListAsync(context, strongFingerprint1, Token).ShouldBeSuccess();
                    _clock.Increment();

                    RocksDbContentLocationDatabase database = (store as RocksDbMemoizationStore)?.RocksDbDatabase;
                    Contract.Assert(database != null);

                    var ctx = new OperationContext(context);
                    await database.GarbageCollectAsync(ctx).ShouldBeSuccess();

                    var r1 = database.GetContentHashList(ctx, strongFingerprint1).ShouldBeSuccess().ContentHashListWithDeterminism;
                    r1.Should().BeEquivalentTo(contentHashListWithDeterminism1);

                    var r2 = database.GetContentHashList(ctx, strongFingerprint2).ShouldBeSuccess().ContentHashListWithDeterminism;
                    r2.ContentHashList.Should().BeNull();
                    r2.Determinism.Should().Be(CacheDeterminism.None);

                    database.Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesRemoved].Value.Should().Be(1);
                    database.Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesScanned].Value.Should().Be(2);
                },
                createStoreFunc: createStoreInternal);

            // This is needed because type errors arise if you inline
            IMemoizationStore createStoreInternal(DisposableDirectory disposableDirectory)
            {
                return CreateStore(testDirectory: disposableDirectory, configMutator: (configuration) =>
                {
                    // Disables automatic GC
                    configuration.GarbageCollectionInterval = Timeout.InfiniteTimeSpan;
                    configuration.MetadataGarbageCollectionMaximumSizeMb = 0.0001;
                });
            }
        }

        [Fact]
        public Task SizeBasedGarbageCollectionIsApproximatelyCorrect()
        {
            var N = 200;

            // This number depends on N, so there's nothing we can do here
            int expectedRemoves = 90;

            var context = new Context(Logger);

            var weakFingerprint = Fingerprint.Random();

            var selectors = new Selector[N];
            var strongFingerprints = new StrongFingerprint[N];
            var contentHashLists = new ContentHashListWithDeterminism[N];
            for (var i = 0; i < N; i++)
            {
                selectors[i] = Selector.Random(outputLengthBytes: 1000);
                strongFingerprints[i] = new StrongFingerprint(weakFingerprint, selectors[i]);
                contentHashLists[i] = new ContentHashListWithDeterminism(ContentHashList.Random(contentHashCount: 100), CacheDeterminism.None);
            }

            return RunTestAsync(context,
                funcAsync: async (store, session) =>
                {
                    _clock.Increment();

                    for (var i = 0; i < N; i++)
                    {
                        await session.AddOrGetContentHashListAsync(context, strongFingerprints[i], contentHashLists[i], Token).ShouldBeSuccess();
                        _clock.Increment();
                    }

                    var database = (store as RocksDbMemoizationStore)?.RocksDbDatabase;
                    Contract.Assert(database != null);

                    var ctx = new OperationContext(context);
                    await database.GarbageCollectAsync(ctx).ShouldBeSuccess();

                    // We should have removed the oldest `expectedRemoves` entries
                    for (var i = 0; i < expectedRemoves; i++)
                    {
                        var chl = database.GetContentHashList(ctx, strongFingerprints[i]).ShouldBeSuccess().ContentHashListWithDeterminism;
                        chl.ContentHashList.Should().BeNull();
                        chl.Determinism.Should().Be(CacheDeterminism.None);
                    }

                    // All the others should be there
                    for (var i = expectedRemoves; i < N; i++)
                    {
                        var chl = database.GetContentHashList(ctx, strongFingerprints[i]).ShouldBeSuccess().ContentHashListWithDeterminism;
                        chl.Should().BeEquivalentTo(contentHashLists[i]);
                    }

                    database.Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesRemoved].Value.Should().Be(expectedRemoves);
                    database.Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesScanned].Value.Should().Be(N);
                },
                createStoreFunc: createStoreInternal);

            // This is needed because type errors arise if you inline
            IMemoizationStore createStoreInternal(DisposableDirectory disposableDirectory)
            {
                return CreateStore(testDirectory: disposableDirectory, configMutator: (Action<RocksDbContentLocationDatabaseConfiguration>)((configuration) =>
                {
                    configuration.MetadataGarbageCollectionMaximumSizeMb = 0.5;

                    // Disables automatic GC
                    configuration.GarbageCollectionInterval = Timeout.InfiniteTimeSpan;
                }));
            }
        }
    }
}
