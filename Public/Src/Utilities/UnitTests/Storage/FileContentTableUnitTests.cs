// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Native.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.Diagnostics;
using BuildXL.Storage.FileContentTableAccessor;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    public sealed class FileContentTableUnitTests : TemporaryStorageTestBase
    {
        private const string TestFileAName = "TestFileA";
        private const string TestFileBName = "TestFileB";
        private const string Table = "FileContentTable";

        private static readonly ContentHash s_hashA = CreateHash(TestFileAName);
        private static readonly ContentHash s_hashB = CreateHash(TestFileBName);

        private readonly PathTable m_pathTable;
        private AbsolutePath m_testFileA;
        private AbsolutePath m_testFileB;

        public FileContentTableUnitTests()
        {
            RegisterEventSource(global::BuildXL.Storage.ETWLogger.Log);
            m_pathTable = new PathTable();
            m_testFileA = AbsolutePath.Create(m_pathTable, GetFullPath(TestFileAName));
            m_testFileB = AbsolutePath.Create(m_pathTable, GetFullPath(TestFileBName));

            string testPrecisionFile = WriteFile("TestFileVersionPrecision", string.Empty);
            FileUtilities.IsPreciseFileVersionSupportedByEnlistmentVolume = VersionedFileIdentity.HasPreciseFileVersion(testPrecisionFile);
        }

        [Fact]
        public async Task CanRoundTripEmptyTable()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            FileContentTable loadedTable = await SaveAndReloadTable(originalTable);

            ExpectHashUnknown(loadedTable, m_testFileA);
            ExpectHashUnknown(loadedTable, m_testFileB);

            VerifyTable(loadedTable);
        }

        [Fact]
        public async Task CanRoundTripSingleEntry()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);

            FileContentTable loadedTable = await SaveAndReloadTable(originalTable);

            ExpectHashKnown(loadedTable, m_testFileA, s_hashA);
            ExpectHashUnknown(loadedTable, m_testFileB);

            VerifyTable(loadedTable);
        }

        [Fact]
        public async Task CanRoundTripMultipleEntries()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            RecordContentHash(originalTable, m_testFileB, s_hashB);

            FileContentTable loadedTable = await SaveAndReloadTable(originalTable);

            ExpectHashKnown(loadedTable, m_testFileA, s_hashA);
            ExpectHashKnown(loadedTable, m_testFileB, s_hashB);

            VerifyTable(loadedTable);
        }

        [Fact]
        public async Task UnusedEntriesAreEvictedWithMinimumTimeToLive()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext, entryTimeToLive: 1);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            RecordContentHash(originalTable, m_testFileB, s_hashB);

            FileContentTable loadedTable = await SaveAndReloadTable(originalTable);

            ExpectHashKnown(loadedTable, m_testFileB, s_hashB);

            FileContentTable laterLoadedTable = await SaveAndReloadTable(loadedTable);

            ExpectHashKnown(laterLoadedTable, m_testFileB, s_hashB);
            ExpectHashUnknown(laterLoadedTable, m_testFileA);

            VerifyTable(laterLoadedTable);
        }

        [Fact]
        public async Task UnusedEntriesAreEvictedWithNonTrivialTimeToLive()
        {
            const byte TimeToLive = 3;

            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext, entryTimeToLive: 2);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            RecordContentHash(originalTable, m_testFileB, s_hashB);

            FileContentTable loadedTable = null;
            for (int i = 0; i < TimeToLive; i++)
            {
                loadedTable = await SaveAndReloadTable(loadedTable ?? originalTable);
                ExpectHashKnown(loadedTable, m_testFileB, s_hashB);
            }

            ExpectHashKnown(loadedTable, m_testFileB, s_hashB);
            ExpectHashUnknown(loadedTable, m_testFileA);

            VerifyTable(loadedTable);
        }

        [Fact]
        public async Task EntryTimeToLiveCanBeResetViaAccess()
        {
            const byte TimeToLive = 4;

            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext, entryTimeToLive: 1);
            RecordContentHash(originalTable, m_testFileA, s_hashA);

            FileContentTable loadedTable = null;
            for (int i = 0; i < TimeToLive - 1; i++)
            {
                loadedTable = await SaveAndReloadTable(originalTable);
            }

            // TTL in memory should be zero (evict on save). This should raise it to 4.
            ExpectHashKnown(loadedTable, m_testFileA, s_hashA);

            // Now drop it to zero again in memory.
            for (int i = 0; i < TimeToLive - 1; i++)
            {
                loadedTable = await SaveAndReloadTable(originalTable);
            }

            // Still known (implying we reset the TTL correctly).
            ExpectHashKnown(loadedTable, m_testFileA, s_hashA);

            VerifyTable(loadedTable);
        }

        [Fact]
        public void ChangedContentInvalidatesEntry()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            ExpectHashKnown(originalTable, m_testFileA, s_hashA);

            ModifyContents(m_testFileA);

            ExpectHashUnknown(originalTable, m_testFileA);

            VerifyTable(originalTable);
        }

        [Fact]
        public void ChangedContentInvalidatesEntryWithSingleHandle()
        {
            using (var fs = File.Open(GetFullPath(TestFileAName), FileMode.CreateNew, FileAccess.ReadWrite))
            {
                // Write at least one byte to avoid the check for null size hash.
                fs.WriteByte(1);

                var table = FileContentTable.CreateNew(LoggingContext);
                for (int i = 0; i < 10; i++)
                {
                    table.RecordContentHash(fs, s_hashA);

                    VersionedFileIdentityAndContentInfo? maybeHash = table.TryGetKnownContentHash(fs);
                    XAssert.IsTrue(maybeHash.HasValue, "Should be known after recording");
                    XAssert.AreEqual(s_hashA, maybeHash.Value.FileContentInfo.Hash);

                    maybeHash = table.TryGetKnownContentHash(fs);
                    XAssert.IsTrue(maybeHash.HasValue, "Should be still known after recording");
                    XAssert.AreEqual(s_hashA, maybeHash.Value.FileContentInfo.Hash);

                    fs.WriteByte(0);

                    maybeHash = table.TryGetKnownContentHash(fs);
                    XAssert.IsFalse(maybeHash.HasValue, "Should be unknown after writing");
                }
            }
        }

        [Fact]
        public async Task ChangedContentInvalidatesEntryAfterRoundtrip()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            ExpectHashKnown(originalTable, m_testFileA, s_hashA);
            await SaveTable(originalTable);

            ModifyContents(m_testFileA);

            FileContentTable loadedTable = await LoadTable();
            ExpectHashUnknown(loadedTable, m_testFileA);

            VerifyTable(loadedTable);
        }

        [Fact]
        public void ChangedTimestampInvalidatesEntry()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            ExpectHashKnown(originalTable, m_testFileA, s_hashA);

            ModifyTimestamp(m_testFileA);

            ExpectHashUnknown(originalTable, m_testFileA);

            VerifyTable(originalTable);
        }

        [Fact]
        public void SwappingFilesViaRenameInvalidatesBothEntries()
        {
            WriteTestFiles();
            SetIdenticalModificationTimestamps(m_testFileA, m_testFileB);

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            RecordContentHash(originalTable, m_testFileB, s_hashB);
            ExpectHashKnown(originalTable, m_testFileA, s_hashA);
            ExpectHashKnown(originalTable, m_testFileB, s_hashB);

            Swap(m_testFileA, m_testFileB);

            if (FileUtilities.IsPreciseFileVersionSupportedByEnlistmentVolume)
            {
                // If precise file version is not supported, then rename may change the file version.
                // Note that rename does not change the content so even if precise file version
                // is not supported, FileContentTable can still be used.
                ExpectHashUnknown(originalTable, m_testFileA);
                ExpectHashUnknown(originalTable, m_testFileB);
            }

            VerifyTable(originalTable);
        }

        [Fact]
        public async Task TruncatedTableResultsInRecoverableException()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            RecordContentHash(originalTable, m_testFileB, s_hashB);
            await SaveTable(originalTable);
            TruncateTable();

            var fileContentTable = await LoadTable();
            XAssert.AreEqual(null, fileContentTable, "Table shouldn't have loaded due to missing data");
        }

        [Fact]
        public async Task TruncatedTableResultsCanBeDiscardedWithWarning()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            RecordContentHash(originalTable, m_testFileB, s_hashB);
            await SaveTable(originalTable);
            TruncateTable();

            FileContentTable loadedTable = await LoadOrCreateTable();
            ExpectHashUnknown(loadedTable, m_testFileA);
            ExpectHashUnknown(loadedTable, m_testFileB);

            AssertInformationalEventLogged(LogEventId.StorageLoadFileContentTable);

            VerifyTable(loadedTable);
        }

        [Fact]
        public void QueryingHashOfNonExistentFileShouldReturnNull()
        {
            var table = FileContentTable.CreateNew(LoggingContext);
            var knownContentHash = table.TryGetKnownContentHash(m_testFileA.ToString(m_pathTable));
            XAssert.IsFalse(knownContentHash.HasValue, "Table shouldn't have had that entry");
        }

        [Fact]
        public async Task LoadOrCreateHandlesNonExistentTable()
        {
            FileContentTable table = await LoadOrCreateTable();
            XAssert.IsNotNull(table);
        }

        [Fact]
        public void StubFileContentTableDoesNotThrowOnQuery()
        {
            var table = FileContentTable.CreateStub(LoggingContext);

            WriteTestFiles();

            XAssert.IsNull(table.TryGetKnownContentHash(m_testFileA.ToString(m_pathTable)));
        }

        [Fact]
        public void StubFileContentTableDoesNotThrowWhenRecording()
        {
            var table = FileContentTable.CreateStub(LoggingContext);

            WriteTestFiles();

            VersionedFileIdentityAndContentInfo info = RecordContentHash(table, m_testFileA, s_hashA);
            AssertVerboseEventLogged(LogEventId.StorageFileContentTableIgnoringFileSinceVersionedFileIdentityIsNotSupported);
            XAssert.AreEqual(VersionedFileIdentity.IdentityKind.Anonymous, info.Identity.Kind);
            XAssert.IsTrue(info.Identity.IsAnonymous);
        }

        [Fact]
        public async Task StubFileContentTableDoesNotRememberHashes()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateStub(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            ExpectHashUnknown(originalTable, m_testFileA);

            FileContentTable loadedTable = await SaveAndReloadTable(originalTable);

            ExpectHashUnknown(loadedTable, m_testFileA);
        }

        [Fact]
        public void StrictModeFlushesDirtyMemoryMappedPages()
        {
            var table = FileContentTable.CreateNew(LoggingContext);

            string testFileAExpandedPath = m_testFileA.ToString(m_pathTable);

            // Writing a file via a memory mapping leaves 'dirty' pages in cache that get lazily flushed to FS.
            // We want to verify that passing strict: true flushes those pages, so that the recorded USN is stable (not invalidated due to lazy flushes).
            using (FileStream file = FileUtilities.CreateFileStream(testFileAExpandedPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Delete | FileShare.Read))
            {
                file.SetLength(1024 * 1024);
                using (var memoryMappedFile = MemoryMappedFile.CreateFromFile(
                        file,
                        mapName: null,
                        capacity: 0,
                        access: MemoryMappedFileAccess.ReadWrite,
                        inheritability: HandleInheritability.None,
                        leaveOpen: false))
                {
                    using (var accessor = memoryMappedFile.CreateViewAccessor())
                    {
                        for (int i = 0; i < 1024 * 1024; i += 4)
                        {
                            accessor.Write(i, (int)0xAB);
                        }
                    }
                }

                RecordContentHash(table, m_testFileA, s_hashA, strict: true);
            }

            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(10);
                ExpectHashKnown(table, m_testFileA, s_hashA);
            }
        }

        /// <summary>
        /// Tests that parallel pairs of (create hardlink -> record hash) agree upon the highest USN,
        /// i.e., we do not forget the hash due to storing a USN for a link other than the oldest one.
        /// (creating or deleting a link generates a new USN).
        /// </summary>
        [Fact]
        public async Task ParallelRecordForHardlinksRemembersHash()
        {
            if (!FileUtilities.IsPreciseFileVersionSupportedByEnlistmentVolume)
            {
                // TODO: Currently fails on OS that does not support precise file version.
                return;
            }

            var table = FileContentTable.CreateNew(LoggingContext);

            const int ThreadCount = 16;
            const int IterationCount = 20;
            const string OriginalFile = "Original";

            WriteFile(OriginalFile, "Data");

            CreateHardLinkStatus testLinkStatus = FileUtilities.TryCreateHardLink(GetFullPath("TestLink"), GetFullPath(OriginalFile));

            if (testLinkStatus == CreateHardLinkStatus.FailedSinceNotSupportedByFilesystem)
            {
                return;
            }

            XAssert.AreEqual(CreateHardLinkStatus.Success, testLinkStatus);

            var linkTasks = new Task[ThreadCount];
            var barrier = new Barrier(ThreadCount);
            for (int j = 0; j < IterationCount; j++)
            {
                for (int i = 0; i < ThreadCount; i++)
                {
                    int threadId = i;
                    linkTasks[threadId] = Task.Run(
                        () =>
                        {
                            string relativePath = "Link-" + threadId;
                            AbsolutePath path = GetFullPath(m_pathTable, relativePath);

                            barrier.SignalAndWait();

                            FileUtilities.DeleteFile(GetFullPath(relativePath)); 
                            CreateHardLinkStatus linkStatus = FileUtilities.TryCreateHardLink(GetFullPath(relativePath), GetFullPath(OriginalFile));
#if PLATFORM_OSX
                            // Catalina seems to have issues when doing mutli-threaded delete / link operations, let's retry several times..
                            if (linkStatus != CreateHardLinkStatus.Success)
                            {
                                var count = 0;
                                while (count < ThreadCount)
                                {
                                    FileUtilities.DeleteFile(GetFullPath(relativePath)); 
                                    linkStatus = FileUtilities.TryCreateHardLink(GetFullPath(relativePath), GetFullPath(OriginalFile));
                                    count++;
                                }
                            }
#endif
                            XAssert.AreEqual(CreateHardLinkStatus.Success, linkStatus);
                            RecordContentHash(table, path, s_hashA);
                        });
                }

                await Task.WhenAll(linkTasks);

                ExpectHashKnown(table, GetFullPath(m_pathTable, OriginalFile), s_hashA);
            }
        }

        [Fact]
        public void RecycledFileContentTableKeepsInformation()
        {
            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext);
            RecordContentHash(originalTable, m_testFileA, s_hashA);
            RecordContentHash(originalTable, m_testFileB, s_hashB);

            var newTable = FileContentTable.CreateFromTable(originalTable, LoggingContext);
            ExpectHashKnown(newTable, m_testFileA, s_hashA);
            ExpectHashKnown(newTable, m_testFileB, s_hashB);

            VerifyTable(newTable);
        }


        [Fact]
        public async Task RecycledFileContentTableHonorsNewConfiguration()
        {

            WriteTestFiles();

            var originalTable = FileContentTable.CreateNew(LoggingContext, entryTimeToLive: 20);
            RecordContentHash(originalTable, m_testFileA, s_hashA);

            var newTable = FileContentTable.CreateFromTable(originalTable, LoggingContext, newEntryTimeToLive: 2);
            RecordContentHash(newTable, m_testFileB, s_hashB);

            FileContentTable loadedTable = null;
            for (int i = 0; i < 3; i++)
            {
                // Consume the TTLs
                // On the first reload the TTL for fileA will change so it will also be consumed
                loadedTable = await SaveAndReloadTable(loadedTable ?? newTable);
            }

            ExpectHashUnknown(loadedTable, m_testFileA);
            ExpectHashUnknown(loadedTable, m_testFileB);

            VerifyTable(loadedTable);
        }

        [Fact]
        public void RecyclingFileContentTableIsTheSameAsSavingAndLoadingFromFile()
        {

            WriteTestFiles();

            var table = FileContentTable.CreateNew(LoggingContext, entryTimeToLive: 2);
            RecordContentHash(table, m_testFileA, s_hashA);                    // TTL(A) = 2

            table = FileContentTable.CreateFromTable(table, LoggingContext);   // TTL(A) = 1     
            table = FileContentTable.CreateFromTable(table, LoggingContext);   // TTL(A) = 0

            RecordContentHash(table, m_testFileB, s_hashB);                    // TTL(B) = 2

            table = FileContentTable.CreateFromTable(table, LoggingContext);
            ExpectHashUnknown(table, m_testFileA);                             // A Evicted 
            ExpectHashKnown(table, m_testFileB, s_hashB);                      // TTL(B) = 1

            VerifyTable(table);
        }

        private void ExpectHashKnown(FileContentTable table, AbsolutePath path, ContentHash hash)
        {
            VersionedFileIdentityAndContentInfo? maybeKnownHash = table.TryGetKnownContentHash(path.ToString(m_pathTable));
            XAssert.IsTrue(maybeKnownHash.HasValue, "Loaded table is missing the entry for {0}", path.ToString(m_pathTable));
            XAssert.AreEqual(hash, maybeKnownHash.Value.FileContentInfo.Hash, "Incorrect known hash for a file that should be known");
            XAssert.AreEqual(VersionedFileIdentity.IdentityKind.StrongUsn, maybeKnownHash.Value.Identity.Kind);
        }

        private void ExpectHashUnknown(FileContentTable table, AbsolutePath path)
        {
            VersionedFileIdentityAndContentInfo? maybeKnownHash = table.TryGetKnownContentHash(path.ToString(m_pathTable));
            XAssert.IsFalse(maybeKnownHash.HasValue, "Loaded table has an unexpected entry for {0}", path.ToString(m_pathTable));
        }

        private async Task<FileContentTable> SaveAndReloadTable(FileContentTable table)
        {
            await SaveTable(table);
            return await LoadTable(table.EntryTimeToLive);
        }

        private void ModifyContents(AbsolutePath file)
        {
            string expandedPath = file.ToString(m_pathTable);
            File.AppendAllText(expandedPath, "Even more text!");
        }

        private void ModifyTimestamp(AbsolutePath file)
        {
            string expandedPath = file.ToString(m_pathTable);
            File.SetLastWriteTimeUtc(expandedPath, File.GetLastWriteTimeUtc(expandedPath).AddSeconds(1));
        }

        private void SetIdenticalModificationTimestamps(AbsolutePath fileA, AbsolutePath fileB)
        {
            DateTime now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(fileA.ToString(m_pathTable), now);
            File.SetLastWriteTimeUtc(fileB.ToString(m_pathTable), now);
        }

        private void Swap(AbsolutePath a, AbsolutePath b)
        {
            string expandedA = a.ToString(m_pathTable);
            string expandedB = b.ToString(m_pathTable);
            string expandedTemp = Path.Combine(Path.GetDirectoryName(expandedA), Path.GetRandomFileName());
            File.Move(expandedA, expandedTemp);
            File.Move(expandedB, expandedA);
            File.Move(expandedTemp, expandedB);
        }

        private VersionedFileIdentityAndContentInfo RecordContentHash(FileContentTable table, AbsolutePath path, ContentHash hash, bool strict = false)
        {
            Contract.Requires(table != null);

            using (
                FileStream fs = FileUtilities.CreateFileStream(
                    path.ToString(m_pathTable),
                    FileMode.Open,
                    strict ? FileAccess.ReadWrite : FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    FileOptions.Asynchronous, true))
            {
                var info = table.RecordContentHash(fs, hash, strict: strict);
                XAssert.AreEqual(hash, info.FileContentInfo.Hash);
                XAssert.IsTrue(info.Identity.Kind == VersionedFileIdentity.IdentityKind.StrongUsn || info.Identity.Kind == VersionedFileIdentity.IdentityKind.Anonymous);
                return info;
            }
        }

        private bool HasPreciseFileVersion(AbsolutePath path) => VersionedFileIdentity.HasPreciseFileVersion(path.ToString(m_pathTable));

        private void WriteTestFiles()
        {
            WriteFile(TestFileAName, TestFileAName);
            WriteFile(TestFileBName, TestFileBName);
        }

        private Task SaveTable(FileContentTable table)
        {
            return table.SaveAsync(GetFullPath(Table));
        }

        private void TruncateTable()
        {
            using (var fs = File.OpenWrite(GetFullPath(Table)))
            {
                fs.SetLength(fs.Length - 1);
            }
        }

        private Task<FileContentTable> LoadOrCreateTable(ushort entryTimeToLive = FileContentTable.DefaultTimeToLive)
        {
            return FileContentTable.LoadOrCreateAsync(LoggingContext, GetFullPath(Table), entryTimeToLive: entryTimeToLive);
        }

        private Task<FileContentTable> LoadTable(ushort entryTimeToLive = FileContentTable.DefaultTimeToLive)
        {
            return FileContentTable.LoadAsync(LoggingContext, GetFullPath(Table), entryTimeToLive);
        }

        private static void VerifyTable(FileContentTable table)
        {
            List<FileContentTableDiagnosticExtensions.IncorrectFileContentEntry> incorrect = null;

            XAssert.IsTrue(FileContentTableAccessorFactory.TryCreate(out IFileContentTableAccessor accessor, out string error), error);

            using (accessor)
            {
                XAssert.IsNotNull(accessor);
                incorrect = table.FindIncorrectEntries(accessor);
            }

            if (incorrect.Count > 0)
            {
                string incorrectSummary = string.Join(
                    Environment.NewLine,
                    incorrect.Select(
                        i => string.Format("\tPath {0} (expected {1} ; actual {2})", i.Path, i.ExpectedHash.ToHex(), i.ActualHash.ToHex())));
                XAssert.Fail("Found incorrect file content table entires in post-validation:\n{0}", incorrectSummary);
            }
        }

        private static ContentHash CreateHash(string testFileName)
        {
            return ContentHashingUtilities.HashBytes(Encoding.UTF8.GetBytes(testFileName));
        }
    }
}
