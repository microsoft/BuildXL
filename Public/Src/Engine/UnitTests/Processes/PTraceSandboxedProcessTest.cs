// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using FileUtilities = BuildXL.Native.IO.FileUtilities;
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
            var staticProcessName = "TestProcessStaticallyLinked";
            var staticProcessPath = Path.Combine(TestBinRoot, "LinuxTestProcesses", staticProcessName);

            var workingDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            var workingDirectoryStr = workingDirectory.Path.ToString(Context.PathTable);
            var staticProcessArtifact = CreateFileArtifactWithName(staticProcessName, workingDirectory.Path.ToString(Context.PathTable));

            // Copy test executables to new working directory
            File.Copy(staticProcessPath, Path.Combine(workingDirectoryStr, staticProcessName));

            var unlinkedPath = Path.Combine(workingDirectoryStr, "unlinkme");
            File.WriteAllText(unlinkedPath, "This file should be deleted by the static process.");

            var writePath = Path.Combine(workingDirectoryStr, "writeme");
            File.WriteAllText(writePath, "Write to this file");

            var rmdirPath = Path.Combine(workingDirectoryStr, "rmdirme");
            Directory.CreateDirectory(rmdirPath);

            var renamedDirectoryOld = Path.Combine(workingDirectoryStr, "renameme");
            var renamedDirectoryNew = Path.Combine(workingDirectoryStr, "renamed");
            var renamePathOld = Path.Combine(renamedDirectoryOld, "insiderenameddir");
            var renamePathNew = Path.Combine(renamedDirectoryNew, "insiderenameddir");
            Directory.CreateDirectory(renamedDirectoryOld);
            File.WriteAllText(renamePathOld, "This file should be deleted then recreated");

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

            AllowWarningEventMaybeLogged(ProcessesLogEventId.LinuxSandboxReportedStaticallyLinkedBinary);

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
            var expectedForkAndExitCount = 10;

            // We should get 8 here because we call fork 4 times (8 create/exit), and the main process will have one create and exit
            // Right now this number is 10 because the bxl observer layer will report the ptracerunner process as a process creation as well because it does not expect the tracing process to be external
            // This will require more changes to the sandbox in upcoming changes to ensure that the sandbox only reports fork/clone/exit.
            // TODO [pgunasekara]: Update the expected fork/exit count back to 8
            XAssert.IsTrue(forksAndExits.Count() == expectedForkAndExitCount, $"Mismatch in the number of process creations and exits. Expected {expectedForkAndExitCount}, got {forksAndExits.Count()}. Process creations and exits:\n{string.Join("\n", forksAndExits)}");

            XAssert.IsTrue(intersection.Count == expectedAccesses.Count, $"Ptrace sandbox did not report the following accesses: {string.Join("\n", expectedAccesses.Except(intersection).ToList())}");
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
                        Context.PathTable, waitToFinish: true, 
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

            AssertWarningEventLogged(ProcessesLogEventId.LinuxSandboxReportedStaticallyLinkedBinary, 2);
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
            AssertWarningEventLogged(ProcessesLogEventId.LinuxSandboxReportedStaticallyLinkedBinary);
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

            AssertWarningEventLogged(ProcessesLogEventId.LinuxSandboxReportedStaticallyLinkedBinary, 2);
        }
    }
}
