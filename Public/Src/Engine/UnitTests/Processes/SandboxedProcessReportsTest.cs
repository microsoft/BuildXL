// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Processes;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Test.BuildXL.Processes
{
    public class SandboxedProcessReportsTest : XunitBuildXLTest
    {
        public SandboxedProcessReportsTest(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void TestParseFileAccessReportLineFencePost()
        {
            var line = "Process:1|1|0|0|1|1|1|1|1|1|1|1|1|1|1";
            XAssert.AreEqual(15, line.Split('|').Length);
            var ok = FileAccessReportLine.TryParse(ref line, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out string error);
            XAssert.IsTrue(ok, error);
        }

        [Fact]
        public void TestRealCase()
        {
            var line = @"GetFileAttributes:15b8|453|0|4|1|1|3|ffffffffffffffff|80000000|1|3|8100000|ffffffff|1000c868|D:\a\_work\1\s\Out\Objects\nuget\Microsoft.Net.Compilers.4.0.1\tools\en\AsyncFixer.resources.dll|";
            var ok = FileAccessReportLine.TryParse(ref line, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out string error);
            XAssert.IsTrue(ok, error);
        }
        
        [Fact]
        public void ParseWithInvalidStatus()
        {
            // The status (the fifth element) is invalid here.
            var line = "Process:1|2|3|0|10|1|1|1|1|1|1|1|1|1|1";
            var ok = FileAccessReportLine.TryParse(ref line, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out string error);
            XAssert.IsFalse(ok, error);
        }
        
        [Fact]
        public void ParseWithInvalidRequestAccess()
        {
            // The status (the fifth element) is invalid here.
            var line = "Process:1|2|3|12312|1|1|1|1|1|1|1|1|1|1|1";
            var ok = FileAccessReportLine.TryParse(ref line, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out string error);
            XAssert.IsFalse(ok, error);
        }
        
        [Fact]
        public void Test14ItemsShouldNotCrash()
        {
            // There was a bug in the old implementation that was causing IndexOutOfRange error on the input with exactly 14 elements.
            // (the min number of items is 15).
            var line = "Process:1|1|0|0|1|1|1|1|1|1|1|1|1|1";
            var ok = FileAccessReportLine.TryParse(ref line, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out string error);
            XAssert.IsFalse(ok);
            XAssert.IsNotNull(error);
        }
        
        [Theory]
        [MemberData(nameof(InsufficientItemsSource))]
        public void ParseFailsWithLackOfData(int items)
        {
            var line = $"Process:{string.Join("|", Enumerable.Range(1, items))}";
            var ok = FileAccessReportLine.TryParse(ref line, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out string error);
            XAssert.IsFalse(ok);
            XAssert.IsNotNull(error);
        }

        public static IEnumerable<object[]> InsufficientItemsSource => Enumerable.Range(0, 15).Select(x => new object[] {x});

        [Fact]
        public void AugmentedFileAccessReportLineRountrip()
        {
            var line = FileAccessReportLine.GetReportLineForAugmentedFileAccess(
                    ReportedFileOperation.Process,
                    1234,
                    RequestedAccess.Enumerate,
                    FileAccessStatus.Allowed,
                    0,
                    Usn.Zero,
                    DesiredAccess.GENERIC_READ,
                    ShareMode.FILE_SHARE_NONE,
                    CreationDisposition.OPEN_ALWAYS,
                    FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                    FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY,
                    "C:\\foo\\bar",
                    enumeratePattern: "*",
                    processArgs: "some args");

            // We need to manually strip the report type
            int splitIndex = line.IndexOf(',');
            XAssert.IsTrue(splitIndex > 0);

            // Let's verify this is an augmented file access report line
            string reportTypeString = line.Substring(0, splitIndex);
            XAssert.AreEqual(ReportType.AugmentedFileAccess, (ReportType)Enum.Parse(typeof(ReportType), reportTypeString));

            line = line.Substring(splitIndex + 1);

            var ok = FileAccessReportLine.TryParse(
                ref line,
                out var processId,
                out var id,
                out var correlationId,
                out var operation,
                out var requestedAccess,
                out var status,
                out var explicitlyReported,
                out var error,
                out var usn,
                out var desiredAccess,
                out var shareMode,
                out var creationDisposition,
                out var flags,
                out var openedFileOrDirectoryAttributes,
                out var absolutePath,
                out var path,
                out var enumeratePattern,
                out var processArgs,
                out string errorMessage);

            XAssert.IsTrue(ok, errorMessage);

            XAssert.AreEqual(ReportedFileOperation.Process, operation);
            XAssert.AreEqual(1234u, processId);
            XAssert.AreEqual(SandboxedProcessReports.FileAccessNoId, id);
            XAssert.AreEqual(SandboxedProcessReports.FileAccessNoId, correlationId);
            XAssert.AreEqual(RequestedAccess.Enumerate, requestedAccess);
            XAssert.AreEqual(FileAccessStatus.Allowed, status);
            XAssert.AreEqual(true, explicitlyReported);
            XAssert.AreEqual(0u, error);
            XAssert.AreEqual(Usn.Zero, usn);
            XAssert.AreEqual(DesiredAccess.GENERIC_READ, desiredAccess);
            XAssert.AreEqual(ShareMode.FILE_SHARE_NONE, shareMode);
            XAssert.AreEqual(CreationDisposition.OPEN_ALWAYS, creationDisposition);
            XAssert.AreEqual(FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL, flags);
            XAssert.AreEqual(FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY, openedFileOrDirectoryAttributes);
            XAssert.AreEqual(AbsolutePath.Invalid, absolutePath);
            XAssert.AreEqual("C:\\foo\\bar", path);
            XAssert.AreEqual("*", enumeratePattern);
            XAssert.AreEqual("some args\r\n", processArgs);
        }
    }
}
