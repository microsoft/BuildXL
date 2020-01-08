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
using BuildXL.Native.IO;
using System.Collections.Generic;
using System.IO;

namespace Test.BuildXL.Processes
{
    public sealed class SandboxedProcessBreakawayTest : SandboxedProcessTestBase
    {
        private sealed class FileAccessCollector : IDetoursEventListener
        {
            private readonly PathTable m_pathTable;

            /// <summary>
            /// All reported paths that could be parsed as absolute paths
            /// </summary>
            public HashSet<AbsolutePath> FileAccessPaths { get; } = new HashSet<AbsolutePath>();

            /// <summary>
            /// All file accesses, and they came (raw) from detours
            /// </summary>
            public HashSet<string> AllFileAccessPaths { get; } = new HashSet<string>();

            public FileAccessCollector(PathTable pathTable) 
            {
                SetMessageHandlingFlags(MessageHandlingFlags.FileAccessNotify | MessageHandlingFlags.FileAccessCollect | MessageHandlingFlags.ProcessDataCollect | MessageHandlingFlags.ProcessDetoursStatusCollect);
                m_pathTable = pathTable;
            }

            public override void HandleFileAccess(long pipId, string pipDescription, ReportedFileOperation operation, RequestedAccess requestedAccess, FileAccessStatus status, bool explicitlyReported, uint processId, uint error, DesiredAccess desiredAccess, ShareMode shareMode, CreationDisposition creationDisposition, FlagsAndAttributes flagsAndAttributes, string path, string processArgs)
            {
                if (AbsolutePath.TryCreate(m_pathTable, path, out AbsolutePath absolutePath))
                {
                    FileAccessPaths.Add(absolutePath);
                }

                AllFileAccessPaths.Add(path);
            }

            public override void HandleDebugMessage(long pipId, string pipDescription, string debugMessage)
            {
            }

            public override void HandleProcessData(long pipId, string pipDescription, string processName, uint processId, DateTime creationDateTime, DateTime exitDateTime, TimeSpan kernelTime, TimeSpan userTime, uint exitCode, IOCounters ioCounters, uint parentProcessId)
            {
            }

            public override void HandleProcessDetouringStatus(ulong processId, uint reportStatus, string processName, string startApplicationName, string startCommandLine, bool needsInjection, ulong hJob, bool disableDetours, uint creationFlags, bool detoured, uint error, uint createProcessStatusReturn)
            {
            }
        }

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

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void MaskedReportAugmentedAccessIsNotReported(bool reportFileAccesses)
        {
            var fam = new FileAccessManifest(
                Context.PathTable,
                childProcessesToBreakawayFromSandbox: new[] { TestProcessToolName })
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

            var collector = new FileAccessCollector(Context.PathTable);

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
                XAssert.Contains(collector.FileAccessPaths, output2.Path);
                XAssert.ContainsNot(collector.FileAccessPaths, output1.Path);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void AugmentedAccessPathsAreCanonicalized()
        {
            var fam = new FileAccessManifest(
                    Context.PathTable,
                    childProcessesToBreakawayFromSandbox: new[] { TestProcessToolName })
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

            var collector = new FileAccessCollector(Context.PathTable);

            var info = ToProcessInfo(
                ToProcess(
                    Operation.AugmentedRead(nonCanonicalSource),
                    Operation.AugmentedWrite(nonCanonicalOutput)),
                fileAccessManifest: fam,
                detoursListener: collector);

            var result = RunProcess(info).GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            // Let's check the raw paths reported by detours to make sure they are canonicalized
            var allRawPaths = collector.AllFileAccessPaths.Select(path => path.ToUpperInvariant());
            XAssert.Contains(allRawPaths, source.Path.ToString(Context.PathTable).ToUpperInvariant(), output.Path.ToString(Context.PathTable).ToUpperInvariant());
        }
    }
}
