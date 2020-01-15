// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Processes;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using System;

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
            var line = "Process:1|1|1|1|1|1|1|1|1|1|1|1";
            XAssert.AreEqual(12, line.Split('|').Length);
            var ok = FileAccessReportLine.TryParse(ref line, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out string error);
            XAssert.IsTrue(ok, error);
        }

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
                out var absolutePath, 
                out var path, 
                out var enumeratePattern, 
                out var processArgs, 
                out string errorMessage);

            XAssert.IsTrue(ok, errorMessage);

            XAssert.AreEqual(ReportedFileOperation.Process, operation);
            XAssert.AreEqual(1234u, processId);
            XAssert.AreEqual(RequestedAccess.Enumerate, requestedAccess);
            XAssert.AreEqual(FileAccessStatus.Allowed, status);
            XAssert.AreEqual(true, explicitlyReported);
            XAssert.AreEqual(0u, error);
            XAssert.AreEqual(Usn.Zero, usn);
            XAssert.AreEqual(DesiredAccess.GENERIC_READ, desiredAccess);
            XAssert.AreEqual(ShareMode.FILE_SHARE_NONE, shareMode);
            XAssert.AreEqual(CreationDisposition.OPEN_ALWAYS, creationDisposition);
            XAssert.AreEqual(FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL, flags);
            XAssert.AreEqual(AbsolutePath.Invalid, absolutePath);
            XAssert.AreEqual("C:\\foo\\bar", path);
            XAssert.AreEqual("*", enumeratePattern);
            XAssert.AreEqual("some args\r\n", processArgs);
        }
    }
}
