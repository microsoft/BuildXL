// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Pips.Builders;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using FileUtilities = BuildXL.Native.IO.FileUtilities;
using Operation = Test.BuildXL.Executables.TestProcess.Operation;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Processes
{
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public sealed class PTraceSandboxedProcessTest : SandboxedProcessTestBase
    {
        private ITestOutputHelper TestOutput { get; }

        public PTraceSandboxedProcessTest(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            TestOutput = output;
        }

        [Fact]
        public async Task StaticallyLinkedProcessWithPTraceSandbox()
        {
            PrepareStaticallyLinkedProcess(
                out FileArtifact staticProcessArtifact, 
                out string unlinkedPath, 
                out string writePath, 
                out string rmdirPath, 
                out string renamedDirectoryOld, 
                out string renamedDirectoryNew, 
                out string renamePathOld, 
                out string renamePathNew, 
                out DirectoryArtifact workingDirectory);

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.FailUnexpectedFileAccesses = false;
            fam.ReportUnexpectedFileAccesses = true;
            fam.EnableLinuxPTraceSandbox = true;

            var staticProcessInfo = ToProcessInfo(
                ToProcess(new Operation[]
                {
                    Operation.SpawnExe(Context.PathTable, staticProcessArtifact, arguments: "0"),
                    Operation.WriteFile(CreateOutputFileArtifact()),
                }),
                workingDirectory: workingDirectory.Path.ToString(Context.PathTable),
                fileAccessManifest: fam
            );

            var result = await RunProcess(staticProcessInfo);

            AssertVerboseEventLogged(ProcessesLogEventId.PTraceSandboxLaunchedForPip);

            var expectedAccesses = new List<(string, ReportedFileOperation)>()
            {
                (unlinkedPath, ReportedFileOperation.KAuthDeleteFile),
                (writePath, ReportedFileOperation.KAuthVNodeProbe), // stat
                (writePath, ReportedFileOperation.KAuthReadFile), // Open
                (writePath, ReportedFileOperation.KAuthVNodeWrite), // Write to opened fd
                (rmdirPath, ReportedFileOperation.KAuthDeleteDir),
                (renamedDirectoryOld, ReportedFileOperation.KAuthDeleteDir),
                (renamedDirectoryNew, ReportedFileOperation.MacVNodeCreate),
                (renamePathOld, ReportedFileOperation.KAuthDeleteFile),
                (renamePathNew, ReportedFileOperation.MacVNodeCreate),
                (staticProcessArtifact.Path.ToString(Context.PathTable), ReportedFileOperation.Process),
                (staticProcessArtifact.Path.ToString(Context.PathTable), ReportedFileOperation.ProcessExit),
            };

            var intersection = result.FileAccesses.ToList()
                .Select(i => (i.GetPath(Context.PathTable), i.Operation))
                .Intersect(expectedAccesses)
                .ToList();

            var forksAndExits = result.FileAccesses.ToList()
                .Where(i => (i.Operation == ReportedFileOperation.Process || i.Operation == ReportedFileOperation.ProcessExit) && i.GetPath(Context.PathTable) == staticProcessArtifact.Path.ToString(Context.PathTable))
                .Select(i => $"{i.Operation}: '{i.GetPath(Context.PathTable)}'")
                .ToList();

            // 5 fork and 5 exit calls are expected here
            // 1 fork + 1 exit for the main process
            // 4 fork calls inside the statically linked process, and 4 matching exits
            var expectedForkAndExitCount = 10;

            XAssert.IsTrue(forksAndExits.Count() == expectedForkAndExitCount, $"Mismatch in the number of process creations and exits. Expected {expectedForkAndExitCount}, got {forksAndExits.Count()}. Process creations and exits:\n{string.Join("\n", forksAndExits)}");
            XAssert.IsTrue(intersection.Count == expectedAccesses.Count, $"Ptrace sandbox did not report the following accesses: {string.Join("\n", expectedAccesses.Except(intersection).ToList())}");
        }

        [Fact]
        public async Task SandboxTeardownOnUnobservedRootProcess()
        {
            PrepareStaticallyLinkedProcess(
               out FileArtifact staticProcessArtifact,
               out _,
               out _,
               out _,
               out _,
               out _,
               out _,
               out _,
               out DirectoryArtifact workingDirectory);

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.FailUnexpectedFileAccesses = false;
            fam.ReportUnexpectedFileAccesses = true;
            // We explicitly turn off ptrace, so the statically linked process we are about to run won't be observed
            fam.EnableLinuxPTraceSandbox = false;

            // Create a pip whose root process is the statically linked one
            var processBuilder = ProcessBuilder.CreateForTesting(Context.PathTable, FrontEndContext.CredentialScanner, LoggingContext);
            processBuilder.Executable = staticProcessArtifact;
            processBuilder.ArgumentsBuilder.Add("0");
            processBuilder.AddInputFile(staticProcessArtifact);
            AddUntrackedWindowsDirectories(processBuilder);
            var ok = processBuilder.TryFinish(PipConstructionHelper, out var process, out _);
            XAssert.IsTrue(ok, "Could not finish creating process builder");

            var staticProcessInfo = ToProcessInfo(
                process,
                workingDirectory: workingDirectory.Path.ToString(Context.PathTable),
                fileAccessManifest: fam
            );

            var result = await RunProcess(staticProcessInfo);

            // The pip should finish successfully (that is, the sandbox is terminating), and we should have noted that
            // the root process start event is missing
            XAssert.AreEqual(0, result.ExitCode);
            AssertLogContains(caseSensitive: true, "We missed the process start event for root process");
        }

        [Fact]
        public async Task MakeDirReturnValueIsReported()
        {
            var existentFile = CreateSourceFileWithPrefix(prefix: "dir");

            var dummyFile = CreateSourceFileWithPrefix(prefix: "dummy");

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.FailUnexpectedFileAccesses = false;
            fam.ReportUnexpectedFileAccesses = true;

            // Turn on the ptrace sandbox unconditionally
            fam.UnconditionallyEnableLinuxPTraceSandbox = true;

            // Create a directory but make sure we perform it on an existing file
            var info = ToProcessInfo(
                ToProcess(
                    // We need to spawn a process because the sandbox assumes the root process is never static and the interposing 
                    // sandbox is always used for it.
                    Operation.Spawn(
                        Context.PathTable, 
                        waitToFinish: true, 
                        Operation.WriteFile(dummyFile),
                        // This tries to create a directory on an existent file, and therefore it should fail
                        Operation.CreateDir(existentFile)
                    )
                ),
                fileAccessManifest: fam);

            var result = await RunProcess(info);
           
            // We should get a report where we see the creation attempt that results in a file exists error
            result.AllUnexpectedFileAccesses.Single(fa =>
                fa.Operation == ReportedFileOperation.KAuthCreateDir &&
                fa.Error != 0);

            // Now perform the same operation but in a way it should succeed
            info = ToProcessInfo(
                ToProcess(
                    // This is a unique file, and therefore the create dir should succeed
                    Operation.CreateDir(FileArtifact.CreateSourceFile(CreateUniqueSourcePath()))
                ),
                fileAccessManifest: fam);

            result = await RunProcess(info);

            // We should get a report where we see the creation attempt with a successful error code
            result.AllUnexpectedFileAccesses.Single(fa =>
                fa.Operation == ReportedFileOperation.KAuthCreateDir &&
                fa.Error == 0);

            AssertVerboseEventLogged(ProcessesLogEventId.PTraceSandboxLaunchedForPip, 2);
        }


        [Fact]
        public async Task FailureInPTraceRunnerKillsThePip()
        {
            var dummyFile = CreateSourceFileWithPrefix(prefix: "dummy");
            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.EnableLinuxPTraceSandbox = true;

            // Turn on the ptrace sandbox unconditionally so the test process triggers a PTraceRunner
            fam.UnconditionallyEnableLinuxPTraceSandbox = true;

            // Create a directory but make sure we perform it on an existing file
            var info = ToProcessInfo(
                ToProcess(
                    // We need to spawn a process because the sandbox assumes the root process is never static and the interposing 
                    // sandbox is always used for it.
                    Operation.Spawn(
                        Context.PathTable, 
                        waitToFinish: true,
                        Operation.Block(),  // We mean to kill this pip, if this test times out, it's failing
                        Operation.WriteFile(dummyFile)
                    )
                ),
                fileAccessManifest: fam) ;

#if NETCOREAPP
            var environmentDictionary = new Dictionary<string, string>(info.EnvironmentVariables.ToDictionary());
#else
            var environmentDictionary = new Dictionary<string, string>(info.EnvironmentVariables.ToDictionary().ToDictionary(x => x.Key, x => x.Value));
#endif
            info.EnvironmentVariables = BuildParameters.GetFactory().PopulateFromDictionary(environmentDictionary);

            try 
            {
                // There's no easy way of passing environment to the PTraceRunner, so just do
                // this globally with a defensive try-finally to unset. 
                Environment.SetEnvironmentVariable("__BUILDXL_TEST_PTRACERUNNER_FAILME", "1");
                var result = await RunProcess(info);
                
                // When PTrace is forced, the process is being reported as statically linked 
                AssertVerboseEventLogged(ProcessesLogEventId.PTraceSandboxLaunchedForPip);

                // The runner logs an error before failing
                AssertErrorEventLogged(ProcessesLogEventId.PTraceRunnerError);

                // When PTraceRunner fails, we kill the pip
                XAssert.IsTrue(result.Killed);
                XAssert.AreNotEqual(0, result.ExitCode);
            }
            finally 
            {
                Environment.SetEnvironmentVariable("__BUILDXL_TEST_PTRACERUNNER_FAILME", null);
            }

        }

        [Fact]
        public async Task PTraceForcedProcessesAreTreatedAsStaticallyLinked()
        {
            // Test that the PTrace forcing via environment variable is working
            // This feature is currently meant for debugging purposes only, here we'd like to
            // make sure that the process name is being propagated correctly to the sandbox.
            var dummyFile = CreateSourceFileWithPrefix(prefix: "dummy");

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.EnableLinuxPTraceSandbox = true;

            // Create a directory but make sure we perform it on an existing file
            var info = ToProcessInfo(
                ToProcess(
                    // We need to spawn a process because the sandbox assumes the root process is never static and the interposing 
                    // sandbox is always used for it.
                    Operation.Spawn(
                        Context.PathTable, waitToFinish: true, 
                        Operation.WriteFile(dummyFile)
                    )
                ),
                fileAccessManifest: fam);

            // Force PTrace with the appropriate environment variable
#if NETCOREAPP
            var environmentDictionary = new Dictionary<string, string>(info.EnvironmentVariables.ToDictionary());
#else
            var environmentDictionary = new Dictionary<string, string>(info.EnvironmentVariables.ToDictionary().ToDictionary(x => x.Key, x => x.Value));
#endif
            environmentDictionary["__BUILDXL_PTRACE_FORCED_PROCESSES"] = $"{TestProcessToolName};SomeOtherToolName";
            info.EnvironmentVariables = BuildParameters.GetFactory().PopulateFromDictionary(environmentDictionary);

            var result = await RunProcess(info);

            // When PTrace is forced, the process is being reported as statically linked 
            AssertVerboseEventLogged(ProcessesLogEventId.PTraceSandboxLaunchedForPip);
        }

        [Fact]
        public async Task RemoveDirReturnValueIsReported()
        {
            // Create the artifact but leave the path as absent
            var directory = FileArtifact.CreateSourceFile(CreateUniqueSourcePath("dir"));

            var dummyFile = CreateSourceFileWithPrefix(prefix: "dummy");

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.FailUnexpectedFileAccesses = false;
            fam.ReportUnexpectedFileAccesses = true;

            // Turn on the ptrace sandbox unconditionally
            fam.UnconditionallyEnableLinuxPTraceSandbox = true;

            // Create a directory but make sure we perform it on an existing file
            var info = ToProcessInfo(
                ToProcess(
                    // We need to spawn a process because the sandbox assumes the root process is never static and the interposing 
                    // sandbox is always used for it.
                    Operation.Spawn(
                        Context.PathTable, waitToFinish: true, 
                        Operation.WriteFile(dummyFile),
                        // This tries to delete a directory on an absent path, and therefore it should fail
                        Operation.DeleteDir(directory)
                    )
                ),
                fileAccessManifest: fam);

            var result = await RunProcess(info);
           
            // We should get a report where we see the deletion attempt that results in an absent dir error
            result.FileAccesses.Single(fa =>
                fa.Operation == ReportedFileOperation.KAuthDeleteDir &&
                fa.Error != 0);

            // Now perform the same operation but in a way it should succeed
            FileUtilities.CreateDirectory(directory.Path.ToString(Context.PathTable));

            info = ToProcessInfo(
                ToProcess(
                    Operation.DeleteDir(directory)
                ),
                fileAccessManifest: fam);

            result = await RunProcess(info);

            // We should get a report where we see the deletion attempt with a successful error code
            result.FileAccesses.Single(fa =>
                fa.Operation == ReportedFileOperation.KAuthDeleteDir &&
                fa.Error == 0);

            AssertVerboseEventLogged(ProcessesLogEventId.PTraceSandboxLaunchedForPip, 2);
        }

        [FactIfSupported(requiresAdmin: true)]
        public async Task ProcessWithCapabilitiesIsDetected()
        {
            var dummyFile = CreateSourceFileWithPrefix(prefix: "dummy");

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.FailUnexpectedFileAccesses = false;
            fam.ReportUnexpectedFileAccesses = true;
            fam.EnableLinuxPTraceSandbox = true;

            // Set an arbitrary capability to the test process executable, so we can validate ptrace
            // is launched when capabilities are detected and accesses are retrieved
            // This operation requires sudo, and 'requiresAdmin' in the test attribute makes sure this is
            // possible
            // This operation is done during the test execution and not at deployment time (on DScript)
            // because regular copy operations do not preserve capabilities (xattrs need to be preserved). In
            // addition to that, we should only set a binary capability when sudo can be made safely
            FileArtifact testProcessExecutableWithCapabilities = CreateTestProcessWithCapabilities();

            var info = ToProcessInfo(
                ToProcess(
                    testProcessExecutableWithCapabilities,
                    Operation.Spawn(
                        Context.PathTable, waitToFinish: true,
                        Operation.WriteFile(dummyFile)
                    )
                ),
                fileAccessManifest: fam);

            var result = await RunProcess(info);

            // We should get a report where we see the write attempt
            result.FileAccesses.Single(fa =>
                fa.Operation == ReportedFileOperation.KAuthVNodeWrite &&
                fa.GetPath(Context.PathTable).Equals(dummyFile.Path.ToString(Context.PathTable), StringComparison.Ordinal));

            AssertVerboseEventLogged(ProcessesLogEventId.PTraceSandboxLaunchedForPip, 1);
        }

        private void PrepareStaticallyLinkedProcess(out FileArtifact staticProcessArtifact, out string unlinkedPath, out string writePath, out string rmdirPath, out string renamedDirectoryOld, out string renamedDirectoryNew, out string renamePathOld, out string renamePathNew, out DirectoryArtifact workingDirectory)
        {
            var staticProcessName = "TestProcessStaticallyLinked";
            var staticProcessPath = Path.Combine(TestBinRoot, "LinuxTestProcesses", staticProcessName);

            workingDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            var workingDirectoryStr = workingDirectory.Path.ToString(Context.PathTable);
            staticProcessArtifact = CreateFileArtifactWithName(staticProcessName, workingDirectory.Path.ToString(Context.PathTable));

            // Copy test executables to new working directory
            File.Copy(staticProcessPath, Path.Combine(workingDirectoryStr, staticProcessName));

            unlinkedPath = Path.Combine(workingDirectoryStr, "unlinkme");
            File.WriteAllText(unlinkedPath, "This file should be deleted by the static process.");

            writePath = Path.Combine(workingDirectoryStr, "writeme");
            File.WriteAllText(writePath, "Write to this file");

            rmdirPath = Path.Combine(workingDirectoryStr, "rmdirme");
            Directory.CreateDirectory(rmdirPath);

            renamedDirectoryOld = Path.Combine(workingDirectoryStr, "renameme");
            renamedDirectoryNew = Path.Combine(workingDirectoryStr, "renamed");
            renamePathOld = Path.Combine(renamedDirectoryOld, "insiderenameddir");
            renamePathNew = Path.Combine(renamedDirectoryNew, "insiderenameddir");
            Directory.CreateDirectory(renamedDirectoryOld);
            File.WriteAllText(renamePathOld, "This file should be deleted then recreated");
        }
    }
}
