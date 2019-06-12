// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;

namespace Test.BuildXL.Storage.Admin
{
    /// <summary>
    /// Unit tests for <see cref="FileContentTable" /> with tracker.
    /// </summary>
    public class FileContentTableWithTrackerTests : TemporaryStorageTestBase
    {
        private readonly PathTable m_pathTable;

        public FileContentTableWithTrackerTests()
        {
            m_pathTable = new PathTable();
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true, requiresJournalScan: true)]
        public void TestNoChange()
        {
            var fileF = WriteFile("F");
            var support = ChangeTrackerSupport.Initialize(this);

            var versionedF1 = support.RecordHashAndTrackFile(fileF);

            support.SaveReloadAndScanForChanges();

            var versionedF2 = support.GetKnownContentHashes(fileF);
            XAssert.IsTrue(versionedF2.HasValue);
            XAssert.AreEqual(versionedF1.FileContentInfo.Hash, versionedF2.Value.FileContentInfo.Hash);
            XAssert.AreEqual(versionedF1.Identity.Usn, versionedF2.Value.Identity.Usn);
        }


        [FactIfSupported(requiresWindowsBasedOperatingSystem: true, requiresJournalScan: true)]
        public void TestDataChange()
        {
            var fileF = WriteFile("F");

            var support = ChangeTrackerSupport.Initialize(this);
            support.RecordHashAndTrackFile(fileF);

            ModifyContents(fileF);

            support.SaveReloadAndScanForChanges();

            var versionedF2 = support.GetKnownContentHashes(fileF);
            XAssert.IsFalse(versionedF2.HasValue);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true, requiresJournalScan: true)]
        public void TestTimestampChange()
        {
            var fileF = WriteFile("F");

            var support = ChangeTrackerSupport.Initialize(this);
            var versionedF1 = support.RecordHashAndTrackFile(fileF);

            ModifyTimestamp(fileF);

            support.SaveReloadAndScanForChanges();
            
            var versionedF2 = support.GetKnownContentHashes(fileF);
            XAssert.IsTrue(versionedF2.HasValue);
            XAssert.AreEqual(versionedF1.FileContentInfo.Hash, versionedF2.Value.FileContentInfo.Hash);
            XAssert.IsTrue(versionedF1.Identity.Usn < versionedF2.Value.Identity.Usn);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true, requiresJournalScan: true)]
        public void TestCreateHardLinkChange()
        {
            var fileF = WriteFile("F");

            var support = ChangeTrackerSupport.Initialize(this);
            var versionedF1 = support.RecordHashAndTrackFile(fileF);

            CreateHardLink("G", fileF);

            support.SaveReloadAndScanForChanges();

            var versionedF2 = support.GetKnownContentHashes(fileF);
            XAssert.IsTrue(versionedF2.HasValue);
            XAssert.AreEqual(versionedF1.FileContentInfo.Hash, versionedF2.Value.FileContentInfo.Hash);
            XAssert.IsTrue(versionedF1.Identity.Usn < versionedF2.Value.Identity.Usn);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true, requiresJournalScan: true)]
        public void TestDeleteHardLinkChange()
        {
            var fileF = WriteFile("F");
            var fileG = CreateHardLink("G", fileF);

            var support = ChangeTrackerSupport.Initialize(this);
            var versionedF1 = support.RecordHashAndTrackFile(fileF);

            DeleteFile(fileG);

            support.SaveReloadAndScanForChanges();

            var versionedF2 = support.GetKnownContentHashes(fileF);
            XAssert.IsTrue(versionedF2.HasValue);
            XAssert.AreEqual(versionedF1.FileContentInfo.Hash, versionedF2.Value.FileContentInfo.Hash);
            XAssert.IsTrue(versionedF1.Identity.Usn < versionedF2.Value.Identity.Usn);
        }

        private class ChangeTrackerSupport
        {
            private const string FileContentTableName = nameof(FileContentTable);
            private const string FileChangeTrackerName = nameof(FileChangeTracker);
            private readonly AbsolutePath m_fileChangeTrackerPath;
            private readonly AbsolutePath m_fileContentTablePath;
            private readonly IChangeJournalAccessor m_journal;
            private readonly LoggingContext m_loggingContext;
            private readonly VolumeMap m_volumeMap;
            private readonly PathTable m_pathTable;
            private FileChangeTracker m_fileChangeTracker;
            private FileContentTable m_fileContentTable;
            private readonly string m_buildEngineFingerprint;

            private ChangeTrackerSupport(
                LoggingContext loggingContext,
                PathTable pathTable,
                FileChangeTracker fileChangeTracker,
                FileContentTable fileContentTable,
                VolumeMap volumeMap,
                IChangeJournalAccessor journal,
                AbsolutePath temporaryDirectory,
                string buildEngineFingerprint)
            {
                Contract.Requires(loggingContext != null);
                Contract.Requires(pathTable != null);
                Contract.Requires(fileChangeTracker != null);
                Contract.Requires(fileContentTable != null);
                Contract.Requires(temporaryDirectory.IsValid);

                m_loggingContext = loggingContext;
                m_pathTable = pathTable;
                m_fileChangeTracker = fileChangeTracker;
                m_fileContentTable = fileContentTable;
                m_volumeMap = volumeMap;
                m_journal = journal;
                m_fileContentTablePath = temporaryDirectory.Combine(m_pathTable, FileContentTableName);
                m_fileChangeTrackerPath = temporaryDirectory.Combine(m_pathTable, FileChangeTrackerName);
                m_buildEngineFingerprint = buildEngineFingerprint;
            }

            public CounterCollection<FileContentTableCounters> FileContentTableCounters => m_fileContentTable.Counters;

            public static ChangeTrackerSupport Initialize(FileContentTableWithTrackerTests test)
            {
                var loggingContext = new LoggingContext("Dummy", "Dummy");
                var fileContentTable = FileContentTable.CreateNew();

                VolumeMap volumeMap = JournalUtils.TryCreateMapOfAllLocalVolumes(loggingContext);
                XAssert.IsNotNull(volumeMap);

                var maybeJournal = JournalUtils.TryGetJournalAccessorForTest(volumeMap);
                XAssert.IsTrue(maybeJournal.Succeeded, "Could not connect to journal");

                var fileChangeTracker = FileChangeTracker.StartTrackingChanges(loggingContext, volumeMap, maybeJournal.Result, null);

                return new ChangeTrackerSupport(
                    loggingContext,
                    test.m_pathTable,
                    fileChangeTracker,
                    fileContentTable,
                    volumeMap,
                    maybeJournal.Result,
                    AbsolutePath.Create(test.m_pathTable, test.TemporaryDirectory),
                    null);
            }

            private void Save()
            {
                FileEnvelopeId fileEnvelopeId = m_fileChangeTracker.GetFileEnvelopeToSaveWith();
                m_fileChangeTracker.SaveTrackingStateIfChanged(m_fileChangeTrackerPath.ToString(m_pathTable), fileEnvelopeId);
                m_fileContentTable.SaveAsync(m_fileContentTablePath.ToString(m_pathTable)).Wait();
            }

            private void Load()
            {
                var loadingTrackerResult = FileChangeTracker.ResumeOrRestartTrackingChanges(
                    m_loggingContext,
                    m_volumeMap,
                    m_journal,
                    m_fileChangeTrackerPath.ToString(m_pathTable),
                    m_buildEngineFingerprint,
                    out m_fileChangeTracker);
                XAssert.IsTrue(loadingTrackerResult.Succeeded);

                m_fileContentTable = FileContentTable.LoadAsync(m_fileContentTablePath.ToString(m_pathTable)).Result;
            }

            public VersionedFileIdentityAndContentInfo RecordHashAndTrackFile(AbsolutePath path)
            {
                Contract.Requires(path.IsValid);

                string expandedPath = path.ToString(m_pathTable);

                using (
                    FileStream fs = File.Open(
                        expandedPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete))
                {
                    var hash = ContentHashingUtilities.HashContentStream(fs);
                    var versionedFile = m_fileContentTable.RecordContentHash(fs, hash);
                    // ReSharper disable once AssignNullToNotNullAttribute
                    m_fileChangeTracker.TryTrackChangesToFile(fs.SafeFileHandle, expandedPath, versionedFile.Identity, TrackingUpdateMode.Supersede);
                    return versionedFile;
                }
            }

            private void SaveAndReload()
            {
                Save();
                Load();
            }

            private void ScanForChanges()
            {
                var fileChangeProcessor = new FileChangeProcessor(m_loggingContext, m_fileChangeTracker);
                fileChangeProcessor.Subscribe(m_fileContentTable);
                var result = fileChangeProcessor.TryProcessChanges();
                XAssert.IsTrue(result.Succeeded);
            }

            public void SaveReloadAndScanForChanges()
            {
                SaveAndReload();
                EnsureTrackerIsTracking();
                ScanForChanges();
            }

            public VersionedFileIdentityAndContentInfo? GetKnownContentHashes(AbsolutePath path)
            {
                return m_fileContentTable.TryGetKnownContentHash(path.ToString(m_pathTable));
            }

            private void EnsureTrackerIsTracking()
            {
                XAssert.IsTrue(m_fileChangeTracker.IsTrackingChanges);
            }            
        }

        public new AbsolutePath WriteFile(string relativePath, string content = null)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(relativePath));

            var fullPath = base.WriteFile(relativePath, content ?? Guid.NewGuid().ToString());
            return AbsolutePath.Create(m_pathTable, fullPath);
        }

        public void ModifyContents(AbsolutePath file, string content = null)
        {
            Contract.Requires(file.IsValid);

            string expandedPath = file.ToString(m_pathTable);
            File.AppendAllText(expandedPath, content ?? Guid.NewGuid().ToString());
        }

        private void ModifyTimestamp(AbsolutePath file)
        {
            Contract.Requires(file.IsValid);

            string expandedPath = file.ToString(m_pathTable);
            File.SetLastWriteTimeUtc(expandedPath, File.GetLastWriteTimeUtc(expandedPath).AddSeconds(1));
        }

        private void DeleteFile(AbsolutePath file)
        {
            Contract.Requires(file.IsValid);

            File.Delete(file.ToString(m_pathTable));
        }

        private AbsolutePath CreateHardLink(string relativePath, AbsolutePath file)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(relativePath));
            Contract.Requires(file.IsValid);

            var fullPath = GetFullPath(relativePath);
            var status = FileUtilities.TryCreateHardLink(fullPath, file.ToString(m_pathTable));
            XAssert.AreEqual(CreateHardLinkStatus.Success, status);

            return AbsolutePath.Create(m_pathTable, fullPath);
        }
    }
}
