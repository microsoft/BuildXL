// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using BuildXL.Cache.Roxis.Server;

namespace BuildXL.Cache.Roxis.Test
{
    public class RoxisMemoizationSessionTests : MemoizationSessionTests
    {
        private readonly MemoryClock _clock = new MemoryClock();

        public RoxisMemoizationSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            var database = GetDatabase(testDirectory);
            var configuration = new RoxisMemoizationDatabaseConfiguration();
            var client = new LocalRoxisClient(database);
            return new DatabaseMemoizationStore(new RoxisMemoizationDatabase(configuration, client, _clock));
        }

        public RoxisDatabase GetDatabase(DisposableDirectory testDirectory)
        {
            var databaseConfiguration = new RoxisDatabaseConfiguration()
            {
                Path = testDirectory.Path.ToString(),
            };

            return new RoxisDatabase(databaseConfiguration, _clock);
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
                GetSelectorResult r2 = getSelectorResults[1];

                Assert.True(r1.Succeeded);
                Assert.True(r1.Selector.Equals(selector1));

                Assert.True(r2.Succeeded);
                Assert.True(r2.Selector.Equals(selector2));
            });
        }

        public override Task EnumerateStrongFingerprints(int strongFingerprintCount)
        {
            // Do nothing, since operation isn't supported in Redis.
            return Task.FromResult(0);
        }

        public override Task EnumerateStrongFingerprintsEmpty()
        {
            // Do nothing, since operation isn't supported in Redis.
            return Task.FromResult(0);
        }
    }
}
