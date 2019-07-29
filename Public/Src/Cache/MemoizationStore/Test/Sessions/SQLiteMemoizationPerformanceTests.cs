// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.SQLite;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Performance;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Test.Sessions;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single class

// ReSharper disable UnusedMember.Global
namespace BuildXL.Cache.MemoizationStore.Test.Performance.Sessions
{
    public abstract class SQLiteMemoizationPerformanceTests : MemoizationPerformanceTests
    {
        protected SQLiteMemoizationPerformanceTests(
            ILogger logger, PerformanceResultsFixture resultsFixture, InitialDatabaseSize initialDatabaseSize, long maxRowCount, SynchronizationMode syncMode)
            : base(
                logger,
                resultsFixture,
                initialDatabaseSize,
                SQLiteMemoizationStore.DefaultDatabaseFileName,
                testDirectory => new SQLiteMemoizationStore(
                    logger,
                    SystemClock.Instance,
                    GenerateMemoizationConfiguration(maxRowCount, syncMode, testDirectory)))
        {
        }

        private static SQLiteMemoizationStoreConfiguration GenerateMemoizationConfiguration(long maxRowCount, SynchronizationMode syncMode, DisposableDirectory testDirectory)
        {
            var memoConfig = new SQLiteMemoizationStoreConfiguration(testDirectory.Path) { MaxRowCount = maxRowCount };

            memoConfig.Database.SyncMode = syncMode;
            // Having the journal disabled won't be indicative of real world performance. It is disabled so the tests run faster
            memoConfig.Database.JournalMode = JournalMode.OFF;

            return memoConfig;
        }
    }

    [Trait("Category", "Performance")]
    public class SQLiteMemoizationEmptyPerformanceTests : SQLiteMemoizationPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SQLiteMemoizationEmptyPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, resultsFixture, InitialDatabaseSize.Empty, MaxRowCount, SynchronizationMode.Off)
        {
        }
    }

    [Trait("Category", "Performance")]
    public class SQLiteMemoizationEmptySyncNormalPerformanceTests : SQLiteMemoizationPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SQLiteMemoizationEmptySyncNormalPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, resultsFixture, InitialDatabaseSize.Empty, MaxRowCount, SynchronizationMode.Normal)
        {
        }
    }

    [Trait("Category", "Performance")]
    public class SQLiteMemoizationFullPerformanceTests : SQLiteMemoizationPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SQLiteMemoizationFullPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, resultsFixture, InitialDatabaseSize.Full, MaxRowCount, SynchronizationMode.Off)
        {
        }

        [Fact]
        public async Task UpgradeStartup()
        {
            const int iterations = 5;
            var stopwatch = new Stopwatch();
            var context = new Context(Logger);

            // ReSharper disable once UnusedVariable
            foreach (var x in Enumerable.Range(0, iterations))
            {
                using (var testDirectory = new DisposableDirectory(FileSystem))
                {
                    await EstablishStartingDatabaseAsync(testDirectory);
                    await RemoveSerializedDeterminismColumnAsync(testDirectory);

                    using (var memoizationStore = CreateStoreFunc(testDirectory))
                    {
                        try
                        {
                            stopwatch.Start();
                            await memoizationStore.StartupAsync(context).ShouldBeSuccess();
                            stopwatch.Stop();
                        }
                        finally
                        {
                            await memoizationStore.ShutdownAsync(context).ShouldBeSuccess();
                        }
                    }
                }
            }

            var averageTime = stopwatch.ElapsedMilliseconds / iterations;
            var name = GetType().Name + ".UpgradeStartup";
            ResultsFixture.Results.Add(name, averageTime, "milliseconds");
        }

        private async Task RemoveSerializedDeterminismColumnAsync(DisposableDirectory testDirectory)
        {
            var context = new Context(Logger);
            var databaseFilePath = testDirectory.Path / SQLiteMemoizationStore.DefaultDatabaseFileName;
            using (var store = new TestSQLiteMemoizationStore(Logger, SystemClock.Instance, new SQLiteMemoizationStoreConfiguration(databaseFilePath) { MaxRowCount = MaxRowCount }))
            {
                try
                {
                    var startupResult = await store.StartupAsync(context);
                    startupResult.ShouldBeSuccess();

                    await store.DeleteColumnAsync("ContentHashLists", "SerializedDeterminism");
                }
                finally
                {
                    var shutdownResult = await store.ShutdownAsync(context);
                    shutdownResult.ShouldBeSuccess();
                }
            }
        }
    }

    [Trait("Category", "Performance")]
    public class SQLiteMemoizationFullSyncNormalPerformanceTests : SQLiteMemoizationPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SQLiteMemoizationFullSyncNormalPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, resultsFixture, InitialDatabaseSize.Full, MaxRowCount, SynchronizationMode.Normal)
        {
        }
    }

    [Trait("Category", "Performance")]
    public class SQLiteMemoizationFullNoLoggingPerformanceTests : SQLiteMemoizationPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SQLiteMemoizationFullNoLoggingPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(NullLogger.Instance, resultsFixture, InitialDatabaseSize.Full, MaxRowCount, SynchronizationMode.Off)
        {
        }
    }

    [Trait("Category", "Performance")]
    public class SQLiteMemoizationFullNoLoggingSyncNormalPerformanceTests : SQLiteMemoizationPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SQLiteMemoizationFullNoLoggingSyncNormalPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(NullLogger.Instance, resultsFixture, InitialDatabaseSize.Full, MaxRowCount, SynchronizationMode.Normal)
        {
        }
    }

    [Trait("Category", "Performance")]
    public class SQLiteMemoizationFullNoLruPerformanceTests : SQLiteMemoizationPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SQLiteMemoizationFullNoLruPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, resultsFixture, InitialDatabaseSize.Full, -1, SynchronizationMode.Off)
        {
        }
    }

    [Trait("Category", "Performance")]
    public class SQLiteMemoizationFullNoLruSyncNormalPerformanceTests : SQLiteMemoizationPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SQLiteMemoizationFullNoLruSyncNormalPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, resultsFixture, InitialDatabaseSize.Full, -1, SynchronizationMode.Normal)
        {
        }
    }
}
