// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class RocksDbMemoizationSessionTests : MemoizationSessionTests
    {
        private readonly MemoryClock _clock = new MemoryClock();

        public RocksDbMemoizationSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
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

            return new RocksDbMemoizationStore(Logger, _clock, memoConfig);
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

                List<GetSelectorResult> getSelectorResults = await session.GetSelectors(context, weakFingerprint, Token).ToList(CancellationToken.None);
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
                
                List<GetSelectorResult> getSelectorResults = await session.GetSelectors(context, weakFingerprint, Token).ToList();
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
        public Task GarbageCollectionRuns()
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
                funcAsync: async (store, session) => {
                    await session.AddOrGetContentHashListAsync(context, strongFingerprint1, contentHashListWithDeterminism1, Token).ShouldBeSuccess();
                    _clock.Increment();

                    // Notice we don't increment the clock here
                    await session.AddOrGetContentHashListAsync(context, strongFingerprint2, contentHashListWithDeterminism2, Token).ShouldBeSuccess();

                    RocksDbContentLocationDatabase database = (store as RocksDbMemoizationStore)?.Database;
                    Contract.Assert(database != null);

                    var ctx = new OperationContext(context);
                    database.GarbageCollect(ctx);

                    var r1 = database.GetContentHashList(ctx, strongFingerprint1).ShouldBeSuccess().ContentHashListWithDeterminism;
                    r1.ContentHashList.Should().BeNull();
                    r1.Determinism.Should().Be(CacheDeterminism.None);

                    var r2 = database.GetContentHashList(ctx, strongFingerprint2).ShouldBeSuccess().ContentHashListWithDeterminism;
                    r2.Should().BeEquivalentTo(contentHashListWithDeterminism2);
                },
                createStoreFunc: createStoreInternal);

            // This is needed because type errors arise if you inline
            IMemoizationStore createStoreInternal(DisposableDirectory disposableDirectory)
            {
                return CreateStore(testDirectory: disposableDirectory, configMutator: (configuration) =>
                {
                    configuration.MetadataGarbageCollectionEnabled = true;
                    configuration.MetadataGarbageCollectionProtectionTime = TimeSpan.FromMilliseconds(1);
                    // Disables automatic GC
                    configuration.GarbageCollectionInterval = Timeout.InfiniteTimeSpan;
                });
            }
        }
    }
}
