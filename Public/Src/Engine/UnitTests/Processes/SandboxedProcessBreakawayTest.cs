// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using System;
using System.Diagnostics;
using System.Linq;
using BuildXL.Processes;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using BuildXL.Native.IO;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Test.BuildXL.TestUtilities;

namespace Test.BuildXL.Processes
{
    public sealed class SandboxedProcessBreakawayTest : SandboxedProcessTestBase
    {
        public SandboxedProcessBreakawayTest(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        private static IEnumerable<object[]> OSAgnosticChildBreakawayData()
        {
            yield return Array.Empty<object[]>();
            yield return new object[] { new FileAccessManifest.BreakawayChildProcess(InfiniteWaiterToolName) };
            yield return new object[] { new FileAccessManifest.BreakawayChildProcess(InfiniteWaiterToolName, "foo", CommandLineArgsSubstringContainmentIgnoreCase: false), "foo bar baz" };
            yield return new object[] { new FileAccessManifest.BreakawayChildProcess(InfiniteWaiterToolName, "foo", CommandLineArgsSubstringContainmentIgnoreCase: true), "bar FOO baz" };
            yield return new object[] { new FileAccessManifest.BreakawayChildProcess(InfiniteWaiterToolName, "foo", CommandLineArgsSubstringContainmentIgnoreCase: false), "bar FOO baz" };
        }

        private static IEnumerable<object[]> WindowsChildBreakawayData()
        {
            yield return new object[] { new FileAccessManifest.BreakawayChildProcess(InfiniteWaiterToolName.ToUpper(), "foo", CommandLineArgsSubstringContainmentIgnoreCase: false), "bar FOO baz" };
        }

        [Theory]
        [MemberData(nameof(OSAgnosticChildBreakawayData))]
        public void ChildProcessCanBreakawayWhenConfiguredOSAgnostic(FileAccessManifest.BreakawayChildProcess breakawayChildProcess = null, string commandArgs = null)
            => ChildProcessCanBreakawayWhenConfiguredHelper(breakawayChildProcess, commandArgs);

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [MemberData(nameof(WindowsChildBreakawayData))]
        public void ChildProcessCanBreakawayWhenConfiguredWindows(FileAccessManifest.BreakawayChildProcess breakawayChildProcess, string commandArgs)
            => ChildProcessCanBreakawayWhenConfiguredHelper(breakawayChildProcess, commandArgs);

        [TheoryIfSupported(requiresEBPFEnabled: true)]
        [InlineData(4094, false)]
        [InlineData(4094, true)]
        [InlineData(4095, false)]
        [InlineData(4095, true)]
        [InlineData(4096, false)]
        [InlineData(4096, true)]
        [InlineData(4097, false)]
        [InlineData(4097, true)]
        [InlineData(8192, false)]
        [InlineData(8192, true)]
        public void BreakawayWithArgumentsAtCaptureBoundary(int capturedLength, bool constrainArguments)
        {
            const string marker = "marker ";
            string executable = CreateFileArtifactWithName(InfiniteWaiterToolName, TestDeploymentDir).Path.ToString(Context.PathTable);
            // argv capture includes argv[0], a separating space and the terminating null.
            string arguments = marker + new string('x', capturedLength - Encoding.UTF8.GetByteCount(executable) - marker.Length - 2);
            var rule = new FileAccessManifest.BreakawayChildProcess(InfiniteWaiterToolName, constrainArguments ? "marker" : null);

            ChildProcessCanBreakawayWhenConfiguredHelper(
                rule, arguments,
                expectedBreakaway: !constrainArguments || capturedLength < 4096,
                expectIncompleteArguments: constrainArguments && capturedLength >= 4096);
        }

        [TheoryIfSupported(requiresEBPFEnabled: true)]
        [InlineData(126, false)]
        [InlineData(126, true)]
        [InlineData(127, false)]
        [InlineData(127, true)]
        [InlineData(128, false)]
        [InlineData(128, true)]
        public void BreakawayWithTooManyArguments(int argumentCount, bool constrainArguments)
        {
            string arguments = string.Join(" ", Enumerable.Repeat("marker", argumentCount));
            var rule = new FileAccessManifest.BreakawayChildProcess(InfiniteWaiterToolName, constrainArguments ? "marker" : null);

            // MAX_ARGV_ARGUMENTS is 128, including argv[0]. A captured prefix must not satisfy the rule.
            ChildProcessCanBreakawayWhenConfiguredHelper(
                rule, arguments,
                expectedBreakaway: !constrainArguments || argumentCount < 128,
                expectIncompleteArguments: constrainArguments && argumentCount >= 128);
        }

        [TheoryIfSupported(requiresEBPFEnabled: true)]
        [InlineData("prefix")]
        [InlineData("suffix")]
        public void BreakawayRequiresExactExecutableName(string extra)
        {
            string toolName = extra == "prefix" ? extra + InfiniteWaiterToolName : InfiniteWaiterToolName + extra;
            ChildProcessCanBreakawayWhenConfiguredHelper(new FileAccessManifest.BreakawayChildProcess(toolName), expectedBreakaway: false);
        }

        private void ChildProcessCanBreakawayWhenConfiguredHelper(
            FileAccessManifest.BreakawayChildProcess breakawayChildProcess = null,
            string commandArgs = null,
            bool? expectedBreakaway = null,
            bool expectIncompleteArguments = false)
        {
            // Skip this test if running on .NET Framework with vstest
            // Reason: when this is the case and code coverage is turned on, launching breakaway 
            //         processes here causes the code coverage monitoring process to hang.
            if (!OperatingSystemHelper.IsDotNetCore && IsRunningInVsTestTestHost())
            {
                return;
            }

            // We use InfiniteWaiter (a process that waits forever) as a long-living process that we can actually check it can
            // escape the job object
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: breakawayChildProcess is null ? null : new[] { breakawayChildProcess })
            {
                FailUnexpectedFileAccesses = false
            };

            // We instruct the regular test process to spawn InfiniteWaiter as a child
            var info = ToProcessInfo(
                ToProcess(
                    Operation.SpawnExe(
                        Context.PathTable,
                        CreateFileArtifactWithName(InfiniteWaiterToolName, TestDeploymentDir),
                        commandArgs)),
                fileAccessManifest: fam);

            // Let's shorten the default time to wait for nested processes, since we are spawning 
            // a process that never ends and we don't want this test to wait for that long
            info.NestedProcessTerminationTimeout = TimeSpan.FromMilliseconds(10);

            var result = RunProcess(info).GetAwaiter().GetResult();
            var spawnedChildren = RetrieveChildProcessesCreatedBySpawnExe(result).ToArray();
            try
            {
                if (expectIncompleteArguments)
                {
                    XAssert.AreEqual(ExitCodes.MessageProcessingFailure, result.ExitCode);
                    XAssert.IsTrue(m_eventListener.GetLogMessagesForEventId((int)global::BuildXL.Processes.Tracing.LogEventId.FullSandboxInternalErrorMessage)
                        .Any(message => message.Contains("Cannot evaluate argument-constrained breakaway rule")));
                    return;
                }

                if (result.ExitCode != 0)
                {
                    XAssert.Fail(
                        $"Process exited with exit code {result.ExitCode}." +
                        $"\n\n=== stdout ===\n\n ${result.StandardOutput.ReadValueAsync().Result}" +
                        $"\n\n=== stderr ===\n\n ${result.StandardError.ReadValueAsync().Result}");
                }

                bool shouldBreakaway = expectedBreakaway ?? (breakawayChildProcess is not null
                    && (breakawayChildProcess.RequiredCommandLineArgsSubstring is null
                        || commandArgs.IndexOf(breakawayChildProcess.RequiredCommandLineArgsSubstring, breakawayChildProcess.CommandLineArgsSubstringContainmentIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) != -1));
                if (!shouldBreakaway)
                {
                    XAssert.IsTrue(result.Killed);
                    XAssert.IsNotNull(result.SurvivingChildProcesses);
                    XAssert.Contains(
                        result.SurvivingChildProcesses.Select(p => p?.Path).Where(p => p != null).Select(p => Path.GetFileName(p).ToUpperInvariant()),
                        InfiniteWaiterToolName.ToUpperInvariant());
                }
                else
                {
                    XAssert.IsFalse(result.Killed);
                    if (result.SurvivingChildProcesses != null && result.SurvivingChildProcesses.Any())
                    {
                        var survivors = string.Join(
                            ", ",
                            result.SurvivingChildProcesses.Select(p => p?.Path != null ? Path.GetFileName(p.Path) : "<unknown>"));
                        XAssert.Fail($"Unexpected {result.SurvivingChildProcesses.Count()} surviving child processes: {survivors}");
                    }

                    var infiniteWaiterInfo = spawnedChildren.Single();
                    using var dummyWaiter = Process.GetProcessById(infiniteWaiterInfo.pid);
                    XAssert.AreEqual(infiniteWaiterInfo.processName, dummyWaiter.ProcessName);
                }
            }
            finally
            {
                // Also clean up on assertion failure, in particular if an incomplete argument capture incorrectly broke away.
                foreach (var child in spawnedChildren)
                {
                    try
                    {
                        using var process = Process.GetProcessById(child.pid);
                        if (!process.HasExited && process.ProcessName == child.processName)
                        {
                            process.Kill();
                        }
                    }
#pragma warning disable ERP022 // Cleanup is best effort; see comment below.
                    catch (Exception)
                    {
                        // Best effort: the sandbox may have already terminated and reaped this child, and the pid
                        // may even have been recycled. Never let cleanup replace a genuine assertion failure.
                    }
#pragma warning restore ERP022
                }
            }
        }

        [Fact]
        public void BreakawayProcessIsNotDetoured()
        {
            // TODO: doesn't currently work on Linux
            if (OperatingSystemHelper.IsLinuxOS) return;

            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: new[] { new FileAccessManifest.BreakawayChildProcess(TestProcessToolName) })
            {
                FailUnexpectedFileAccesses = false,
                ReportUnexpectedFileAccesses = true,
                ReportFileAccesses = true
            };

            var srcFile1 = CreateSourceFile();
            var srcFile2 = CreateSourceFile();

            var info = ToProcessInfo(
                ToProcess(
                    Operation.ReadFile(srcFile1),
                    Operation.Spawn(
                        Context.PathTable,
                        true,
                        Operation.ReadFile(srcFile2))),
                fileAccessManifest: fam);

            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            var observedAccesses = result.FileAccesses
                .Select(reportedAccess => AbsolutePath.TryCreate(Context.PathTable, reportedAccess.GetPath(Context.PathTable), out AbsolutePath result) ? result : AbsolutePath.Invalid)
                .ToArray();

            // We should see the access that happens on the main test process
            XAssert.Contains(observedAccesses, srcFile1.Path);
            // We shouldn't see the access that happens on the spawned process
            XAssert.ContainsNot(observedAccesses, srcFile2.Path);

            // Only a single process should be reported: the parent one
            var testProcess = ExcludeInjectedOnes(result.Processes).Single();
            XAssert.AreEqual(TestProcessToolName.ToLowerInvariant(), Path.GetFileName(testProcess.Path).ToLowerInvariant());
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void BreakawayProcessCanReportAugmentedAccesses()
        {
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: new[] { new FileAccessManifest.BreakawayChildProcess(TestProcessToolName) })
            {
                FailUnexpectedFileAccesses = false,
                ReportUnexpectedFileAccesses = true,
                ReportFileAccesses = true
            };

            var srcFile = CreateSourceFile();
            var output = CreateOutputFileArtifact();

            fam.AddScope(srcFile, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);
            fam.AddScope(output, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);

            var info = ToProcessInfo(
                ToProcess(
                    Operation.AugmentedRead(srcFile),
                    Operation.AugmentedWrite(output)),
                fileAccessManifest: fam);

            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            var observedAccesses = result.FileAccesses.Select(fa => fa.ManifestPath);

            XAssert.Contains(observedAccesses, srcFile.Path, output.Path);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void NonBreakawayProcessCannotReportAugmentedAccesses()
        {
            var fam = new FileAccessManifest(
                Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportUnexpectedFileAccesses = true,
                ReportFileAccesses = true
            };

            var output = CreateOutputFileArtifact();

            var info = ToProcessInfo(
                ToProcess(
                    Operation.AugmentedWrite(output)),
                fileAccessManifest: fam);

            // We expect a failure due to not being able to report the augmented access
            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(1, result.ExitCode);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void AugmentedAccessHasTheRightManifestPath()
        {
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: new[] { new FileAccessManifest.BreakawayChildProcess(TestProcessToolName) })
            {
                FailUnexpectedFileAccesses = false,
                ReportUnexpectedFileAccesses = true,
                ReportFileAccesses = true
            };

            var basePath = TestBinRootPath.Combine(Context.PathTable, "foo");

            var output = CreateOutputFileArtifact(basePath);

            fam.AddScope(basePath, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowAll | FileAccessPolicy.ReportAccess);

            var info = ToProcessInfo(
                ToProcess(
                    Operation.AugmentedWrite(output)),
                fileAccessManifest: fam);

            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            var fileAccess = result.ExplicitlyReportedFileAccesses.Single(rfa => rfa.Method == FileAccessStatusMethod.TrustedTool);
            XAssert.AreEqual(basePath, fileAccess.ManifestPath);
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void MaskedReportAugmentedAccessIsNotReported(bool reportFileAccesses)
        {
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: new[] { new FileAccessManifest.BreakawayChildProcess(TestProcessToolName) })
            {
                FailUnexpectedFileAccesses = false,
                ReportUnexpectedFileAccesses = true,
                ReportFileAccesses = reportFileAccesses
            };

            var basePath = TestBinRootPath.Combine(Context.PathTable, "foo");

            var output1 = CreateOutputFileArtifact(basePath);
            var output2 = CreateOutputFileArtifact(basePath);

            // We mask reporting accesses for output1 and enable it for output2
            fam.AddScope(output1.Path, ~FileAccessPolicy.ReportAccess, FileAccessPolicy.AllowAll);
            fam.AddScope(output2.Path, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowAll | FileAccessPolicy.ReportAccess);

            var collector = new FileAccessDetoursListenerCollector(Context.PathTable);

            var info = ToProcessInfo(
                ToProcess(
                    Operation.AugmentedWrite(output1),
                    Operation.AugmentedWrite(output2)),
                fileAccessManifest: fam,
                detoursListener: collector);

            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            // We should get a single explicit access with output2, since output1 shouldn't be reported
            var accessPath = result.ExplicitlyReportedFileAccesses.Single(rfa => rfa.Method == FileAccessStatusMethod.TrustedTool).ManifestPath;
            XAssert.AreEqual(output2.Path, accessPath);

            // We should get both accesses as part of the (optional) FileAccess on request
            if (reportFileAccesses)
            {
                var allTrustedAcceses = result.FileAccesses.Where(rfa => rfa.Method == FileAccessStatusMethod.TrustedTool).Select(rfa => rfa.ManifestPath);
                XAssert.Contains(allTrustedAcceses, output1.Path, output2.Path);
            }
            else
            {
                // Make sure the access related to output1 is not actually reported, and the only one the listener got is output2
                XAssert.Contains(collector.GetFileAccessPaths(), output2.Path);
                XAssert.ContainsNot(collector.GetFileAccessPaths(), output1.Path);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void AugmentedAccessPathsAreCanonicalized()
        {
            var fam = new FileAccessManifest(
                    Context.PathTable,
                    childProcessesToBreakawayFromSandbox: new[] { new FileAccessManifest.BreakawayChildProcess(TestProcessToolName) })
            {
                FailUnexpectedFileAccesses = false,
                ReportUnexpectedFileAccesses = true,
                ReportFileAccesses = true
            };

            var basePath = TestBinRootPath.Combine(Context.PathTable, "foo").Combine(Context.PathTable, "bar");

            // Let's create a path that is equivalent to base path but it is constructed with '..'
            string nonCanonicalBasePath = Path.Combine(basePath.ToString(Context.PathTable), "..", "bar");

            var source = CreateSourceFile(basePath);
            var output = CreateOutputFileArtifact(basePath);

            // Now create non-canonical paths for source and output
            string nonCanonicalSource = Path.Combine(nonCanonicalBasePath, source.Path.GetName(Context.PathTable).ToString(Context.StringTable));
            string nonCanonicalOutput = Path.Combine(nonCanonicalBasePath, output.Path.GetName(Context.PathTable).ToString(Context.StringTable));

            var collector = new FileAccessDetoursListenerCollector(Context.PathTable);

            var info = ToProcessInfo(
                ToProcess(
                    Operation.AugmentedRead(nonCanonicalSource),
                    Operation.AugmentedWrite(nonCanonicalOutput)),
                fileAccessManifest: fam,
                detoursListener: collector);

            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            // Let's check the raw paths reported by detours to make sure they are canonicalized
            var allRawPaths = collector.GetAllFileAccessPaths().Select(path => path.ToUpperInvariant());
            XAssert.Contains(allRawPaths, source.Path.ToString(Context.PathTable).ToUpperInvariant(), output.Path.ToString(Context.PathTable).ToUpperInvariant());
        }
    }
}
