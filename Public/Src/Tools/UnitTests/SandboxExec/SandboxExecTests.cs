// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.SandboxExec;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.SandboxExec
{
    /// <summary>
    /// Tests for <see cref="SandboxExec"/>.
    /// /// </summary>
    public class SandboxExecTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public SandboxExecTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void DedupeReportedFileAccesses()
        {
            var process = new ReportedProcess(1000, "/usr/bin/touch");
            var writeReport = new ReportedFileAccess(ReportedFileOperation.CreateFile,
                process,
                RequestedAccess.Write,
                FileAccessStatus.Allowed,
                true,
                0,
                Usn.Zero,
                DesiredAccess.GENERIC_WRITE,
                ShareMode.FILE_SHARE_WRITE,
                CreationDisposition.CREATE_NEW,
                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                AbsolutePath.Invalid,
                "/tmp/out1",
                "*");

            var readReport = new ReportedFileAccess(ReportedFileOperation.GetFileAttributes,
                process,
                RequestedAccess.Read,
                FileAccessStatus.Allowed,
                true,
                0,
                Usn.Zero,
                DesiredAccess.GENERIC_READ,
                ShareMode.FILE_SHARE_READ,
                CreationDisposition.OPEN_EXISTING,
                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                AbsolutePath.Invalid,
                "/tmp/out1",
                "*");

            HashSet<ReportedFileAccess> allFileAccesses = new HashSet<ReportedFileAccess>() { writeReport, readReport };
            HashSet<ReportedFileAccess> explicitFileAccesses = new HashSet<ReportedFileAccess>() { readReport, writeReport };

            var runner = CreateSandboxExecRunner();
            var dedupedReports = runner.DedupeAccessReports(allFileAccesses, explicitFileAccesses);

            XAssert.IsTrue(dedupedReports.Count == 2);
            var result = new string[2];
            dedupedReports.CopyTo(result);
            XAssert.AreArraysEqual(new string[] { " W  /tmp/out1", " R  /tmp/out1" }, result, true);
        }

        private static readonly SandboxExecRunner.Options Defaults = SandboxExecRunner.Options.Defaults;

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestParseArgsNoToolArgs(bool withSeparator)
        {
            var procArgs = new string[] { "/bin/ls", "-l" };
            var args = withSeparator
                ? new[] { "--" }.Concat(procArgs).ToArray()
                : procArgs;
            var result = SandboxExecRunner.ParseArgs(args);
            XAssert.AreEqual(Defaults, result.toolOptions);
            XAssert.ArrayEqual(procArgs, result.procArgs);
        }

        [Fact]
        public void TestParseArgsWithDoubleDashInProcArgs()
        {
            var procArgs = new string[] { "/bin/cat", "--" };
            var args = new[] { "--" }.Concat(procArgs).ToArray();
            var result = SandboxExecRunner.ParseArgs(args);
            XAssert.AreEqual(Defaults, result.toolOptions);
            XAssert.ArrayEqual(procArgs, result.procArgs);
        }

        [Theory]
        [InlineData("/verbose", true)]
        [InlineData("/verbose-", false)]
        [InlineData("/v", true)]
        [InlineData("/v+", true)]
        [InlineData("/v-", false)]
        public void TestParseArgsWithToolVerboseArgs(string arg, bool expectedVerboseValue)
        {
            var procArgs = new string[] { "/bin/cat", "--" };
            var args = new[] { arg, "--" }.Concat(procArgs).ToArray();
            var result = SandboxExecRunner.ParseArgs(args);
            XAssert.AreEqual(expectedVerboseValue, result.toolOptions.Verbose);
            XAssert.AreEqual(Defaults.ReportQueueSizeMB, result.toolOptions.ReportQueueSizeMB);
            XAssert.ArrayEqual(procArgs, result.procArgs);
        }

        [Theory]
        [InlineData("/verbose", "/verbose-", false, null)]
        [InlineData("/verbose-", "/v", true, null)]
        [InlineData("/v", "/r:400", true, 400)]
        [InlineData("/r:300", "/r:500", null, 500)]
        [InlineData("/r:300", "/verbose", true, 300)]
        public void TestParseArgsWithTwoToolArgs(string arg1, string arg2, bool? expectedVerboseValue, int? expectedQueueSizeValue)
        {
            var procArgs = new string[] { "/bin/cat", "--" };
            var args = new[] { arg1, arg2, "--" }.Concat(procArgs).ToArray();
            var result = SandboxExecRunner.ParseArgs(args);
            XAssert.AreEqual(expectedVerboseValue ?? Defaults.Verbose, result.toolOptions.Verbose);
            XAssert.AreEqual(expectedQueueSizeValue ?? (int)Defaults.ReportQueueSizeMB, (int)result.toolOptions.ReportQueueSizeMB);
            XAssert.ArrayEqual(procArgs, result.procArgs);
        }

        [Fact]
        public void CommandLineArgumentsGetParsedCorrectly()
        {
            var input = new string[] { "/usr/bin/clang", "test.c", "-o", "test", "'a b c'", "/a/b/c/d e f.app"};
            var arguments = SandboxExecRunner.ExtractAndEscapeCommandLineArguments(input);
            if (OperatingSystemHelper.IsUnixOS)
            {
                XAssert.AreEqual("'test.c' '-o' 'test' ''\\''a b c'\\''' '/a/b/c/d e f.app'", arguments);
            }
            else
            {
                XAssert.AreEqual("test.c -o test 'a b c' /a/b/c/d e f.app", arguments);
            }
        }

        [Fact]
        public void CreateSandboxedProcessInfoWithExplicitReporting()
        {
            var instance = CreateSandboxExecRunner();
            var processInfo = SandboxExecRunner.CreateSandboxedProcessInfo("/usr/bin/touch", instance);

            // Make sure the SandboxExec sandboxed process info is always gerenated to explicitly report all file accesses
            XAssert.IsTrue(processInfo.FileAccessManifest.ReportFileAccesses);
            XAssert.IsFalse(processInfo.FileAccessManifest.FailUnexpectedFileAccesses);
        }

        // Run this on Unix only as our CI pipeline makes sure the sandbox is running prior to execution of this test
        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public async Task CheckForFileAccessReportsWhenRunningProcessWithKextLoaded()
        {
            try
            {
                var instance = CreateSandboxExecRunner();
                using (var process = await SandboxExecRunner.ExecuteAsync(instance, new string[] { "/bin/ls", "." }, TestOutputDirectory))
                {
                    var result = await process.GetResultAsync();

                    var distinctAccessReports = instance.DedupeAccessReports(
                        result.FileAccesses,
                        result.ExplicitlyReportedFileAccesses,
                        result.AllUnexpectedFileAccesses);

                    XAssert.IsTrue(distinctAccessReports.Count > 0);
                    XAssert.Contains(distinctAccessReports, " R  /bin/ls");
                }
            }
            catch (Exception ex)
            {
                // This should not happen if the sandbox is loaded and non of the report processing did throw
                XAssert.Fail("CheckForFileAccessReportsWhenRunningProcessWithKextLoaded, threw an exception: {0}", ex);
            }
        }

        private SandboxExecRunner CreateSandboxExecRunner()
        {
            return new SandboxExecRunner(GetSandboxConnection());
        }
    }
}
