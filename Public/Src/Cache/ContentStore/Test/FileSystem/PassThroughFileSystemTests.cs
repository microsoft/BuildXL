// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using ContentStoreTest.Test;
using BuildXL.Native.IO;
using Xunit;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using FluentAssertions;

namespace ContentStoreTest.FileSystem
{
    public sealed class PassThroughFileSystemTests : AbsFileSystemTests
    {
        public PassThroughFileSystemTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger))
        {
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] 
        public void CreateHardLinkResultsBasedOnDestinationDirectoryExistence()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / "source.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileUtilities.SetFileAccessControl(sourcePath.Path, FileSystemRights.Write | FileSystemRights.ReadAndExecute, false);

                // Covering short path case
                CreateHardLinkResultsBasedOnDestinationDirectoryExistenceCore(testDirectory.Path, sourcePath, new string('a', 10));

                // Covering long path case
                CreateHardLinkResultsBasedOnDestinationDirectoryExistenceCore(testDirectory.Path, sourcePath, new string('a', 200));
            }

            void CreateHardLinkResultsBasedOnDestinationDirectoryExistenceCore(AbsolutePath root, AbsolutePath sourcePath, string pathSegment)
            {
                var destinationPath = root / pathSegment / pathSegment / "destination.txt";
                
                // Creating hard link fails if the destination directory does not exist.
                // But this is the case only when the path is not a long path on a system with no long path support.
                if (destinationPath.IsLongPath && !AbsolutePath.LongPathsSupported)
                {
                    // In this case, the test case is skipped.
                    return;
                }

                var result = FileSystem.CreateHardLink(sourcePath, destinationPath, false);
                var expectedResult = CreateHardLinkResult.FailedDestinationDirectoryDoesNotExist;
                Assert.Equal(expectedResult, result);

                FileSystem.CreateDirectory(destinationPath.Parent);
                result = FileSystem.CreateHardLink(sourcePath, destinationPath, false);
                Assert.Equal(CreateHardLinkResult.Success, result);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void CreateDirectoryWithLargeSegmentFailsEvenWhenLongPathsSupported()
        {
            if (!AbsolutePath.LongPathsSupported)
            {
                return;
            }

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // Even with long path support windows won't let you to create a directory
                // with a folder name longer then 260 characters.
                var longPart = new string('a', 300);

                var sourcePath = testDirectory.Path / longPart / "source.txt";

                Action a = () => FileSystem.CreateDirectory(sourcePath.Parent);
                // Should fail with:
                // System.IO.IOException : The filename, directory name, or volume label syntax is incorrect.
                Assert.Throws<IOException>(a);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // We do not block deletion on MacOS
        public void MoveFileWithReplaceShouldSucceedWithReadonlyAttributes()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";
                
                FileSystem.CreateDirectory(source.Parent);
                FileSystem.CreateDirectory(destination.Parent);
                FileSystem.WriteAllBytes(source, new byte[] { 1, 2, 3 });
                var destinationBytes = new byte[] { 4, 5, 6 };
                FileSystem.WriteAllBytes(destination, destinationBytes);
                FileSystem.DenyFileWrites(destination);
                FileSystem.DenyAttributeWrites(destination);

                Action a = () => FileSystem.MoveFile(source, destination, false);
                Assert.Throws<IOException>(a);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // Only FileShare.None is acknowledged on *nix
        public async Task CreateFileDestinationIsOpenedOverwrite()
        {
            // Deletion logic is too powerful for this case and we have disable it.
            FileUtilities.PosixDeleteMode = PosixDeleteMode.NoRun;

            await Task.Yield();
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var destination = testDirectory.Path / "parent2" / "dst";

                FileSystem.CreateDirectory(destination.Parent);
                var destinationBytes = new byte[] { 4, 5, 6 };
                FileSystem.WriteAllBytes(destination, destinationBytes);

                using (await FileSystem.OpenAsync(destination, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Func<Task> a = async () =>
                                   {
                                       using (await FileSystem.OpenAsync(
                                           destination, FileAccess.Write, FileMode.Create, FileShare.Delete))
                                       {
                                       }
                                   };

                    var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(a);
                    exception.Message.Should().ContainAny("Handle was used by", "Did not find any actively running processes using the handle");
                }
            }
        }

        [Fact]
        public void CreateHardLinkFromFileWithDenyWriteReadExecute()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / "source.txt";
                var destinationPath = testDirectory.Path / "destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileUtilities.SetFileAccessControl(sourcePath.Path, FileSystemRights.Write | FileSystemRights.ReadAndExecute, false);

                var result = FileSystem.CreateHardLink(sourcePath, destinationPath, false);
                Assert.Equal(CreateHardLinkResult.Success, result);
            }
        }

        [Fact]
        [Trait("Category", "QTestSkip")]
        public void VolumeInfoUpdated()
        {
            const int tempFileSize = 1024 * 1024;

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var info1 = FileSystem.GetVolumeInfo(testDirectory.Path);

                var path = testDirectory.CreateRandomFileName();
                FileSystem.WriteAllBytes(path, ThreadSafeRandom.GetBytes(tempFileSize));

                var success = false;
                for (var i = 0; i < 10 && !success; i++)
                {
                    var info2 = FileSystem.GetVolumeInfo(testDirectory.Path);
                    success = (info2.FreeSpace + tempFileSize) <= info1.FreeSpace;

                    if (!success)
                    {
                        Thread.Sleep(100);
                    }
                }

                // This test could become flaky if other content is being deleted around the same time.
                Assert.True(success);
            }
        }

        [Fact]
        public void MoveFileSourceDirectoryDoesNotExistThrowsWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";
                Action a = () => FileSystem.MoveFile(source, destination, false);

                var exception = Record.Exception(a);
                Assert.NotNull(exception);
            }
        }

        [Fact]
        public void MoveFileSourceDoesNotExistThrowsWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";

                FileSystem.CreateDirectory(source.Parent);

                Action a = () => FileSystem.MoveFile(source, destination, false);

                var exception = Record.Exception(a);

                Assert.NotNull(exception);
                Assert.IsType<FileNotFoundException>(exception);
            }
        }

        [Fact]
        public void MoveFileDestinationDirectoryDoesNotExistThrowsWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";

                FileSystem.CreateDirectory(source.Parent);
                FileSystem.WriteAllBytes(source, new byte[] { 1, 2, 3 });

                Action a = () => FileSystem.MoveFile(source, destination, false);

                var exception = Record.Exception(a);

                Assert.NotNull(exception);
                Assert.IsType<DirectoryNotFoundException>(exception);
                Assert.True(exception.Message.Contains("The system cannot find the path specified.") || exception.Message.Contains("Could not find a part of the path"), exception.Message);
            }
        }

        [Fact]
        public void MoveFileDestinationAlreadyExistsThrowsWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";

                FileSystem.CreateDirectory(source.Parent);
                FileSystem.CreateDirectory(destination.Parent);
                FileSystem.WriteAllBytes(source, new byte[] { 1, 2, 3 });
                var destinationBytes = new byte[] { 4, 5, 6 };
                FileSystem.WriteAllBytes(destination, destinationBytes);

                Action a = () => FileSystem.MoveFile(source, destination, false);

                var exception = Record.Exception(a);

                Assert.NotNull(exception);
                Assert.IsType<IOException>(exception);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // ShareRead does not block Delete or Move in coreclr
        public async Task MoveFileDestinationIsOpenedOverwriteThrowsWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / "parent1" / "src";
                var destination = testDirectory.Path / @"parent2" / "dst";

                FileSystem.CreateDirectory(source.Parent);
                FileSystem.CreateDirectory(destination.Parent);
                FileSystem.WriteAllBytes(source, new byte[] { 1, 2, 3 });
                var destinationBytes = new byte[] { 4, 5, 6 };
                FileSystem.WriteAllBytes(destination, destinationBytes);

                using (await FileSystem.OpenAsync(destination, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Action a = () => FileSystem.MoveFile(source, destination, true);

                    var exception = Record.Exception(a);

                    Assert.NotNull(exception);
                    Assert.IsType<UnauthorizedAccessException>(exception);
                    Assert.Contains("Access is denied.", exception.Message, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task ReaderBlocksDeleteWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] { 1 });
                using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Action a = () => FileSystem.DeleteFile(source);

                    var exception = Record.Exception(a);

                    Assert.NotNull(exception);
                    Assert.IsType<UnauthorizedAccessException>(exception);
                    Assert.Contains(
                        "The process cannot access the file because it is being used by another process.",
                        exception.Message,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task WriterBlocksDeleteWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.CreateNew, FileShare.None))
                {
                    Action a = () => FileSystem.DeleteFile(source);

                    var exception = Record.Exception(a);

                    Assert.NotNull(exception);
                    Assert.IsType<UnauthorizedAccessException>(exception);
                    Assert.Contains(
                        "The process cannot access the file because it is being used by another process.",
                        exception.Message,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task DeleteOneLinkWhileOtherLinkIsOpenReadOnlySharingFailsWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                Assert.Equal(CreateHardLinkResult.Success, FileSystem.CreateHardLink(sourcePath, destinationPath, true));
                using (await FileSystem.OpenAsync(sourcePath, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Action a = () => FileSystem.DeleteFile(destinationPath);

                    var exception = Record.Exception(a);

                    Assert.NotNull(exception);
                    Assert.IsType<UnauthorizedAccessException>(exception);
                    Assert.Contains(
                        "The process cannot access the file because it is being used by another process.",
                        exception.Message,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task DeleteOneLinkWhileOneOtherLinkIsOpenReadOnlySharingFailsWithAppropriateError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath1 = testDirectory.Path / @"destination1.txt";
                var destinationPath2 = testDirectory.Path / @"destination2.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                Assert.Equal(CreateHardLinkResult.Success, FileSystem.CreateHardLink(sourcePath, destinationPath1, true));
                Assert.Equal(CreateHardLinkResult.Success, FileSystem.CreateHardLink(sourcePath, destinationPath2, true));

                using (await FileSystem.OpenAsync(sourcePath, FileAccess.Read, FileMode.Open, ShareReadDelete))
                using (await FileSystem.OpenAsync(destinationPath2, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Action a = () => FileSystem.DeleteFile(destinationPath1);

                    var exception = Record.Exception(a);

                    Assert.NotNull(exception);
                    Assert.IsType<UnauthorizedAccessException>(exception);
                    Assert.Contains(
                        "The process cannot access the file because it is being used by another process.",
                        exception.Message,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
