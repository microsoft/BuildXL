// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public abstract class MemoizationSessionTestBase : IDisposable
    {
        protected const string Name = "name";
        protected const int DeterminismNone = 0;
        protected const int DeterminismCache1 = 1;
        protected const int DeterminismCache1Expired = 2;
        protected const int DeterminismCache2 = 3;
        protected const int DeterminismCache2Expired = 4;
        protected const int DeterminismTool = 5;
        protected const int DeterminismSinglePhaseNon = 6;

        protected static readonly CacheDeterminism[] Determinism =
        {
            CacheDeterminism.None,
            CacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), DateTime.UtcNow + TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid("2ABD62E7-6117-4D8B-ABB2-D3346E5AF102"), DateTime.UtcNow - TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid("78559E55-E0C3-4C77-A908-8AE9E6590764"), DateTime.UtcNow + TimeSpan.FromDays(7)),
            CacheDeterminism.ViaCache(new Guid("78559E55-E0C3-4C77-A908-8AE9E6590764"), DateTime.UtcNow - TimeSpan.FromDays(7)),
            CacheDeterminism.Tool,
            CacheDeterminism.SinglePhaseNonDeterministic
        };

        protected static readonly CancellationToken Token = CancellationToken.None;
        protected readonly IAbsFileSystem FileSystem;
        protected readonly ILogger Logger;
        private bool _disposed;

        protected MemoizationSessionTestBase(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger)
        {
            FileSystem = createFileSystemFunc();
            Logger = logger;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);

            _disposed = true;
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                FileSystem?.Dispose();
                Logger?.Flush();
            }
        }

        protected abstract IMemoizationStore CreateStore(DisposableDirectory testDirectory);

        [Fact]
        public Task Constructor()
        {
            var context = new Context(Logger);
            return RunReadOnlyTestAsync(context, session => Task.FromResult(true));
        }

        [Fact]
        public async Task MultipleAccesses()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await RunTestAsync(context, testDirectory, store => Task.FromResult(true));
                await RunTestAsync(context, testDirectory, store => Task.FromResult(true));
            }
        }

        [Fact]
        public Task StartupShutdownPropertiesCorrect()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async store =>
            {
                var r = store.CreateSession(context, Name);
                r.ShouldBeSuccess();
                using (var session = r.Session)
                {
                    Assert.False(session.StartupStarted);
                    Assert.False(session.StartupCompleted);
                    Assert.False(session.ShutdownStarted);
                    Assert.False(session.ShutdownCompleted);

                    try
                    {
                        await session.StartupAsync(context).ShouldBeSuccess();
                    }
                    finally
                    {
                        await session.ShutdownAsync(context).ShouldBeSuccess();
                    }

                    Assert.True(session.StartupStarted);
                    Assert.True(session.StartupCompleted);
                    Assert.True(session.ShutdownStarted);
                    Assert.True(session.ShutdownCompleted);
                }
            });
        }

        [Fact]
        public Task GetSelectorsGivesZeroTasks()
        {
            var context = new Context(Logger);
            var weakFingerprint = Fingerprint.Random();

            return RunReadOnlyTestAsync(context, async session =>
            {
                IEnumerable<GetSelectorResult> tasks = await session.GetSelectors(context, weakFingerprint, Token).ToList();
                Assert.Equal(0, tasks.Count());
            });
        }

        [Fact]
        public Task GetSelectorsGivesSelectors()
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
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint1, contentHashListWithDeterminism1, Token).ShouldBeSuccess();
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint2, contentHashListWithDeterminism2, Token).ShouldBeSuccess();

                List<GetSelectorResult> getSelectorResults = await session.GetSelectors(context, weakFingerprint, Token).ToList(CancellationToken.None);
                Assert.Equal(2, getSelectorResults.Count);

                GetSelectorResult r1 = getSelectorResults[0];
                Assert.True(r1.Succeeded);
                Assert.True(r1.Selector == selector1 || r1.Selector == selector2);

                GetSelectorResult r2 = getSelectorResults[1];
                Assert.True(r2.Succeeded);
                Assert.True(r2.Selector == selector1 || r2.Selector == selector2);
            });
        }

        [Fact]
        public Task GetNonExisting()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();

            return RunReadOnlyTestAsync(context, async session =>
            {
                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None)), result);
            });
        }

        [Fact]
        public Task GetExisting()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTestAsync(context, async session =>
            {
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();

                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), result);
            });
        }

        [Fact]
        public Task AddOrGetAddsNew()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTestAsync(context, async session =>
            {
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token);
                Assert.Equal(new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None)), addResult);

                var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), getResult);
            });
        }

        [Fact]
        public Task AddMultipleHashTypes()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHash1 = ContentHash.Random();
            var contentHash2 = ContentHash.Random(HashType.SHA1);
            var contentHash3 = ContentHash.Random(HashType.MD5);
            var contentHashList = new ContentHashList(new[] {contentHash1, contentHash2, contentHash3});
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None);

            return RunTestAsync(context, async session =>
            {
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();

                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), result);
            });
        }

        [Fact]
        public Task AddPayload()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var payload = new byte[] {0, 1, 2, 3};
            var contentHashListWithDeterminism =
                new ContentHashListWithDeterminism(ContentHashList.Random(payload: payload), CacheDeterminism.None);

            return RunTestAsync(context, async session =>
            {
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();

                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), result);
                Assert.True(result.ContentHashListWithDeterminism.ContentHashList.Payload.SequenceEqual(payload));
            });
        }

        [Fact]
        public Task AddNullPayload()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();

            // ReSharper disable once RedundantArgumentDefaultValue
            var contentHashListWithDeterminism =
                new ContentHashListWithDeterminism(ContentHashList.Random(payload: null), CacheDeterminism.None);

            return RunTestAsync(context, async session =>
            {
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();

                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), result);
                Assert.Null(result.ContentHashListWithDeterminism.ContentHashList.Payload);
            });
        }

        [Fact]
        public Task AddUnexpiredDeterminism()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var expirationUtc = DateTime.UtcNow + TimeSpan.FromDays(7);
            var guid = CacheDeterminism.NewCacheGuid();
            var determinism = CacheDeterminism.ViaCache(guid, expirationUtc);
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), determinism);

            return RunTestAsync(context, async session =>
            {
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();
                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(guid, result.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
            });
        }

        [Fact]
        public Task AddExpiredDeterminism()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var expirationUtc = DateTime.UtcNow - TimeSpan.FromDays(7);
            var guid = CacheDeterminism.NewCacheGuid();
            var determinism = CacheDeterminism.ViaCache(guid, expirationUtc);
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), determinism);

            return RunTestAsync(context, async session =>
            {
                await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();
                var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(CacheDeterminism.None.EffectiveGuid, result.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
            });
        }

        [Theory]
        [InlineData(DeterminismNone, DeterminismTool)]
        [InlineData(DeterminismNone, DeterminismCache1)]
        [InlineData(DeterminismCache1, DeterminismCache2)]
        [InlineData(DeterminismCache2, DeterminismCache1)]
        [InlineData(DeterminismCache1, DeterminismTool)]
        [InlineData(DeterminismTool, DeterminismNone)]
        [InlineData(DeterminismTool, DeterminismCache1)]
        [InlineData(DeterminismCache1, DeterminismNone)]
        [InlineData(DeterminismNone, DeterminismNone)]
        [InlineData(DeterminismCache1, DeterminismCache1)]
        [InlineData(DeterminismTool, DeterminismTool)]
        [InlineData(DeterminismSinglePhaseNon, DeterminismSinglePhaseNon)]
        [InlineData(DeterminismTool, DeterminismTool)]
        public Task AlwaysReplaceWhenPreviousContentMissing(int fromDeterminism, int toDeterminism)
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashList = ContentHashList.Random();

            return RunTestAsync(context, async session =>
            {
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[fromDeterminism]), Token);
                Assert.Equal(Determinism[fromDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // What we will do here is AddOrGet() a record that we already know is
                // there but with the determinism bit changed.
                addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[toDeterminism]), Token)
;
                Assert.Null(addResult.ContentHashListWithDeterminism.ContentHashList);
                Assert.Equal(Determinism[toDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                var getResult = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                Assert.Equal(Determinism[toDeterminism].EffectiveGuid, getResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
            });
        }

        [Theory]
        [InlineData(DeterminismNone, DeterminismSinglePhaseNon)] // Overwriting SinglePhaseNonDeterministic with anything else is an error.
        [InlineData(DeterminismCache1, DeterminismSinglePhaseNon)]
        [InlineData(DeterminismTool, DeterminismSinglePhaseNon)]
        [InlineData(DeterminismSinglePhaseNon, DeterminismNone)] // Overwriting anything else with SinglePhaseNonDeterministic is an error.
        [InlineData(DeterminismSinglePhaseNon, DeterminismCache1)]
        [InlineData(DeterminismSinglePhaseNon, DeterminismTool)]
        public Task MismatchedSinglePhaseFails(int fromDeterminism, int toDeterminism)
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashList = ContentHashList.Random();

            return RunTestAsync(context, async session =>
            {
                var addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[fromDeterminism]), Token);
                Assert.Equal(Determinism[fromDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // What we will do here is AddOrGet() a record that we already know is
                // there but with the determinism bit changed.
                addResult = await session.AddOrGetContentHashListAsync(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[toDeterminism]), Token);
                Assert.Equal(AddOrGetContentHashListResult.ResultCode.SinglePhaseMixingError, addResult.Code);
            });
        }

        [Fact]
        public Task EnumerateStrongFingerprintsEmpty()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async store =>
            {
                using (var strongFingerprintEnumerator = store.EnumerateStrongFingerprints(context).GetEnumerator())
                {
                    Assert.Equal(false, await strongFingerprintEnumerator.MoveNext());
                }
            });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1337)]
        public Task EnumerateStrongFingerprints(int strongFingerprintCount)
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async (cache, session) =>
            {
                var expected = await AddRandomContentHashListsAsync(context, strongFingerprintCount, session);
                var enumerated =
                    (await cache.EnumerateStrongFingerprints(context).ToList())
                    .Where(result => result.Succeeded)
                    .Select(result => result.Data)
                    .ToHashSet();
                Assert.Equal(expected.Count, enumerated.Count);
                Assert.True(expected.SetEquals(enumerated));
            });
        }

        private async Task<HashSet<StrongFingerprint>> AddRandomContentHashListsAsync(
            Context context, int count, IMemoizationSession session)
        {
            var strongFingerprints = new HashSet<StrongFingerprint>();
            for (int i = 0; i < count; i++)
            {
                var strongFingerprint = StrongFingerprint.Random();
                var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
                await session.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListWithDeterminism, Token).ShouldBeSuccess();
                strongFingerprints.Add(strongFingerprint);
            }

            return strongFingerprints;
        }

        protected Task RunTestAsync(Context context, Func<IMemoizationSession, Task> funcAsync)
        {
            return RunTestAsync(context, (store, session) => funcAsync(session));
        }

        protected async Task RunTestAsync(Context context, Func<IMemoizationStore, Task> funcAsync)
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // ReSharper disable once ConvertClosureToMethodGroup
                await RunTestAsync(context, testDirectory, store => funcAsync(store));
            }
        }

        private Task RunReadOnlyTestAsync(Context context, Func<IReadOnlyMemoizationSession, Task> funcAsync)
        {
            return RunTestAsync(context, async store =>
            {
                var createResult = store.CreateReadOnlySession(context, Name);
                createResult.ShouldBeSuccess();
                using (var session = createResult.Session)
                {
                    try
                    {
                        var r = await session.StartupAsync(context);
                        r.ShouldBeSuccess();

                        await funcAsync(session);
                    }
                    finally
                    {
                        var r = await session.ShutdownAsync(context);
                        r.ShouldBeSuccess();
                    }
                }
            });
        }

        protected async Task RunTestAsync(Context context, Func<IMemoizationStore, IMemoizationSession, Task> funcAsync, Func<DisposableDirectory, IMemoizationStore> createStoreFunc = null)
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // ReSharper disable once ConvertClosureToMethodGroup
                await RunTestAsync(context, testDirectory, (store, session) => funcAsync(store, session), createStoreFunc);
            }
        }

        protected abstract Task RunTestAsync(
            Context context, DisposableDirectory testDirectory, Func<IMemoizationStore, IMemoizationSession, Task> funcAsync, Func<DisposableDirectory, IMemoizationStore> createStoreFunc = null);

        protected async Task RunTestAsync(Context context, DisposableDirectory testDirectory, Func<IMemoizationStore, Task> funcAsync, Func<DisposableDirectory, IMemoizationStore> createStoreFunc = null)
        {
            if (createStoreFunc == null)
            {
                createStoreFunc = CreateStore;
            }

            using (var store = createStoreFunc(testDirectory))
            {
                try
                {
                    var r = await store.StartupAsync(context);
                    r.ShouldBeSuccess();

                    await funcAsync(store);
                }
                finally
                {
                    var r = await store.ShutdownAsync(context);
                    r.ShouldBeSuccess();
                }
            }
        }
    }
}
