// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using System;
using System.Diagnostics;
using System.Linq;
using BuildXL.Processes;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public sealed class SandboxedProcessBreakawayTest : SandboxedProcessTestBase
    {
        public SandboxedProcessBreakawayTest(ITestOutputHelper output)
            : base(output) { }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void ChildProcessCanBreakawayWhenConfigured(bool letInfiniteWaiterSurvive)
        {
            // We use InfiniteWaiter (a process that waits forever) as a long-living process that we can actually check it can
            // escape the job object
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: letInfiniteWaiterSurvive ? new[] { InfiniteWaiterToolName } : null)
            {
                FailUnexpectedFileAccesses = false
            };

            // We instruct the regular test process to spawn InfiniteWaiter as a child
            var info = ToProcessInfo(
                ToProcess(
                    Operation.SpawnExe(
                        Context.PathTable, 
                        CreateFileArtifactWithName(InfiniteWaiterToolName, TestDeploymentDir))),
                fileAccessManifest: fam);

            // Let's shorten the default time to wait for nested processes, since we are spawning 
            // a process that never ends and we don't want this test to wait for that long
            info.NestedProcessTerminationTimeout = TimeSpan.FromMilliseconds(10);

            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            if (!letInfiniteWaiterSurvive)
            {
                // If we didn't let infinite waiter escape, we should have killed it when the job object was finalized
                XAssert.IsTrue(result.Killed);
                XAssert.Contains(
                    result.SurvivingChildProcesses.Select(p => p?.Path).Where(p => p != null).Select(p => System.IO.Path.GetFileName(p).ToUpperInvariant()),
                    InfiniteWaiterToolName.ToUpperInvariant());
            }
            else
            {
                // If we did let it escape, then nothing should have been killed (nor tried to survive and later killed, from the job object point of view)
                XAssert.IsFalse(result.Killed);
                XAssert.IsTrue(result.SurvivingChildProcesses == null);

                // Let's retrieve the child process and confirm it survived
                var infiniteWaiterInfo = RetrieveChildProcessesCreatedBySpawnExe(result).Single();
                // The fact that this does not throw confirms survival
                var dummyWaiter = Process.GetProcessById(infiniteWaiterInfo.pid);
                // Just being protective, let's make sure we are talking about the same process
                XAssert.AreEqual(infiniteWaiterInfo.processName, dummyWaiter.ProcessName);

                // Now let's kill the surviving process, since we don't want it to linger around unnecesarily
                dummyWaiter.Kill();
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void BreakawayProcessIsNotDetoured()
        {
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: new[] { TestProcessToolName })
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
                .Where(reportedAccess => reportedAccess.Path != null)
                .Select(reportedAccess => AbsolutePath.TryCreate(Context.PathTable, reportedAccess.Path, out AbsolutePath result) ? result : AbsolutePath.Invalid);

            // We should see the access that happens on the main test process
            XAssert.Contains(observedAccesses, srcFile1.Path);
            // We shouldn't see the access that happens on the spawned process
            XAssert.ContainsNot(observedAccesses, srcFile2.Path);

            // Only a single process should be reported: the parent one
            var testProcess = result.Processes.Single();
            XAssert.AreEqual(TestProcessToolName.ToLowerInvariant(), System.IO.Path.GetFileName(testProcess.Path).ToLowerInvariant());
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void BreakawayProcessCanReportAugmentedAccesses()
        {
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: new[] { TestProcessToolName })
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
                childProcessesToBreakawayFromSandbox: new[] { TestProcessToolName })
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

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void MaskedReportAugmentedAccessIsNotReported()
        {
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: new[] { TestProcessToolName })
            {
                FailUnexpectedFileAccesses = false,
                ReportUnexpectedFileAccesses = true,
                ReportFileAccesses = true
            };

            var basePath = TestBinRootPath.Combine(Context.PathTable, "foo");

            var output1 = CreateOutputFileArtifact(basePath);
            var output2 = CreateOutputFileArtifact(basePath);

            // We mask reporting accesses for output1 and enable it for output2
            fam.AddScope(output1.Path, ~FileAccessPolicy.ReportAccess, FileAccessPolicy.AllowAll);
            fam.AddScope(output2.Path, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowAll | FileAccessPolicy.ReportAccess);
            
            var info = ToProcessInfo(
                ToProcess(
                    Operation.AugmentedWrite(output1),
                    Operation.AugmentedWrite(output2)),
                fileAccessManifest: fam);

            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            // We should get a single access with output2, since output1 should be ignored
            var accessPath = result.FileAccesses.Single(rfa => rfa.Method == FileAccessStatusMethod.TrustedTool).ManifestPath;
            XAssert.AreEqual(output2.Path, accessPath);
        }
    }
}
