// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Performance;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using Xunit;
using Record = BuildXL.Cache.MemoizationStore.Sessions.Record;

namespace BuildXL.Cache.MemoizationStore.Test.Performance.Sessions
{
    public abstract class MemoizationPerformanceTests : TestBase
    {
        protected const int MaxRowCount = 10_000;
        private const string ItemCountEnvironmentVariableName = "MemoizationPerformanceTestsItemCount";
        private const int ItemCountDefault = 1000;
        private const string Name = "name";
        private static readonly CancellationToken Token = CancellationToken.None;
        private readonly int _itemCount;
        private readonly Context _context;
        private readonly InitialDatabaseSize _initialDatabaseSize;
        private readonly AbsolutePath _prePopulatedRootPath;
        private readonly Func<IMemoizationSession, IMemoizationStore, Task> _nullSetupFunc = null;

        protected readonly PerformanceResultsFixture ResultsFixture;
        protected readonly Func<DisposableDirectory, IMemoizationStore> CreateStoreFunc;

        protected enum InitialDatabaseSize
        {
            Empty,
            Full
        }

        protected MemoizationPerformanceTests
            (
            ILogger logger,
            PerformanceResultsFixture resultsFixture,
            InitialDatabaseSize initialDatabaseSize,
            string databaseFileName,
            Func<DisposableDirectory, IMemoizationStore> createStoreFunc
            )
            : base(() => new PassThroughFileSystem(logger), logger)
        {
            _context = new Context(Logger);
            var itemCountEnvironmentVariable = Environment.GetEnvironmentVariable(ItemCountEnvironmentVariableName);
            _itemCount = itemCountEnvironmentVariable == null ? ItemCountDefault : int.Parse(itemCountEnvironmentVariable);
            _context.Debug($"Using itemCount=[{_itemCount}] (MaxRowCount=[{MaxRowCount}])");

            ResultsFixture = resultsFixture;
            _initialDatabaseSize = initialDatabaseSize;
            CreateStoreFunc = createStoreFunc;

            _prePopulatedRootPath = FileSystem.GetTempPath() / "CloudStore" / "MemoizationPerformanceTestsPrePopulated";
            if (!FileSystem.DirectoryExists(_prePopulatedRootPath))
            {
                FileSystem.CreateDirectory(_prePopulatedRootPath);
            }

            AbsolutePath databaseFilePath = _prePopulatedRootPath / databaseFileName;
            if (FileSystem.FileExists(databaseFilePath))
            {
                return;
            }

            _context.Always($"Creating prepopulated database at path={databaseFilePath}");

            using (var disposableDirectory = new DisposableDirectory(FileSystem))
            {
                using (var store = createStoreFunc(disposableDirectory))
                {
                    try
                    {
                        var startupStoreResult = store.StartupAsync(_context).Result;
                        startupStoreResult.ShouldBeSuccess();

                        var createSessionResult = store.CreateSession(_context, Name);
                        createSessionResult.ShouldBeSuccess();

                        using (var session = createSessionResult.Session)
                        {
                            try
                            {
                                var startupSessionResult = session.StartupAsync(_context).Result;
                                startupSessionResult.ShouldBeSuccess();

                                for (var i = 0; i < MaxRowCount; i++)
                                {
                                    var strongFingerprint = StrongFingerprint.Random();
                                    var contentHashList = ContentHashList.Random();
                                    var r = session.AddOrGetContentHashListAsync(
                                        _context, strongFingerprint, new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None), Token).Result;
                                    r.Succeeded.Should().BeTrue();
                                    r.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
                                }
                            }
                            finally
                            {
                                var shutdownSessionResult = session.ShutdownAsync(_context).Result;
                                shutdownSessionResult.ShouldBeSuccess();
                            }
                        }
                    }
                    finally
                    {
                        var shutdownStoreResult = store.ShutdownAsync(_context).Result;
                        shutdownStoreResult.ShouldBeSuccess();
                    }
                }

                FileSystem.CopyFileAsync(disposableDirectory.Path / databaseFileName, databaseFilePath, false).Wait();
            }
        }

        [Fact]
        public Task Startup()
        {
            return RunStartStopIterations(new Context(Logger), true);
        }

        [Fact]
        public Task Shutdown()
        {
            return RunStartStopIterations(new Context(Logger), false);
        }

        private async Task RunStartStopIterations(Context context, bool start)
        {
            const int iterations = 5;
            var stopwatch1 = new Stopwatch();
            var stopwatch2 = new Stopwatch();

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await EstablishStartingDatabaseAsync(testDirectory);

                // ReSharper disable once UnusedVariable
                foreach (var x in Enumerable.Range(0, iterations))
                {
                    using (var memoizationStore = CreateStoreFunc(testDirectory))
                    {
                        stopwatch1.Start();
                        await memoizationStore.StartupAsync(context).ShouldBeSuccess();
                        stopwatch1.Stop();

                        stopwatch2.Start();
                        await memoizationStore.ShutdownAsync(context).ShouldBeSuccess();
                        stopwatch2.Stop();
                    }
                }
            }

            var stopwatch = start ? stopwatch1 : stopwatch2;
            var averageTime = stopwatch.ElapsedMilliseconds / iterations;
            var name = GetType().Name + "." + (start ? "Startup" : "Shutdown");
            ResultsFixture.AddResults(Output, name, averageTime, "milliseconds");
        }

        [Fact]
        public async Task GetExistingSelectors()
        {
            IList<Fingerprint> weakFingerprints = null;

            await Run(
                nameof(GetExistingSelectors),
                async (session, store) =>
                {
                    if (_initialDatabaseSize == InitialDatabaseSize.Full)
                    {
                        var strongFingerprints = await EnumerateStrongFingerprintsAsync(_context, store, _itemCount);
                        weakFingerprints = strongFingerprints.Select(x => x.WeakFingerprint).ToList();
                    }
                    else
                    {
                        List<Record> records = CreateRandom(_itemCount);
                        await AddOrGet(_context, session, records);
                        weakFingerprints = records.Select(x => x.StrongFingerprint.WeakFingerprint).ToList();
                    }
                },
                async session => await GetSelectors(_context, session, weakFingerprints));
        }

        [Fact]
        public Task GetNonExistingSelectors()
        {
            IList<Fingerprint> weakFingerprints =
                Enumerable.Range(0, _itemCount).Select(x => Fingerprint.Random()).ToList();

            return Run(
                nameof(GetNonExistingSelectors),
                _nullSetupFunc,
                async session => await GetSelectors(_context, session, weakFingerprints));
        }

        [Fact]
        public async Task GetExistingContentHashList()
        {
            IList<StrongFingerprint> strongFingerprints = null;

            await Run(
                nameof(GetExistingContentHashList),
                async (session, store) =>
                {
                    if (_initialDatabaseSize == InitialDatabaseSize.Full)
                    {
                        strongFingerprints = await EnumerateStrongFingerprintsAsync(_context, store, _itemCount);
                    }
                    else
                    {
                        List<Record> records = CreateRandom(_itemCount);
                        await AddOrGet(_context, session, records);
                        strongFingerprints = records.Select(record => record.StrongFingerprint).ToList();
                    }
                },
                async session => await Get(_context, session, strongFingerprints));
        }

        [Fact]
        public Task GetNonExistingContentHashList()
        {
            IList<StrongFingerprint> strongFingerprints =
                Enumerable.Range(0, _itemCount).Select(x => StrongFingerprint.Random()).ToList();

            return Run(nameof(GetNonExistingContentHashList), _nullSetupFunc, async session =>
                await Get(_context, session, strongFingerprints));
        }

        [Fact]
        public Task AddOrGetContentHashListAdds()
        {
            var items = CreateRandom(_itemCount);
            return Run(nameof(AddOrGetContentHashListAdds), _nullSetupFunc, async session =>
                await AddOrGet(_context, session, items));
        }

        [Fact]
        public async Task AddOrGetContentHashListGets()
        {
            List<Record> items = null;

            await Run(
                nameof(AddOrGetContentHashListGets),
                async (session, store) =>
                {
                    List<StrongFingerprint> strongFingerprints;
                    if (_initialDatabaseSize == InitialDatabaseSize.Full)
                    {
                        strongFingerprints = await EnumerateStrongFingerprintsAsync(_context, store, _itemCount);
                    }
                    else
                    {
                        List<Record> records = CreateRandom(_itemCount);
                        await AddOrGet(_context, session, records);
                        strongFingerprints = records.Select(record => record.StrongFingerprint).ToList();
                    }

                    items = strongFingerprints.Select(strongFingerprint => new Record(
                            strongFingerprint,
                            new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None))).ToList();
                },
                async session => await AddOrGet(_context, session, items));
        }

        private static List<Record> CreateRandom(int count)
        {
            var list = new List<Record>(count);

            while (list.Count < count)
            {
                var strongFingerprint = StrongFingerprint.Random();
                var contentHashList = ContentHashList.Random();
                var determinism = CacheDeterminism.None;
                list.Add(new Record(strongFingerprint, new ContentHashListWithDeterminism(contentHashList, determinism)));
            }

            return list;
        }

        private static async Task GetSelectors(Context context, IMemoizationSession session, IList<Fingerprint> weakFingerprints)
        {
            foreach (var weakFingerprint in weakFingerprints)
            {
                var getSelectorsEnumerator = session.GetSelectors(context, weakFingerprint, Token);
                Async::System.Collections.Generic.IAsyncEnumerator<GetSelectorResult> enumerator = getSelectorsEnumerator.GetEnumerator();
                while (await enumerator.MoveNext(CancellationToken.None))
                {
                    GetSelectorResult result = enumerator.Current;
                    result.Succeeded.Should().BeTrue();
                }
            }
        }

        private static async Task Get(Context context, IMemoizationSession session, IList<StrongFingerprint> strongFingerprints)
        {
            var tasks = Enumerable.Range(0, strongFingerprints.Count).Select(i => Task.Run(async () =>
            {
                var r = await session.GetContentHashListAsync(context, strongFingerprints[i], Token);
                r.Succeeded.Should().BeTrue();
            }));

            await TaskSafetyHelpers.WhenAll(tasks);
        }

        private static async Task AddOrGet(
            Context context, IMemoizationSession session, List<Record> records)
        {
            var tasks = Enumerable.Range(0, records.Count).Select(i => Task.Run(async () =>
            {
                var r = await session.AddOrGetContentHashListAsync(
                    context, records[i].StrongFingerprint, records[i].ContentHashListWithDeterminism, Token);
                r.Succeeded.Should().BeTrue();
                r.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();
            }));

            await TaskSafetyHelpers.WhenAll(tasks);
        }

        private async Task<List<StrongFingerprint>> EnumerateStrongFingerprintsAsync(Context context, IMemoizationStore store, int count)
        {
            var asyncEnumerator = store.EnumerateStrongFingerprints(context).GetEnumerator();
            var strongFingerprints = new List<StrongFingerprint>();

            for (int i = 0; i < count && await asyncEnumerator.MoveNext() && asyncEnumerator.Current.Succeeded; i++)
            {
                strongFingerprints.Add(asyncEnumerator.Current.Data);
            }

            return strongFingerprints;
        }

        protected async Task EstablishStartingDatabaseAsync(DisposableDirectory testDirectory)
        {
            if (_initialDatabaseSize == InitialDatabaseSize.Full)
            {
                foreach (var fileInfo in FileSystem.EnumerateFiles(_prePopulatedRootPath, EnumerateOptions.None))
                {
                    var destinationPath = testDirectory.Path / fileInfo.FullPath.FileName;
                    if (!FileSystem.FileExists(destinationPath))
                    {
                        await FileSystem.CopyFileAsync(fileInfo.FullPath, destinationPath, false);
                    }
                }

                Logger.Debug("Starting with a full database");
            }
            else
            {
                Logger.Debug("Starting with an empty database");
            }
        }

        private async Task Run
            (
            string method,
            Func<IMemoizationSession, IMemoizationStore, Task> setupFuncAsync,
            Func<IMemoizationSession, Task> testFuncAsync
            )
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await EstablishStartingDatabaseAsync(testDirectory);

                using (var store = CreateStoreFunc(testDirectory))
                {
                    store.Should().NotBeNull();
                    try
                    {
                        var storeBoolResult = await store.StartupAsync(_context);
                        storeBoolResult.ShouldBeSuccess();

                        var createSessionResult = store.CreateSession(_context, Name);
                        createSessionResult.ShouldBeSuccess();

                        using (var session = createSessionResult.Session)
                        {
                            try
                            {
                                var sessionBoolResult = await session.StartupAsync(_context);
                                sessionBoolResult.ShouldBeSuccess();

                                if (setupFuncAsync != null)
                                {
                                    await setupFuncAsync(session, store);
                                }

                                var stopwatch = Stopwatch.StartNew();
                                await testFuncAsync(session);
                                stopwatch.Stop();

                                var rate = (long)(_itemCount / stopwatch.Elapsed.TotalSeconds);
                                var name = GetType().Name + "." + method;
                                ResultsFixture.AddResults(Output, name, rate, "items/sec", _itemCount);
                            }
                            finally
                            {
                                var sessionBoolResult = await session.ShutdownAsync(_context);
                                sessionBoolResult.ShouldBeSuccess();
                            }
                        }
                    }
                    finally
                    {
                        var storeBoolResult = await store.ShutdownAsync(_context);
                        storeBoolResult.ShouldBeSuccess();
                    }
                }
            }
        }
    }
}
