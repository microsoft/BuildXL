using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    /// <summary>
    /// These tests are ported from MemoizationSessionTestBase (not referenced).
    /// 
    /// TODO(jubayard): remove when CaChaaS is part of memoization
    /// </summary>
    public class RocksDbContentLocationDatabaseTests : TestWithOutput
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

        protected readonly MemoryClock Clock = new MemoryClock();

        protected readonly DisposableDirectory _workingDirectory;

        protected ContentLocationDatabaseConfiguration DefaultConfiguration { get; } = null;

        public RocksDbContentLocationDatabaseTests(ITestOutputHelper output)
            : base(output)
        {
            // Need to use unique folder for each test instance, because more then one test may be executed simultaneously.
            var uniqueOutputFolder = Guid.NewGuid().ToString();
            _workingDirectory = new DisposableDirectory(new PassThroughFileSystem(TestGlobal.Logger), Path.Combine(uniqueOutputFolder, "redis"));

            DefaultConfiguration = new RocksDbContentLocationDatabaseConfiguration(_workingDirectory.Path / "rocksdb");
        }

        private async Task RunTest(Action<OperationContext, ContentLocationDatabase> action) => await RunCustomTest(DefaultConfiguration, action);

        private async Task RunCustomTest(ContentLocationDatabaseConfiguration configuration, Action<OperationContext, ContentLocationDatabase> action, OperationContext? overwrite = null)
        {
            var tracingContext = new Context(TestGlobal.Logger);
            var operationContext = overwrite ?? new OperationContext(tracingContext);

            var database = ContentLocationDatabase.Create(Clock, configuration, () => new MachineId[] { });
            await database.StartupAsync(operationContext).ShouldBeSuccess();
            database.SetDatabaseMode(isDatabaseWritable: true);

            action(operationContext, database);

            await database.ShutdownAsync(operationContext).ShouldBeSuccess();
        }

        [Fact]
        public Task GetSelectorsGivesZeroTasks()
        {
            var weakFingerprint = Fingerprint.Random();

            return RunTest((context, contentLocationDatabase) =>
            {
                IEnumerable<GetSelectorResult> tasks = contentLocationDatabase.GetSelectors(context, weakFingerprint).ToList();
                Assert.Equal(0, tasks.Count());
            });
        }

        [Fact]
        public Task GetSelectorsGivesSelectors()
        {
            var weakFingerprint = Fingerprint.Random();
            var selector1 = Selector.Random();
            var selector2 = Selector.Random();
            var strongFingerprint1 = new StrongFingerprint(weakFingerprint, selector1);
            var strongFingerprint2 = new StrongFingerprint(weakFingerprint, selector2);
            var contentHashListWithDeterminism1 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            var contentHashListWithDeterminism2 = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTest((context, contentLocationDatabase) =>
            {
                contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint1, contentHashListWithDeterminism1).ShouldBeSuccess();
                contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint2, contentHashListWithDeterminism2).ShouldBeSuccess();

                List<GetSelectorResult> getSelectorResults = contentLocationDatabase.GetSelectors(context, weakFingerprint).ToList();
                Assert.Equal(2, getSelectorResults.Count);

                GetSelectorResult r1 = getSelectorResults[0].ShouldBeSuccess();
                Assert.True(r1.Selector == selector1 || r1.Selector == selector2);

                GetSelectorResult r2 = getSelectorResults[1].ShouldBeSuccess();
                Assert.True(r2.Selector == selector1 || r2.Selector == selector2);
            });
        }

        [Fact]
        public Task GetNonExisting()
        {
            var strongFingerprint = StrongFingerprint.Random();

            return RunTest((context, contentLocationDatabase) =>
            {
                var result = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
                Assert.Equal(new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None)), result);
            });
        }

        [Fact]
        public Task GetExisting()
        {
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTest((context, contentLocationDatabase) =>
            {
                contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, contentHashListWithDeterminism).ShouldBeSuccess();

                var result = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), result);
            });
        }

        [Fact]
        public Task AddOrGetAddsNew()
        {
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);

            return RunTest((context, contentLocationDatabase) =>
            {
                var addResult = contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, contentHashListWithDeterminism);
                Assert.Equal(new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None)), addResult);

                var getResult = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), getResult);
            });
        }

        [Fact]
        public Task AddMultipleHashTypes()
        {
            var strongFingerprint = StrongFingerprint.Random();
            var contentHash1 = ContentHash.Random();
            var contentHash2 = ContentHash.Random(HashType.SHA1);
            var contentHash3 = ContentHash.Random(HashType.MD5);
            var contentHashList = new ContentHashList(new[] { contentHash1, contentHash2, contentHash3 });
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None);

            return RunTest((context, contentLocationDatabase) =>
            {
                contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, contentHashListWithDeterminism).ShouldBeSuccess();

                var result = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), result);
            });
        }

        [Fact]
        public Task AddPayload()
        {
            var strongFingerprint = StrongFingerprint.Random();
            var payload = new byte[] { 0, 1, 2, 3 };
            var contentHashListWithDeterminism =
                new ContentHashListWithDeterminism(ContentHashList.Random(payload: payload), CacheDeterminism.None);

            return RunTest((context, contentLocationDatabase) =>
            {
                contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, contentHashListWithDeterminism).ShouldBeSuccess();

                var result = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), result);
                Assert.True(result.ContentHashListWithDeterminism.ContentHashList.Payload.SequenceEqual(payload));
            });
        }

        [Fact]
        public Task AddNullPayload()
        {
            var strongFingerprint = StrongFingerprint.Random();

            // ReSharper disable once RedundantArgumentDefaultValue
            var contentHashListWithDeterminism =
                new ContentHashListWithDeterminism(ContentHashList.Random(payload: null), CacheDeterminism.None);

            return RunTest((context, contentLocationDatabase) =>
            {
                contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, contentHashListWithDeterminism).ShouldBeSuccess();

                var result = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
                Assert.Equal(new GetContentHashListResult(contentHashListWithDeterminism), result);
                Assert.Null(result.ContentHashListWithDeterminism.ContentHashList.Payload);
            });
        }

        [Fact]
        public Task AddUnexpiredDeterminism()
        {
            var strongFingerprint = StrongFingerprint.Random();
            var expirationUtc = DateTime.UtcNow + TimeSpan.FromDays(7);
            var guid = CacheDeterminism.NewCacheGuid();
            var determinism = CacheDeterminism.ViaCache(guid, expirationUtc);
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), determinism);

            return RunTest((context, contentLocationDatabase) =>
            {
                contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, contentHashListWithDeterminism).ShouldBeSuccess();
                var result = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
                Assert.Equal(guid, result.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
            });
        }

        [Fact]
        public Task AddExpiredDeterminism()
        {
            var strongFingerprint = StrongFingerprint.Random();
            var expirationUtc = DateTime.UtcNow - TimeSpan.FromDays(7);
            var guid = CacheDeterminism.NewCacheGuid();
            var determinism = CacheDeterminism.ViaCache(guid, expirationUtc);
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), determinism);

            return RunTest((context, contentLocationDatabase) =>
            {
                contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, contentHashListWithDeterminism).ShouldBeSuccess();
                var result = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
                Assert.Equal(CacheDeterminism.None.EffectiveGuid, result.ContentHashListWithDeterminism.Determinism.EffectiveGuid);
            });
        }

        [Theory]
        [InlineData(DeterminismNone, DeterminismTool)]
        [InlineData(DeterminismNone, DeterminismCache1)]
        [InlineData(DeterminismCache1, DeterminismCache2)]
        [InlineData(DeterminismCache2, DeterminismCache1)]
        [InlineData(DeterminismCache1, DeterminismTool)]
        // TODO(jubayard): These tests will permanently fail until this is integrated with the content store, as they rely on the content not being there when attempting to pin.
        //[InlineData(DeterminismTool, DeterminismNone)]
        //[InlineData(DeterminismTool, DeterminismCache1)]
        //[InlineData(DeterminismCache1, DeterminismNone)]
        [InlineData(DeterminismNone, DeterminismNone)]
        [InlineData(DeterminismCache1, DeterminismCache1)]
        [InlineData(DeterminismTool, DeterminismTool)]
        [InlineData(DeterminismSinglePhaseNon, DeterminismSinglePhaseNon)]
        [InlineData(DeterminismTool, DeterminismTool)]
        public Task AlwaysReplaceWhenPreviousContentMissing(int fromDeterminism, int toDeterminism)
        {
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashList = ContentHashList.Random();

            return RunTest((context, contentLocationDatabase) =>
            {
                var addResult = contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[fromDeterminism]));
                Assert.Equal(Determinism[fromDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // What we will do here is AddOrGet() a record that we already know is
                // there but with the determinism bit changed.
                addResult = contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[toDeterminism]))
;
                // We always expect the new determinism bit to take over
                Assert.Null(addResult.ContentHashListWithDeterminism.ContentHashList);
                Assert.Equal(Determinism[toDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // Validate that it was actually updated in the DB
                var getResult = contentLocationDatabase.GetContentHashList(context, strongFingerprint);
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
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashList = ContentHashList.Random();

            return RunTest((context, contentLocationDatabase) =>
            {
                var addResult = contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[fromDeterminism]));
                Assert.Equal(Determinism[fromDeterminism].EffectiveGuid, addResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid);

                // What we will do here is AddOrGet() a record that we already know is
                // there but with the determinism bit changed.
                addResult = contentLocationDatabase.AddOrGetContentHashList(
                    context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, Determinism[toDeterminism]));
                Assert.Equal(AddOrGetContentHashListResult.ResultCode.SinglePhaseMixingError, addResult.Code);
            });
        }

        [Fact]
        public Task EnumerateStrongFingerprintsEmpty()
        {
            return RunTest((context, store) =>
            {
                using (var strongFingerprintEnumerator = store.EnumerateStrongFingerprints(context).GetEnumerator())
                {
                    Assert.Equal(false, strongFingerprintEnumerator.MoveNext());
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
            return RunTest((context, session) =>
            {
                var expected = AddRandomContentHashLists(context, strongFingerprintCount, session);
                var enumerated =
                    (session.EnumerateStrongFingerprints(context).ToList())
                    .Where(result => result.Succeeded)
                    .Select(result => result.Data)
                    .ToHashSet();
                Assert.Equal(expected.Count, enumerated.Count);
                Assert.True(expected.SetEquals(enumerated));
            });
        }

        private HashSet<StrongFingerprint> AddRandomContentHashLists(
            OperationContext context, int count, ContentLocationDatabase session)
        {
            var strongFingerprints = new HashSet<StrongFingerprint>();
            for (int i = 0; i < count; i++)
            {
                var strongFingerprint = StrongFingerprint.Random();
                var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
                session.AddOrGetContentHashList(context, strongFingerprint, contentHashListWithDeterminism).ShouldBeSuccess();
                strongFingerprints.Add(strongFingerprint);
            }

            return strongFingerprints;
        }
    }
}
