// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using Xunit;

namespace Test.BuildXL.Processes
{
    public class TraceFileBuilderTest : XunitBuildXLTest
    {
        public TraceFileBuilderTest(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void CanReportInvalidPathAndItWillBeSaved()
        {
            var builder = SandboxedProcessTraceBuilder.CreateBuilderForTest();
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, new AbsolutePath(1), @"$%^/\@#", 0, false, null);
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, AbsolutePath.Invalid, @"$%^/\@#[]{}", 0, false, null);

            XAssert.AreEqual(2, builder.Paths.Count);
            XAssert.AreEqual(2, builder.Operations.Count);
        }

        [Fact]
        public void PathCaseSensitivityDependsOnOperatingSystem()
        {
            var builder = SandboxedProcessTraceBuilder.CreateBuilderForTest();
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, new AbsolutePath(2), @"C:\foo.txt", 0, false, null);
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, new AbsolutePath(2), @"C:\FOO.txt", 0, false, null);
            builder.ReportFileAccess(1, ReportedFileOperation.CreateFile, RequestedAccess.Write, new AbsolutePath(2), @"C:\foo.TXT", 0, false, null);

            if (OperatingSystemHelper.IsPathComparisonCaseSensitive)
            {
                // Linux paths are case sensitive
                XAssert.AreEqual(3, builder.Paths.Count);
            }
            else
            {
                // MacOS and Windows paths are not 
                XAssert.AreEqual(1, builder.Paths.Count);
            }

            XAssert.AreEqual(3, builder.Operations.Count);
        }
    }
}
