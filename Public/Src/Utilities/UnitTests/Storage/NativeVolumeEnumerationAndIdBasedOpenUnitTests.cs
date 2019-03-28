// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using FileUtilities = BuildXL.Native.IO.FileUtilities;

namespace Test.BuildXL.Storage
{
    /// <summary>
    /// Tests for enumerating volumes, accessing files by ID (relative to a volume-root handle), etc.
    /// </summary>
    [Trait("Category", "WindowsOSOnly")]
    public sealed class NativeVolumeEnumerationAndIdBasedOpenUnitTests : TemporaryStorageTestBase
    {
        [Fact]
        public void CanOpenDirectoryAndGetShortSerial()
        {
            XAssert.IsTrue(Directory.Exists(TemporaryDirectory));

            SafeFileHandle directoryHandle = OpenTemporaryDirectory();

            using (directoryHandle)
            {
                uint serial = FileUtilities.GetShortVolumeSerialNumberByHandle(directoryHandle);
                XAssert.AreNotEqual(0, serial, "A volume serial of zero is very unlikely");
            }
        }

        [Fact]
        public void DirectoryAndFileSerialsAgree()
        {
            XAssert.IsTrue(Directory.Exists(TemporaryDirectory));

            SafeFileHandle directoryHandle = OpenTemporaryDirectory();

            using (directoryHandle)
            {
                using (FileStream fileStream = File.Create(GetFullPath("Junk")))
                {
                    uint directoryShortSerial = FileUtilities.GetShortVolumeSerialNumberByHandle(directoryHandle);
                    XAssert.AreNotEqual(0, directoryShortSerial, "A volume serial of zero (volume handle) is very unlikely");

                    uint fileShortSerial = FileUtilities.GetShortVolumeSerialNumberByHandle(fileStream.SafeFileHandle);
                    XAssert.AreNotEqual(0, fileShortSerial, "File reports a volume short serial of zero (unlikely), and this disagrees with the volume serial");
                    XAssert.AreEqual(directoryShortSerial, fileShortSerial, "File and volume short serials disagree");

                    ulong directorySerial = FileUtilities.GetVolumeSerialNumberByHandle(directoryHandle);
                    XAssert.AreEqual(directoryShortSerial, unchecked((uint)directorySerial), "Directory long and short serials disagree");

                    ulong fileSerial = FileUtilities.GetVolumeSerialNumberByHandle(fileStream.SafeFileHandle);
                    XAssert.AreEqual(fileShortSerial, unchecked((uint)fileSerial), "File long and short serials disagree");
                }
            }
        }

        [Fact]
        public void VolumeEnumerationFindsKnownVolumeSerial()
        {
            XAssert.IsTrue(Directory.Exists(TemporaryDirectory));

            List<Tuple<VolumeGuidPath, ulong>> volumeInfo = FileUtilities.ListVolumeGuidPathsAndSerials();
            XAssert.IsTrue(volumeInfo.Count > 0);

            SafeFileHandle directoryHandle = OpenTemporaryDirectory();

            ulong serial;
            using (directoryHandle)
            {
                serial = FileUtilities.GetVolumeSerialNumberByHandle(directoryHandle);
                XAssert.AreNotEqual(0, serial, "A volume serial of zero is very unlikely");
            }

            List<VolumeGuidPath> matchingVolumePaths = volumeInfo.Where(t => t.Item2 == serial).Select(t => t.Item1).ToList();
            XAssert.AreNotEqual(0, matchingVolumePaths.Count, "No volumes were found matching the serial {0:X}", serial);
            XAssert.AreEqual(1, matchingVolumePaths.Count, "Serial collision for {0:X}", serial);
        }

        [Fact]
        public void VolumeEnumerationReturnsUniqueGuidPaths()
        {
            List<Tuple<VolumeGuidPath, ulong>> volumeInfo = FileUtilities.ListVolumeGuidPathsAndSerials();
            XAssert.IsTrue(volumeInfo.Count > 0);

            var guidPaths = new HashSet<VolumeGuidPath>();
            foreach (Tuple<VolumeGuidPath, ulong> t in volumeInfo)
            {
                bool added = guidPaths.Add(t.Item1);
                XAssert.IsTrue(added, "Duplicate guid path: {0}", t.Item1);
            }
        }

        [Fact]
        public void CanOpenFileByIdViaDirectoryHandle()
        {
            XAssert.IsTrue(Directory.Exists(TemporaryDirectory));

            SafeFileHandle directoryHandle = OpenTemporaryDirectory();

            string junkPath = GetFullPath("Junk");

            using (directoryHandle)
            {
                FileId fileId;
                using (FileStream fileStream = File.Create(junkPath))
                {
                    fileStream.WriteByte(0xFF);
                    fileId = FileUtilities.ReadFileUsnByHandle(fileStream.SafeFileHandle).Value.FileId;

                    XAssert.AreEqual(
                        FileUtilities.GetVolumeSerialNumberByHandle(directoryHandle),
                        FileUtilities.GetVolumeSerialNumberByHandle(fileStream.SafeFileHandle));
                }

                XAssert.IsTrue(File.Exists(junkPath));

                SafeFileHandle handleFromId;
                var openByIdResult = FileUtilities.TryOpenFileById(
                    directoryHandle,
                    fileId,
                    FileDesiredAccess.GenericRead,
                    FileShare.Read | FileShare.Delete,
                    FileFlagsAndAttributes.None,
                    out handleFromId);
                using (handleFromId)
                {
                    XAssert.IsTrue(openByIdResult.Succeeded);

                    using (var fileStream = new FileStream(handleFromId, FileAccess.Read))
                    {
                        XAssert.AreEqual(0xFF, fileStream.ReadByte(), "Wrong contents for file opened by ID");
                    }
                }
            }
        }

        [Fact]
        public void CannotOpenDeletedFileById()
        {
            XAssert.IsTrue(Directory.Exists(TemporaryDirectory));

            SafeFileHandle directoryHandle = OpenTemporaryDirectory();

            string junkPath = GetFullPath("Junk");

            using (directoryHandle)
            {
                FileId fileId;
                using (FileStream fileStream = File.Create(junkPath))
                {
                    fileStream.WriteByte(0xFF);
                    fileId = FileUtilities.ReadFileUsnByHandle(fileStream.SafeFileHandle).Value.FileId;

                    XAssert.AreEqual(
                        FileUtilities.GetVolumeSerialNumberByHandle(directoryHandle),
                        FileUtilities.GetVolumeSerialNumberByHandle(fileStream.SafeFileHandle));
                }

                File.Delete(junkPath);
                XAssert.IsFalse(File.Exists(junkPath));

                SafeFileHandle handleFromId;
                var openByIdResult = FileUtilities.TryOpenFileById(
                    directoryHandle,
                    fileId,
                    FileDesiredAccess.GenericRead,
                    FileShare.Read | FileShare.Delete,
                    FileFlagsAndAttributes.None,
                    out handleFromId);
                using (handleFromId)
                {
                    XAssert.IsFalse(openByIdResult.Succeeded, "Somehow opened a deleted file?");
                    XAssert.AreEqual(openByIdResult.Status, OpenFileStatus.FileNotFound);
                }
            }
        }

        [Fact]
        public void CanGetFullGuidPathFromHandle()
        {
            List<Tuple<VolumeGuidPath, ulong>> volumePathsAndSerials = FileUtilities.ListVolumeGuidPathsAndSerials();

            using (SafeFileHandle directoryHandle = OpenTemporaryDirectory())
            {
                string directoryHandlePath = FileUtilities.GetFinalPathNameByHandle(directoryHandle, volumeGuidPath: true);

                XAssert.IsFalse(string.IsNullOrEmpty(directoryHandlePath));
                XAssert.IsTrue(directoryHandlePath.StartsWith(@"\\?\"), "GUID paths must start with a long-path prefix");
                XAssert.IsTrue(directoryHandlePath.EndsWith(Path.GetFileName(TemporaryDirectory)), "GUID path must end in the directory name (even if there are symlinks, mounted volumes, etc.)");

                ulong serial = FileUtilities.GetVolumeSerialNumberByHandle(directoryHandle);
                VolumeGuidPath volumeGuidPath = volumePathsAndSerials.Single(t => t.Item2 == serial).Item1;

                XAssert.IsTrue(directoryHandlePath.ToUpperInvariant().StartsWith(volumeGuidPath.Path.ToUpperInvariant()), "GUID path fo the volume should be a prefix of the path to the directory");
            }
        }

        [Fact]
        public void CanGetDosStylePathFromHandle()
        {
            using (SafeFileHandle directoryHandle = OpenTemporaryDirectory())
            {
                string directoryHandlePath = FileUtilities.GetFinalPathNameByHandle(directoryHandle, volumeGuidPath: false);

                XAssert.IsFalse(string.IsNullOrEmpty(directoryHandlePath));
                XAssert.IsTrue(directoryHandlePath.EndsWith(Path.GetFileName(TemporaryDirectory)), "Path must end in the directory name (even if there are symlinks, mounted volumes, etc.)");
                XAssert.IsFalse(directoryHandlePath.StartsWith(@"\\?\"), "DOS-style paths must not start with a long-path prefix (and it is doubtful the volume with this test directory is unmounted)");
            }
        }

        private SafeFileHandle OpenTemporaryDirectory()
        {
            SafeFileHandle directoryHandle;
            var directoryOpenResult = FileUtilities.TryOpenDirectory(TemporaryDirectory, FileShare.Read | FileShare.Write | FileShare.Delete, out directoryHandle);
            XAssert.IsTrue(directoryOpenResult.Succeeded);
            return directoryHandle;
        }
    }
}
