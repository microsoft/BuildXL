// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.SQLite;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
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

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class SQLiteMemoizationSessionTests : MemoizationSessionTests
    {
        private const long MaxRowCount = 10000;
        private readonly MemoryClock _clock = new MemoryClock();

        public SQLiteMemoizationSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            // Create a database in the temp directory
            return CreateSQLiteMemoizationStore(testDirectory.Path);
        }

        private IMemoizationStore CreateSQLiteMemoizationStore(AbsolutePath path, SynchronizationMode syncMode = SQLiteMemoizationStore.DefaultSyncMode)
        {
            var memoConfig = new SQLiteMemoizationStoreConfiguration(path) { MaxRowCount = MaxRowCount };
            memoConfig.Database.SyncMode = syncMode;
            memoConfig.Database.JournalMode = JournalMode.OFF;
            return new TestSQLiteMemoizationStore(Logger, _clock, memoConfig);
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

                await ((SQLiteMemoizationStore)store).SyncAsync();

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

        [Theory]
        [InlineData(DeterminismNone, DeterminismNone)]
        [InlineData(DeterminismCache1, DeterminismNone)]
        [InlineData(DeterminismCache1Expired, DeterminismNone)]
        [InlineData(DeterminismTool, DeterminismTool)]
        [InlineData(DeterminismSinglePhaseNon, DeterminismSinglePhaseNon)]
        public async Task UpgradeFromBeforeSerializedDeterminism(int oldDeterminism, int shouldBecomeDeterminism)
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), Determinism[oldDeterminism]);

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token);
                    Assert.True(result.Succeeded);

                    await ((TestSQLiteMemoizationStore)store).DeleteColumnAsync("ContentHashLists", "SerializedDeterminism");
                });

                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.GetContentHashListAsync(context, strongFingerprint, Token);
                    Assert.Equal(
                        new GetContentHashListResult(new ContentHashListWithDeterminism(
                            contentHashListWithDeterminism.ContentHashList, Determinism[shouldBecomeDeterminism])), result);
                });
            }
        }

        private async Task CorruptSqliteDbAtPathAsync(AbsolutePath dbPath)
        {
            using (var corruptedDb = await FileSystem.OpenAsync(dbPath, FileAccess.Write, FileMode.Create, FileShare.Delete))
            {
                var corruptedDbContent = System.Text.Encoding.ASCII.GetBytes("Corrupted SqlLite DB for testing");
                await corruptedDb.WriteAsync(corruptedDbContent, 0, corruptedDbContent.Length);
            }
        }

        [Fact]
        public async Task CorruptedDbShouldNotCrashStoreOnStartup()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await CorruptSqliteDbAtPathAsync(testDirectory.Path / SQLiteMemoizationStore.DefaultDatabaseFileName);
                await RunTestAsync(
                    new Context(Logger),
                    testDirectory,
                    (store, session) =>
                    {
                        return Task.FromResult(BoolResult.Success);
                    }
                );
            }
        }

        [Fact]
        public async Task VerifyBackupDbIsUsable()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), Determinism[DeterminismNone]);

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var safeDefaultFilePath = Path.GetTempFileName();
                AbsolutePath
                    dbFilePath = new AbsolutePath(safeDefaultFilePath),
                    dbBackupPath = new AbsolutePath(safeDefaultFilePath);

                // Write a cached entry to a fresh master DB file
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token);
                    Assert.True(result.Succeeded);

                    dbFilePath = ((TestSQLiteMemoizationStore)store).DatabaseFilePathExtracted;
                    dbBackupPath = ((TestSQLiteMemoizationStore)store).DatabaseBackupPathExtracted;
                });

                Assert.True(dbFilePath.Path != safeDefaultFilePath);
                Assert.True(dbBackupPath.Path != safeDefaultFilePath);

                // Clone the master into backup and corrupt the master DB file
                File.Copy(dbFilePath.Path, dbBackupPath.Path, overwrite: true);
                await CorruptSqliteDbAtPathAsync(dbFilePath);

                // This step should load the backup, since the master DB is corrupted
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.GetContentHashListAsync(
                        context, strongFingerprint, Token);
                    Assert.True(result.Succeeded);
                    Assert.Equal(result.ContentHashListWithDeterminism, contentHashListWithDeterminism);
                });
            }
        }

        [Fact]
        public async Task VerifyStartupSucceedsWhenMasterAndBackupDbAreCorrupt()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), Determinism[DeterminismNone]);

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var safeDefaultFilePath = Path.GetTempFileName();
                AbsolutePath
                    dbFilePath = new AbsolutePath(safeDefaultFilePath),
                    dbBackupPath = new AbsolutePath(safeDefaultFilePath);

                // Write a cached entry to a fresh master DB file
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token);
                    Assert.True(result.Succeeded);

                    dbFilePath = ((TestSQLiteMemoizationStore)store).DatabaseFilePathExtracted;
                    dbBackupPath = ((TestSQLiteMemoizationStore)store).DatabaseBackupPathExtracted;
                });

                Assert.True(dbFilePath.Path != safeDefaultFilePath);
                Assert.True(dbBackupPath.Path != safeDefaultFilePath);

                // Corrupt the master and backup DB files
                await CorruptSqliteDbAtPathAsync(dbFilePath);
                await CorruptSqliteDbAtPathAsync(dbBackupPath);

                // This step will start with a fresh, empty DB when both DB files are corrupt
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.GetContentHashListAsync(
                        context, strongFingerprint, Token);
                    Assert.True(result.Succeeded);
                    Assert.Null(result.ContentHashListWithDeterminism.ContentHashList);
                });
            }
        }

        private Task RunTestWithIntegrityCheckAtStartupAsync(Context context, DisposableDirectory testDirectory, System.Func<IMemoizationStore, IMemoizationSession, Task> funcAsync)
        {
            return RunTestAsync(context, testDirectory, funcAsync, _ => {
                var memoConfig = new SQLiteMemoizationStoreConfiguration(testDirectory.Path) { MaxRowCount = MaxRowCount };
                memoConfig.Database.VerifyIntegrityOnStartup = true;
                return new TestSQLiteMemoizationStore(Logger, _clock, memoConfig);
            });
        }

        private async Task BloatDbAsync(Context context, IMemoizationSession session)
        {
            uint dummyFingerprintsToAdd = 40; // generates a ~52KB DB file
            var addBlock = new ActionBlock<int>(
                async _ =>
                {
                    var strongFingerprint = StrongFingerprint.Random();
                    var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                    ContentHashList.Random(), Determinism[DeterminismNone]);

                    var result = await session.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token);
                    Assert.True(result.Succeeded);
                },
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount});

            while (--dummyFingerprintsToAdd > 0)
            {
                await addBlock.SendAsync(0);
            }

            addBlock.Complete();
            await addBlock.Completion;
        }

        private async Task CorruptRandomDbPageThatIsNotAtTheHeadAsync(AbsolutePath dbPath)
        {
            const int assumedMaxSqliteDbPageSizeInBytes = 4096;
            const int assumedMiniumumPagesNeededForTailCorruptionRepro = 10;
            const int minimumDbSizeForCorruptionRepro =
                assumedMaxSqliteDbPageSizeInBytes * assumedMiniumumPagesNeededForTailCorruptionRepro;

            var corruptedDbString = "Corrupted SqlLite DB for testing" + new string('Z', assumedMaxSqliteDbPageSizeInBytes);
            var corruptedDbBytes = System.Text.Encoding.ASCII.GetBytes(corruptedDbString);
            var offsetToCorrupt = minimumDbSizeForCorruptionRepro - corruptedDbBytes.Length - 100;
            Assert.True(offsetToCorrupt > assumedMaxSqliteDbPageSizeInBytes);

            using (var corruptedDb = await FileSystem.OpenAsync(dbPath, FileAccess.Write, FileMode.Open, FileShare.Delete))
            {
                Assert.True(corruptedDb.Length > minimumDbSizeForCorruptionRepro);
                corruptedDb.Position = offsetToCorrupt;
                await corruptedDb.WriteAsync(corruptedDbBytes, 0, corruptedDbBytes.Length);
            }
        }

        [Fact]
        public async Task VerifyIntegrityCheckAtStartupForcesDelayedNotificationsOfDbCorruptionToBeReportedImmediately()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), Determinism[DeterminismNone]);

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var safeDefaultFilePath = Path.GetTempFileName();
                AbsolutePath dbFilePath = new AbsolutePath(safeDefaultFilePath);

                // Write a cached entry to a fresh master DB file and bloat the DB by adding random fingerprints
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token);
                    Assert.True(result.Succeeded);

                    dbFilePath = ((TestSQLiteMemoizationStore)store).DatabaseFilePathExtracted;
                    await BloatDbAsync(context, session);
                });

                // Corrupt the DB
                Assert.True(dbFilePath.Path != safeDefaultFilePath);
                await CorruptRandomDbPageThatIsNotAtTheHeadAsync(dbFilePath);

                // Accessing the first value in the DB should succeed since it only needs the head page to be
                // read. DB accesses will only fail if and when the corrupted page has is eventually read.
                // (The corrupted page may be preloaded, which is why the db corruption helper must corrupt
                // a page that is pretty far away from the first page)
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.GetContentHashListAsync(
                        context, strongFingerprint, Token);
                    Assert.True(result.Succeeded);
                    Assert.Equal(result.ContentHashListWithDeterminism, contentHashListWithDeterminism);
                });

                // The corrupted DB should be cleaned out when an integrity check is done, and we should have a
                // fresh DB that no longer has the fingerprint inserted at the start of this test.
                await RunTestWithIntegrityCheckAtStartupAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.GetContentHashListAsync(
                        context, strongFingerprint, Token);
                    Assert.True(result.Succeeded);
                    Assert.Null(result.ContentHashListWithDeterminism.ContentHashList);
                });
            }
        }

        [Fact]
        public async Task VerifyIntegrityCheckRunsAtStartupAutomaticallyIfPreviousCacheRunFailedToShutdownCleanly()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), Determinism[DeterminismNone]);

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var safeDefaultFilePath = Path.GetTempFileName();
                AbsolutePath dbFilePath = new AbsolutePath(safeDefaultFilePath),
                    dbInUseMarkerPath = new AbsolutePath(safeDefaultFilePath);

                // Write a cached entry to a fresh master DB file and bloat the DB by adding random fingerprints
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.AddOrGetContentHashListAsync(
                        context, strongFingerprint, contentHashListWithDeterminism, Token);
                    Assert.True(result.Succeeded);

                    dbFilePath = ((TestSQLiteMemoizationStore)store).DatabaseFilePathExtracted;
                    dbInUseMarkerPath = ((TestSQLiteMemoizationStore)store).DatabaseInUseMarkerFilePathExtracted;
                    Assert.True(File.Exists(dbInUseMarkerPath.Path));
                    await BloatDbAsync(context, session);
                });

                Assert.True(dbFilePath.Path != safeDefaultFilePath);
                Assert.True(dbInUseMarkerPath.Path != safeDefaultFilePath);

                // Corrupt the DB and make sure there is no marker to force a integrity check
                await CorruptRandomDbPageThatIsNotAtTheHeadAsync(dbFilePath);
                Assert.True(!File.Exists(dbInUseMarkerPath.Path));

                // The DB has been corrupted at the tail. Reading a value from one of the early pages should succeed,
                // even though the db is corrupted
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.GetContentHashListAsync(
                        context, strongFingerprint, Token);
                    Assert.True(result.Succeeded);
                    Assert.Equal(result.ContentHashListWithDeterminism, contentHashListWithDeterminism);
                    Assert.True(File.Exists(dbInUseMarkerPath.Path));
                });

                Assert.True(!File.Exists(dbInUseMarkerPath.Path));

#pragma warning disable AsyncFixer02 // WriteAllBytesAsync should be used instead of File.WriteAllBytes
                // Leave a marker, to make sure an integrity check is automatically run on the next startup
                File.WriteAllBytes(dbInUseMarkerPath.Path, new byte[] { });
#pragma warning restore AsyncFixer02 // WriteAllBytesAsync should be used instead of File.WriteAllBytes

                // When the in use marker is left behind, and integrity check should be run even if not explicitly
                // asked for (to ask explicitly see RunTestWithIntegrityCheckAtStartupAsync in this file). The
                // integrity check should fail and clear out the database. A fresh DB is used that no longer has the
                // fingerprint inserted at the start of this test.
                await RunTestAsync(context, testDirectory, async (store, session) =>
                {
                    var result = await session.GetContentHashListAsync(
                        context, strongFingerprint, Token);
                    Assert.True(result.Succeeded);
                    Assert.Null(result.ContentHashListWithDeterminism.ContentHashList);
                });

                Assert.True(!File.Exists(dbInUseMarkerPath.Path));
            }
        }

        [Fact]
        public async Task StartupWithSpecificDatabaseName()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // Constructing with a full database name instead of just a root directory
                using (var store = CreateSQLiteMemoizationStore(testDirectory.Path / SQLiteMemoizationStore.DefaultDatabaseFileName))
                {
                    BoolResult result = await store.StartupAsync(context);
                    result.ShouldBeSuccess();

                    result = await store.ShutdownAsync(context);
                    result.ShouldBeSuccess();
                }
            }
        }

        [Theory]
        [InlineData(SynchronizationMode.Off, SynchronizationMode.Normal)]
        [InlineData(SynchronizationMode.Normal, SynchronizationMode.Off)]
        [InlineData(SynchronizationMode.Off, SynchronizationMode.Full)]
        [InlineData(SynchronizationMode.Full, SynchronizationMode.Off)]
        [InlineData(SynchronizationMode.Normal, SynchronizationMode.Full)]
        [InlineData(SynchronizationMode.Full, SynchronizationMode.Normal)]
        public async Task MixSyncModes(SynchronizationMode fromSyncMode, SynchronizationMode toSyncMode)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                StrongFingerprint sfp = StrongFingerprint.Random();
                ContentHashListWithDeterminism value = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
                await RunTestAsync(
                    context,
                    testDirectory,
                    async (store, session) =>
                    {
                        // Add a value to a store with one sync mode
                        AddOrGetContentHashListResult addResult = await session.AddOrGetContentHashListAsync(context, sfp, value, Token);
                        Assert.True(addResult.Succeeded);
                        Assert.Null(addResult.ContentHashListWithDeterminism.ContentHashList);
                    },
                    testDir => CreateSQLiteMemoizationStore(testDirectory.Path, fromSyncMode));

                await RunTestAsync(
                    context,
                    testDirectory,
                    async (store, session) =>
                    {
                        // Make sure the same value can still be read from another sync mode
                        GetContentHashListResult getResult = await session.GetContentHashListAsync(context, sfp, Token);
                        getResult.ShouldBeSuccess();
                        Assert.Equal(value, getResult.ContentHashListWithDeterminism);

                        // Make sure a new value can be written in another sync mode
                        StrongFingerprint newSfp = StrongFingerprint.Random();
                        ContentHashListWithDeterminism newValue =
                            new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
                        AddOrGetContentHashListResult addResult = await session.AddOrGetContentHashListAsync(context, newSfp, newValue, Token);
                        Assert.True(addResult.Succeeded);
                        Assert.Null(addResult.ContentHashListWithDeterminism.ContentHashList);
                    },
                    testDir => CreateSQLiteMemoizationStore(testDirectory.Path, toSyncMode));
            }
        }
    }
}
