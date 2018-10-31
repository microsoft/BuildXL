// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Native.IO.Windows;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public sealed class ReportedFileAccessTests : XunitBuildXLTest
    {
        public ReportedFileAccessTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void DoesPathContainsWildcardsTests()
        {
            XAssert.AreEqual(ReportedFileAccess.DoesPathContainsWildcards("\\\\?\\c:\\foo\\bar\\?"), true);
            XAssert.AreEqual(ReportedFileAccess.DoesPathContainsWildcards("\\\\?\\c:\\foo\\bar\\*"), true);
            XAssert.AreEqual(ReportedFileAccess.DoesPathContainsWildcards("\\\\?\\c:\\foo\\bar\\a"), false);
            XAssert.AreEqual(ReportedFileAccess.DoesPathContainsWildcards("c:\\foo\\bar\\?"), true);
            XAssert.AreEqual(ReportedFileAccess.DoesPathContainsWildcards("c:\\foo\\bar\\*"), true);
            XAssert.AreEqual(ReportedFileAccess.DoesPathContainsWildcards("c:\\foo\\bar\\a"), false);
            XAssert.AreEqual(ReportedFileAccess.DoesPathContainsWildcards("ab?cd"), true);
            XAssert.AreEqual(ReportedFileAccess.DoesPathContainsWildcards("abcd"), false);
        }

        [Fact]
        public void ReportedFileAccessEquality()
        {
            var pathTable = new PathTable();
            AbsolutePath file1 = AbsolutePath.Create(pathTable, A("t", "file1.txt"));
            AbsolutePath file2 = AbsolutePath.Create(pathTable, A("t", "file2.txt"));

            var process = new ReportedProcess(0, string.Empty);
            Test.BuildXL.TestUtilities.Xunit.StructTester.TestEquality(
                baseValue:
                    ReportedFileAccess.Create(
                        ReportedFileOperation.CreateFile,
                        process,
                        RequestedAccess.Read,
                        FileAccessStatus.Allowed,
                        true,
                        0,
                        ReportedFileAccess.NoUsn,
                        DesiredAccess.GENERIC_READ,
                        ShareMode.FILE_SHARE_NONE,
                        CreationDisposition.OPEN_ALWAYS,
                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                        file1),
                equalValue:
                    ReportedFileAccess.Create(
                        ReportedFileOperation.CreateFile,
                        process,
                        RequestedAccess.Read,
                        FileAccessStatus.Allowed,
                        true,
                        0,
                        ReportedFileAccess.NoUsn,
                        DesiredAccess.GENERIC_READ,
                        ShareMode.FILE_SHARE_NONE,
                        CreationDisposition.OPEN_ALWAYS,
                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                        file1),
                notEqualValues: new[]
                                {
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Denied,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        file1),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Allowed,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        file2),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Allowed,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        pathTable,
                                        A("u", "file3.txt")),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Denied,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        pathTable,
                                        A("u", "file4.txt")),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Allowed,
                                        false,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        file1),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Write,
                                        FileAccessStatus.Allowed,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        file1)
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right,
                skipHashCodeForNotEqualValues: true);

            Test.BuildXL.TestUtilities.Xunit.StructTester.TestEquality(
                baseValue:
                    ReportedFileAccess.Create(
                        ReportedFileOperation.CreateFile,
                        process,
                        RequestedAccess.Read,
                        FileAccessStatus.Allowed,
                        true,
                        0,
                        ReportedFileAccess.NoUsn,
                        DesiredAccess.GENERIC_READ,
                        ShareMode.FILE_SHARE_NONE,
                        CreationDisposition.OPEN_ALWAYS,
                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                        pathTable,
                        A("x", "file5.txt")),
                equalValue:
                    ReportedFileAccess.Create(
                        ReportedFileOperation.CreateFile,
                        process,
                        RequestedAccess.Read,
                        FileAccessStatus.Allowed,
                        true,
                        0,
                        ReportedFileAccess.NoUsn,
                        DesiredAccess.GENERIC_READ,
                        ShareMode.FILE_SHARE_NONE,
                        CreationDisposition.OPEN_ALWAYS,
                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                        pathTable,
                        A("x", "file5.txt")),
                notEqualValues: new[]
                                {
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Denied,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        pathTable,
                                        A("x", "file5.txt")),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Allowed,
                                        true,
                                        0,
                                        new Usn(0),
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        pathTable,
                                        A("x", "file5.txt")),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Allowed,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        file1),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Denied,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        file2),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Allowed,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        pathTable,
                                        A("u", "file3.txt")),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Allowed,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        pathTable,
                                        A("u", "file4.txt")),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Read,
                                        FileAccessStatus.Allowed,
                                        false,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        pathTable,
                                        A("x", "file5.txt")),
                                    ReportedFileAccess.Create(
                                        ReportedFileOperation.CreateFile,
                                        process,
                                        RequestedAccess.Write,
                                        FileAccessStatus.Allowed,
                                        true,
                                        0,
                                        ReportedFileAccess.NoUsn,
                                        DesiredAccess.GENERIC_READ,
                                        ShareMode.FILE_SHARE_NONE,
                                        CreationDisposition.OPEN_ALWAYS,
                                        FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                                        pathTable,
                                        A("x", "file5.txt"))
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right,
                skipHashCodeForNotEqualValues: true);
         }

        [Fact]
        public void ReportedFileAccessCreate()
        {
            var pathTable = new PathTable();
            AbsolutePath file1 = AbsolutePath.Create(pathTable, A("t", "file1.txt"));

            var process = new ReportedProcess(0, string.Empty);

            ReportedFileAccess rfa1 = ReportedFileAccess.Create(
                ReportedFileOperation.CreateFile,
                process,
                RequestedAccess.Read,
                FileAccessStatus.Allowed,
                true,
                0,
                ReportedFileAccess.NoUsn,
                DesiredAccess.GENERIC_READ,
                ShareMode.FILE_SHARE_NONE,
                CreationDisposition.OPEN_ALWAYS,
                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                file1);
            XAssert.AreEqual(rfa1.Status, FileAccessStatus.Allowed);
            XAssert.AreEqual(rfa1.ManifestPath, file1);
            XAssert.AreEqual(rfa1.Path, null);
            XAssert.AreEqual(A("t", "file1.txt"), rfa1.GetPath(pathTable));

            ReportedFileAccess rfa2 = ReportedFileAccess.Create(
                ReportedFileOperation.CreateFile,
                process,
                RequestedAccess.Read,
                FileAccessStatus.CannotDeterminePolicy,
                true,
                0,
                new Usn(0),
                DesiredAccess.GENERIC_READ,
                ShareMode.FILE_SHARE_NONE,
                CreationDisposition.OPEN_ALWAYS,
                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                pathTable,
                A("t", "file1.txt"));
            XAssert.AreEqual(rfa2.Status, FileAccessStatus.CannotDeterminePolicy);
            XAssert.AreEqual(rfa2.ManifestPath, file1);
            XAssert.AreEqual(rfa2.Path, null);
            XAssert.AreEqual(A("t", "file1.txt"), rfa2.GetPath(pathTable));

            ReportedFileAccess rfa3 = ReportedFileAccess.Create(
                ReportedFileOperation.CreateFile,
                process,
                RequestedAccess.Read,
                FileAccessStatus.Denied,
                true,
                0,
                ReportedFileAccess.NoUsn,
                DesiredAccess.GENERIC_READ,
                ShareMode.FILE_SHARE_NONE,
                CreationDisposition.OPEN_ALWAYS,
                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                pathTable,
                A("t", "file2.txt"));
            XAssert.AreEqual(rfa3.Status, FileAccessStatus.Denied);
            XAssert.AreEqual(rfa3.ManifestPath, AbsolutePath.Invalid);
            XAssert.AreEqual(rfa3.Path, A("t", "file2.txt"));
            XAssert.AreEqual(A("t", "file2.txt"), rfa3.GetPath(pathTable));
    }
    }
}
