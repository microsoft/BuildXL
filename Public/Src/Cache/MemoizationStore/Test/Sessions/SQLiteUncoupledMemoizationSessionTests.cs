// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class SQLiteUncoupledMemoizationSessionTests : UncoupledMemoizationSessionTests
    {
        private const long MaxRowCount = 10000;
        private readonly MemoryClock _clock = new MemoryClock();

        public SQLiteUncoupledMemoizationSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            var memoConfig = new SQLiteMemoizationStoreConfiguration(testDirectory.Path) { MaxRowCount = MaxRowCount };
            memoConfig.Database.JournalMode = ContentStore.SQLite.JournalMode.OFF;
            return new SQLiteMemoizationStore(Logger, _clock, memoConfig);
        }

        [Fact]
        public Task EvictionInLruOrder()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async session =>
            {
                // Write more than MaxRowCount items so the first ones should fall out.
                var strongFingerprints = Enumerable.Range(0, (int)MaxRowCount + 3).Select(i => StrongFingerprint.Random()).ToList();
                foreach (var strongFingerprint in strongFingerprints)
                {
                    await session.AddOrGetContentHashListAsync(
                        context,
                        strongFingerprint,
                        new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None),
                        Token).ShouldBeSuccess();
                    _clock.Increment();
                }

                // Make sure store purging completes.
                await ((ReadOnlySQLiteMemoizationSession)session).PurgeAsync(context);

                // Check the first items written have fallen out.
                for (var i = 0; i < 3; i++)
                {
                    GetContentHashListResult r = await session.GetContentHashListAsync(context, strongFingerprints[i], Token);
                    r.Succeeded.Should().BeTrue();
                    r.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
                }

                // Check the rest are still present.
                for (var i = 3; i < strongFingerprints.Count; i++)
                {
                    GetContentHashListResult r = await session.GetContentHashListAsync(context, strongFingerprints[i], Token);
                    r.Succeeded.Should().BeTrue();
                    r.ContentHashListWithDeterminism.ContentHashList.Should().NotBeNull();
                }
            });
        }
    }
}
