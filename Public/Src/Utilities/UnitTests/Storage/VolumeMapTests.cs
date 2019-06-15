// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
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

namespace Test.BuildXL.Storage
{
    /// <summary>
    /// Tests for <see cref="VolumeMap"/>
    /// </summary>
     [Trait("Category", "WindowsOSOnly")]
    public class VolumeMapTests : TemporaryStorageTestBase
    {
        [Fact]
        public void CanGetVolumePath()
        {
            VolumeMap map = CreateMapOfAllLocalVolumes();

            ulong thisVolumeSerial;
            SafeFileHandle directoryHandle;
            FileUtilities.TryOpenDirectory(TemporaryDirectory, FileShare.ReadWrite | FileShare.Delete, out directoryHandle);
            using (directoryHandle)
            {
                XAssert.IsNotNull(directoryHandle, "Failed to open the TemporaryDirectory, which we just created");
                thisVolumeSerial = FileUtilities.GetVolumeSerialNumberByHandle(directoryHandle);
            }

            VolumeGuidPath volumePath = map.TryGetVolumePathBySerial(thisVolumeSerial);
            XAssert.IsTrue(volumePath.IsValid);

            var openResult = FileUtilities.TryOpenDirectory(volumePath.Path, FileShare.ReadWrite | FileShare.Delete, out directoryHandle);
            using (directoryHandle)
            {
                XAssert.IsTrue(openResult.Succeeded, "Failed to open the volume root (but we didn't ask for any access)");
                XAssert.IsTrue(openResult.OpenedOrTruncatedExistingFile);
                XAssert.IsNotNull(directoryHandle);
                XAssert.AreEqual(thisVolumeSerial, FileUtilities.GetVolumeSerialNumberByHandle(directoryHandle), "The volume root path has the wrong volume serial");
            }
        }

        [Fact]
        public void CannotGetVolumePathForSerialThatDoesNotExist()
        {
            var serials = new HashSet<ulong>(FileUtilities.ListVolumeGuidPathsAndSerials().Select(t => t.Item2));
            ulong uniqueSerial = serials.First();
            do
            {
                uniqueSerial++;
            }
            while (serials.Contains(uniqueSerial));

            VolumeMap map = CreateMapOfAllLocalVolumes();
            XAssert.IsFalse(map.TryGetVolumePathBySerial(uniqueSerial).IsValid, "Found a path for a serial that seemed to not exist");
        }

        [Fact]
        public void CanAccessMultipleFilesById()
        {
            var fileIdAndVolumeSerialPairs = new List<Tuple<FileId, ulong>>();
            for (int i = 0; i < 3; i++)
            {
                string path = GetFullPath("F" + i);
                using (FileStream fs = File.Create(path))
                {
                    fs.WriteByte((byte)i);

                    ulong volumeSerial = FileUtilities.GetVolumeSerialNumberByHandle(fs.SafeFileHandle);
                    FileId fileId = FileUtilities.ReadFileUsnByHandle(fs.SafeFileHandle).Value.FileId;
                    fileIdAndVolumeSerialPairs.Add(Tuple.Create(fileId, volumeSerial));
                }
            }

            VolumeMap map = CreateMapOfAllLocalVolumes();
            using (FileAccessor accessor = map.CreateFileAccessor())
            {
                for (int i = 0; i < fileIdAndVolumeSerialPairs.Count; i++)
                {
                    Tuple<FileId, ulong> fileIdAndVolumeSerial = fileIdAndVolumeSerialPairs[i];

                    SafeFileHandle handle;
                    FileAccessor.OpenFileByIdResult openResult = accessor.TryOpenFileById(
                        fileIdAndVolumeSerial.Item2,
                        fileIdAndVolumeSerial.Item1,
                        FileDesiredAccess.GenericRead,
                        FileShare.ReadWrite,
                        FileFlagsAndAttributes.None,
                        out handle);
                    using (handle)
                    {
                        XAssert.AreEqual(openResult, FileAccessor.OpenFileByIdResult.Succeeded);
                        XAssert.IsNotNull(handle);

                        using (var fileStream = new FileStream(handle, FileAccess.Read))
                        {
                            XAssert.AreEqual(i, fileStream.ReadByte());
                        }
                    }
                }
            }
        }

        [Fact]
        public void CannotAccessDeletedFile()
        {
            ulong volumeSerial;
            FileId fileId;
            string path = GetFullPath("F");
            using (FileStream fs = File.Create(path))
            {
                volumeSerial = FileUtilities.GetVolumeSerialNumberByHandle(fs.SafeFileHandle);
                fileId = FileUtilities.ReadFileUsnByHandle(fs.SafeFileHandle).Value.FileId;
            }

            File.Delete(path);

            VolumeMap map = CreateMapOfAllLocalVolumes();
            using (FileAccessor accessor = map.CreateFileAccessor())
            {
                SafeFileHandle handle;
                FileAccessor.OpenFileByIdResult openResult = accessor.TryOpenFileById(
                    volumeSerial,
                    fileId,
                    FileDesiredAccess.GenericRead,
                    FileShare.ReadWrite,
                    FileFlagsAndAttributes.None,
                    out handle);
                using (handle)
                {
                    XAssert.AreEqual(FileAccessor.OpenFileByIdResult.FailedToFindFile, openResult);
                    XAssert.IsNull(handle);
                }
            }
        }

        private static VolumeMap CreateMapOfAllLocalVolumes()
        {
            VolumeMap map = JournalUtils.TryCreateMapOfAllLocalVolumes(new LoggingContext("Dummy", "Dummy"));
            XAssert.IsNotNull(map);
            return map;
        }

        [Fact]
        public void OverlappingJunctionRoots()
        {
            List<string> junctionRoots = new List<string>() { @"c:\windows", @"c:\windows" };
            VolumeMap map = JournalUtils.TryCreateMapOfAllLocalVolumes(new LoggingContext("Dummy", "Dummy"), junctionRoots);
        }
    }
}
