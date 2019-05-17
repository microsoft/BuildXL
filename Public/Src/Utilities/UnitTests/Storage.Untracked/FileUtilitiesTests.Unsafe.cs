// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static BuildXL.Native.IO.Windows.FileUtilitiesWin;
using FileUtilities = BuildXL.Native.IO.FileUtilities;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Storage
{
    [Trait("Category", "WindowsOSOnly")]
    public sealed class FileUtilitiesUnsafeTests : TemporaryStorageTestBase
    {
        public FileUtilitiesUnsafeTests()
        {
            RegisterEventSource(global::BuildXL.Native.ETWLogger.Log);
        }

        [Fact]
        public void DeleteDirectoryContentsHandleOpen()
        {
            string directory = Path.Combine(TemporaryDirectory, "directoryToDelete");
            Directory.CreateDirectory(directory);
            string openFile = Path.Combine(directory, "openfileDelDirContents.txt");
            using (Stream s = new FileStream(openFile, FileMode.Create))
            {
                Exception exception = null;
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);
                try
                {
                    FileUtilities.DeleteDirectoryContents(directory);
                }
                catch (BuildXLException ex)
                {
                    exception = ex;
                }

                XAssert.IsNotNull(exception, "Expected failure since a handle to a contained file was still open");
                XAssert.IsTrue(exception.Message.Contains(FileUtilitiesMessages.FileDeleteFailed + NormalizeDirectoryPath(openFile)), exception.Message);
                XAssert.IsTrue(exception.Message.Contains("Handle was used by"), exception.Message);
            }

            // Try again with the handle closed. There should be no crash.
            FileUtilities.DeleteDirectoryContents(directory);

            // Clean up files so we don't leave undeletable files on disk
            FileUtilities.DeleteDirectoryContents(directory, deleteRootDirectory: true);
        }


        [Fact]
        public void CreateReplacementFileRecreatesWhenDenyWriteACLPresent()
        {
            const string Target = @"Target";

            FileId originalId;
            using (FileStream original = File.Create(GetFullPath(Target)))
            {
                originalId = FileUtilities.ReadFileUsnByHandle(original.SafeFileHandle).Value.FileId;
            }

            AddDenyWriteACL(GetFullPath(Target));

            using (FileStream fs = FileUtilities.CreateReplacementFile(GetFullPath(Target), FileShare.Read | FileShare.Delete))
            {
                XAssert.IsNotNull(fs);
                XAssert.AreNotEqual(originalId, FileUtilities.ReadFileUsnByHandle(fs.SafeFileHandle).Value.FileId, "File was truncated rather than replaced");
                XAssert.AreEqual(0, fs.Length);
                fs.WriteByte(1);
            }
        }

        [Fact]
        public void CreateReplacementFileRecreatesAfterRemovingReadonlyFlag()
        {
            const string Target = @"Target";

            FileId originalId;
            using (FileStream original = File.Create(GetFullPath(Target)))
            {
                originalId = FileUtilities.ReadFileUsnByHandle(original.SafeFileHandle).Value.FileId;
            }

            SetReadonlyFlag(GetFullPath(Target));

            using (FileStream fs = FileUtilities.CreateReplacementFile(GetFullPath(Target), FileShare.Read | FileShare.Delete))
            {
                XAssert.IsNotNull(fs);
                XAssert.AreNotEqual(originalId, FileUtilities.ReadFileUsnByHandle(fs.SafeFileHandle).Value.FileId, "File was truncated rather than replaced");
                XAssert.AreEqual(0, fs.Length);
                fs.WriteByte(1);
            }
        }

        [Fact]
        public void CreateReplacementFileReplacesAfterRemovingReadonlyFlagIfDenyWriteACLPresent()
        {
            const string Target = @"Target";

            FileId originalId;
            using (FileStream original = File.Create(GetFullPath(Target)))
            {
                originalId = FileUtilities.ReadFileUsnByHandle(original.SafeFileHandle).Value.FileId;
            }

            SetReadonlyFlag(GetFullPath(Target));
            AddDenyWriteACL(GetFullPath(Target));

            using (FileStream fs = FileUtilities.CreateReplacementFile(GetFullPath(Target), FileShare.Read | FileShare.Delete))
            {
                XAssert.IsNotNull(fs);
                XAssert.AreNotEqual(originalId, FileUtilities.ReadFileUsnByHandle(fs.SafeFileHandle).Value.FileId, "File was truncated rather than replaced");
                XAssert.AreEqual(0, fs.Length);
                fs.WriteByte(1);
            }
        }

        [Fact]
        public void RetryEmptyDirectoryDelete()
        {
            // Create an empty directory
            string dir = Path.Combine(TemporaryDirectory, "dir");
            Directory.CreateDirectory(dir);

            SafeFileHandle childHandle = null;
            FileUtilities.TryCreateOrOpenFile(
                dir,
                FileDesiredAccess.GenericRead,
                FileShare.Read,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagBackupSemantics,
                out childHandle);

            using (childHandle)
            {
                Exception exception = null;
                try
                {
                    // Fails because of handle open to /dir
                    FileUtilities.DeleteDirectoryContents(dir, deleteRootDirectory: true);
                }
                catch (Exception e)
                {
                    exception = e;
                }
                XAssert.IsTrue(exception != null);
                XAssert.IsTrue(FileUtilities.Exists(dir));
            }

            AssertVerboseEventLogged(EventId.RetryOnFailureException, Helpers.DefaultNumberOfAttempts);
        }

        /// <summary>
        /// Check that <see cref="FileUtilities.DeleteDirectoryContents(string, bool, Func{string, bool})"/> retries
        /// on ERROR_DIR_NOT_EMPTY when deleting an empty directory immediately after verifying the deletion of that same directories contents
        /// </summary>
        /// <remarks>
        /// When attempting to recursively delete directory contents, there may be times when
        /// we successfully delete a file or subdirectory then immediately try to delete the parent directory
        /// and receive ERROR_DIR_NOT_EMPTY due to Windows marking the deleted file or subdirectory as pending
        /// deletion. In these cases, we should back off and retry.
        /// <see cref="FileUtilities.IsPendingDelete(SafeFileHandle)"/> for more information.
        /// </remarks>
        [Fact]
        public void RetryDeleteDirectoryContentsIfContentsPendingDelete()
        {
            try
            {
                // Need to disable POSIX delete to reproduce the Windows pending deletion state,
                // which does not exist in POSIX
                FileUtilities.PosixDeleteMode = PosixDeleteMode.NoRun;

                string dir = Path.Combine(TemporaryDirectory, "dir");
                Directory.CreateDirectory(dir);

                string nestedFile = Path.Combine(dir, "nestedFile");
                File.WriteAllText(nestedFile, "asdf");

                SafeFileHandle nestedFileHandle;
                FileUtilities.TryCreateOrOpenFile(
                    nestedFile,
                    FileDesiredAccess.GenericWrite,
                    FileShare.ReadWrite | FileShare.Delete,
                    FileMode.Open,
                    FileFlagsAndAttributes.None,
                    out nestedFileHandle);

                // Hold open a handle to \file, but allow all file sharing permissions
                using (nestedFileHandle)
                {
                    // Sanity check that pending delete doesn't always return true
                    XAssert.IsFalse(FileUtilities.IsPendingDelete(nestedFileHandle));

                    Exception exception = null;
                    try
                    {
                        // Fails because of the open file that cannot be deleted
                        FileUtilities.DeleteDirectoryContents(dir, true);
                    }
                    catch (BuildXLException e)
                    {
                        exception = e;
                        XAssert.IsTrue(e.Message.StartsWith(FileUtilitiesMessages.DeleteDirectoryContentsFailed + NormalizeDirectoryPath(dir)));

                        // Rebuild the exception message using StringBuilder to handle breaklines
                        StringBuilder builder = new StringBuilder();
                        builder.AppendLine(NormalizeDirectoryPath(nestedFile));
                        builder.AppendLine(FileUtilitiesMessages.NoProcessesUsingHandle);
                        builder.AppendLine(FileUtilitiesMessages.PathMayBePendingDeletion);
                        XAssert.IsTrue(e.Message.Contains(builder.ToString()));
                    }

                    XAssert.IsTrue(exception != null);

                    // Check the open handle forced the file to be placed on the Windows pending deletion queue
                    XAssert.IsTrue(FileUtilities.IsPendingDelete(nestedFileHandle));
                }

                // After the file handle is closed, the delete goes through
                XAssert.IsFalse(File.Exists(nestedFile));

                // Check for retries and ERROR_DIR_NOT_EMPTY
                AssertVerboseEventLogged(EventId.RetryOnFailureException, Helpers.DefaultNumberOfAttempts);
                string logs = EventListener.GetLog();
                var numMatches = Regex.Matches(logs, Regex.Escape("Native: RemoveDirectoryW for RemoveDirectory failed (0x91: The directory is not empty")).Count;
                XAssert.AreEqual(Helpers.DefaultNumberOfAttempts, numMatches);
            }
            finally
            {
                // Re-enable POSIX delete for the remainder of tests
                FileUtilities.PosixDeleteMode = PosixDeleteMode.RunFirst;
            }
        }

        [Fact]
        public void TestFindAllOpenHandlesInDirectory()
        {
            string topDir = Path.Combine(TemporaryDirectory, "top");
            Directory.CreateDirectory(topDir);

            string nestedDir = Path.Combine(topDir, "nestedDir");
            Directory.CreateDirectory(nestedDir);

            // top-level file handle to look for
            string nestedFile = Path.Combine(topDir, "nestedFile");
            File.WriteAllText(nestedFile, "asdf");

            // recursive file handle to look for
            string doubleNestedFile = Path.Combine(nestedDir, "doubleNestedFile");
            File.WriteAllText(doubleNestedFile, "hjkl");

            string openHandles;
            string openHandleNestedFile = FileUtilitiesMessages.ActiveHandleUsage + nestedFile;
            string openHandleDoubleNestedFile = FileUtilitiesMessages.ActiveHandleUsage + doubleNestedFile;

            // Hold open handles to both files
            using (new FileStream(nestedFile, FileMode.Open))
            {
                using (new FileStream(doubleNestedFile, FileMode.Open))
                {
                    openHandles = FileUtilities.FindAllOpenHandlesInDirectory(topDir);
                    XAssert.IsTrue(openHandles.Contains(openHandleNestedFile));
                    XAssert.IsTrue(openHandles.Contains(openHandleDoubleNestedFile));
                }
            }

            openHandles = FileUtilities.FindAllOpenHandlesInDirectory(topDir);
            XAssert.IsFalse(openHandles.Contains(openHandleNestedFile));
            XAssert.IsFalse(openHandles.Contains(openHandleDoubleNestedFile));
        }

        /// <summary>
        /// If a file or directory is on the Windows pending deletion queue,
        /// no process will report having an open handle
        /// </summary>
        [FactIfSupported(requiresHeliumDriversNotAvailable: true)]
        public void FailToFindAllOpenHandlesPendingDeletion()
        {
            string dir = Path.Combine(TemporaryDirectory, "dir");
            Directory.CreateDirectory(dir);

            string file = Path.Combine(dir, "file");
            File.WriteAllText(file, "asdf");

            SafeFileHandle fileHandle;
            FileUtilities.TryCreateOrOpenFile(
                file,
                FileDesiredAccess.GenericWrite,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.None,
                out fileHandle);

            // Hold open a handle to \file, but allow all file sharing permissions
            using (fileHandle)
            {
                XAssert.IsFalse(FileUtilities.IsPendingDelete(fileHandle));

                // This will succeed without throwing an exception
                File.Delete(file);

                // But the open handle forces the file to be placed on a pending deletion queue
                XAssert.IsTrue(FileUtilities.IsPendingDelete(fileHandle));

                // Fail to detect open handles for files pending deletion
                HashSet<string> deletedPaths = new HashSet<string>() { file };
                string openHandles = FileUtilities.FindAllOpenHandlesInDirectory(TemporaryDirectory, pathsPossiblyPendingDelete: deletedPaths);
                // Rebuild the exception message using StringBuilder to handle breaklines
                StringBuilder builder = new StringBuilder();
                builder.AppendLine(file);
                builder.AppendLine(FileUtilitiesMessages.NoProcessesUsingHandle);
                builder.AppendLine(FileUtilitiesMessages.PathMayBePendingDeletion);
                // Before Windows 10 Version 1903, attempting to create a file handle to a file pending deletion would throw an access exception, including calling File.Exists
                // With Windows 10 Version 1903 and later, creating handles to files on the pending deletion queue does not throw exceptions and pending deletion files are considered deleted by File.Exists
                // This change in behavior is NOT true for directories, see testing below for the directory behavior
                XAssert.IsTrue(openHandles.Contains(builder.ToString()) || /* Check for Windows 10 Version 1903 and later */ !File.Exists(file));
                XAssert.IsFalse(openHandles.Contains(FileUtilitiesMessages.ActiveHandleUsage + file));
            }

            XAssert.IsFalse(File.Exists(file));

            SafeFileHandle dirHandle;
            FileUtilities.TryCreateOrOpenFile(
                dir,
                FileDesiredAccess.GenericWrite,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagBackupSemantics,
                out dirHandle);

            // Hold open a handle to \dir, but allow all sharing permissions
            using (dirHandle)
            {
                XAssert.IsFalse(FileUtilities.IsPendingDelete(dirHandle));

                // This will succeed without throwing an exception
                Directory.Delete(dir);

                // But the open handle forces the directory to be placed on a pending deletion queue
                XAssert.IsTrue(FileUtilities.IsPendingDelete(dirHandle));

                // Fail to detect open handles for dirs pending deletion
                HashSet<string> deletedPaths = new HashSet<string>() { dir };
                string openHandles = FileUtilities.FindAllOpenHandlesInDirectory(TemporaryDirectory, pathsPossiblyPendingDelete: deletedPaths);
                // Rebuild the exception message using StringBuilder to handle breaklines
                StringBuilder builder = new StringBuilder();
                builder.AppendLine(dir);
                builder.AppendLine(FileUtilitiesMessages.NoProcessesUsingHandle);
                builder.AppendLine(FileUtilitiesMessages.PathMayBePendingDeletion);
                XAssert.IsTrue(openHandles.Contains(builder.ToString()));
                XAssert.IsFalse(openHandles.Contains(FileUtilitiesMessages.ActiveHandleUsage + dir));
            }

            XAssert.IsFalse(Directory.Exists(dir));
        }

        [Fact]
        public void TestDeleteDirectoryContentsWithExclusions()
        {
            // \topDir
            string topDir = Path.Combine(TemporaryDirectory, "top");
            Directory.CreateDirectory(topDir);

            // \topDir\nestedDir
            string nestedDir = Path.Combine(topDir, "nestedDir");
            Directory.CreateDirectory(nestedDir);

            // \topDir\nestedFile
            string nestedFile = Path.Combine(topDir, "nestedFile");
            File.WriteAllText(nestedFile, "asdf");

            // \topDir\nestedDir\doubleNestedFile
            string doubleNestedFile = Path.Combine(nestedDir, "doubleNestedFile");
            File.WriteAllText(doubleNestedFile, "hjkl");

            FileUtilities.DeleteDirectoryContents(
                path: topDir,
                deleteRootDirectory: true,
                shouldDelete: (path) =>
                    {
                        // exclude \nestedDir\*
                        return !path.Contains(nestedDir);
                    });

            // Even though deleteRootDirectory was marked as true,
            // \topDir should still exist because \topDir\nestedDir was excluded
            XAssert.IsTrue(Directory.Exists(topDir));
            // Successfully delete non-excluded file
            XAssert.IsFalse(File.Exists(nestedFile));

            // Excluded entries stay
            XAssert.IsTrue(Directory.Exists(nestedDir));
            XAssert.IsTrue(File.Exists(doubleNestedFile));
        }

        /// <summary>
        /// A stress test that rapidly creates and deletes directories,
        /// a worst case scenario for successfully deleting directories in Windows.
        /// This passes, but is disabled since it takes a significant amount of time.
        /// </summary>
        [Fact(Skip = "Long running test")]
        public void PosixDeleteDirectoryStressTest()
        {
            FileUtilities.PosixDeleteMode = PosixDeleteMode.RunFirst;
            string target = Path.Combine(TemporaryDirectory, "loop");
            string nested = Path.Combine(target, "nested");
            for (int i = 0; i < 100000; ++i)
            {
                Directory.CreateDirectory(target);
                Directory.CreateDirectory(nested);
                FileUtilities.DeleteDirectoryContents(target, deleteRootDirectory: true);
            }
        }


        /// <summary>
        /// A stress test that rapidly creates and deletes directories,
        /// a worst case scenario for successfully deleting directories in Windows.
        /// This passes, but is disabled since it takes a significant amount of time.
        /// </summary>
        /// <remarks>
        /// The move-delete stress test takes noticably longer than the POSIX-delete stress test.
        /// This may be worth looking into for performance.
        /// </remarks>
        [Fact(Skip = "Long running test")]
        public void MoveDeleteDirectoryStressTest()
        {
            try
            {
                FileUtilities.PosixDeleteMode = PosixDeleteMode.NoRun;
                string target = Path.Combine(TemporaryDirectory, "loop");
                string nested = Path.Combine(target, "nested");
                for (int i = 0; i < 100000; ++i)
                {
                    Directory.CreateDirectory(target);
                    Directory.CreateDirectory(nested);
                    FileUtilities.DeleteDirectoryContents(target, deleteRootDirectory: true, tempDirectoryCleaner: MoveDeleteCleaner);
                }
            }
            finally
            {
                FileUtilities.PosixDeleteMode = PosixDeleteMode.RunFirst;
            }
        }

        /// <summary>
        /// A stress test that rapidly creates and deletes directories,
        /// a worst case scenario for successfully deleting directories in Windows.
        /// This passes, but is disabled since it takes a significant amount of time (minutes).
        /// </summary>
        /// <remarks>
        /// This test does not test any BuildXL code, but exists to document the behavior
        /// of Windows deletes.
        /// </remarks>
        [Theory]
        public void DeleteDirectoryStressTest(int x)
        {
            string target = Path.Combine(TemporaryDirectory, "loop");
            string nested = Path.Combine(target, "nested");
            Exception exception = null;
            try
            {
                for (int i = 0; i < 100000; ++i)
                {
                    Directory.CreateDirectory(target);
                    Directory.CreateDirectory(nested);
                    Directory.Delete(target, recursive: true);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // Access denied when trying to create \nested. Directory.CreateDirectory(nested)
                // likely could not obtain a handle to the directory while it was pending deletion.
                exception = ex;
            }
            catch (DirectoryNotFoundException ex)
            {
                // Cannot find part of path when trying to create \nested (immediately after creating \target)
                // This implies CreateDirectory(target) failed without throwing any exceptions. The directory likely
                // existed during CreateDirectory(target), but was deleted before Directory.CreateDirectory(nested)
                // completed.
                exception = ex;
            }

            XAssert.IsFalse(exception == null);
            // exception occurs during creation of one of the directories
            XAssert.IsTrue(exception.StackTrace.ToString().Contains("System.IO.Directory.InternalCreateDirectoryHelper"));
        }

        /// <summary>
        /// Sanity check that links to running executables are tricky to delete.
        /// </summary>
        /// <remarks>
        /// BuildXL's CreateHardlink method relies on CreateHardlinkW, which requires WriteAttributes access on the file.
        /// </remarks>
        [Fact]
        public void TrivialDeleteFailsForLinkToRunningExecutable()
        {
            string exeLink = GetFullPath("DummyWaiterLink");
            XAssert.IsTrue(CreateHardLinkIfSupported(link: exeLink, linkTarget: DummyWaiter.GetDummyWaiterExeLocation()));

            using (var waiter = DummyWaiter.RunAndWait())
            {

                try
                {
                    File.Delete(exeLink);
                    XAssert.Fail("Expected deletion to fail due to the executable being loaded and running.");
                }
                catch (UnauthorizedAccessException)
                {
                    // expected.
                }
                catch (IOException)
                {
                    // expected.
                }
            }

            // Should succeed when the executable is no longer running.
            File.Delete(exeLink);
        }

        /// <remarks>
        /// BuildXL's CreateHardlink method relies on CreateHardlinkW, which requires WriteAttributes access on the file.
        /// </remarks>
        [Fact]
        public void CreateReplacementFileCanReplaceRunningExecutableLink()
        {
            string exeLink = GetFullPath("DummyWaiterLink");
            XAssert.IsTrue(CreateHardLinkIfSupported(link: exeLink, linkTarget: DummyWaiter.GetDummyWaiterExeLocation()));

            using (var waiter = DummyWaiter.RunAndWait())
            {
                using (FileStream fs = FileUtilities.CreateReplacementFile(exeLink, FileShare.Delete))
                {
                    XAssert.AreEqual(0, fs.Length);
                }
            }
        }

        /// <remarks>
        /// BuildXL's CreateHardlink method relies on CreateHardlinkW, which requires WriteAttributes access on the file.
        /// </remarks>
        [Fact]
        public void CanDeleteRunningExecutableLink()
        {
            string exeLink = GetFullPath("DummyWaiterLink");
            XAssert.IsTrue(CreateHardLinkIfSupported(link: exeLink, linkTarget: DummyWaiter.GetDummyWaiterExeLocation()));

            using (var waiter = DummyWaiter.RunAndWait())
            {
                XAssert.IsTrue(File.Exists(exeLink));
                FileUtilities.DeleteFile(exeLink);
                XAssert.IsFalse(File.Exists(exeLink));
            }
        }

        [Fact]
        public void CanDeleteReadonlyFile()
        {
            string file = GetFullPath("File");
            File.WriteAllText(file, string.Empty);
            SetReadonlyFlag(file);

            XAssert.IsTrue(File.Exists(file));
            FileUtilities.DeleteFile(file);
            XAssert.IsFalse(File.Exists(file));
        }

        [FactIfSupported(requiresAdmin: true)]
        public void CanDeleteFileWithDenyACL()
        {
            string file = GetFullPath("File with space");
            string directory = Path.GetDirectoryName(file);
            File.WriteAllText(file, string.Empty);
            try
            {
                FileInfo fi = new FileInfo(file);
                FileSecurity accessControl = fi.GetAccessControl(AccessControlSections.All);
                accessControl.PurgeAccessRules(WindowsIdentity.GetCurrent().User);
                accessControl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, AccessControlType.Deny));
                fi.SetAccessControl(accessControl);
                DirectoryInfo di = new DirectoryInfo(directory);
                DirectorySecurity ds = di.GetAccessControl(AccessControlSections.All);
                ds.PurgeAccessRules(WindowsIdentity.GetCurrent().User);
                ds.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.CreateFiles, AccessControlType.Deny));
                di.SetAccessControl(ds);

                XAssert.IsTrue(File.Exists(file));
                FileUtilities.DeleteFile(file);
                XAssert.IsFalse(File.Exists(file));
            }
            finally
            {
                DirectoryInfo di = new DirectoryInfo(directory);
                DirectorySecurity ds = di.GetAccessControl(AccessControlSections.All);
                ds.PurgeAccessRules(WindowsIdentity.GetCurrent().User);
                ds.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, AccessControlType.Allow));
                di.SetAccessControl(ds);
                di.Delete(true);
            }
        }

        [FactIfSupported(requiresAdmin: true)]
        public void CanDeleteReadonlyDenyWriteAttribute()
        {
            string file = GetFullPath("File");
            string directory = Path.GetDirectoryName(file);
            File.WriteAllText(file, string.Empty);

            // Make the file readonly & deny modifying attributes
            FileInfo fi = new FileInfo(file);
            fi.IsReadOnly = true;
            FileSecurity accessControl = fi.GetAccessControl(AccessControlSections.All);
            accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.WriteAttributes, AccessControlType.Deny));
            fi.SetAccessControl(accessControl);

            // see if we can delete it
            XAssert.IsTrue(File.Exists(file));
            FileUtilities.DeleteFile(file);
            XAssert.IsFalse(File.Exists(file));
        }

        [Fact]
        public void TestTryMoveDelete()
        {
            string file = Path.Combine(TemporaryDirectory, "file");
            File.WriteAllText(file, "asdf");

            string trashDirectory = Path.Combine(TemporaryDirectory, "trash");
            Directory.CreateDirectory(trashDirectory);

            using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
            {
                FileUtilities.TryMoveDelete(file, trashDirectory);
            }

            // Check the file was deleted/moved
            XAssert.IsFalse(File.Exists(file));
            // Check the trash directory the file was moved to is empty
            XAssert.IsTrue(Directory.GetDirectories(trashDirectory).Length + Directory.GetFiles(trashDirectory).Length == 0);
        }

        /// <summary>
        /// In previous versions of Windows, if any handle to a hardlink is open without FileShare Delete mode,
        /// no hardlink to the same underlying file can be deleted
        /// This appears to be fixed in RS3, so this test has inconsistent results on different OSes (it passes in RS3)
        /// </summary>
        [Fact(Skip = "Skip")]
        public void CanDeleteWithOtherOpenHardlinks()
        {
            string hardlink1 = Path.Combine(TemporaryDirectory, "hardlink1");
            File.WriteAllText(hardlink1, "asdf");

            string hardlink2 = Path.Combine(TemporaryDirectory, "hardlink2");
            XAssert.IsTrue(CreateHardLinkIfSupported(hardlink2, hardlink1));

            // Open a handle to hardlink2 without FileShare Delete mode
            using (new FileStream(hardlink2, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                BuildXLException exception = null;
                try
                {
                    FileUtilities.DeleteFile(hardlink1);
                }
                catch (BuildXLException ex)
                {
                    exception = ex;
                }
                // Successfully delete file
                XAssert.IsTrue(exception == null);
                XAssert.IsFalse(File.Exists(hardlink1));
            }
        }


        /// <remarks>
        /// BuildXL's CreateHardlink method relies on CreateHardlinkW, which requires WriteAttributes access on the file.
        /// </remarks>
        [Fact]
        public void DeleteDirectoryContents()
        {
            string rootDir = GetFullPath("Directory");
            Directory.CreateDirectory(rootDir);
            Directory.CreateDirectory(Path.Combine(rootDir, "subdir1"));
            Directory.CreateDirectory(Path.Combine(rootDir, "subdir2"));

            // Create a readonly file
            string nestedFile = Path.Combine(rootDir, "subdir1", "File.txt");
            File.WriteAllText(nestedFile, "asdf");
            File.SetAttributes(nestedFile, FileAttributes.ReadOnly);

            // And a normal file
            File.WriteAllText(Path.Combine(rootDir, "File.txt"), "asdf");

            string exeLink = Path.Combine(rootDir, "hardlink");
            XAssert.IsTrue(CreateHardLinkIfSupported(link: exeLink, linkTarget: DummyWaiter.GetDummyWaiterExeLocation()));

            // Add a hardlink
            using (var waiter = DummyWaiter.RunAndWait())
            {
                FileUtilities.DeleteDirectoryContents(rootDir);
            }

            XAssert.AreEqual(0, Directory.GetFileSystemEntries(rootDir).Length);
        }

        /// <summary>
        /// Tests that <see cref="FileUtilities.Exists(string)"/> returns true positives (file exists when it does),
        /// where <see cref="File.Exists(string)"/> returns false negatives (file does not exist when it does)
        /// </summary>
        [Fact]
        public void FileUtilitiesExists()
        {
            string directory = Path.Combine(TemporaryDirectory, "directory");
            Directory.CreateDirectory(directory);

            string file = Path.Combine(directory, "openfileExists.txt");
            File.WriteAllText(file, "asdf");

            // Path leads to directory
            XAssert.IsTrue(FileUtilities.Exists(directory));
            XAssert.IsFalse(File.Exists(directory));

            // Path is normalized with characters File.Exists considers invalid
            XAssert.IsTrue(FileUtilities.Exists(@"\\?\" + file));

            bool expectedExistenceForLongPath = LongPathsSupported;
            XAssert.AreEqual(expectedExistenceForLongPath, File.Exists(@"\\?\" + file));

            // Remove access permissions to the file
            var fi = new FileInfo(file);
            FileSecurity fileSecurity = fi.GetAccessControl();
            fileSecurity.AddAccessRule(new FileSystemAccessRule($@"{Environment.UserDomainName}\{Environment.UserName}", FileSystemRights.FullControl, AccessControlType.Deny));
            fi.SetAccessControl(fileSecurity);

            Exception exception = null;
            try
            {
                File.ReadAllText(file);
            }
            catch (UnauthorizedAccessException ex)
            {
                exception = ex;
            }

            // File access successfully removed
            XAssert.IsTrue(exception != null);

            // Both versions will return correctly
            XAssert.IsTrue(FileUtilities.Exists(file));
            XAssert.IsTrue(File.Exists(file));
        }

        /// <remarks>
        /// BuildXL's CreateHardlink method relies on CreateHardlinkW, which requires WriteAttributes access on the file.
        /// </remarks>
        [Fact]
        public void DeleteDirectoryContentsLongPath()
        {
            string originalRoot = GetFullPath("testRoot");

            // Create a directory with a path that's too long to normally delete
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 100; i++)
            {
                sb.Append("a");
            }

            string rootDir = @"\\?\" + originalRoot;
            XAssert.IsTrue(CreateDirectoryW(rootDir, IntPtr.Zero));
            rootDir = rootDir + "\\" + sb.ToString();
            XAssert.IsTrue(CreateDirectoryW(rootDir, IntPtr.Zero));
            rootDir = rootDir + "\\" + sb.ToString();
            XAssert.IsTrue(CreateDirectoryW(rootDir, IntPtr.Zero));

            // Write some files in the directory. Set their attributes as readonly to exercise the related logic in DeleteFile
            string shorterPath = Path.Combine(originalRoot, "myFile.txt");
            File.WriteAllText(shorterPath, "foo");
            File.SetAttributes(shorterPath, FileAttributes.ReadOnly);

            // And a file with a filename longer than maxpath
            string longerPath = rootDir + @"\myFile.txt";
            SafeFileHandle fileHandle;
            var result = FileUtilities.TryCreateOrOpenFile(
                longerPath,
                FileDesiredAccess.GenericWrite,
                FileShare.Delete,
                FileMode.Create,
                FileFlagsAndAttributes.FileAttributeNormal,
                out fileHandle);
            XAssert.IsTrue(result.Succeeded);
            using (FileStream stream = new FileStream(fileHandle, FileAccess.Write))
            {
                stream.WriteByte(255);
            }

            FileUtilities.SetFileAttributes(longerPath, FileAttributes.ReadOnly);

            string exeLink = Path.Combine(rootDir, "hardlink");
            XAssert.IsTrue(CreateHardLinkIfSupported(link: exeLink, linkTarget: DummyWaiter.GetDummyWaiterExeLocation()));

            // Add a hardlink. Perform deletion attempts while the hardlink target is in use
            using (var waiter = DummyWaiter.RunAndWait())
            {
                // Attempt to delete with the managed API. This should fail because it contains nested paths that are too long
                bool isPathTooLong = false;
                try
                {
                    Directory.Delete(rootDir, recursive: true);
                }
                catch (UnauthorizedAccessException)
                {
                    // expected: when the long paths are supported, then
                    // CreateHardLinkW would be finished successfully,
                    // and directory deletion will fail because the executable is still running.
                }
                catch (PathTooLongException)
                {
                    isPathTooLong = true;
                }

                // Expect failure only if long paths are not supported by the current .NET Framework version.
                bool expectedIsPathTooLong = !LongPathsSupported;
                XAssert.AreEqual(expectedIsPathTooLong, isPathTooLong, "Expected to encounter a PathTooLongException. If no exception is thrown by the System.IO method, this test isn't validating anything");

                // Now use the native API. This should succeed
                FileUtilities.DeleteDirectoryContents(originalRoot);
            }

            XAssert.IsTrue(Directory.Exists(originalRoot));
            XAssert.AreEqual(0, Directory.GetFileSystemEntries(originalRoot).Length);
        }

        [Fact]
        public void CreateHardlinkSupportsLongPath()
        {
            var longPath = Enumerable.Range(0, NativeIOConstants.MaxDirectoryPath).Aggregate(TemporaryDirectory, (path, _) => Path.Combine(path, "dir"));

            FileUtilities.CreateDirectory(longPath);

            var file = Path.Combine(longPath, "out.txt");
            var link = Path.Combine(longPath, "hardlink");

            SafeFileHandle fileHandle;
            var result = FileUtilities.TryCreateOrOpenFile(
                file,
                FileDesiredAccess.GenericWrite,
                FileShare.Delete,
                FileMode.Create,
                FileFlagsAndAttributes.FileAttributeNormal,
                out fileHandle);
            XAssert.IsTrue(result.Succeeded);
            using (FileStream stream = new FileStream(fileHandle, FileAccess.Write))
            {
                stream.WriteByte(255);
            }

            XAssert.IsTrue(CreateHardLinkIfSupported(link: link, linkTarget: file));
        }

        [Fact]
        public void TryFindOpenHandlesToFileWithFormatString()
        {
            var fileNameWithCurly = Path.Combine(TemporaryDirectory, "fileWith{curly}InName");
            XAssert.IsTrue(FileUtilities.TryFindOpenHandlesToFile(fileNameWithCurly, out var diag));
        }

        [Fact]
        public void LongPathAccessControlTest()
        {
            var longPath = Enumerable.Range(0, NativeIOConstants.MaxDirectoryPath).Aggregate(TemporaryDirectory, (path, _) => Path.Combine(path, "dir"));
            var file = Path.Combine(longPath, "fileWithWriteAccess.txt");

            FileUtilities.CreateDirectory(longPath);           
            SafeFileHandle fileHandle;
            var result = FileUtilities.TryCreateOrOpenFile(
                file,
                FileDesiredAccess.GenericWrite,
                FileShare.Delete,
                FileMode.Create,
                FileFlagsAndAttributes.FileAttributeNormal,
                out fileHandle);
            XAssert.IsTrue(result.Succeeded);

            FileUtilities.SetFileAccessControl(file, FileSystemRights.WriteAttributes, true);
            XAssert.IsTrue(FileUtilities.HasWritableAccessControl(file));

            //Delete the created directory
            fileHandle.Close();
            FileUtilities.DeleteDirectoryContents(longPath, deleteRootDirectory: true);
        }


        private static void SetReadonlyFlag(string path)
        {
            File.SetAttributes(path, FileAttributes.Normal | FileAttributes.ReadOnly);
        }

        private static void AddDenyWriteACL(string path)
        {
            AddDenyACL(path, FileSystemRights.WriteData | FileSystemRights.AppendData);
        }

        private static void AddDenyACL(string path, FileSystemRights deny)
        {
            ModifyDenyACL(path, deny, add: true);
        }

        private static void ModifyDenyACL(string path, FileSystemRights deny, bool add)
        {
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            var rule = new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                deny,
                AccessControlType.Deny);
            if (add)
            {
                security.AddAccessRule(rule);

            }
            else
            {
                security.RemoveAccessRule(rule);
            }

            fileInfo.SetAccessControl(security);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryW(
            string lpPathName,
            IntPtr lpSecurityAttributes);
            
        private static bool CreateHardLinkIfSupported(string link, string linkTarget)
        {
            CreateHardLinkStatus status = FileUtilities.TryCreateHardLink(link: link, linkTarget: linkTarget);
            if (status == CreateHardLinkStatus.Success)
            {
                return true;
            }

            if (status == CreateHardLinkStatus.FailedSinceNotSupportedByFilesystem && !OperatingSystemHelper.IsUnixOS)
            {
                return false;
            }

            if(status == CreateHardLinkStatus.Failed)
            {
                return false;
            }

            XAssert.Fail("Creating a hardlink failed unexpectedly: {0:G}", status);
            return false;
        }

        /// <summary>
        /// Returns true if paths longer then 260 characters are supported.
        /// </summary>
        private static bool LongPathsSupported { get; } = GetLongPathSupport();

        private static bool GetLongPathSupport()
        {
            string longString = new string('a', NativeIOConstants.MaxPath + 1);
            try
            {
                string path = $@"\\?\c:\foo{longString}.txt";
                var directoryName = System.IO.Path.GetDirectoryName(path);
                return true;

            }
            catch (PathTooLongException)
            {
                return false;
            }
        }
    }
}

#pragma warning restore AsyncFixer02
