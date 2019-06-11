// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;

namespace BuildXL.Cache.ContentStore.InterfacesTest.FileSystem
{
    public abstract class AbsFileSystemTests
    {
        protected const FileShare ShareRead = FileShare.Read;
        protected const FileShare ShareReadDelete = FileShare.Read | FileShare.Delete;
        protected const FileShare ShareNone = FileShare.None;
        protected const FileShare ShareDelete = FileShare.Delete;
        protected readonly IAbsFileSystem FileSystem;

        protected AbsFileSystemTests(Func<IAbsFileSystem> createFileSystemFunc)
        {
            FileSystem = createFileSystemFunc();
        }

        [Fact]
        public void CreateDirectoryWhereFileExistsThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var filePath = testDirectory.Path / @"file";
                FileSystem.WriteAllBytes(filePath, new byte[] {1, 2, 3});

                Action a = () => FileSystem.CreateDirectory(filePath);
                Assert.Throws<IOException>(a);
            }
        }

        [Fact]
        public async Task TestSharedReadAccess()
        {
            if (!(FileSystem is PassThroughFileSystem))
            {
                // Bug 1334691
                return;
            }

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var destination = testDirectory.Path / "foo.txt";
                using (var file1 = await FileSystem.OpenAsync(destination, FileAccess.Write, FileMode.OpenOrCreate, FileShare.Read))
                {
                    const string content = "My content";
                    using (var file1Writer = new StreamWriter(file1, Encoding.UTF8, 128_000, leaveOpen: true))
                    {
                        await file1Writer.WriteLineAsync(content);
                    }

                    string readContent = await FileSystem.TryReadFileAsync(destination);
                    // Content from the file system contains trailing \r\n
                    Assert.Contains(content, readContent);
                }
            }
        }

        [Fact]
        public void DeleteNonexistentDirectory()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                Action a = () => FileSystem.DeleteDirectory(testDirectory.Path / @"dir", DeleteOptions.All);
                Assert.Throws<DirectoryNotFoundException>(a);
            }
        }

        [Fact]
        public void DeleteDirectoryNonexistentParentDirectory()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                Action a = () => FileSystem.DeleteDirectory(testDirectory.Path / @"parent\dir", DeleteOptions.All);
                Assert.Throws<DirectoryNotFoundException>(a);
            }
        }

        [Fact]
        public void DeleteNonexistentFile()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                FileSystem.DeleteFile(testDirectory.Path / @"dir");
            }
        }

        [Fact]
        public void DeleteFileNonexistentParentDirectory()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                FileSystem.DeleteFile(testDirectory.Path / @"parent\dir");
            }
        }

        [Fact]
        public void MoveFileSourceDirectoryDoesNotExist()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";
                Action a = () => FileSystem.MoveFile(source, destination, false);
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        public void MoveFileSourceDoesNotExist()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";

                FileSystem.CreateDirectory(source.Parent);

                Action a = () => FileSystem.MoveFile(source, destination, false);
                Assert.Throws<FileNotFoundException>(a);
            }
        }

        [Fact]
        public void MoveFileDestinationDirectoryDoesNotExist()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";

                FileSystem.CreateDirectory(source.Parent);
                FileSystem.WriteAllBytes(source, new byte[] {1, 2, 3});

                Action a = () => FileSystem.MoveFile(source, destination, false);
                Assert.Throws<DirectoryNotFoundException>(a);
            }
        }

        [Fact]
        public void MoveFileDestinationAlreadyExists()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";

                FileSystem.CreateDirectory(source.Parent);
                FileSystem.CreateDirectory(destination.Parent);
                FileSystem.WriteAllBytes(source, new byte[] {1, 2, 3});
                var destinationBytes = new byte[] {4, 5, 6};
                FileSystem.WriteAllBytes(destination, destinationBytes);

                Action a = () => FileSystem.MoveFile(source, destination, false);
                Assert.Throws<IOException>(a);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // We do not block deletion on MacOS
        public async Task MoveFileDestinationIsOpenedOverwrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var destination = testDirectory.Path / @"parent2\dst";

                FileSystem.CreateDirectory(source.Parent);
                FileSystem.CreateDirectory(destination.Parent);
                FileSystem.WriteAllBytes(source, new byte[] {1, 2, 3});
                var destinationBytes = new byte[] {4, 5, 6};
                FileSystem.WriteAllBytes(destination, destinationBytes);

                using (await FileSystem.OpenAsync(destination, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Action a = () => FileSystem.MoveFile(source, destination, true);
                    Assert.Throws<UnauthorizedAccessException>(a);
                }
            }
        }

        [Fact]
        public void MoveFileDestinationAlreadyExistsOverwrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                var sourceBytes = new byte[] {1, 2, 3};
                var destination = testDirectory.Path / @"parent2\dst";

                FileSystem.CreateDirectory(source.Parent);
                FileSystem.CreateDirectory(destination.Parent);
                FileSystem.WriteAllBytes(source, sourceBytes);
                var destinationBytes = new byte[] {4, 5, 6};
                FileSystem.WriteAllBytes(destination, destinationBytes);

                FileSystem.MoveFile(source, destination, true);

                Assert.True(sourceBytes.SequenceEqual(FileSystem.ReadAllBytes(destination)));
            }
        }

        [Fact]
        public void MoveDirectorySourceDoesNotExist()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / "source";
                var destinationPath = testDirectory.Path / "destination";

                Action a = () => FileSystem.MoveDirectory(sourcePath, destinationPath);
                Assert.Throws<DirectoryNotFoundException>(a);
            }
        }

        [Fact]
        public void MoveDirectoryDestinationExists()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / "source";
                var destinationPath = testDirectory.Path / "destination";
                FileSystem.CreateDirectory(sourcePath);
                FileSystem.CreateDirectory(destinationPath);

                Action a = () => FileSystem.MoveDirectory(sourcePath, destinationPath);
                Assert.Throws<IOException>(a);
            }
        }

        [Fact]
        public void MoveDirectoryEmpty()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / "source";
                var destinationPath = testDirectory.Path / "destination";
                FileSystem.CreateDirectory(sourcePath);

                FileSystem.MoveDirectory(sourcePath, destinationPath);

                Assert.False(FileSystem.DirectoryExists(sourcePath));
                Assert.True(FileSystem.DirectoryExists(destinationPath));
            }
        }

        [Fact]
        public void MoveDirectoryNotEmpty()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / "source";
                var destinationPath = testDirectory.Path / "destination";
                FileSystem.CreateDirectory(sourcePath);
                FileSystem.WriteAllBytes(sourcePath / "file1.txt", new byte[] {0});
                FileSystem.CreateDirectory(sourcePath / "dir");
                FileSystem.WriteAllBytes(sourcePath / "dir" / "file2.txt", new byte[] {0});

                FileSystem.MoveDirectory(sourcePath, destinationPath);

                Assert.False(FileSystem.DirectoryExists(sourcePath));
                Assert.True(FileSystem.DirectoryExists(destinationPath));
                Assert.True(FileSystem.FileExists(destinationPath / "file1.txt"));
                Assert.True(FileSystem.DirectoryExists(destinationPath / "dir"));
                Assert.True(FileSystem.FileExists(destinationPath / "dir" / "file2.txt"));
            }
        }

        [Fact]
        public async Task OpenWithNonexistentParentReturnsNull()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                Assert.Null(await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead));
            }
        }

        [Fact]
        public async Task OpenWithNonexistentReturnsNull()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                FileSystem.CreateDirectory(source.Parent);
                Assert.Null(await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead));
            }
        }

        [Fact]
        public async Task OpenWithUnsupportedModeThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                FileSystem.CreateDirectory(source.Parent);
                Func<Task> a = async () =>
                {
                    using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Append, ShareRead))
                    {
                    }
                };
                await Assert.ThrowsAsync<NotImplementedException>(a);
            }
        }

        [Fact]
        public async Task OpenReadOnlyWithNonexistentReturnsNull()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                FileSystem.CreateDirectory(source.Parent);
                Assert.Null(await FileSystem.OpenReadOnlyAsync(source, ShareRead));
            }
        }

        [Fact]
        public async Task OpenReadOnlyWitExistingFileAcquiresReadOnlyLock()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                FileSystem.CreateDirectory(source.Parent);
                FileSystem.WriteAllBytes(source, new byte[] { 1 });
                using (Stream sourceStream = await FileSystem.OpenReadOnlyAsync(source, ShareRead))
                {
                    Assert.NotNull(sourceStream);

                    // i can open another stream to a file with this lock since we're only opening read only.
                    using (
                        Stream anotherHandle =
                            await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, FileShare.Read))
                    {
                        Assert.NotNull(anotherHandle);
                    }
                }
            }
        }

        [Fact]
        public void CopyFileSucceeds()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourePath = testDirectory.Path / "source.txt";
                var destinationPath = testDirectory.Path / "destination.txt";
                FileSystem.WriteAllBytes(sourePath, new byte[] {1, 2, 3});
                FileSystem.CopyFile(sourePath, destinationPath, true);
                Assert.True(FileSystem.FileExists(destinationPath));
            }
        }

        [Fact]
        public void GetFileIdWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                Action a = () => FileSystem.GetFileId(source);
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        public void GetFileSizeMissingFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var path = testDirectory.Path / @"src";
                Action a = () => FileSystem.GetFileSize(path);
                Assert.Throws<FileNotFoundException>(a);
            }
        }

        [Fact]
        public void GetFileSizeExistingFileSucceeds()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var path = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                var size = FileSystem.GetFileSize(path);
                Assert.Equal(3, size);
            }
        }

        [Fact]
        public void GetFileAttributesWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                Action a = () => FileSystem.GetFileAttributes(source);
                Assert.Throws<FileNotFoundException>(a);
            }
        }

        [Fact]
        public void SetFileAttributesWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                Action a = () => FileSystem.SetFileAttributes(source, FileAttributes.Normal);
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        public void FileAttributesAreSubsetWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                Action a = () => FileSystem.FileAttributesAreSubset(source, FileAttributes.Normal);
                Assert.Throws<FileNotFoundException>(a);
            }
        }

        [Fact]
        public void SetFileAttributesOnDirectoryDoesNothing()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.CreateDirectory(source);
                FileSystem.SetFileAttributes(source, FileAttributes.Normal);
            }
        }

        [Fact]
        public void SetFileAttributesAsDirectoryDoesNothing()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                FileAttributes originalAttributes = FileSystem.GetFileAttributes(source);
                Assert.False(originalAttributes.HasFlag(FileAttributes.Directory));
                FileSystem.SetFileAttributes(source, originalAttributes | FileAttributes.Directory);
                Assert.False(FileSystem.GetFileAttributes(source).HasFlag(FileAttributes.Directory));
            }
        }

        [Fact]
        public void SetFileAttributesUnsupportedAttributeThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                Action a = () => FileSystem.SetFileAttributes(source, FileAttributes.Temporary);
                Assert.Throws<NotImplementedException>(a);
            }
        }

        [Fact]
        public void FileAttributesAreSubsetWithSupportedAttributes()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                FileSystem.SetFileAttributes(source, FileAttributes.Normal);
                Assert.True(FileSystem.FileAttributesAreSubset(source, FileAttributes.Normal | FileAttributes.ReadOnly | FileAttributes.Directory));
            }
        }

        [Fact]
        public void FileAttributesAreSubsetWithUnsupportedAttributesDoesNotThrow()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                Assert.False(FileSystem.FileAttributesAreSubset(source, FileAttributes.Temporary));
            }
        }

        [Fact]
        public void CreateDirectoryRoot()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                FileSystem.CreateDirectory(testDirectory.Path);
            }
        }

        [Fact]
        public void DeleteDirectoryOnFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                Action a = () => FileSystem.DeleteDirectory(source, DeleteOptions.All);
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        public void RecursiveDeleteDirectoryWithReadOnlyFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\src";
                FileSystem.CreateDirectory(source.Parent);
                FileSystem.WriteAllBytes(source, new byte[] {1});
                FileSystem.SetFileAttributes(source, FileAttributes.ReadOnly);
                Assert.True(FileSystem.FileExists(source), $"Directory {source.Path} doesn't exist");
                Action a = () => FileSystem.DeleteDirectory(source, DeleteOptions.All);
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // We do not block deletion on MacOS
        public void DeleteReadOnlyDirectoryThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1";
                FileSystem.CreateDirectory(source);
                FileSystem.SetFileAttributes(source, FileAttributes.ReadOnly | FileAttributes.Directory);
                Action a = () => FileSystem.DeleteDirectory(source, DeleteOptions.None);
                Assert.Throws<IOException>(a);
                FileSystem.SetFileAttributes(source, FileAttributes.Directory);
            }
        }

        [Fact]
        public void DeleteNonemptyDirectoryThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"parent1\folder1";
                FileSystem.CreateDirectory(source);
                Action a = () => FileSystem.DeleteDirectory(source.Parent, DeleteOptions.None);
                Assert.Throws<IOException>(a);
            }
        }

        [Fact]
        public async Task ReaderBlocksWriter()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Func<Task> a = async () =>
                    {
                        using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.Open, ShareNone))
                        {
                        }
                    };

                    await Assert.ThrowsAnyAsync<Exception>(a);
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // FileShare.Delete is not observed in Unix
        public async Task WriterBlocksWriter()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";

                using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.CreateNew, ShareDelete))
                {
                    Func<Task> a = async () =>
                    {
                        using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.Open, ShareDelete))
                        {
                        }
                    };

                    await Assert.ThrowsAsync<UnauthorizedAccessException>(a);
                }
            }
        }

        [Fact]
        public async Task WriterBlocksWriterShareNone()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";

                using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.CreateNew, ShareNone))
                {
                    Func<Task> a = async () =>
                    {
                        using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.Open, ShareNone))
                        {
                        }
                    };

                    await Assert.ThrowsAnyAsync<Exception>(a);
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // Only FileShare.None is acknowledged on *nix
        public async Task WriterBlocksReader()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.Open, ShareReadDelete))
                {
                    Func<Task> a = async () =>
                    {
                        using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead))
                        {
                        }
                    };

                    await Assert.ThrowsAsync<UnauthorizedAccessException>(a);
                }
            }
        }

        [Fact]
        public async Task WriterBlocksReaderShareNone()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] { 1 });
                using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.Open, ShareNone))
                {
                    Func<Task> a = async () =>
                    {
                        using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead))
                        {
                        }
                    };

                    await Assert.ThrowsAnyAsync<Exception>(a);
                }
            }
        }

        [Fact]
        public async Task ReaderDoesNotBlockReader()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead))
                    {
                    }
                }
            }
        }

        [Fact]
        public async Task ReaderDoesNotBlockDelete()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareReadDelete))
                {
                    FileSystem.DeleteFile(source);
                }
            }
        }

        [Fact(Skip = "QTestSkip")]
        [Trait("Category", "QTestSkip")] // Flaky
        public async Task ReaderBlocksDelete()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] { 1 });
                using (await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Action a = () => FileSystem.DeleteFile(source);
                    Assert.Throws<UnauthorizedAccessException>(a);
                }
            }
        }

        [Fact]
        public async Task WriterDoesNotBlockDelete()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.CreateNew, ShareDelete))
                {
                    FileSystem.DeleteFile(source);
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task WriterBlocksDelete()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.CreateNew, ShareNone))
                {
                    Action a = () => FileSystem.DeleteFile(source);
                    Assert.Throws<UnauthorizedAccessException>(a);
                }
            }
        }

        [Fact]
        public async Task OpenReadOnlyFileForWriteThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                FileSystem.SetFileAttributes(source, FileAttributes.ReadOnly);
                Func<Task> a = async () =>
                {
                    using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.Open, ShareRead))
                    {
                    }
                };

                await Assert.ThrowsAsync<UnauthorizedAccessException>(a);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // FileShare.Delete is essentially ignored on *nix
        public async Task OpenReadOnlyFileForWriteOverwrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                FileSystem.SetFileAttributes(source, FileAttributes.ReadOnly);
                using (await FileSystem.OpenAsync(source, FileAccess.Write, FileMode.Create, ShareDelete))
                {
                }
            }
        }

        [Fact]
        public async Task OpenFileCanStillBeReadAfterDelete()
        {
            const int count = 4 * 1024;
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                FileSystem.WriteAllBytes(source, new byte[count]);
                using (var stream = await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareDelete))
                {
                    FileSystem.DeleteFile(source);
                    var buffer = new byte[count];
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    Assert.Equal(count, bytesRead);
                }
            }
        }

        [Fact]
        [Trait("Category", "SkipDotNetCore")] // DotNetCore does not block nested opens, even with FileShare.None
        public async Task OpenDenyWriteFileForWriteThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath sourcePath = testDirectory.Path / @"src";

                using (Stream file = await FileSystem.OpenAsync(
                    sourcePath, FileAccess.Write, FileMode.CreateNew, ShareNone))
                {
                    file.WriteByte(1);
                    FileSystem.DenyFileWrites(sourcePath);
                }

                Assert.False(FileSystem.GetFileAttributes(sourcePath).HasFlag(FileAttributes.ReadOnly));

                Func<Task> a = async () =>
                {
                    using (await FileSystem.OpenAsync(sourcePath, FileAccess.Write, FileMode.Open, ShareDelete))
                    {
                    }
                };

                await Assert.ThrowsAsync<UnauthorizedAccessException>(a);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // Unix cannot block attribute changes
        public async Task SetFileAttributesDenyAttributesWritesFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath sourcePath = testDirectory.Path / @"src";

                using (Stream file = await FileSystem.OpenAsync(
                    sourcePath, FileAccess.Write, FileMode.CreateNew, ShareNone))
                {
                    file.WriteByte(1);
                    FileSystem.DenyAttributeWrites(sourcePath);
                }

                Assert.False(FileSystem.GetFileAttributes(sourcePath).HasFlag(FileAttributes.ReadOnly));

                Action a = () => FileSystem.SetFileAttributes(sourcePath, FileAttributes.ReadOnly);
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // Unix does not support denial of attribute changes
        public void SetFileAttributesDenyAttributeWritesDirectoryThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                FileSystem.DenyAttributeWrites(testDirectory.Path);

                Action a = () => FileSystem.SetFileAttributes(testDirectory.Path, FileAttributes.ReadOnly);
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        public void DenyAttributeWritesWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath sourcePath = testDirectory.CreateRandomFileName();

                Action a = () => FileSystem.DenyAttributeWrites(sourcePath);

                Exception ex = Assert.ThrowsAny<Exception>(a);
                Assert.Contains(sourcePath.Path, ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void AllowAttributeWritesWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath sourcePath = testDirectory.CreateRandomFileName();

                Action a = () => FileSystem.AllowAttributeWrites(sourcePath);

                Exception ex = Assert.ThrowsAny<Exception>(a);
                Assert.Contains(sourcePath.Path, ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void DenyFileWritesWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath sourcePath = testDirectory.CreateRandomFileName();

                Action a = () => FileSystem.DenyFileWrites(sourcePath);

                Exception ex = Assert.ThrowsAny<Exception>(a);
                Assert.Contains(sourcePath.Path, ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void AllowFileWritesWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath sourcePath = testDirectory.CreateRandomFileName();

                Action a = () => FileSystem.AllowFileWrites(sourcePath);

                Exception ex = Assert.ThrowsAny<Exception>(a);
                Assert.Contains(sourcePath.Path, ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // TODO: Update to check for immutable (?)
        public async Task CanReadDenyWriteFile()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath sourcePath = testDirectory.Path / @"src";

                using (Stream file = await FileSystem.OpenAsync(
                    sourcePath, FileAccess.Write, FileMode.CreateNew, ShareNone))
                {
                    file.WriteByte(1);
                    FileSystem.DenyFileWrites(sourcePath);
                }

                Assert.False(FileSystem.GetFileAttributes(sourcePath).HasFlag(FileAttributes.ReadOnly));

                using (Stream file = await FileSystem.OpenAsync(
                    sourcePath, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Assert.Equal(1, file.ReadByte());
                }
            }
        }

        [Fact]
        public async Task CanRemoveDenyWrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath sourcePath = testDirectory.Path / @"src";

                using (Stream file = await FileSystem.OpenAsync(
                    sourcePath, FileAccess.Write, FileMode.CreateNew, ShareNone))
                {
                    file.WriteByte(1);
                    FileSystem.DenyFileWrites(sourcePath);
                }

                Assert.False(FileSystem.GetFileAttributes(sourcePath).HasFlag(FileAttributes.ReadOnly));

                FileSystem.AllowFileWrites(sourcePath);

                using (Stream file = await FileSystem.OpenAsync(
                    sourcePath, FileAccess.Write, FileMode.Create, ShareDelete))
                {
                    file.WriteByte(0);
                }
            }
        }

        [Fact]
        public async Task OpenDirectoryThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"dir";
                FileSystem.CreateDirectory(source);
                Func<Task> a = async () =>
                {
                    using (await FileSystem.OpenAsync(
                        source, FileAccess.Read, FileMode.Open, ShareNone))
                    {
                    }
                };

                await Assert.ThrowsAsync<UnauthorizedAccessException>(a);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void InvalidDriveThrows()
        {
            Action a = () =>
            {
                for (char driveLetter = 'a'; driveLetter <= 'z'; driveLetter++)
                {
                    var source = new AbsolutePath(driveLetter + @":\dir");
                    FileSystem.CreateDirectory(source);
                }
            };
            Assert.ThrowsAny<IOException>(a);
        }

        [Fact]
        public void TreatFileAsDirectoryThrowsRootCase()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"file1";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                Action a = () => FileSystem.WriteAllBytes(source / "file2", new byte[] {2});
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        public void TreatFileAsDirectoryThrowsChildCase()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"dir1\file1";
                FileSystem.CreateDirectory(source.Parent);
                FileSystem.WriteAllBytes(source, new byte[] {1});
                Action a = () => FileSystem.WriteAllBytes(source / "file2", new byte[] {2});
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        public async Task ReadStreamCannotBeWrittenTo()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"file1";
                FileSystem.WriteAllBytes(source, new byte[] {1});
                using (var stream = await FileSystem.OpenAsync(source, FileAccess.Read, FileMode.Open, ShareRead))
                {
                    Assert.False(stream.CanWrite);

                    try
                    {
                        await stream.WriteAsync(new byte[] {1}, 0, 1);
                        Assert.True(false);
                    }
                    catch (NotSupportedException)
                    {
                    }

                    Action a1 = () => stream.WriteByte(1);
                    Assert.Throws<NotSupportedException>(a1);

                    Action a2 = () => stream.BeginWrite(new byte[] {1}, 0, 1, null, null);
                    Assert.Throws<NotSupportedException>(a2);
                }
            }
        }

        [Fact]
        public async Task StreamCanBeDisposedTwice()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"file1";

                using (var stream = await FileSystem.OpenAsync(
                    source, FileAccess.Write, FileMode.CreateNew, ShareDelete))
                {
#pragma warning disable AsyncFixer02
                    stream.Dispose();
#pragma warning restore AsyncFixer02
                }
            }
        }

        [Fact]
        public void GetHardLinkCountWithNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var source = testDirectory.Path / @"src";
                Action a = () => FileSystem.GetHardLinkCount(source);
                Assert.ThrowsAny<Exception>(a);
            }
        }

        [Fact]
        public void GetHardLinkCountSucceeds()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                AbsolutePath sourcePath = testDirectory.Path / @"dir1\file1";
                AbsolutePath linkPath = testDirectory.Path / @"dir1\link1";

                Assert.Equal(1, FileSystem.GetHardLinkCount(sourcePath));

                FileSystem.CreateHardLink(sourcePath, linkPath, false);

                Assert.Equal(2, FileSystem.GetHardLinkCount(sourcePath));
                Assert.Equal(2, FileSystem.GetHardLinkCount(linkPath));
            }
        }

        [Fact]
        public void HardLinkCopiesContent()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                AbsolutePath sourcePath = testDirectory.Path / @"dir1\file1";
                AbsolutePath linkPath = testDirectory.Path / @"dir1\link1";

                FileSystem.CreateHardLink(sourcePath, linkPath, false);

                byte[] sourceBytes = FileSystem.ReadAllBytes(sourcePath);
                byte[] linkBytes = FileSystem.ReadAllBytes(linkPath);

                Assert.True(sourceBytes.SequenceEqual(linkBytes));
            }
        }

        [Fact]
        public void HardLinkContentChangesWhenSourceContentChanges()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                AbsolutePath sourcePath = testDirectory.Path / @"dir1\file1";
                AbsolutePath linkPath = testDirectory.Path / @"dir1\link1";

                // Write original source bytes
                byte[] sourceBytes = ThreadSafeRandom.GetBytes(10);
                FileSystem.WriteAllBytes(sourcePath, sourceBytes);

                FileSystem.CreateHardLink(sourcePath, linkPath, false);

                // Change the source bytes
                byte[] newBytes = ThreadSafeRandom.GetBytes(10);
                FileSystem.WriteAllBytes(sourcePath, newBytes);
                Assert.False(newBytes.SequenceEqual(sourceBytes));

                byte[] linkBytes = FileSystem.ReadAllBytes(linkPath);

                Assert.True(newBytes.SequenceEqual(linkBytes));
            }
        }

        [Fact]
        public void HardLinkContentChangesWhenLinkContentChanges()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                AbsolutePath sourcePath = testDirectory.Path / @"dir1\file1";
                AbsolutePath linkPath = testDirectory.Path / @"dir1\link1";

                // Write original source bytes
                byte[] sourceBytes = ThreadSafeRandom.GetBytes(10);
                FileSystem.WriteAllBytes(sourcePath, sourceBytes);

                FileSystem.CreateHardLink(sourcePath, linkPath, false);

                // Change the link bytes
                byte[] newBytes = ThreadSafeRandom.GetBytes(10);
                FileSystem.WriteAllBytes(linkPath, newBytes);
                Assert.False(newBytes.SequenceEqual(sourceBytes));

                byte[] newSourceBytes = FileSystem.ReadAllBytes(sourcePath);

                Assert.True(newBytes.SequenceEqual(newSourceBytes));
            }
        }

        [Fact]
        public void HardLinkToNonexistentFileThrows()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                var result = FileSystem.CreateHardLink(
                    testDirectory.Path / @"fileThatDoesNotExist.txt", testDirectory.Path / @"link1", false);
                Assert.Equal(CreateHardLinkResult.FailedSourceDoesNotExist, result);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void WriteAllBytesToLongFilePaths()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var max = FileSystemConstants.MaxShortPath;
                foreach (var length in new[] { max - 10, max + 10, max + 100, max + 1000 })
                {
                    bool succeeded = writeAllBytes(testDirectory, length);
                    var expectedSuccess = length < FileSystemConstants.MaxShortPath || FileSystemConstants.LongPathsSupported;

                    Assert.Equal(expectedSuccess, succeeded);
                }
            }

            bool writeAllBytes(DisposableDirectory testDirectory, int length)
            {
                try
                {
                    AbsolutePath rootPath = testDirectory.Path / @"dir";
                    FileSystem.CreateDirectory(rootPath);
                    AbsolutePath sourceFilePath = FileSystem.MakeLongPath(rootPath, length);

                    // Write original source bytes
                    byte[] sourceBytes = ThreadSafeRandom.GetBytes(10);
                    FileSystem.WriteAllBytes(sourceFilePath, sourceBytes);
                    return true;
                }
                catch (PathTooLongException)
                {
                    return false;
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void HardLinkToLongSourceAndDestinationFilePaths()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                var max = FileSystemConstants.MaxShortPath;
                foreach (var length in new[] { max - 10, max + 10, max + 100, max + 1000 })
                {
                    if ((testDirectory.Path.Length + length) < FileSystemConstants.MaxShortPath || AbsolutePath.LongPathsSupported)
                    {
                        // Skipping if the path is a long path but the current system doesn't support it.
                        // Because in this case we just can't create a directory to store the source file.
                        var result = HardLinkToLongFileSourceAndDestinationPath(testDirectory, length);
                        var expectedCreateHardLinkResult = CreateHardLinkResult.Success;
                        Assert.True(
                            expectedCreateHardLinkResult == result,
                            $"Expected {expectedCreateHardLinkResult}, found {result}. Result: {result.ToString()}, Destination path length: {length}.");
                    }
                }
            }
        }

        private CreateHardLinkResult HardLinkToLongFileSourceAndDestinationPath(DisposableDirectory testDirectory, int length)
        {
            AbsolutePath rootSourcePath = testDirectory.Path / "source";
            FileSystem.CreateDirectory(rootSourcePath);
            AbsolutePath sourcePath = FileSystem.MakeLongPath(rootSourcePath, length);

            AbsolutePath rootLinkPath = testDirectory.Path / @"dir";
            FileSystem.CreateDirectory(rootLinkPath);
            AbsolutePath linkPath = FileSystem.MakeLongPath(rootLinkPath, length);

            // Write original source bytes
            byte[] sourceBytes = ThreadSafeRandom.GetBytes(10);
            FileSystem.WriteAllBytes(sourcePath, sourceBytes);

            return FileSystem.CreateHardLink(sourcePath, linkPath, false);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void HardLinkToLongDestinationFilePaths()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                var max = FileSystemConstants.MaxShortPath;
                foreach (var length in new[] { max - 10, max + 10, max + 100, max + 1000 })
                {
                    if ((testDirectory.Path.Length + length) < FileSystemConstants.MaxShortPath || AbsolutePath.LongPathsSupported)
                    {
                        // Skipping if the path is a long path but the current system doesn't support it.
                        // Because in this case we just can't create a directory to store the source file.
                        var result = HardLinkToLongFileDestinationPath(testDirectory, length);
                        var expectedCreateHardLinkResult = CreateHardLinkResult.Success;
                        Assert.True(result == expectedCreateHardLinkResult, $"Expected {expectedCreateHardLinkResult}, found {result}. Result: {result.ToString()}, Destination path length: {length}.");
                    }
                }
            }
        }

        private CreateHardLinkResult HardLinkToLongFileDestinationPath(DisposableDirectory testDirectory, int length)
        {
            AbsolutePath sourcePath = testDirectory.Path / @"dir1\file1";
            AbsolutePath rootLinkPath = testDirectory.Path / @"dir";

            FileSystem.CreateDirectory(rootLinkPath);
            AbsolutePath linkPath = FileSystem.MakeLongPath(rootLinkPath, length);

            // Write original source bytes
            byte[] sourceBytes = ThreadSafeRandom.GetBytes(10);
            FileSystem.WriteAllBytes(sourcePath, sourceBytes);

            return FileSystem.CreateHardLink(sourcePath, linkPath, false);
        }

        [Fact]
        public void MovingFileMaintainsHardLinkProperties()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                AbsolutePath sourcePath = testDirectory.Path / @"dir1\file1";
                AbsolutePath linkPath = testDirectory.Path / @"dir1\link1";

                // Write original source bytes
                byte[] sourceBytes = ThreadSafeRandom.GetBytes(10);
                FileSystem.WriteAllBytes(sourcePath, sourceBytes);

                // Create a hard link and then move it to a new location
                FileSystem.CreateHardLink(sourcePath, linkPath, false);
                AbsolutePath newLinkPath = testDirectory.Path / @"dir1\newLink1";
                FileSystem.MoveFile(linkPath, newLinkPath, false);

                // Change the new link bytes
                byte[] newLinkBytes = ThreadSafeRandom.GetBytes(10);
                FileSystem.WriteAllBytes(newLinkPath, newLinkBytes);
                Assert.False(newLinkBytes.SequenceEqual(sourceBytes));

                byte[] newSourceBytes = FileSystem.ReadAllBytes(sourcePath);
                Assert.True(newLinkBytes.SequenceEqual(newSourceBytes));
            }
        }

        [Fact]
        public void CreatingHardLinkWhenFileAlreadyExistsReturnsFalse()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileSystem.WriteAllBytes(destinationPath, ThreadSafeRandom.GetBytes(10));

                var result = FileSystem.CreateHardLink(sourcePath, destinationPath, false);
                Assert.Equal(CreateHardLinkResult.FailedDestinationExists, result);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // Increasing MaxLinks to int.MaxValue just hangs, never fails out
        public void HardLinkOverRefCountLimitReturnsFalse()
        {
            int hardLinkLimit = FileSystemConstants.MaxLinks;

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);
                AbsolutePath pathToFile = testDirectory.Path / "dir1" / "file1";

                for (int k = 1; k <= hardLinkLimit - 1; k++)
                {
                    AbsolutePath pathToLink = testDirectory.Path / "dir1" / ("newFile" + k);
                    var successfulHardLinkResult = FileSystem.CreateHardLink(pathToFile, pathToLink, false);
                    Assert.True(CreateHardLinkResult.Success == successfulHardLinkResult, $"Error at hard link number {k}");
                }

                AbsolutePath pathToLinkOverLimit = testDirectory.Path / "dir1" / ("newFile" + hardLinkLimit);
                var result = FileSystem.CreateHardLink(pathToFile, pathToLinkOverLimit, false);
                Assert.Equal(CreateHardLinkResult.FailedMaxHardLinkLimitReached, result);
                Assert.Equal(hardLinkLimit, FileSystem.GetHardLinkCount(pathToFile));
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // See Bug #1355843
        public void CreatingHardLinkWhenDenyWritesFileAlreadyExistsOverwrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileSystem.WriteAllBytes(destinationPath, ThreadSafeRandom.GetBytes(10));
                FileSystem.DenyFileWrites(destinationPath);
                var result = FileSystem.CreateHardLink(sourcePath, destinationPath, true);
                Assert.Equal(CreateHardLinkResult.Success, result);
                Assert.True(FileSystem.ReadAllBytes(sourcePath).SequenceEqual(FileSystem.ReadAllBytes(destinationPath)));
            }
        }

        [Fact]
        public async Task CreatingFileWhenDenyWritesFileAlreadyExistsOverwrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(destinationPath, ThreadSafeRandom.GetBytes(10));
                FileSystem.DenyFileWrites(destinationPath);
                using (await FileSystem.OpenAsync(
                    destinationPath, FileAccess.ReadWrite, FileMode.Create, ShareDelete))
                {
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // See Bug #1355843
        public void CreatingHardLinkWhenFileAlreadyExistsOverwrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileSystem.WriteAllBytes(destinationPath, ThreadSafeRandom.GetBytes(10));

                var result = FileSystem.CreateHardLink(sourcePath, destinationPath, true);
                Assert.Equal(CreateHardLinkResult.Success, result);
                Assert.True(FileSystem.ReadAllBytes(sourcePath).SequenceEqual(FileSystem.ReadAllBytes(destinationPath)));
            }
        }

        [Fact]
        public async Task CreateOverwriteDenyWriteFile()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(destinationPath, ThreadSafeRandom.GetBytes(10));
                FileSystem.DenyFileWrites(destinationPath);
                using (await FileSystem.OpenAsync(
                    destinationPath, FileAccess.Write, FileMode.Create, ShareDelete))
                {
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // FileShare.Read and FileShare.Delete are effectively ignored on *nix
        public async Task CreatingHardLinkWhenFileAlreadyOpenOverwrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileSystem.WriteAllBytes(destinationPath, ThreadSafeRandom.GetBytes(10));

                using (await FileSystem.OpenAsync(destinationPath, FileAccess.Read, FileMode.Open, ShareReadDelete))
                {
                    var result = FileSystem.CreateHardLink(sourcePath, destinationPath, true);
                    Assert.Equal(CreateHardLinkResult.FailedAccessDenied, result);
                }
            }
        }

        [Fact]
        public async Task CreateOverwriteOneLinkWhileOtherLinkIsOpen()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                Assert.Equal(CreateHardLinkResult.Success, FileSystem.CreateHardLink(sourcePath, destinationPath, true));
                using (await FileSystem.OpenAsync(sourcePath, FileAccess.Read, FileMode.Open, ShareReadDelete))
                {
                    using (await FileSystem.OpenAsync(
                        destinationPath, FileAccess.Write, FileMode.Create, ShareDelete))
                    {
                    }
                }
            }
        }

        [Fact(Skip = "MoveFile fails with UnauthorizedAccessException starting December 2017")]
        public async Task MoveOverwriteOneLinkWhileOtherLinkIsOpen()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                var newContentPath = testDirectory.Path / @"new.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileSystem.WriteAllBytes(newContentPath, ThreadSafeRandom.GetBytes(10));
                Assert.Equal(CreateHardLinkResult.Success, FileSystem.CreateHardLink(sourcePath, destinationPath, true));
                using (await FileSystem.OpenAsync(sourcePath, FileAccess.Read, FileMode.Open, ShareReadDelete))
                {
                    FileSystem.MoveFile(newContentPath, destinationPath, true);
                }
            }
        }

        [Fact]
        public void MoveOverwriteFileWithDenyWrites()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var destinationPath = testDirectory.Path / @"destination.txt";
                var newContentPath = testDirectory.Path / @"new.txt";
                FileSystem.WriteAllBytes(destinationPath, ThreadSafeRandom.GetBytes(10));
                FileSystem.DenyFileWrites(destinationPath);
                FileSystem.WriteAllBytes(newContentPath, ThreadSafeRandom.GetBytes(10));
                FileSystem.MoveFile(newContentPath, destinationPath, true);
            }
        }

        [Fact(Skip="Second CreateHardLink fails with AccessDenied starting December 2017")]
        public async Task CreateLinkOverwriteOneLinkWhileOtherLinkIsOpen()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                var newContentPath = testDirectory.Path / @"new.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileSystem.WriteAllBytes(newContentPath, ThreadSafeRandom.GetBytes(10));
                Assert.Equal(CreateHardLinkResult.Success, FileSystem.CreateHardLink(sourcePath, destinationPath, true));
                using (await FileSystem.OpenAsync(sourcePath, FileAccess.Read, FileMode.Open, ShareReadDelete))
                {
                    Assert.Equal(CreateHardLinkResult.Success, FileSystem.CreateHardLink(newContentPath, destinationPath, true));
                }
            }
        }

        [Fact]
        public async Task DeleteOneLinkWhileOtherLinkIsOpenDeleteSharingSucceeds()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                Assert.Equal(CreateHardLinkResult.Success, FileSystem.CreateHardLink(sourcePath, destinationPath, true));
                using (await FileSystem.OpenAsync(sourcePath, FileAccess.Read, FileMode.Open, ShareReadDelete))
                {
                    FileSystem.DeleteFile(destinationPath);
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task DeleteOneLinkWhileOtherLinkIsOpenReadOnlySharingFails()
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
                    Assert.Throws<UnauthorizedAccessException>(a);
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task DeleteOneLinkWhileOneOtherLinkIsOpenReadOnlySharingFails()
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
                    Assert.Throws<UnauthorizedAccessException>(a);
                }
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // See Bug #1355843
        public void CreatingHardLinkWhenReadOnlyFileAlreadyExistsOverwrite()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / @"source.txt";
                var destinationPath = testDirectory.Path / @"destination.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileSystem.WriteAllBytes(destinationPath, ThreadSafeRandom.GetBytes(10));
                FileSystem.SetFileAttributes(destinationPath, FileAttributes.ReadOnly);

                var result = FileSystem.CreateHardLink(sourcePath, destinationPath, true);
                Assert.Equal(CreateHardLinkResult.Success, result);
                Assert.True(FileSystem.ReadAllBytes(sourcePath).SequenceEqual(FileSystem.ReadAllBytes(destinationPath)));
            }
        }

        [Fact]
        public void CreateHardLinkCopiesReadOnlyProperties()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                var sourcePath = testDirectory.Path / @"dir1\file1";
                var linkPath = testDirectory.Path / @"dir1\link.txt";
                FileSystem.CreateHardLink(sourcePath, linkPath, false);

                FileAttributes linkAttributesBefore = FileSystem.GetFileAttributes(linkPath);
                Assert.False(linkAttributesBefore.HasFlag(FileAttributes.ReadOnly));

                FileSystem.SetFileAttributes(sourcePath, FileAttributes.ReadOnly);

                FileAttributes linkAttributesAfter = FileSystem.GetFileAttributes(linkPath);
                Assert.True(linkAttributesAfter.HasFlag(FileAttributes.ReadOnly));
            }
        }

        [Fact]
        public void DeletingHardLinkKeepsOriginalContent()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                var sourcePath = testDirectory.Path / @"dir1\file1";
                var linkPath = testDirectory.Path / @"dir1\link.txt";

                FileSystem.CreateHardLink(sourcePath, linkPath, false);
                Assert.True(FileSystem.FileExists(linkPath));

                FileSystem.DeleteFile(linkPath);

                Assert.False(FileSystem.FileExists(linkPath));
                Assert.True(FileSystem.FileExists(sourcePath));
            }
        }

        [Fact]
        public void DeletingOriginalContentKeepsHardLink()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                var sourcePath = testDirectory.Path / @"dir1\file1";
                var linkPath = testDirectory.Path / @"dir1\link.txt";

                FileSystem.CreateHardLink(sourcePath, linkPath, false);
                Assert.True(FileSystem.FileExists(linkPath));

                FileSystem.DeleteFile(sourcePath);

                Assert.True(FileSystem.FileExists(linkPath));
                Assert.False(FileSystem.FileExists(sourcePath));
            }
        }

        [Fact]
        public void WriteAllBytesThrowsWithoutDirectory()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                var newDir = testDirectory.Path / "newDir";
                var file = newDir / "file1";

                Action a = () => FileSystem.WriteAllBytes(file, new byte[0]);
                Assert.Throws<DirectoryNotFoundException>(a);
            }
        }

        private void CreateTestTree(DisposableDirectory testDirectory)
        {
            FileSystem.CreateDirectory(testDirectory.Path / @"dir1");
            FileSystem.CreateDirectory(testDirectory.Path / @"dir2");
            FileSystem.CreateDirectory(testDirectory.Path / @"dir2\dir3");
            FileSystem.WriteAllBytes(testDirectory.Path / @"dir1\file1", new byte[] {1});
            FileSystem.WriteAllBytes(testDirectory.Path / @"dir2\file2", new byte[] {2});
            FileSystem.WriteAllBytes(testDirectory.Path / @"dir2\dir3\file3", new byte[] {3});
            FileSystem.WriteAllBytes(testDirectory.Path / @"file4", new byte[] {4});
        }

        [Fact]
        public void EnumerateFilesRecurse()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                List<FileInfo> infos = FileSystem.EnumerateFiles(testDirectory.Path, EnumerateOptions.Recurse).ToList();
                Assert.Equal(4, infos.Count);
                Assert.True(infos.Any(info => info.FullPath == (testDirectory.Path / @"dir1\file1") && info.Length == 1));
                Assert.True(infos.Any(info => info.FullPath == (testDirectory.Path / @"dir2\file2") && info.Length == 1));
                Assert.True(
                    infos.Any(info => info.FullPath == (testDirectory.Path / @"dir2\dir3\file3") && info.Length == 1));
                Assert.True(infos.Any(info => info.FullPath == (testDirectory.Path / @"file4") && info.Length == 1));
            }
        }

        [Fact]
        public void EnumerateFilesNoRecurse()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                List<AbsolutePath> files = FileSystem.EnumerateFiles(testDirectory.Path, EnumerateOptions.None)
                    .Select(fi => fi.FullPath)
                    .ToList();
                Assert.Equal(1, files.Count);
                Assert.True(files.Any(path => path.Path == (testDirectory.Path / @"file4").Path));
            }
        }

        [Fact]
        public void EnumerateDirectoriesRecurse()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                List<AbsolutePath> files =
                    FileSystem.EnumerateDirectories(testDirectory.Path, EnumerateOptions.Recurse).ToList();
                Assert.Equal(3, files.Count);
                Assert.True(files.Any(path => path.Path == (testDirectory.Path / @"dir1").Path));
                Assert.True(files.Any(path => path.Path == (testDirectory.Path / @"dir2").Path));
                Assert.True(files.Any(path => path.Path == (testDirectory.Path / @"dir2\dir3").Path));
            }
        }

        [Fact]
        public void EnumerateDirectoriesNoRecurse()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                CreateTestTree(testDirectory);

                List<AbsolutePath> files =
                    FileSystem.EnumerateDirectories(testDirectory.Path, EnumerateOptions.None).ToList();
                Assert.Equal(2, files.Count);
                Assert.True(files.Any(path => path.Path == (testDirectory.Path / @"dir1").Path));
                Assert.True(files.Any(path => path.Path == (testDirectory.Path / @"dir2").Path));
            }
        }
    }
}
