// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
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
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Xunit.Abstractions;

namespace ContentStoreTest.FileSystem
{
    [Trait("Category", "Integration")]
    public sealed class PassThroughFileSystemTests : AbsFileSystemTests
    {
        public PassThroughFileSystemTests(ITestOutputHelper helper)
            : base(helper, () => new PassThroughFileSystem(TestGlobal.Logger))
        {
        }
        [Fact]
        public async Task CopyFileAsyncShouldOverrideContentWhenReplaceExistingFlagIsPassed()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / "source.txt";
                var destinationPath = testDirectory.Path / "destination.txt";
                string sourceContent = "Short text";
                string defaultDestinationContent = sourceContent + ". Some extra stuff.";

                FileSystem.WriteAllText(sourcePath, sourceContent);
                FileSystem.WriteAllText(destinationPath, defaultDestinationContent);

                // CopyFileAsync should truncate the existing content.
                await FileSystem.CopyFileAsync(sourcePath, destinationPath, true);
                var destinationContent = FileSystem.ReadAllText(destinationPath);
                Assert.Equal(sourceContent, destinationContent);
            }
        }

        [Fact]
        public async Task DoNotFailWithFileNotFoundExceptionWhenTheFileIsDeletedDuringOpenAsyncCall()
        {
            // There was a race condition in PassThroughFileSystem.OpenAsync implementation
            // that could have caused FileNotFoundException instead of returning null.
            using (var testDirectory = new DisposableDirectory(FileSystem, FileSystem.GetTempPath() / "TestDir"))
            {
                var path = testDirectory.Path / Guid.NewGuid().ToString();

                int iterations = 100;

                for(var i = 0; i < iterations; i++)
                {
                    await writeAllTextAsync(FileSystem, path, "test", attemptCount: 5);
                    var t1 = openAndClose(FileSystem, path);
                    var t2 = openAndClose(FileSystem, path);
                    var t3 = openAndClose(FileSystem, path);
                    var t4 = openAndClose(FileSystem, path);
                    FileSystem.DeleteFile(path);

                    await Task.WhenAll(t1, t2, t3, t4);
                }
            }

            static async Task openAndClose(IAbsFileSystem fileSystem, AbsolutePath path)
            {
                await Task.Yield();
                try
                {
                    // This operation may fail with UnauthorizedAccessException because
                    // the test tries to delete the file in the process.
                    using var f = await fileSystem.OpenAsync(path, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete);
                }
                catch(UnauthorizedAccessException)
                { }
            }

            static async Task writeAllTextAsync(IAbsFileSystem fileSystem, AbsolutePath path, string content, int attemptCount)
            {
                // There is a subtle race condition dealing with file system deletion.
                // It is possible that the deletion that happened on the previous iteration is not fully done yet
                // and the following WriteAllText may fail with 'FileNotFound' exception.
                for (int i = 0; i < attemptCount; i++)
                {
                    try
                    {
                        fileSystem.WriteAllText(path, content);
                        await Task.Delay(10);
                    }
                    catch (FileNotFoundException)
                    { }
                }
            }
        }

        [Fact(Skip = "Test does not work on all versions of Windows where BuildXL tests run")]
        [Trait("Category", "WindowsOSOnly")] 
        public async Task TestDeleteWithOpenFileStream()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem, FileSystem.GetTempPath() / "TestDir"))
            {
                var path = testDirectory.Path / Guid.NewGuid().ToString();
                var otherPath = testDirectory.Path / Guid.NewGuid().ToString();
                FileSystem.WriteAllText(path, "Hello");
                FileSystem.WriteAllText(otherPath, "Other");

                using (Stream stream = await FileSystem.OpenAsync(path, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete))
                {
                    FileUtilities.PosixDeleteMode = PosixDeleteMode.RunFirst;

                    FileSystem.FileExists(path).Should().BeFalse();

                    FileSystem.MoveFile(otherPath, path, replaceExisting: false);
                }
            }
        }

        [Fact]
        public void TryGetFileAttributeReturnsFalseForNonExistingFile()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / Guid.NewGuid().ToString();
                bool result = FileSystem.TryGetFileAttributes(sourcePath, out _);
                Assert.False(result);
            }
        }

        [Fact]
        public void CreateHardLinkFromFileWithDenyWrites()
        {
            // This test mimics an actual behavior:
            // The content inside the cache is ackled with deny writes.
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path /  "source.txt";
                var destinationPath = testDirectory.Path /  "destination.txt";
                
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));
                FileSystem.DenyFileWrites(sourcePath);

                var result = FileSystem.CreateHardLink(sourcePath, destinationPath, false);
                result.Should().Be(CreateHardLinkResult.Success, $"SourcePath='{sourcePath}', DestinationPath={destinationPath}");
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] 
        public void CreateHardLinkResultsBasedOnDestinationDirectoryExistence()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var sourcePath = testDirectory.Path / "source.txt";
                FileSystem.WriteAllBytes(sourcePath, ThreadSafeRandom.GetBytes(10));

                // Covering short path case
                createHardLinkResultsBasedOnDestinationDirectoryExistenceCore(testDirectory.Path, sourcePath, new string('a', 10));

                // Covering long path case
                createHardLinkResultsBasedOnDestinationDirectoryExistenceCore(testDirectory.Path, sourcePath, new string('a', 200));
            }

            void createHardLinkResultsBasedOnDestinationDirectoryExistenceCore(AbsolutePath root, AbsolutePath sourcePath, string pathSegment)
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
                result.Should().Be(CreateHardLinkResult.Success, $"SourcePath='{sourcePath}', DestinationPath={destinationPath}");
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

        [Fact]
        public async Task DeleteOnCloseRemovesFileTest()
        {
            using var testDirectory = new DisposableDirectory(FileSystem, FileSystem.GetTempPath() / "TestDir");
            var filePath = testDirectory.Path / "Foo.txt";

            using (Stream file = await FileSystem.OpenSafeAsync(
                filePath,
                FileAccess.Write,
                FileMode.CreateNew,
                FileShare.None,
                FileOptions.DeleteOnClose))
            {
                await file.WriteAsync(Enumerable.Range(1, 10).Select(b => (byte)b).ToArray(), 0, 10);
                Assert.True(FileSystem.FileExists(filePath));
            }

            Assert.False(FileSystem.FileExists(filePath));
        }

        [Fact]
        public async Task CanMoveFileIfYouShareDelete()
        {
            using var testDirectory = new DisposableDirectory(FileSystem, FileSystem.GetTempPath() / "TestDir");
            var filePath = testDirectory.Path / "Foo.txt";
            var replacementFilePath = testDirectory.Path / "Bar.txt";

            using (Stream file = await FileSystem.OpenSafeAsync(
                filePath,
                FileAccess.Write,
                FileMode.CreateNew,
                FileShare.Delete,
                FileOptions.DeleteOnClose))
            {
                await file.WriteAsync(Enumerable.Range(1, 10).Select(b => (byte)b).ToArray(), 0, 10);
                Assert.True(FileSystem.FileExists(filePath));

                FileSystem.MoveFile(filePath, replacementFilePath, replaceExisting: true);

                Assert.False(FileSystem.FileExists(filePath));
                Assert.True(FileSystem.FileExists(replacementFilePath));
            }

            Assert.False(FileSystem.FileExists(filePath));
        }

        [Theory(Skip = "For manual experiments only")] // for running manually only!
        //[Theory] // for running manually only!
        [InlineData(/*dop*/1, /*fileSize*/500_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048, /*writeChunkSize*/4048)]
        [InlineData(/*dop*/1, /*fileSize*/500_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048, /*writeChunkSize*/4048*50)]
        [InlineData(/*dop*/1, /*fileSize*/500_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048*50, /*writeChunkSize*/4048*50)]

        [InlineData(/*dop*/1, /*fileSize*/100_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048, /*writeChunkSize*/4048)]
        [InlineData(/*dop*/1, /*fileSize*/100_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048, /*writeChunkSize*/4048*50)]

        [InlineData(/*dop*/10, /*fileSize*/500_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048, /*writeChunkSize*/4048)]
        [InlineData(/*dop*/10, /*fileSize*/500_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048, /*writeChunkSize*/4048 * 50)]

        [InlineData(/*dop*/40, /*fileSize*/500_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048, /*writeChunkSize*/4048)]
        [InlineData(/*dop*/40, /*fileSize*/500_000, /*totalFilesSize*/100_000_000, /*fileBufferSize*/4048, /*writeChunkSize*/4048 * 50)]
        public async Task WriteFilePlaygroundDurationTests(
            int degreeOfParallelism,
            int fileSize,
            long totalFilesSize,
            int fileBufferSize,
            int writeChunkSize)
        {
            int numberOfFiles = (int)(totalFilesSize / fileSize);

            var context = new OperationContext(new Context(TestGlobal.Logger));

            context.TraceDebug($"Writing {numberOfFiles} files...");
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                

                await ParallelAlgorithms.WhenDoneAsync(
                    degreeOfParallelism, CancellationToken.None, (s, i) => writeRandomDataToFile(i),
                    Enumerable.Range(1, numberOfFiles).ToArray());

                async Task<(TimeSpan duration, TimeSpan sumDuration)> writeRandomDataToFile(int index)
                {
                    await Task.Yield();
                    var destinationPath = testDirectory.Path / $"destination{index}.txt";

                    var sw = StopwatchSlim.Start();
                    using (StreamWithLength? fs = await FileSystem.OpenAsync(
                        destinationPath,
                        FileAccess.Write,
                        FileMode.Create,
                        FileShare.None,
                        FileOptions.None,
                        fileBufferSize))
                    {
                        int totalSize = 0;
                        TrackingFileStream file = (TrackingFileStream)fs!.Value.Stream;

                        int iterationCount = fileSize / writeChunkSize;

                        for (int i = 0; i < iterationCount; i++)
                        {
                            await writeRandomBytesToFile(writeChunkSize);
                        }

                        var reminder = fileSize % writeChunkSize;
                        if (reminder != 0)
                        {
                            await writeRandomBytesToFile(reminder);
                        }

                        async Task writeRandomBytesToFile(int size)
                        {
                            var bytes = ThreadSafeRandom.GetBytes(size);
                            totalSize += bytes.Length;
                            await file.WriteAsync(bytes, 0, bytes.Length);
                        }

                        context.TraceDebug($"{index}: {sw.Elapsed}, {file.WriteDuration}, totalSize={totalSize}");
                        return (sw.Elapsed, file.WriteDuration);

                        
                    }
                }
            }
        }
    }
}
