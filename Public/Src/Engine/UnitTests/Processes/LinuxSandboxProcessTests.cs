// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Runs native tests for the Linux Sandbox.
    /// </summary>
    /// <remarks>
    /// To add a new test here, first add a function to 'Public/Src/Sandbox/Linux/UnitTests/TestProcesses/TestProcess/main.cpp' with the test to run.
    /// Ensure that the test name used is the same as the function name of the test that is being on the native process.
    /// </remarks>
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public sealed class LinuxSandboxProcessTests : SandboxedProcessTestBase
    {
        private ITestOutputHelper TestOutput { get; }
        private string TestProcessExe => Path.Combine(TestBinRoot, "LinuxTestProcesses", "LinuxTestProcess");

        public LinuxSandboxProcessTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.ProcessPipExecutor.ETWLogger.Log);
            TestOutput = output;
        }

        [Fact]
        public void CallAnonymousFileTest()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var process = CreateTestProcess(
                    Context.PathTable,
                    tempFiles,
                    "AnonymousFileTest",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                var processInfo = ToProcessInfo(process);
                processInfo.FileAccessManifest.ReportFileAccesses = true;
                processInfo.FileAccessManifest.MonitorChildProcesses = true;
                processInfo.FileAccessManifest.FailUnexpectedFileAccesses = false;

                var result = RunProcess(processInfo).Result;

                XAssert.AreEqual(result.ExitCode, 0, $"{Environment.NewLine}Standard Output: '{result.StandardOutput.ReadValueAsync().Result}'{Environment.NewLine}Standard Error: '{result.StandardError.ReadValueAsync().Result}'");

                XAssert.IsFalse(
                    result.FileAccesses.Select(f => f.ManifestPath.ToString(Context.PathTable)).Contains("/memfd:testFile (deleted)"),
                    $"Anonymous file reported by sandbox, file accesses:{Environment.NewLine}{string.Join(Environment.NewLine, result.FileAccesses.Select(f => f.ManifestPath.ToString(Context.PathTable)).ToArray())}");
            }
        }

        private Process CreateTestProcess(
            PathTable pathTable,
            TempFileStorage tempFileStorage,
            string testName,
            ReadOnlyArray<FileArtifact> inputFiles,
            ReadOnlyArray<DirectoryArtifact> inputDirectories,
            ReadOnlyArray<FileArtifactWithAttributes> outputFiles,
            ReadOnlyArray<DirectoryArtifact> outputDirectories,
            ReadOnlyArray<AbsolutePath> untrackedScopes)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(tempFileStorage != null);
            Contract.Requires(testName != null);
            Contract.Requires(Contract.ForAll(inputFiles, artifact => artifact.IsValid));
            Contract.Requires(Contract.ForAll(inputDirectories, artifact => artifact.IsValid));
            Contract.Requires(Contract.ForAll(outputFiles, artifact => artifact.IsValid));
            Contract.Requires(Contract.ForAll(outputDirectories, artifact => artifact.IsValid));

            XAssert.IsTrue(File.Exists(TestProcessExe));

            FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, TestProcessExe));
            var untrackedList = new List<AbsolutePath>(CmdHelper.GetCmdDependencies(pathTable));
            var allUntrackedScopes = new List<AbsolutePath>(untrackedScopes);
            allUntrackedScopes.AddRange(CmdHelper.GetCmdDependencyScopes(pathTable));

            var inputFilesWithExecutable = new List<FileArtifact>(inputFiles) { executableFileArtifact };

            var arguments = new PipDataBuilder(pathTable.StringTable);
            arguments.Add("-t");
            arguments.Add(testName);

            return new Process(
                executableFileArtifact,
                AbsolutePath.Create(pathTable, tempFileStorage.RootDirectory),
                arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                FileArtifact.Invalid,
                PipData.Invalid,
                ReadOnlyArray<EnvironmentVariable>.Empty,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                tempFileStorage.GetUniqueDirectory(pathTable),
                null,
                null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(inputFilesWithExecutable.ToArray()),
                outputs: outputFiles,
                directoryDependencies: inputDirectories,
                directoryOutputs: outputDirectories,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedList),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(allUntrackedScopes),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(0),
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(Context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                options: default);
        }
    }
}
