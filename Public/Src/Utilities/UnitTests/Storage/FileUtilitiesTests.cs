// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
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
            RegisterEventSource(global::BuildXL.Native.ETWLogger.Log);
        }

        [Fact]
        public void TestEnumerateDirectoryWithPattern()
        {
            const string Target = "targetDir";
            string nestedDirectory = GetFullPath(Path.Combine(Target, "sub1", "sub2", "sub3"));
            FileUtilities.CreateDirectory(nestedDirectory);

            File.WriteAllText(Path.Combine(nestedDirectory, "sub_file.txt"), "my text");

            var entries = FileUtilities.FileSystem.EnumerateDirectories(GetFullPath(Target), pattern: "sub*", recursive: true);
            XAssert.SetEqual(new []{"sub1", "sub2", "sub3", "sub_file.txt"}, entries.Select(e => e.FileName).ToHashSet());

            // Enumerate directory does not provide the size.
            XAssert.AreEqual(0, entries.First(e => e.FileName == "sub_file.txt").Size);

            XAssert.AreEqual(1, FileUtilities.FileSystem.EnumerateDirectories(GetFullPath(Target), pattern: "sub1", recursive: true).Count);
            XAssert.AreEqual(1, FileUtilities.FileSystem.EnumerateDirectories(GetFullPath(Target), pattern: "sub_file*", recursive: true).Count);
            XAssert.AreEqual(1, FileUtilities.FileSystem.EnumerateDirectories(GetFullPath(Target), pattern: "*.txt", recursive: true).Count);

            // Non recursive case
            XAssert.AreEqual(0, FileUtilities.FileSystem.EnumerateDirectories(GetFullPath(Target), pattern: "*.txt", recursive: false).Count);
            
            // Pattern with 0 matches
            XAssert.AreEqual(0, FileUtilities.FileSystem.EnumerateDirectories(GetFullPath(Target), pattern: "foo", recursive: true).Count);
        }

        [Fact]
        public void TestEnumerateFilesWithPattern()
        {
            const string Target = "targetDir";
            string nestedDirectory = GetFullPath(Path.Combine(Target, "sub1", "sub2", "sub3"));
            FileUtilities.CreateDirectory(nestedDirectory);

            const string Content = "my text";
            File.WriteAllText(Path.Combine(nestedDirectory, "sub_file.txt"), Content);

            var entries = FileUtilities.FileSystem.EnumerateFiles(GetFullPath(Target), pattern: "sub*", recursive: true);
            XAssert.SetEqual(new []{"sub_file.txt"}, entries.Select(e => e.FileName).ToHashSet());

            // Checking the size of the file.
            XAssert.AreEqual(Content.Length, entries.First(e => e.FileName == "sub_file.txt").Size);

            XAssert.AreEqual(0, FileUtilities.FileSystem.EnumerateFiles(GetFullPath(Target), pattern: "sub1", recursive: true).Count);
            XAssert.AreEqual(1, FileUtilities.FileSystem.EnumerateFiles(GetFullPath(Target), pattern: "sub_file*", recursive: true).Count);
            XAssert.AreEqual(1, FileUtilities.FileSystem.EnumerateFiles(GetFullPath(Target), pattern: "*.txt", recursive: true).Count);
            
            // Non recursive case
            XAssert.AreEqual(0, FileUtilities.FileSystem.EnumerateFiles(GetFullPath(Target), pattern: "*.txt", recursive: false).Count);
            
            // Pattern with 0 matches
            XAssert.AreEqual(0, FileUtilities.FileSystem.EnumerateFiles(GetFullPath(Target), pattern: "foo", recursive: true).Count);
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
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(intermediateLink, targetFile, isTargetFile: true));
            XAssert.IsTrue(File.Exists(intermediateLink));

            string sourceLink = GetFullPath("source.lnk");
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceLink, intermediateLink, isTargetFile: true));
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
                XAssert.ArePathEqual(sourceJunction, TrimPathPrefix(chain[0]));
                XAssert.ArePathEqual(intermediateJunction, TrimPathPrefix(chain[1]));
                XAssert.ArePathEqual(targetDirectory, TrimPathPrefix(chain[2]));
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        [Trait("Category", "WindowsOSOnly")] // TODO: Fix for non Windows
        public void GetChainOfSymlinksWithDirectorySymlink()
        {
            // File and directory layout:
            //    Enlist
            //    |
            //    +---Intermediate
            //    |   \---Current
            //    |           file.lnk ==> ..\..\Target\file.txt
            //    |
            //    +---Source ==> Intermediate\Current
            //    |
            //    \---Target
            //            file.txt

            // Create a concrete file /Target/file.txt
            string targetFile = GetFullPath(R("Target", "file.txt"));
            FileUtilities.CreateDirectory(Path.GetDirectoryName(targetFile));
            File.WriteAllText(targetFile, "Contents");

            // Create a symlink /Intermediate/Current/file.lnk --> ../../Target/file.txt
            string intermediateLink = GetFullPath(R("Intermediate", "Current", "file.lnk"));
            FileUtilities.CreateDirectory(Path.GetDirectoryName(intermediateLink));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(intermediateLink, R("..", "..", "Target", "file.txt"), isTargetFile: true));

            // Create a directory symlink /Source --> Intermediate/Current
            // Access /Source/file.lnk.
            string sourceLink = GetFullPath(R("Source", "file.lnk"));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(Path.GetDirectoryName(sourceLink), R("Intermediate", "Current"), isTargetFile: false));

            using (SafeFileHandle handle = OpenHandleForReparsePoint(sourceLink))
            {
                var chain = new List<string>();
                FileUtilities.GetChainOfReparsePoints(handle, sourceLink, chain);

                // There are two ways of reaching file.lnk, (1) /Source/file.lnk, and (2) /Intermediate/Current/file.lnk.
                // For Windows, BuildXL does not track all possible paths reaching to file.lnk; only what the tool/user accesses.
                // In this case, we access /Source/file.lnk. Thus, only /Source/file.lnk and /Target/file.txt are in the chain.
                // TODO: Reconcile this with Mac implementation.
                XAssert.AreEqual(2, chain.Count, "Chain of reparse points: " + string.Join(" -> ", chain));
                XAssert.ArePathEqual(sourceLink, TrimPathPrefix(chain[0]));
                XAssert.ArePathEqual(targetFile, TrimPathPrefix(chain[1]));
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

            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkPath, symlinkTarget, isTargetFile: true));

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

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void TestResolveSymlinks()
        {
            var tests = new List<(string symlinkPath, string target, string expectedResult)>()
            {
                // Z:\D1\f1.lnk --> f1.txt ==> Z:\D1\f1.txt
                (GetFullPath(R("D1", "f1.lnk")), "f1.txt", Path.Combine(GetFullPath("D1"), "f1.txt")),

                // Z:\D1\f2.lnk --> .\f2.txt ==> Z:\D1\f2.txt
                (GetFullPath(R("D1", "f2.lnk")), R(".", "f2.txt"), Path.Combine(GetFullPath("D1"), "f2.txt")),

                // Z:\E1\E2\f3.lnk --> ..\f3.txt ==> Z:\E1\f3.txt
                (GetFullPath(R("E1", "E2", "f3.lnk")), R("..", "f3.txt"), Path.Combine(GetFullPath("E1"), "f3.txt")),

                // Z:\E1\E2\f4.lnk --> ..\.\f4.txt ==> Z:\E1\f4.txt
                (GetFullPath(R("E1", "E2", "f4.lnk")), R("..", ".", "f4.txt"), Path.Combine(GetFullPath("E1"), "f4.txt")),

                // Z:\E1\E2\f5.lnk --> .\..\f5.txt ==> Z:\E1\f5.txt
                (GetFullPath(R("E1", "E2", "f5.lnk")), R(".", "..", "f5.txt"), Path.Combine(GetFullPath("E1"), "f5.txt")),

                // Z:\E1\E2\f6.lnk --> ..\..\f6.txt ==> Z:\f6.txt
                (GetFullPath(R("E1", "E2", "f6.lnk")), R("..", "..", "f6.txt"), GetFullPath("f6.txt")),

                // Z:\E1\f7.lnk --> ..\... <99x> ..\..\f7.txt ==> failed
                (GetFullPath(R("E1", "f7.lnk")), R(Enumerable.Select(Enumerable.Range(1, 100), i => i != 100 ? ".." : "f7.txt").ToArray()), null),

                // Z:\E1\f8.lnk --> Z:\E2\f8.txt ==> Z:\E2\f8.txt
                (GetFullPath(R("E1", "f8.lnk")), GetFullPath(R("E2", "f8.txt")), GetFullPath(R("E2", "f8.txt"))),
            };
            
            foreach (var (symlinkPath, target, expectedResult) in tests)
            {
                VerifyResolveSymlink(symlinkPath, target, expectedResult);
            }
        }

        private void VerifyResolveSymlink(string symlinkPath, string target, string expectedResult)
        {
            FileUtilities.CreateDirectory(Path.GetDirectoryName(symlinkPath));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkPath, target, isTargetFile: true));
            var maybeResult = FileUtilities.ResolveSymlinkTarget(symlinkPath, target);
            if (expectedResult != null)
            {
                XAssert.IsTrue(maybeResult.Succeeded, maybeResult.Succeeded ? string.Empty : maybeResult.Failure.DescribeIncludingInnerFailures());
                XAssert.ArePathEqual(expectedResult, maybeResult.Result);
            }
            else
            {
                XAssert.IsFalse(maybeResult.Succeeded);
            }
        }

        [Trait("Category", "WindowsOSOnly")] // Windows OS only because junctions do not exist on other platforms.
        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public void TestResolveSymlinkWithDirectorySymlinkOrJunction(bool useJunction, bool oneDotDot)
        {
            // File and directory layout:
            //    Enlist
            //    |
            //    +---Intermediate
            //    |   \---Current
            //    |           file.lnk ==> ..\..\Target\file.txt (or ..\Target\file.txt)
            //    |
            //    +---Source ==> Intermediate\Current
            //    |
            //    \---Target
            //            file.txt

            // Create a symlink Enlist/Intermediate/Current/file.lnk --> ../../Target/file.txt (or ../Target/file.txt)
            string symlinkFile = GetFullPath(R("Enlist", "Intermediate", "Current", "file.lnk"));
            FileUtilities.CreateDirectory(GetFullPath(R("Enlist", "Intermediate", "Current")));

            var relativeTarget = oneDotDot ? R("..", "Target", "file.txt") : R("..", "..", "Target", "file.txt");
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkFile, relativeTarget, isTargetFile: true));

            // Create a directory symlink Enlist/Source --> Enlist/Intermediate/Current
            string symlinkDirectory = GetFullPath(R("Enlist", "Source"));

            if (useJunction)
            {
                FileUtilities.CreateDirectory(symlinkDirectory);
                FileUtilities.CreateJunction(symlinkDirectory, GetFullPath(R("Enlist", "Intermediate", "Current")));
            }
            else
            {
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkDirectory, R("Intermediate", "Current"), isTargetFile: false));
            }

            string expectedFinalPath = null;

            if (useJunction)
            {
                expectedFinalPath = oneDotDot ? GetFullPath(R("Enlist", "Target", "file.txt")) : GetFullPath(R("Target", "file.txt"));
            }
            else
            {
                expectedFinalPath = oneDotDot ? GetFullPath(R("Enlist", "Intermediate", "Target", "file.txt")) : GetFullPath(R("Enlist", "Target", "file.txt"));
            }

            // Resolve symlink Enlist/Source/file.lnk by supplying the symlink relative target path (../../Target/file.txt or ../Target/file.txt).
            var maybeResult = FileUtilities.ResolveSymlinkTarget(GetFullPath(R("Enlist", "Source", "file.lnk")), relativeTarget);
            XAssert.PossiblySucceeded(maybeResult);

            XAssert.ArePathEqual(expectedFinalPath, maybeResult.Result);

            // Resolve symlink Enlist/Source/file.lnk without supplying the symlink target path
            // The result should be Enlist/Target/file.txt
            maybeResult = FileUtilities.ResolveSymlinkTarget(GetFullPath(R("Enlist", "Source", "file.lnk")));
            XAssert.PossiblySucceeded(maybeResult);

            XAssert.ArePathEqual(expectedFinalPath, maybeResult.Result);
        }

        [Trait("Category", "WindowsOSOnly")] // Windows OS only because junctions do not exist on other platforms.
        [FactIfSupported(requiresSymlinkPermission: true)]
        public void TestResolveSymlinkWithMixedDirectorySymlinkAndJunction()
        {
            // File and directory layout:
            //    Enlist
            //    |
            //    +---Intermediate
            //    |   \---Current
            //    |       \---X64
            //    |              file.lnk ==> ..\..\..\Target\file.txt
            //    +---Data
            //    |   \---Source
            //    |
            //    +---Source ==> \Enlist\Data\Source (junction)
            //    |   \---X64 ==> ..\Intermediate\Current\X64 (directory symlink)
            //    |
            //    \---Target
            //        \---X64
            //                file.txt

            // Create a symlink Enlist/Intermediate/Current/X64/file.lnk --> ../../../Target/X64/file.txt.
            string symlinkFile = GetFullPath(R("Enlist", "Intermediate", "Current", "X64", "file.lnk"));
            FileUtilities.CreateDirectory(GetFullPath(R("Enlist", "Intermediate", "Current", "X64")));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkFile, R("..", "..", "..", "Target", "X64", "file.txt"), isTargetFile: true));

            // Create a junction Enlist/Source --> Enlist/Data/Source.
            FileUtilities.CreateDirectory(GetFullPath(R("Enlist", "Data", "Source")));
            FileUtilities.CreateDirectory(GetFullPath(R("Enlist", "Source")));
            FileUtilities.CreateJunction(GetFullPath(R("Enlist", "Source")), GetFullPath(R("Enlist", "Data", "Source")));

            // Create directory symlink.
            string symlinkDirectory = GetFullPath(R("Enlist", "Source", "X64"));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkDirectory, R("..", "Intermediate", "Current", "X64"), isTargetFile: false));

            string expectedFinalPath = GetFullPath(R("Enlist", "Target", "X64", "file.txt"));

            // Resolve symlink Enlist/Source/x64/file.lnk by supplying the symlink relative target path (../../../Target/X64/file.txt).
            var maybeResult = FileUtilities.ResolveSymlinkTarget(GetFullPath(R("Enlist", "Source", "X64", "file.lnk")), R("..", "..", "..", "Target", "X64", "file.txt"));
            XAssert.PossiblySucceeded(maybeResult);

            XAssert.ArePathEqual(expectedFinalPath, maybeResult.Result);

            // Resolve symlink Enlist/Source/X64/file.lnk without supplying the symlink target path
            maybeResult = FileUtilities.ResolveSymlinkTarget(GetFullPath(R("Enlist", "Source", "X64", "file.lnk")));
            XAssert.PossiblySucceeded(maybeResult);

            XAssert.ArePathEqual(expectedFinalPath, maybeResult.Result);
        }

        [Trait("Category", "WindowsOSOnly")] // Windows OS only because junctions do not exist on other platforms.
        [FactIfSupported(requiresSymlinkPermission: true)]
        public void TestResolveRelativeSymlinks()
        {
            VerifyResolveRelativeSymlink(
                expected: R("A1", "A2", "A4", "A5", "A7"),
                link:     C(DL(R("A1", "A2", "A3"), R("A4")), R("A5", "A6")), 
                target:   R("A7"));

            VerifyResolveRelativeSymlink(
                expected: R("B1", "B4", "B7"),
                link:     C(DL(R("B1", "B2", "B3"), R("..", "B4")), R("B5", "B6")),
                target:   R("..", "B7"));

            VerifyResolveRelativeSymlink(
                expected: R("C1", "C2", "C3", "C5", "C7"),
                link:     C(J(R("C1", "C2", "C3"), R("C4")), R("C5", "C6")),
                target:   R("C7"));

            VerifyResolveRelativeSymlink(
                expected: R("D1", "D2", "D3", "D7"),
                link:     C(J(R("D1", "D2", "D3"), R("D1", "D4")), R("D5", "D6")),
                target:   R("..", "D7"));

            VerifyResolveRelativeSymlink(
                expected: R("E1", "E2", "E3", "E7", "E10"),
                link:     C(DL(J(R("E1", "E2", "E3"), R("E1", "E4")), R("E5", "E6"), R("..", "E7")), R("E8", "E9")),
                target:   R("..", "E10"));

            VerifyResolveRelativeSymlink(
                expected: R("F1", "F4", "F5", "F6", "F10"),
                link:     C(J(DL(R("F1", "F2", "F3"), R("..", "F4")), R("F5", "F6"), R("F1", "F7")), R("F8", "F9")),
                target:   R("..", "F10"));
        }

        private void VerifyResolveRelativeSymlink(string expected, string link, string target)
        {
            expected = GetFullPath(expected);
            link = FL(link, target);
            var maybeActual = FileUtilities.FileSystem.TryResolveReparsePointRelativeTarget(link, target);
            XAssert.IsTrue(maybeActual.Succeeded, maybeActual.Succeeded ? string.Empty : maybeActual.Failure.Describe());
            XAssert.AreEqual(expected.ToUpperInvariant(), maybeActual.Result.ToUpperInvariant());
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
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(intermediateLink, originalFile, isTargetFile: true));

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
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symPath, target, isTargetFile));
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

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void OpenFileResultFindHandlesOnSharingViolation()
        {
            var file = GetFullPath("test.txt");
            var dummyBlockedResult = OpenFileResult.Create(file, NativeIOConstants.ErrorSharingViolation, FileMode.Create, handleIsValid: false);
            var dummyArbitraryFailResult = OpenFileResult.Create(file, NativeIOConstants.ErrorHandleEof, FileMode.Create, handleIsValid: false);

            using (var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var dummyBlockedError = dummyBlockedResult.CreateExceptionForError();
                XAssert.IsTrue(dummyBlockedError.Message.Contains(FileUtilitiesMessages.ActiveHandleUsage));

                var dummyBlockedFailure = dummyBlockedResult.CreateFailureForError();
                XAssert.IsTrue(dummyBlockedFailure.DescribeIncludingInnerFailures().Contains(FileUtilitiesMessages.ActiveHandleUsage));

                var dummyArbitraryError = dummyArbitraryFailResult.CreateExceptionForError();
                XAssert.IsFalse(dummyArbitraryError.Message.Contains(FileUtilitiesMessages.ActiveHandleUsage));

                var dummyArbitraryFailure = dummyArbitraryFailResult.CreateFailureForError();
                XAssert.IsFalse(dummyArbitraryFailure.DescribeIncludingInnerFailures().Contains(FileUtilitiesMessages.ActiveHandleUsage));
            }
        }

        [Fact]
        public void TestCopyOnWrite()
        {
            var file = GetFullPath(nameof(TestCopyOnWrite));
            File.WriteAllText(file, nameof(TestCopyOnWrite));

            if (!SupportCopyOnWrite(file))
            {
                return;
            }

            // Currently only APFS supports copy-on-write.
            XAssert.AreEqual(FileSystemType.APFS, GetFileSystemType(file));

            var clonedFile = GetFullPath(nameof(TestCopyOnWrite) + "_cloned");

            var possiblyCopyOnWrite = FileUtilities.TryCreateCopyOnWrite(file, clonedFile, followSymlink: false);
            XAssert.IsTrue(possiblyCopyOnWrite.Succeeded);
            
            var fileTimestamp = FileUtilities.GetFileTimestamps(file);
            var clonedFileTimestamp = FileUtilities.GetFileTimestamps(clonedFile);

            // The access time and modified time must be equal, but not the last status change time.
            // The latter is hard to test when the underlying file system doesn't support high-precision file timestamp.
            XAssert.AreEqual(fileTimestamp.AccessTime, clonedFileTimestamp.AccessTime);
            XAssert.AreEqual(fileTimestamp.LastWriteTime, clonedFileTimestamp.LastWriteTime);
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void TestCopyOnWriteWithSymlink(bool followSymlink)
        {
            var file = GetFullPath(nameof(TestCopyOnWriteWithSymlink));
            File.WriteAllText(file, nameof(TestCopyOnWriteWithSymlink));

            if (!SupportCopyOnWrite(file))
            {
                return;
            }

            // Currently only APFS supports copy-on-write.
            XAssert.AreEqual(FileSystemType.APFS, GetFileSystemType(file));

            var symlink = GetFullPath(nameof(TestCopyOnWriteWithSymlink) + "_symlink");
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlink, file, isTargetFile: true));

            var clonedFile = GetFullPath(nameof(TestCopyOnWriteWithSymlink) + "_cloned");

            var possiblyCopyOnWrite = FileUtilities.TryCreateCopyOnWrite(symlink, clonedFile, followSymlink: followSymlink);
            XAssert.IsTrue(possiblyCopyOnWrite.Succeeded);

            var verifiedTimestamp = followSymlink 
                ? FileUtilities.GetFileTimestamps(file) 
                : FileUtilities.GetFileTimestamps(symlink);
            var clonedFileTimestamp = FileUtilities.GetFileTimestamps(clonedFile);

            // The access time and modified time must be equal, but not the last status change time.
            // The latter is hard to test when the underlying file system doesn't support high-precision file timestamp.
            XAssert.AreEqual(verifiedTimestamp.AccessTime, clonedFileTimestamp.AccessTime);
            XAssert.AreEqual(verifiedTimestamp.LastWriteTime, clonedFileTimestamp.LastWriteTime);
        }

        [Fact]
        public void TestVolumeFileSystemName()
        {
            var file = GetFullPath(nameof(TestVolumeFileSystemName));
            File.WriteAllText(file, nameof(TestVolumeFileSystemName));

            using (var stream = FileUtilities.CreateFileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                FileSystemType fsType = FileUtilities.GetVolumeFileSystemByHandle(stream.SafeFileHandle);
                XAssert.AreNotEqual(FileSystemType.Unknown, fsType);
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

        private string J(string path, string target)
        {
            if (!FileUtilities.FileSystem.IsPathRooted(path))
            {
                path = GetFullPath(path);
            }

            if (!FileUtilities.FileSystem.IsPathRooted(target))
            {
                target = GetFullPath(target);
            }

            FileUtilities.CreateDirectory(target);
            FileUtilities.CreateDirectory(path);
            FileUtilities.CreateJunction(path, target);

            return path;
        }

        private string J(string path, string relative, string target)
        {
            return J(Path.Combine(path, relative), target);
        }

        private string DL(string path, string target)
        {
            if (!FileUtilities.FileSystem.IsPathRooted(path))
            {
                path = GetFullPath(path);
            }

            string parentPath = Path.GetDirectoryName(path);
            FileUtilities.CreateDirectory(parentPath);
            var maybeSymlink = FileUtilities.TryCreateSymbolicLink(path, target, isTargetFile: false);
            XAssert.IsTrue(maybeSymlink.Succeeded, maybeSymlink.Succeeded ? string.Empty : maybeSymlink.Failure.Describe());

            if (!FileUtilities.FileSystem.IsPathRooted(target))
            {
                FileUtilities.CreateDirectory(Path.GetFullPath(Path.Combine(parentPath, target)));
            }
            else
            {
                FileUtilities.CreateDirectory(target);
            }

            return path;
        }

        private string DL(string path, string relative, string target)
        {
            return DL(Path.Combine(path, relative), target);
        }

        private string FL(string path, string target)
        {
            string parentPath = Path.GetDirectoryName(path);
            FileUtilities.CreateDirectory(parentPath);
            var maybeSymlink = FileUtilities.TryCreateSymbolicLink(path, target, isTargetFile: true);
            XAssert.IsTrue(maybeSymlink.Succeeded, maybeSymlink.Succeeded ? string.Empty : maybeSymlink.Failure.Describe());

            return path;
        }

        private string FL(string path, string relative, string target)
        {
            return FL(Path.Combine(path, relative), target);
        }

        private string C(string path, string relative) 
            => FileUtilities.FileSystem.IsPathRooted(path) 
            ? Path.Combine(path, relative) 
            : Path.Combine(GetFullPath(path), relative);

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
                        inheritability: HandleInheritability.None,
                        leaveOpen: false))
                {
                    action();
                }
            }
        }

        private static FileSystemType GetFileSystemType(string path)
        {
            using (FileStream fileStream = FileUtilities.CreateFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                return FileUtilities.GetVolumeFileSystemByHandle(fileStream.SafeFileHandle);
            }
        }

        private static bool SupportCopyOnWrite(string path)
        {
            using (FileStream fileStream = FileUtilities.CreateFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                return FileUtilities.CheckIfVolumeSupportsCopyOnWriteByHandle(fileStream.SafeFileHandle);
            }
        }
    }
}

#pragma warning restore AsyncFixer02
