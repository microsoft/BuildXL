// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class TempCleanerTests : TemporaryStorageTestBase
    {
        public TempCleanerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void CleanNonexisting()
        {
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);

            string baseDir = TemporaryDirectory;
            string dir = Path.Combine(baseDir, "DirDoesntExist");
            string file = Path.Combine(baseDir, "FileDoesntExist.txt");
            using (TempCleaner cleaner = new TempCleaner())
            {
                cleaner.RegisterDirectoryToDelete(dir, deleteRootDirectory: false);
                cleaner.RegisterFileToDelete(file);
                cleaner.WaitPendingTasksForCompletion();
            }

            // No warnings for missing file/folder
            m_expectedWarningCount = 0;
        }

        [Fact]
        public void CleanTempDirsAndFiles()
        {
            // Arrange
            Tuple<string, string> dirs = CreateDirs();
            
            string tempFile = Path.Combine(dirs.Item1, "SomeTemporaryFile.txt");
            PrepareFileWithConent(tempFile, "sampleContent");

            // Act
            using (TempCleaner cleaner = new TempCleaner())
            {
                cleaner.RegisterDirectoryToDelete(dirs.Item1, deleteRootDirectory: false);
                cleaner.RegisterDirectoryToDelete(dirs.Item2, deleteRootDirectory: false);
                cleaner.RegisterFileToDelete(tempFile);

                // Registering file that is not exists
                string notExistedPath = Path.Combine(dirs.Item1, "UnknownFile.txt");
                XAssert.IsFalse(File.Exists(notExistedPath));
                cleaner.RegisterFileToDelete(notExistedPath);

                // Waiting for pending tasks to complete
                cleaner.WaitPendingTasksForCompletion();

                // Assert
                XAssert.AreEqual(2, cleaner.SucceededDirectories);
                XAssert.AreEqual(0, cleaner.PendingDirectories);
                XAssert.AreEqual(0, cleaner.FailedDirectories);
                
                XAssert.AreEqual(2, cleaner.SucceededFiles);
                XAssert.AreEqual(0, cleaner.PendingFiles);
                XAssert.AreEqual(0, cleaner.FailedFiles);
            }

            AssertDirectoryEmpty(dirs.Item1);
            AssertDirectoryEmpty(dirs.Item2);
        }

        // Unix file systems allow for multiple file deletion without access violations
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CleanTempDirsAndFilesWithOpenedFile()
        {
            // Arrange
            Tuple<string, string> dirs = CreateDirs();

            string file = Path.Combine(dirs.Item1, "openedFile.txt");

            // Warning for a failed deleted file
            IgnoreWarnings();

            using (StreamWriter writer = new StreamWriter(file))
            {
                writer.Write("asdf");
                writer.Flush();
                XAssert.IsTrue(File.Exists(file));

                // Act
                using (TempCleaner cleaner = new TempCleaner())
                {
                    // This should fail
                    cleaner.RegisterDirectoryToDelete(dirs.Item1, deleteRootDirectory: false);
                    cleaner.RegisterDirectoryToDelete(dirs.Item2, deleteRootDirectory: false);

                    // This should fail
                    cleaner.RegisterFileToDelete(file);

                    // Waiting for pending tasks to complete
                    cleaner.WaitPendingTasksForCompletion();

                    // Assert
                    XAssert.AreEqual(1, cleaner.SucceededDirectories);
                    XAssert.AreEqual(0, cleaner.PendingDirectories);
                    XAssert.AreEqual(1, cleaner.FailedDirectories);

                    XAssert.AreEqual(0, cleaner.SucceededFiles);
                    XAssert.AreEqual(0, cleaner.PendingFiles);
                    XAssert.AreEqual(1, cleaner.FailedFiles);
                }

                XAssert.IsTrue(File.Exists(file));
            }

            AssertDirectoryEmpty(dirs.Item2);
        }

        /// <summary>
        /// Validate that TempCleaner cleans its own TempDirectory. This directory
        /// can be used for move-deleting files.
        /// </summary>
        [Fact]
        public void CleanTempCleanerTempDirectory()
        {
            // Make a temp directory for TempCleaner
            string deletionTemp = Path.Combine(TemporaryDirectory, "DeletionTemp");
            Directory.CreateDirectory(deletionTemp);

            // Make a file outside of TempCleaner temp directory
            string deletedFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            File.WriteAllText(deletedFile, "asdf");

            // Create a temp cleaner with a temp directory
            using (TempCleaner cleaner = new TempCleaner(deletionTemp))
            {
                // Move-delete a file into the TempCleaner temp directory
                FileUtilities.TryMoveDelete(deletedFile, cleaner.TempDirectory);

                cleaner.WaitPendingTasksForCompletion();
                XAssert.AreEqual(1, cleaner.SucceededDirectories);
            }

            // TempCleaner should clean out \deletionTemp before or during Dispose
            XAssert.IsFalse(File.Exists(deletedFile));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TempCleanerCleanRootDirectory(bool deleteRoot)
        {
            string moveDeletionTemp = Path.Combine(TemporaryDirectory, "MoveDeletionTemp");
            Directory.CreateDirectory(moveDeletionTemp);

            string directoryToBeDeleted = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(directoryToBeDeleted);
            string deletedFile = Path.Combine(directoryToBeDeleted, "file");
            File.WriteAllText(deletedFile, "asdf");

            string movedFile = Path.Combine(TemporaryDirectory, "movedFile");
            File.WriteAllText(movedFile, "asdf");

            // Create a temp cleaner with a temp directory
            using (TempCleaner cleaner = new TempCleaner(moveDeletionTemp))
            {
                // Move-delete a file into the TempCleaner temp directory
                FileUtilities.TryMoveDelete(movedFile, cleaner.TempDirectory);

                cleaner.RegisterDirectoryToDelete(directoryToBeDeleted, deleteRoot);

                cleaner.WaitPendingTasksForCompletion();
            }

            XAssert.AreEqual(!deleteRoot, Directory.Exists(directoryToBeDeleted));
            XAssert.IsTrue(Directory.Exists(moveDeletionTemp));
        }

        private Tuple<string, string> CreateDirs()
        {
            string dir1 = Path.Combine(TemporaryDirectory, "CleanTempDirs", "dir1");
            string dir2 = Path.Combine(TemporaryDirectory, "CleanTempDirs", "dir2");
            string subdir = Path.Combine(TemporaryDirectory, "CleanTempDirs", "subdir");

            Directory.CreateDirectory(dir1);
            Directory.CreateDirectory(dir2);
            Directory.CreateDirectory(subdir);
            File.WriteAllText(Path.Combine(subdir, "myfile.txt"), "asdf");

            return new Tuple<string, string>(dir1, dir2);
        }

        private void AssertDirectoryEmpty(string directoryPath)
        {
            DirectoryInfo di = new DirectoryInfo(directoryPath);
            XAssert.IsFalse(di.EnumerateFileSystemInfos().Any(), "Directory was not cleaned");
        }

        private void DeleteIfExists(string tempFile)
        {
            if (File.Exists(tempFile))
            {
                FileUtilities.DeleteFile(tempFile);
            }
        }

        private void PrepareFileWithConent(string path, string content)
        {
            DeleteIfExists(path);

            File.WriteAllText(path, content);
        }
    }
}
