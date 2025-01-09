// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Native.IO;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using UnixPaths = BuildXL.Interop.Unix.UnixPaths;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace Test.BuildXL.Processes
{
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public class InterposeSandboxProcessTest : SandboxedProcessTestBase
    {
        private ITestOutputHelper TestOutput { get; }
        
        public InterposeSandboxProcessTest(ITestOutputHelper output) : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            TestOutput = output;
        }

        [Fact]
        public void CallBoostInterposeTests()
        {
            RunTest("interpose");
        }

        [Fact]
        public void CallBoostRealpathTests()
        {
            var fileStorage = new TempFileStorage(canGetFileNames: true);

            // .
            // {root}
            //   `-- symlink1 [->real1]
            //   `-- real1
            //         `-- symlink2 [->real2]
            //   `-- real2
            //         `-- file.txt
            //         `-- symlink4.txt [-> real3.txt]
            //   `-- real3.txt
            //   `-- symlink3 [->real2]
            var root = fileStorage.RootDirectory;
            var realPath1 = $"{root}/real1";
            var realPath2 = $"{root}/real2";
            var realPath3 = $"{root}/real3.txt";
            var symlinkPath1 = $"{root}/symlink1";
            var symlinkPath2 = $"{realPath1}/symlink2";
            var symlinkPath3 = $"{root}/symlink3";
            var symlinkPath4 = $"{realPath2}/symlink4.txt";
            var filePath = $"{realPath2}/file.txt";

            Directory.CreateDirectory(realPath1);
            Directory.CreateDirectory(realPath2);
            File.WriteAllText(realPath3, "contents");
            File.WriteAllText(filePath, "contents");
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(symlinkPath1, realPath1, isTargetFile: false).Succeeded);
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(symlinkPath2, realPath2, isTargetFile: false).Succeeded);
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(symlinkPath3, realPath2, isTargetFile: false).Succeeded);
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(symlinkPath4, realPath3, isTargetFile: true).Succeeded);

            var result = RunTest("realpath", fileStorage);
            
            // The process calls:
            //      realpath("symlink1/symlink2/file.txt")
            //      realpath("symlink3/nonexistenfile.txt")
            //      realpath("real2/symlink4.txt")
            // We should get accesses on the paths of the 4 symlinks
            XAssert.IsNotNull(result.ExplicitlyReportedFileAccesses);
            XAssert.IsNotNull(result.AllUnexpectedFileAccesses);
            var accesses = result.ExplicitlyReportedFileAccesses!.Union(result.AllUnexpectedFileAccesses!).Select(a => (Path: a.GetPath(Context.PathTable), Access: a.DesiredAccess, IsNonexistent: a.IsNonexistent));
            
            foreach (var symlink in new [] { symlinkPath1, symlinkPath2, symlinkPath3 })
            {
                var symlinkAccesses = accesses.Where(a => a.Path.Equals(symlink));
                XAssert.AreEqual(1, symlinkAccesses.Count());
                XAssert.AreEqual(DesiredAccess.GENERIC_READ, symlinkAccesses.Single().Access);
            }

            // For symlink4, we should get a probe on the full path (because realpath implies a probe), plus a readlink on the symlink
            var symlink4Accesses = accesses.Where(a => a.Path.Equals(symlinkPath4)).ToList();
            XAssert.AreEqual(2, symlink4Accesses.Count);
            XAssert.AreEqual(DesiredAccess.GENERIC_READ, symlink4Accesses[0].Access);
            XAssert.AreEqual(DesiredAccess.GENERIC_READ, symlink4Accesses[1].Access);

            // We get probes on the queried and returned paths
            XAssert.AreEqual(1, accesses.Where(a => a.Path == filePath).Count());
            XAssert.AreEqual(1, accesses.Where(a => a.Path.Contains("nonexistentfile.txt")).Count());
            XAssert.AreEqual(1, accesses.Where(a => a.Path == realPath3).Count());
        }

        [Fact]
        public void CallBoostReadlinkAbsentPathTests()
        {
            var fileStorage = new TempFileStorage(canGetFileNames: true);
            var absentFilePath = Path.Combine(fileStorage.RootDirectory, "absentFile.o");

            // This test calls readlink(absentFilePath)
            var result = RunTest("readlink_absent_path", fileStorage);
            XAssert.IsNotNull(result.FileAccesses);

            // There should be only one absent file access
            // NOTE: on some Linux distributions, /proc/<pid>/stat is absent when the test process runs so it shows up as non-existent. We can ignore these.
            var absentPathAccesses = result.FileAccesses!.Where(a => a.IsNonexistent && !a.ManifestPath.ToString(Context.PathTable).StartsWith(UnixPaths.Proc));
            XAssert.AreEqual(1, absentPathAccesses.Count(), $"Expected 1 absent path access, but got {absentPathAccesses.Count()} with paths: {Environment.NewLine}{string.Join(Environment.NewLine, absentPathAccesses.Select(a => a.ManifestPath.ToString(Context.PathTable)))}");

            // readlink on absent path is reported backed as probe
            var absentFileAccess = absentPathAccesses.Single();
            XAssert.AreEqual(absentFilePath, absentFileAccess.GetPath(Context.PathTable));
            XAssert.AreEqual(RequestedAccess.Probe, absentFileAccess.RequestedAccess);
        }

        [Fact]
        public void CallBoostObserverUtilitiesTests()
        {
            RunTest("observer_utilities_test");
        }

        private SandboxedProcessResult RunTest(string testExeName, TempFileStorage? workingDirectoryStorage = null)
        {
            var testExecutable = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, Path.Combine(TestBinRoot, "LinuxTestProcesses", testExeName)));
            XAssert.IsTrue(File.Exists(testExecutable.Path.ToString(Context.PathTable)), $"Test executable '{testExecutable.Path.ToString(Context.PathTable)}' not found.");

            workingDirectoryStorage ??= new TempFileStorage(canGetFileNames: true);
            using (workingDirectoryStorage)
            {
                var workingDirectoryAbsolutePath = AbsolutePath.Create(Context.PathTable, workingDirectoryStorage.RootDirectory);
                var arguments = new PipDataBuilder(Context.PathTable.StringTable);
                var allDependencies = new List<FileArtifact> { testExecutable };
                var allOutputs = new List<FileArtifactWithAttributes>(1);

                var dummyOutputArtifact = FileArtifact.CreateSourceFile(workingDirectoryStorage.GetFileName(Context.PathTable, "dummy"));
                allOutputs.Add(dummyOutputArtifact.CreateNextWrittenVersion().WithAttributes(FileExistence.Optional));

                var pip = new Process(
                    testExecutable,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.Empty,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    workingDirectoryStorage.GetUniqueDirectory(Context.PathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(allOutputs.ToArray()),
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(0),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(Context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty
                    );

                var processInfo = ToProcessInfo(pip, workingDirectory: workingDirectoryStorage.RootDirectory);
                processInfo.FileAccessManifest.ReportFileAccesses = true;
                processInfo.FileAccessManifest.MonitorChildProcesses = true;
                processInfo.FileAccessManifest.FailUnexpectedFileAccesses = false;

                var result = RunProcess(processInfo).Result;

                XAssert.AreEqual(result.ExitCode, 0);

                return result;
            }
        }
    }
}
