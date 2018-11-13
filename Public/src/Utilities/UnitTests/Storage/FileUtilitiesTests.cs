// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static BuildXL.Utilities.FormattableStringEx;
using FileUtilities = BuildXL.Native.IO.FileUtilities;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Storage
{
    public sealed class FileUtilitiesTests : TemporaryStorageTestBase
    {
        public FileUtilitiesTests()
        {
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // can not make assumption about case - Mac OS Extended (Journaled) is not sensitive
        public void GetFileNameCasing()
        {
            const string UppercaseFileName = @"UPPERCASE.txt";
            const string UppercaseFileNameLowercased = @"uppercase.txt";
            const string Contents = "File contents.";

            File.WriteAllText(GetFullPath(UppercaseFileName), Contents);

            Possible<string> maybeFileName = FileUtilities.GetFileName(GetFullPath(UppercaseFileNameLowercased));
            XAssert.IsTrue(maybeFileName.Succeeded);
            XAssert.AreEqual(UppercaseFileName, maybeFileName.Result);
            XAssert.AreNotEqual(UppercaseFileNameLowercased, maybeFileName.Result);
        }

        [Fact]
        public async Task CopyFileWithReplacement()
        {
            const string Src = "src";
            const string Target = "target";
            const string OriginalContents = "Your ad here.";
            const string NewContents = "This is your ad.";

            File.WriteAllText(GetFullPath(Src), OriginalContents);
            await FileUtilities.CopyFileAsync(GetFullPath(Src), GetFullPath(Target));

            XAssert.AreEqual(OriginalContents, File.ReadAllText(GetFullPath(Target)));
            File.WriteAllText(GetFullPath(Src), NewContents);

            await FileUtilities.CopyFileAsync(GetFullPath(Src), GetFullPath(Target));
            XAssert.AreEqual(NewContents, File.ReadAllText(GetFullPath(Target)));
        }

        [Fact]
        public async Task CopyPredicate()
        {
            const string Src = "src";
            const string Target = "target";

            File.WriteAllText(GetFullPath(Src), "Source");
            XAssert.IsFalse(File.Exists(GetFullPath(Target)));

            bool copied = await FileUtilities.CopyFileAsync(
                GetFullPath(Src),
                GetFullPath(Target),
                (source, dest) =>
                {
                    XAssert.IsNull(dest, "Destination handle should be null if the file does not exist");
                    return true;
                });

            XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            XAssert.IsTrue(copied);
            XAssert.AreEqual("Source", File.ReadAllText(GetFullPath(Target)));
            File.WriteAllText(GetFullPath(Target), "Leave this here");

            copied = await FileUtilities.CopyFileAsync(
                GetFullPath(Src),
                GetFullPath(Target),
                (source, dest) => false);

            XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            XAssert.IsFalse(copied);
            XAssert.AreEqual("Leave this here", File.ReadAllText(GetFullPath(Target)));

            copied = await FileUtilities.CopyFileAsync(
                GetFullPath(Src),
                GetFullPath(Target),
                (source, dest) => true);

            XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            XAssert.IsTrue(copied);
            XAssert.AreEqual("Source", File.ReadAllText(GetFullPath(Target)));
        }

        [Fact]
        public async Task CopyCompletionCallback()
        {
            const string Src = "src";
            const string Target = "target";

            File.WriteAllText(GetFullPath(Src), "Source");

            Usn closeUsn = default(Usn);
            var completionHandlerCalled = false;

            await FileUtilities.CopyFileAsync(
                GetFullPath(Src),
                GetFullPath(Target),
                onCompletion: (source, dest) =>
                {
                    completionHandlerCalled = true;

                    if (!OperatingSystemHelper.IsUnixOS)
                    {
                        Usn? maybeCloseUsn = FileUtilities.TryWriteUsnCloseRecordByHandle(dest);
                        XAssert.IsNotNull(maybeCloseUsn);
                        closeUsn = maybeCloseUsn.Value;
                    }
                });

            XAssert.IsTrue(completionHandlerCalled);
            XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            XAssert.AreEqual("Source", File.ReadAllText(GetFullPath(Target)));

            if (!OperatingSystemHelper.IsUnixOS)
            {
                XAssert.AreNotEqual(0, closeUsn, "Completion callback skipped");

                using (FileStream dest = File.OpenRead(GetFullPath(Target)))
                {
                    Usn usn = FileUtilities.ReadFileUsnByHandle(dest.SafeFileHandle).Value.Usn;
                    XAssert.AreEqual(closeUsn, usn, "CLOSE usn should have been written during the callback.");
                }
            }
        }

        [Fact]
        public void CreateDirectory()
        {
            const string Target = "targetDir";

            FileUtilities.CreateDirectory(GetFullPath(Target));
            XAssert.IsTrue(Directory.Exists(GetFullPath(Target)));

            FileUtilities.CreateDirectory(GetFullPath(Target));
            XAssert.IsTrue(Directory.Exists(GetFullPath(Target)));
        }

        [Fact]
        public void CreateDirectoryNested()
        {
            string Target = PathGeneratorUtilities.GetRelativePath("parentDir", "targetDir");

            FileUtilities.CreateDirectory(GetFullPath(Target));
            XAssert.IsTrue(Directory.Exists(GetFullPath(Target)));
        }

        [Fact]
        public async Task WriteAllText()
        {
            const string Target = @"target";
            const string Contents = "Write me";

            await FileUtilities.WriteAllTextAsync(GetFullPath(Target), Contents, Encoding.UTF8);
            XAssert.AreEqual(Contents, File.ReadAllText(GetFullPath(Target)));
        }

        [Fact]
        public async Task WriteAllBytes()
        {
            const string Target = @"target";
            const string Contents = "Write me";

            await FileUtilities.WriteAllBytesAsync(GetFullPath(Target), Encoding.UTF8.GetBytes(Contents));
            XAssert.AreEqual(Contents, File.ReadAllText(GetFullPath(Target)));
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // written from WriteAllByeAsync is true... fails XAssert
        public async Task WriteAllBytesPredicate()
        {
            const string Target = "target";
            const string Contents = "Contents";
            const string Replacement = "Replacement";

            XAssert.IsFalse(File.Exists(GetFullPath(Target)));

            bool written = await FileUtilities.WriteAllBytesAsync(
                GetFullPath(Target),
                Encoding.UTF8.GetBytes(Contents),
                stream =>
                {
                    XAssert.IsNull(stream, "Destination should be null if the file does not exist");

                    return true;
                });

            XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            XAssert.AreEqual(Contents, File.ReadAllText(GetFullPath(Target)));
            XAssert.IsTrue(written);

            written = await FileUtilities.WriteAllBytesAsync(
                GetFullPath(Target),
                Encoding.UTF8.GetBytes(Replacement),
                stream => false);

            XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            XAssert.AreEqual(Contents, File.ReadAllText(GetFullPath(Target)));
            XAssert.IsFalse(written);

            written = await FileUtilities.WriteAllBytesAsync(
                GetFullPath(Target),
                Encoding.UTF8.GetBytes(Replacement),
                stream => true);

            XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            XAssert.AreEqual(Replacement, File.ReadAllText(GetFullPath(Target)));
            XAssert.IsTrue(written);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public async Task WriteAllBytesCallback()
        {
            const string Target = "target";

            Usn closeUsn = default(Usn);
            await FileUtilities.WriteAllBytesAsync(
                GetFullPath(Target),
                Encoding.UTF8.GetBytes("Target"),
                onCompletion: stream =>
                {
                    Usn? maybeCloseUsn = FileUtilities.TryWriteUsnCloseRecordByHandle(stream);
                    XAssert.IsNotNull(maybeCloseUsn);
                    closeUsn = maybeCloseUsn.Value;
                });

            XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            XAssert.AreEqual("Target", File.ReadAllText(GetFullPath(Target)));
            XAssert.AreNotEqual(0, closeUsn, "Completion callback skipped");

            using (FileStream dest = File.OpenRead(GetFullPath(Target)))
            {
                Usn usn = FileUtilities.ReadFileUsnByHandle(dest.SafeFileHandle).Value.Usn;
                XAssert.AreEqual(closeUsn, usn, "CLOSE usn should have been written during the callback.");
            }
        }

        [Fact]
        public void CreateAsyncFileStreamToCreateNewFile()
        {
            const string Target = @"newFile";

            using (FileUtilities.CreateAsyncFileStream(GetFullPath(Target), FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete))
            {
                XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            }
        }

        [Fact]
        public void CreateAsyncFileStreamToOpenExistingFle()
        {
            const string Target = @"existingFile";

            File.WriteAllText(GetFullPath(Target), "Definitely useful content");

            using (FileUtilities.CreateAsyncFileStream(GetFullPath(Target), FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Delete))
            {
                XAssert.IsTrue(File.Exists(GetFullPath(Target)));
            }
        }

        [Fact]
        public void CreateReplacementFileInitiallyAbsent()
        {
            using (FileStream fs = FileUtilities.CreateReplacementFile(GetFullPath("Ghost"), FileShare.Read | FileShare.Delete))
            {
                XAssert.IsNotNull(fs);
                XAssert.AreEqual(0, fs.Length);
                fs.WriteByte(1);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void CreateReplacementFileReplacesFileEvenIfTruncationWasPossible()
        {
            const string Target = @"Target";

            FileId originalId;
            using (FileStream original = File.Create(GetFullPath(Target)))
            {
                originalId = FileUtilities.ReadFileUsnByHandle(original.SafeFileHandle).Value.FileId;
            }

            using (FileStream fs = FileUtilities.CreateReplacementFile(GetFullPath(Target), FileShare.Read | FileShare.Delete))
            {
                XAssert.IsNotNull(fs);
                XAssert.AreNotEqual(originalId, FileUtilities.ReadFileUsnByHandle(fs.SafeFileHandle).Value.FileId, "File was truncated rather than replaced");
                XAssert.AreEqual(0, fs.Length);
                fs.WriteByte(1);
            }
        }

        [Fact]
        public void CreateReplacementFileCanReplaceMemoryMappedFile()
        {
            string file = GetFullPath("File");
            string link = GetFullPath("link");

            WithNewFileMemoryMapped(
                file,
                () =>
                {
                    if (!CreateHardLinkIfSupported(link: link, linkTarget: file))
                    {
                        return;
                    }

                    using (FileStream fs = FileUtilities.CreateReplacementFile(link, FileShare.Delete))
                    {
                        XAssert.AreEqual(0, fs.Length);
                    }
                });
        }

        [Fact]
        public void CanDeleteMemoryMappedFile()
        {
            string file = GetFullPath("File");
            string link = GetFullPath("link");

            WithNewFileMemoryMapped(
                file,
                () =>
                {
                    if (!CreateHardLinkIfSupported(link: link, linkTarget: file))
                    {
                        return;
                    }

                    XAssert.IsTrue(File.Exists(link));
                    FileUtilities.DeleteFile(link);
                    XAssert.IsFalse(File.Exists(link));
                });
        }

        // HandleSearcher is Windows dependent
        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void FindOpenHandle()
        {
            string directory = Path.Combine(TemporaryDirectory, "directoryToDelete");
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, "openfileFindOpen.txt");
            List<int> pids;
            string diagnosticInfo;
            using (Stream s = new FileStream(filePath, FileMode.Create))
            {
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);

                XAssert.IsTrue(HandleSearcher.GetProcessIdsUsingHandle(filePath, out pids));
                int thisProcess = Process.GetCurrentProcess().Id;
                XAssert.IsTrue(pids.Contains(thisProcess));

                XAssert.IsTrue(FileUtilities.TryFindOpenHandlesToFile(filePath, out diagnosticInfo));
                XAssert.IsTrue(diagnosticInfo.Contains(filePath));
                XAssert.IsTrue(diagnosticInfo.Contains(Process.GetCurrentProcess().ProcessName));
            }

            // The handle is now closed. We shouldn't find any usage
            XAssert.IsTrue(HandleSearcher.GetProcessIdsUsingHandle(filePath, out pids));
            XAssert.AreEqual(0, pids.Count);

            // This should still succeed even if there aren't any open handles
            XAssert.IsTrue(FileUtilities.TryFindOpenHandlesToFile(filePath, out diagnosticInfo));
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void GetChainOfSymlinks()
        {
            string targetFile = GetFullPath("target");
            File.WriteAllText(targetFile, "Contents");

            string intermediateLink = GetFullPath("intermediate.lnk");
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(intermediateLink, targetFile, isTargetFile: true));
            XAssert.IsTrue(File.Exists(intermediateLink));

            string sourceLink = GetFullPath("source.lnk");
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(sourceLink, intermediateLink, isTargetFile: true));
            XAssert.IsTrue(File.Exists(sourceLink));

            using (SafeFileHandle handle = OpenHandleForReparsePoint(sourceLink))
            {
                var chain = new List<string>();
                FileUtilities.GetChainOfReparsePoints(handle, sourceLink, chain);
                XAssert.AreEqual(3, chain.Count, "Chain of reparse points: " + string.Join(" -> ", chain));
                XAssert.AreEqual(sourceLink.ToUpperInvariant(), TrimPathPrefix(chain[0].ToUpperInvariant()));
                XAssert.AreEqual(intermediateLink.ToUpperInvariant(), TrimPathPrefix(chain[1].ToUpperInvariant()));
                XAssert.AreEqual(targetFile.ToUpperInvariant(), TrimPathPrefix(chain[2].ToUpperInvariant()));
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        [Trait("Category", "WindowsOSOnly")]
        public void GetChainOfJunctions()
        {
            string targetDirectory = GetFullPath("target");
            Directory.CreateDirectory(targetDirectory);

            string intermediateJunction = GetFullPath("intermediate.jct");
            Directory.CreateDirectory(intermediateJunction);
            FileUtilities.CreateJunction(intermediateJunction, targetDirectory);

            string sourceJunction = GetFullPath("source.jct");
            Directory.CreateDirectory(sourceJunction);
            FileUtilities.CreateJunction(sourceJunction, intermediateJunction);

            using (SafeFileHandle handle = OpenHandleForReparsePoint(sourceJunction))
            {
                var chain = new List<string>();
                FileUtilities.GetChainOfReparsePoints(handle, sourceJunction, chain);
                XAssert.AreEqual(3, chain.Count, "Chain of reparse points: " + string.Join(" -> ", chain));
                XAssert.AreEqual(sourceJunction.ToUpperInvariant(), TrimPathPrefix(chain[0].ToUpperInvariant()));
                XAssert.AreEqual(intermediateJunction.ToUpperInvariant(), TrimPathPrefix(chain[1].ToUpperInvariant()));
                XAssert.AreEqual(targetDirectory.ToUpperInvariant(), TrimPathPrefix(chain[2].ToUpperInvariant()));
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void GetReparsePointTarget()
        {
            string symlinkPath = GetFullPath("symlink");
            // the length of the target path must be at least 128 chars, so we could properly test the parsing
            // of the struct returned from DeviceIoControl.
            string symlinkTarget = PathGeneratorUtilities.GetAbsolutePath("Z", Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), $"{Guid.NewGuid().ToString()}.txt");

            XAssert.IsTrue(symlinkTarget.Length >= 128);

            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(symlinkPath, symlinkTarget, isTargetFile: true));

            Possible<ReparsePointType> reparsePointType = FileUtilities.TryGetReparsePointType(symlinkPath);
            XAssert.IsTrue(reparsePointType.Succeeded);
            XAssert.IsTrue(reparsePointType.Result == ReparsePointType.SymLink);

            using (var symlinkHandle = OpenHandleForReparsePoint(symlinkPath))
            {
                var possibleSymlinkTargetToCheck = FileUtilities.TryGetReparsePointTarget(symlinkHandle, symlinkPath);
                XAssert.IsTrue(possibleSymlinkTargetToCheck.Succeeded, I($"Failed to get the reparse point target for '{symlinkPath}'"));
                XAssert.AreEqual(symlinkTarget, possibleSymlinkTargetToCheck.Result);
            }
        }

        [Fact]
        public void ConvertReparsePointTargetPathToAbsolutePath()
        {
            // Z:\file.txt + .\file2.txt => Z:\file2.txt
            string root = A("Z", "file.txt");
            string relativePath = R(".", "file2.txt");
            AssertResolvedPath(root, relativePath, A("Z", "file2.txt"));

            // Z:\d1\\\\file.txt + .\file2.txt => Z:\d1\file2.txt
            // (check the we properly throw away extra slashes)
            root = A("Z", "d1", "", "", "", "file.txt");
            relativePath = R(".", "file2.txt");
            AssertResolvedPath(root, relativePath, A("Z", "d1", "file2.txt"));

            // Z:\d1\d2\d3\file.txt + .\file2.txt => Z:\d1\d2\d3\file2.txt
            root = A("Z", "d1", "d2", "d3", "file.txt");
            relativePath = R(".", "file2.txt");
            AssertResolvedPath(root, relativePath, A("Z", "d1", "d2", "d3", "file2.txt"));

            // The result of the method should not depend on the presence of a trailing slash

            // Z:\d1\d2\d3 + .\file2.txt => Z:\d1\d2\file2.txt
            root = A("Z", "d1", "d2", "d3");
            relativePath = R(".", "file2.txt");
            AssertResolvedPath(root, relativePath, A("Z", "d1", "d2", "file2.txt"));

            // Z:\d1\d2\d3\ + .\file2.txt => Z:\d1\d2\file2.txt  (trailing '\' case)
            root = A("Z", "d1", "d2", "d3", "");
            relativePath = R(".", "file2.txt");
            AssertResolvedPath(root, relativePath, A("Z", "d1", "d2", "file2.txt"));

            // Z:\d1\d2\d3\file.txt + ..\..\file2.txt => Z:\d1\file2.txt
            root = A("Z", "d1", "d2", "d3", "file.txt");
            relativePath = R("..", "..", "file2.txt");
            AssertResolvedPath(root, relativePath, A("Z", "d1", "file2.txt"));

            // Z:\d1\d2\d3\file.txt + ..\..\..\file2.txt => Z:\file2.txt
            root = A("Z", "d1", "d2", "d3", "file.txt");
            relativePath = R("..", "..", "..", "file2.txt");
            AssertResolvedPath(root, relativePath, A("Z", "file2.txt"));

            // Z:\d1\d2\d3\file.txt + ..\..\..\..\file2.txt => exception (cannot go above 'Z:\')
            root = A("Z", "d1", "d2", "d3", "file.txt");
            relativePath = R("..", "..", "..", "..", "file2.txt");
            Assert.Throws<BuildXLException>(() => AssertResolvedPath(root, relativePath, string.Empty));

            if (!OperatingSystemHelper.IsUnixOS)
            {
                // paths with \\.\ and \\?\ prefixes are only available on Windows
                root = @"\\.\C:\d1\d2\d3\file.txt";
                relativePath = R("..", "..", "..", "file2.txt");
                AssertResolvedPath(root, relativePath, @"\\.\C:\file2.txt");

                // relative path cannot navigate past \\.\C:\
                relativePath = R("..", "..", "..", "..", "file2.txt");
                Assert.Throws<BuildXLException>(() => AssertResolvedPath(root, relativePath, string.Empty));

                // network path: \\server\share\d1\d2\d3\file.txt
                root = string.Format("{0}{0}server{0}share{0}d1{0}d2{0}d3{0}file.txt", Path.DirectorySeparatorChar);
                relativePath = R("..", "..", "..", "file2.txt");
                AssertResolvedPath(root, relativePath, string.Format("{0}{0}server{0}share{0}file2.txt", Path.DirectorySeparatorChar));

                // relative paths should not be allowed to navigate beyond \\server\share
                relativePath = R("..", "..", "..", "..", "file2.txt");

                Assert.Throws<BuildXLException>(() => AssertResolvedPath(root, relativePath, string.Empty));
            }
        }

        [Fact]
        public void SetAndGetFileTimestamps()
        {
            string targetFile = GetFullPath("somefile.txt");
            File.WriteAllText(targetFile, "Important Data");

            DateTime test = new DateTime(1999, 12, 31, 1, 1, 1, DateTimeKind.Utc);
            FileUtilities.SetFileTimestamps(targetFile, new FileTimestamps(test));

            var timestamps = FileUtilities.GetFileTimestamps(targetFile);

            // We dont look at last changed and access time as the test process touches the permissions of the output file and
            // the system indexes the files so the access time changes too!
            XAssert.AreEqual(test, timestamps.CreationTime);
            XAssert.AreEqual(test, timestamps.LastWriteTime);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void SetAndGetFileTimestampsOnlyAffectSymlink()
        {
            string originalFile = GetFullPath("somefile.txt");
            File.WriteAllText(originalFile, "Important Data");

            string intermediateLink = GetFullPath("someLinkThatWillChange.lnk");
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(intermediateLink, originalFile, isTargetFile: true));

            var test = new DateTime(2001, 12, 31, 1, 1, 1, DateTimeKind.Utc);
            FileUtilities.SetFileTimestamps(intermediateLink, new FileTimestamps(test));

            var originalTimestamps = FileUtilities.GetFileTimestamps(originalFile);
            var symlinkTimestamps = FileUtilities.GetFileTimestamps(intermediateLink);

            // We dont look at last changed and access time as the test process touches the permissions of the output file and
            // the system indexes the files so the access time changes too!
            XAssert.AreEqual(test, symlinkTimestamps.CreationTime);
            XAssert.AreEqual(test, symlinkTimestamps.LastWriteTime);

            XAssert.AreNotEqual(originalTimestamps.CreationTime, symlinkTimestamps.CreationTime);
            XAssert.AreNotEqual(originalTimestamps.LastWriteTime, symlinkTimestamps.LastWriteTime);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void TestTryProbePathExistence()
        {
            // check regular file
            string file = GetFullPath("file");
            File.WriteAllText(file, "I'm a file");
            XAssert.IsTrue(File.Exists(file)); // sanity check
            AssertExistsAsFile(FileUtilities.TryProbePathExistence(file, followSymlink: false));
            AssertExistsAsFile(FileUtilities.TryProbePathExistence(file, followSymlink: true));

            // check regular directory
            string dir = GetFullPath("dir");
            Directory.CreateDirectory(dir);
            XAssert.IsTrue(Directory.Exists(dir)); // sanity check
            AssertExistsAsDir(FileUtilities.TryProbePathExistence(dir, followSymlink: false));
            AssertExistsAsDir(FileUtilities.TryProbePathExistence(dir, followSymlink: true));

            // check absent
            string absent = GetFullPath("absent");
            XAssert.IsFalse(File.Exists(absent));
            XAssert.IsFalse(Directory.Exists(absent));
            AssertNonexistent(FileUtilities.TryProbePathExistence(absent, followSymlink: false));
            AssertNonexistent(FileUtilities.TryProbePathExistence(absent, followSymlink: true));

            // check symlink to file
            string symFile = GetFullPath("sym-file");
            CreateAndCheckSymlink(symFile, file, isTargetFile: true);
  
            // check symlink to directory
            string symDir = GetFullPath("sym-dir");
            CreateAndCheckSymlink(symDir, dir, isTargetFile: false);

            // check symlink to absent
            string symAbsent = GetFullPath("sym-absent");
            CreateAndCheckSymlink(symAbsent, absent, isTargetFile: true);

            // check symlink to symlink absent
            string symSymAbsent = GetFullPath("sym-sym-absent");
            CreateAndCheckSymlink(symSymAbsent, symAbsent, isTargetFile: true);

            void CreateAndCheckSymlink(string symPath, string target, bool isTargetFile)
            {
                // when not following, symlinks exist as files
                XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(symPath, target, isTargetFile));
                AssertExistsAsFile(FileUtilities.TryProbePathExistence(symPath, followSymlink: false));

                // when following, replicate File/Directory.Exists() behavior for backward compatibility reasons
                var expectedExistenceWhenFollowing = Directory.Exists(symPath)
                    ? PathExistence.ExistsAsDirectory
                    : PathExistence.ExistsAsFile;
                AssertPathExistence(
                    expectedExistenceWhenFollowing,
                    FileUtilities.TryProbePathExistence(symPath, followSymlink: true));
            }
        }

        private void AssertNonexistent(Possible<PathExistence, NativeFailure> maybeFileExistence)
            => AssertPathExistence(PathExistence.Nonexistent, maybeFileExistence);

        private void AssertExistsAsDir(Possible<PathExistence, NativeFailure> maybeFileExistence)
            => AssertPathExistence(PathExistence.ExistsAsDirectory, maybeFileExistence);

        private void AssertExistsAsFile(Possible<PathExistence, NativeFailure> maybeFileExistence)
            => AssertPathExistence(PathExistence.ExistsAsFile, maybeFileExistence);

        private void AssertPathExistence(PathExistence expected, Possible<PathExistence, NativeFailure> maybeFileExistence)
        {
            XAssert.IsTrue(maybeFileExistence.Succeeded);
            XAssert.AreEqual(expected, maybeFileExistence.Result);
        }

        private void AssertResolvedPath(string root, string relativePath, string expectedResolvedPath)
        {
            string resolvedPath = FileUtilities.ConvertReparsePointTargetPathToAbsolutePath(root, relativePath);
            XAssert.AreEqual(expectedResolvedPath, resolvedPath);
        }

        private string TrimPathPrefix(string path) => path.StartsWith(@"\??\") || path.StartsWith(@"\\?\") ? path.Substring(4) : path;

        private static SafeFileHandle OpenHandleForReparsePoint(string fileOrDirectoryPath)
        {
            var result = FileUtilities.TryOpenDirectory(
                fileOrDirectoryPath,
                FileDesiredAccess.GenericRead,
                FileShare.ReadWrite | FileShare.Delete,
                FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                out var handle);

            XAssert.IsTrue(result.Succeeded, I($"Failed to create a handle to file/directory '{fileOrDirectoryPath}'"));

            return handle;
        }

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

        private static void WithNewFileMemoryMapped(string path, Action action)
        {
            using (FileStream file = FileUtilities.CreateFileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Delete | FileShare.Read))
            {
                file.SetLength(1024);
                using (
                    MemoryMappedFile.CreateFromFile(
                        file,
                        mapName: null,
                        capacity: 0,
                        access: MemoryMappedFileAccess.ReadWrite,
#if !DISABLE_FEATURE_MEMORYMAP_SECURITY
                        memoryMappedFileSecurity: null,
#endif
                        inheritability: HandleInheritability.None,
                        leaveOpen: false))
                {
                    action();
                }
            }
        }
    }
}

#pragma warning restore AsyncFixer02
