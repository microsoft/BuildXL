// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
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
    public class LinuxSandboxProcessTestsBase : SandboxedProcessTestBase
    {
        protected ITestOutputHelper TestOutput { get; }
        protected string TestProcessExe => Path.Combine(TestBinRoot, "LinuxTestProcesses", "LinuxTestProcess");

        public LinuxSandboxProcessTestsBase(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            TestOutput = output;
        }

        /// <summary>
        /// Some stat tests don't run depending on the glibc version on the machine
        /// </summary>
        protected bool SkipStatTests()
        {
            string originalLog = EventListener.GetLog();

            // Stat tests will call open first to create a file to stat, if this doesn't exist then stat won't be called
            return !originalLog.Contains("CreateFileOpen");
        }

        protected string GetNativeTestName(string functionName)
        {
            return functionName.Replace("Call", "");
        }

        protected string GetSyscallName(string functionName)
        {
            return functionName.Replace("CallTest", "");
        }

        protected (SandboxedProcessResult result, string rootDirectory) RunNativeTest(
            string testName,
            TempFileStorage workingDirectory = null,
            bool unconditionallyEnableLinuxPTraceSandbox = false,
            bool reportProcessArgs = false)
        {
            workingDirectory ??= new TempFileStorage(canGetFileNames: true);
            using (workingDirectory)
            {
                var process = CreateTestProcess(
                    Context.PathTable,
                    workingDirectory,
                    testName,
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                var processInfo = ToProcessInfo(process, workingDirectory: workingDirectory.RootDirectory);
                processInfo.FileAccessManifest.ReportFileAccesses = true;
                processInfo.FileAccessManifest.MonitorChildProcesses = true;
                processInfo.FileAccessManifest.FailUnexpectedFileAccesses = false;
                processInfo.FileAccessManifest.EnableLinuxSandboxLogging = true;
                processInfo.FileAccessManifest.UnconditionallyEnableLinuxPTraceSandbox = unconditionallyEnableLinuxPTraceSandbox;
                processInfo.FileAccessManifest.ReportProcessArgs = reportProcessArgs;

                var result = RunProcess(processInfo).Result;

                string message = $"Test terminated with exit code {result.ExitCode}.{Environment.NewLine}stdout: {result.StandardOutput.ReadValueAsync().Result}{Environment.NewLine}stderr: {result.StandardError.ReadValueAsync().Result}";
                XAssert.IsTrue(result.ExitCode == 0, message);

                return (result, workingDirectory.RootDirectory);
            }
        }

        protected Process CreateTestProcess(
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

        protected void TestForFileOperation((SandboxedProcessResult result, string rootDirectory) result, ReportedFileOperation op, string path, int count = 1)
        {
            var matches = result.result.FileAccesses.Where(access => access.Operation == op);
            var assertString = $"{op}";
            if (!string.IsNullOrEmpty(path))
            {
                matches = matches.Where(access => access.ManifestPath.ToString(Context.PathTable) == path);
                assertString += $":{path}";
            }

            XAssert.IsTrue(matches.ToList().Count == count,
                $"Did not find expected count ({count}) of file access '{assertString}'{Environment.NewLine}Reported Accesses:{Environment.NewLine}{string.Join(Environment.NewLine, result.result.FileAccesses.Select(fa => $"{fa.Operation}:{fa.ManifestPath.ToString(Context.PathTable)}").ToList())}");
        }

        protected Regex GetRegex(string fileOperation, string path) => new Regex($@".*\(\( *{fileOperation}: *[0-9]* *\)\).*{Regex.Escape(path)}.*", RegexOptions.IgnoreCase);
    }
}
