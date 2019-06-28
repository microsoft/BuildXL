// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Storage;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using FileUtilities = BuildXL.Native.IO.FileUtilities;

namespace Test.BuildXL.Storage.Admin
{
    public sealed class UsnJournalDirectAccessUnitTests : TemporaryStorageTestBase
    {

        [FactIfSupported(requiresAdmin:true, requiresWindowsBasedOperatingSystem: true)]
        public void QueryJournal()
        {
            WithVolumeHandle(
                volumeHandle =>
                {
                    using (FileStream file = File.Create(GetFullPath("File")))
                    {
                        QueryUsnJournalData journalState = QueryJournal(volumeHandle);

                        Usn usn = FileUtilities.ReadFileUsnByHandle(file.SafeFileHandle).Value.Usn;
                        XAssert.IsTrue(journalState.LowestValidUsn <= usn);
                        XAssert.IsTrue(journalState.NextUsn > usn);
                    }
                });
        }

        [FactIfSupported(requiresAdmin: true, requiresWindowsBasedOperatingSystem: true)]
        public void ReadEarliestUsnRecords()
        {
            WithVolumeHandle(
                volumeHandle =>
                {
                    QueryUsnJournalData journalState = QueryJournal(volumeHandle);

                    byte[] buffer = new byte[4096];
                    ReadUsnJournalResult readJournalResult = FileUtilities.TryReadUsnJournal(volumeHandle, buffer, journalState.UsnJournalId, startUsn: new Usn(0));
                    XAssert.AreEqual(ReadUsnJournalStatus.Success, readJournalResult.Status);

                    XAssert.IsFalse(readJournalResult.NextUsn.IsZero);
                    XAssert.IsTrue(readJournalResult.NextUsn >= journalState.FirstUsn);

                    XAssert.AreNotEqual(0, readJournalResult.Records.Count, "It is unlikely that this journal should be empty, since this test's execution has written to the volume.");

                    var firstRecord = readJournalResult.Records.First();

                    XAssert.IsTrue(firstRecord.Usn == journalState.FirstUsn);
                    XAssert.IsTrue(firstRecord.Usn < readJournalResult.NextUsn);

                    var lastUsn = firstRecord.Usn;

                    foreach (UsnRecord record in readJournalResult.Records.Skip(1))
                    {
                        XAssert.IsTrue(record.Usn > lastUsn, "Expected USNs to be monotically increasing.");
                        lastUsn = record.Usn;

                        XAssert.IsTrue(record.Usn >= journalState.FirstUsn);
                        XAssert.IsTrue(record.Usn < readJournalResult.NextUsn);
                    }
                });
        }

        [FactIfSupported(requiresAdmin: true, requiresWindowsBasedOperatingSystem: true)]
        public void ReadToEnd()
        {
            WithVolumeHandle(
                volumeHandle =>
                {
                    byte[] buffer = new byte[4096];

                    while (true)
                    {
                        QueryUsnJournalData journalState = QueryJournal(volumeHandle);

                        ReadUsnJournalResult readJournalResult = FileUtilities.TryReadUsnJournal(
                            volumeHandle,
                            buffer,
                            journalState.UsnJournalId,
                            startUsn: journalState.NextUsn);
                        XAssert.AreEqual(ReadUsnJournalStatus.Success, readJournalResult.Status);

                        XAssert.IsFalse(readJournalResult.NextUsn.IsZero);
                        XAssert.IsTrue(readJournalResult.NextUsn >= journalState.NextUsn);

                        if (readJournalResult.Records.Count == 0)
                        {
                            break;
                        }
                    }
                });
        }

        [FactIfSupported(requiresAdmin: true, requiresWindowsBasedOperatingSystem: true)]
        public void ReadChangesToFile()
        {
            string path = GetFullPath("File");

            WithVolumeHandle(
                volumeHandle =>
                {
                    Usn initialUsn = QueryJournal(volumeHandle).NextUsn;

                    FileId fileId;
                    using (FileStream file = File.Create(path))
                    {
                        fileId = FileUtilities.ReadFileUsnByHandle(file.SafeFileHandle).Value.FileId;
                        file.WriteByte(1);
                    }

                    const UsnChangeReasons ExpectedCreationChangeReasons =
                        UsnChangeReasons.FileCreate |
                        UsnChangeReasons.DataExtend |
                        UsnChangeReasons.Close;

                    Usn nextUsn;
                    ExpectChangesSinceUsn(ExpectedCreationChangeReasons, volumeHandle, initialUsn, fileId, out nextUsn);

                    using (FileStream file = File.OpenWrite(path))
                    {
                        file.WriteByte(1);
                    }

                    File.SetAttributes(path, FileAttributes.Normal | FileAttributes.ReadOnly);

                    const UsnChangeReasons ExpectedModificationChangeReasons =
                        UsnChangeReasons.DataOverwrite |
                        UsnChangeReasons.BasicInfoChange |
                        UsnChangeReasons.Close;

                    ExpectChangesSinceUsn(ExpectedModificationChangeReasons, volumeHandle, nextUsn, fileId, out nextUsn);
                });
        }

        private void ExpectChangesSinceUsn(
            UsnChangeReasons expectedChangeReasons, 
            SafeFileHandle volumeHandle, 
            Usn startUsn, 
            FileId fileId, 
            out Usn nextUsn, 
            TimeSpan? timeLimit = default(TimeSpan?))
        {
            const int DefaultTimeLimitForScanningInSecond = 30; // 30 sec for scanning.

            QueryUsnJournalData journalState = QueryJournal(volumeHandle);
            byte[] buffer = new byte[64 * 1024]; // 655 records per read.
            timeLimit = timeLimit.HasValue ? timeLimit : TimeSpan.FromSeconds(DefaultTimeLimitForScanningInSecond);
            var stopWatch = System.Diagnostics.Stopwatch.StartNew();
            UsnChangeReasons foundChangeReasons = 0;
            nextUsn = startUsn;

            while (true)
            {
                if (stopWatch.ElapsedTicks > timeLimit.Value.Ticks)
                {
                    break;
                }

                ReadUsnJournalResult result = FileUtilities.TryReadUsnJournal(
                    volumeHandle,
                    buffer,
                    journalState.UsnJournalId,
                    startUsn);

                nextUsn = result.NextUsn;

                if (!result.Succeeded)
                {
                    break;
                }

                if (result.Records.Count == 0)
                {
                    break;
                }

                foundChangeReasons |= UsnJournalUtilities.GetAggregateChangeReasons(fileId, result.Records);

                if (expectedChangeReasons == (foundChangeReasons & expectedChangeReasons))
                {
                    // Found all expected change reasons.
                    return;
                }

                startUsn = result.NextUsn;
            }

            XAssert.AreEqual(expectedChangeReasons, foundChangeReasons & expectedChangeReasons);
        }

        private ReadUsnJournalResult ReadChangesSinceUsn(SafeFileHandle volumeHandle, Usn startUsn)
        {
            QueryUsnJournalData journalState = QueryJournal(volumeHandle);

            // TODO: On a busy volume, we may need to read up to a particular expected USN - or we will fill up the single buffer before finding the expected records.
            byte[] buffer = new byte[32768];
            ReadUsnJournalResult readJournalResult = FileUtilities.TryReadUsnJournal(volumeHandle, buffer, journalState.UsnJournalId, startUsn: startUsn);
            XAssert.AreEqual(ReadUsnJournalStatus.Success, readJournalResult.Status);

            return readJournalResult;
        }

        private QueryUsnJournalData QueryJournal(SafeFileHandle volumeHandle)
        {
            QueryUsnJournalResult queryResult = FileUtilities.TryQueryUsnJournal(volumeHandle);
            XAssert.AreEqual(
                QueryUsnJournalStatus.Success,
                queryResult.Status,
                "Failed to query the volume's change journal. Is it enabled on volume of the test temporary directory '" + TemporaryDirectory + "'?");
            XAssert.IsTrue(queryResult.Succeeded);

            return queryResult.Data;
        }

        private void WithVolumeHandle(Action<SafeFileHandle> action)
        {
            VolumeMap map = JournalUtils.TryCreateMapOfAllLocalVolumes(new LoggingContext("Dummy", "Dummy"));
            XAssert.IsNotNull(map, "Failed to create a volume map");

            using (VolumeAccessor volumeAccessor = map.CreateVolumeAccessor())
            {
                SafeFileHandle directoryHandle;
                var directoryOpenResult = FileUtilities.TryOpenDirectory(
                    TemporaryDirectory,
                    FileShare.ReadWrite | FileShare.Delete,
                    out directoryHandle);
                using (directoryHandle)
                {
                    XAssert.IsTrue(directoryOpenResult.Succeeded, "Failed to open the temporary directory to query its volume membership");

                    SafeFileHandle volumeHandle = volumeAccessor.TryGetVolumeHandle(directoryHandle);
                    XAssert.IsNotNull(volumeHandle, "Failed to open a volume handle");

                    action(volumeHandle);
                }
            }
        }
    }
}
