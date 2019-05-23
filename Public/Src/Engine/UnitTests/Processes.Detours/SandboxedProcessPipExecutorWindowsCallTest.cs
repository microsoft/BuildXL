// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Processes.Detours
{
    public sealed partial class SandboxedProcessPipExecutorTest : TemporaryStorageTestBase
    {
#if TEST_PLATFORM_X86
        private const string DetourTestFolder = "x86"; 
#else
        private const string DetourTestFolder = "x64";
#endif

        public SandboxedProcessPipExecutorTest(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        #region Test Helper Methods

        private async Task ProcessWindowsCallHelper(
            string functionName,
            SandboxConfiguration config = null,
            IEnumerable<string> extraDependencies = null,
            IEnumerable<string> extraOutputs = null,
            int callCount = 1,
            string commandPrefix = "",
            bool readsAndWritesDirectories = false,
            bool untrackedOutputs = false,
            string[] expectedWarningStrings = null,
            string[] expectedErrorStrings = null)
        {
            if (config == null)
            {
                config = new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = true };
            }

            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;
            var fileContentTable = FileContentTable.CreateNew();

            // have to force the config for truncation
            config.OutputReportingMode = OutputReportingMode.FullOutputOnWarningOrError;

            bool expectSuccess = expectedErrorStrings == null && expectedWarningStrings == null;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                string workingDirectory = tempFiles.GetUniqueDirectory();
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                XAssert.IsTrue(File.Exists(executable), "Could not find the test file: " + executable);
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                var extraUntrackedScopes = new List<AbsolutePath>();

                var dependencies = new List<FileArtifact> { executableFileArtifact };
                if (extraDependencies != null)
                {
                    foreach (string file in extraDependencies)
                    {
                        string filePath = Path.Combine(workingDirectory, file);
                        AbsolutePath path = AbsolutePath.Create(pathTable, filePath);

                        if (readsAndWritesDirectories)
                        {
                            Directory.CreateDirectory(filePath);

                            // We don't support directories as inputs in BuildXL yet.
                            extraUntrackedScopes.Add(path);
                        }
                        else
                        {
                            File.WriteAllText(filePath, "Definitely a file");
                            FileArtifact fileArtifact = FileArtifact.CreateSourceFile(path);
                            dependencies.Add(fileArtifact);
                        }
                    }
                }

                var outputs = new List<FileArtifactWithAttributes>();
                if (extraOutputs != null)
                {
                    foreach (string file in extraOutputs)
                    {
                        string filePath = Path.Combine(workingDirectory, file);
                        AbsolutePath path = AbsolutePath.Create(pathTable, filePath);

                        if (readsAndWritesDirectories)
                        {
                            // We don't support directory outputs in BuildXL at the moment, so e.g. deleting a directory needs to be untracked.
                            extraUntrackedScopes.Add(path);
                        }
                        else if (untrackedOutputs)
                        {
                            extraUntrackedScopes.Add(path);
                        }
                        else
                        {
                            FileArtifact fileArtifact = FileArtifact.CreateSourceFile(path).CreateNextWrittenVersion();
                            outputs.Add(fileArtifact.WithAttributes());
                        }
                    }
                }

                var tempDirectory = tempFiles.GetUniqueDirectory();
                var environmentVariables = new List<EnvironmentVariable>();
                var environmentValue = new PipDataBuilder(pathTable.StringTable);
                var tempPath = AbsolutePath.Create(pathTable, tempDirectory);
                environmentValue.Add(tempPath);
                environmentVariables.Add(new EnvironmentVariable(StringId.Create(pathTable.StringTable, "TMP"), environmentValue.ToPipData(" ", PipDataFragmentEscaping.NoEscaping)));
                environmentVariables.Add(new EnvironmentVariable(StringId.Create(pathTable.StringTable, "TEMP"), environmentValue.ToPipData(" ", PipDataFragmentEscaping.NoEscaping)));

                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = extraUntrackedScopes.Concat(CmdHelper.GetCmdDependencyScopes(pathTable).Concat(new[] { tempPath })).Distinct();

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.NoEscaping, commandPrefix + functionName + "Logging"),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.From(dependencies),
                    ReadOnlyArray<FileArtifactWithAttributes>.From(outputs),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                if (expectSuccess)
                {
                    await AssertProcessSucceedsAsync(
                        context,
                        config,
                        pip);
                }
                else
                {
                    await AssertProcessCompletesWithStatus(
                        SandboxedProcessPipExecutionStatus.ExecutionFailed,
                        context,
                        config,
                        pip,
                        null);
                }
            }

            int expectedErrorCount = 0;
            int expectedWarningCount = 0;
            IEnumerable<string> requiredLogMessageSubstrings = new string[] { };

            if (expectedErrorStrings != null)
            {
                expectedErrorCount = expectedErrorStrings.Count();
                requiredLogMessageSubstrings = requiredLogMessageSubstrings.Concat(expectedErrorStrings);
            }

            if (expectedWarningStrings != null)
            {
                expectedWarningCount = expectedWarningStrings.Count();
                requiredLogMessageSubstrings = requiredLogMessageSubstrings.Concat(expectedWarningStrings);
            }

            SetExpectedFailures(expectedErrorCount, expectedWarningCount, requiredLogMessageSubstrings.ToArray());
        }

        #endregion

        #region Calls Logged

        [Fact]
        public Task ProcessWindowsCreateProcessW()
        {
            return ProcessWindowsCallHelper("CreateProcessW");
        }

        [Fact]
        public Task ProcessWindowsCreateProcessA()
        {
            return ProcessWindowsCallHelper("CreateProcessA");
        }

        [Fact]
        public Task ProcessWindowsCreateFileW()
        {
            string[] outputs = { "CreateFileWLoggingTest.txt" };
            return ProcessWindowsCallHelper("CreateFileW", extraOutputs: outputs);
        }

        [Fact]
        public Task ProcessWindowsCreateFileA()
        {
            string[] outputs = { "CreateFileALoggingTest.txt" };
            return ProcessWindowsCallHelper("CreateFileA", extraOutputs: outputs);
        }

        [Fact]
        public Task ProcessWindowsGetVolumePathNameW()
        {
            return ProcessWindowsCallHelper("GetVolumePathNameW");
        }

        [Fact]
        public Task ProcessWindowsGetFileAttributesA()
        {
            return ProcessWindowsCallHelper("GetFileAttributesA");
        }

        [Fact]
        public Task ProcessWindowsGetFileAttributesW()
        {
            return ProcessWindowsCallHelper("GetFileAttributesW");
        }

        [Fact]
        public Task ProcessWindowsGetFileAttributesExW()
        {
            return ProcessWindowsCallHelper("GetFileAttributesExW");
        }

        [Fact]
        public Task ProcessWindowsGetFileAttributesExA()
        {
            return ProcessWindowsCallHelper("GetFileAttributesExA");
        }

        [Fact]
        public Task ProcessWindowsCopyFileW()
        {
            string[] outputs = { "CopyFileWLoggingTest2.txt" };
            string[] inputs = { "CopyFileWLoggingTest1.txt" };
            return ProcessWindowsCallHelper("CopyFileW", extraDependencies: inputs, extraOutputs: outputs);
        }

        [Fact]
        public Task ProcessWindowsCopyFileA()
        {
            string[] outputs = { "CopyFileALoggingTest2.txt" };
            string[] inputs = { "CopyFileALoggingTest1.txt" };
            return ProcessWindowsCallHelper("CopyFileA", extraDependencies: inputs, extraOutputs: outputs);
        }

        [Fact]
        public Task ProcessWindowsCopyFileExW()
        {
            string[] outputs = { "CopyFileExWLoggingTest2.txt" };
            string[] inputs = { "CopyFileExWLoggingTest1.txt" };
            return ProcessWindowsCallHelper("CopyFileExW", extraDependencies: inputs, extraOutputs: outputs);
        }

        [Fact]
        public Task ProcessWindowsCopyFileExA()
        {
            string[] outputs = { "CopyFileExALoggingTest2.txt" };
            string[] inputs = { "CopyFileExALoggingTest1.txt" };
            return ProcessWindowsCallHelper("CopyFileExA", extraDependencies: inputs, extraOutputs: outputs);
        }

        [Fact]
        public Task ProcessWindowsMoveFileW()
        {
            string[] outputs = { "MoveFileWLoggingTest1.txt", "MoveFileWLoggingTest2.txt" };
            string[] inputs = { "MoveFileWLoggingTest1.txt" };

            // Outputs untracked since the source file is deleted.
            return ProcessWindowsCallHelper("MoveFileW", extraDependencies: inputs, extraOutputs: outputs, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsMoveFileA()
        {
            string[] outputs = { "MoveFileALoggingTest1.txt", "MoveFileALoggingTest2.txt" };
            string[] inputs = { "MoveFileALoggingTest1.txt" };

            // Outputs untracked since the source file is deleted.
            return ProcessWindowsCallHelper("MoveFileA", extraDependencies: inputs, extraOutputs: outputs, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsMoveFileExW()
        {
            string[] outputs = { "MoveFileExWLoggingTest1.txt", "MoveFileExWLoggingTest2.txt" };
            string[] inputs = { "MoveFileExWLoggingTest1.txt" };

            // Outputs untracked since the source file is deleted.
            return ProcessWindowsCallHelper("MoveFileExW", extraDependencies: inputs, extraOutputs: outputs, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsMoveFileExA()
        {
            string[] outputs = { "MoveFileExALoggingTest1.txt", "MoveFileExALoggingTest2.txt" };
            string[] inputs = { "MoveFileExALoggingTest1.txt" };

            // Outputs untracked since the source file is deleted.
            return ProcessWindowsCallHelper("MoveFileExA", extraDependencies: inputs, extraOutputs: outputs, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsMoveFileWithProgressW()
        {
            string[] outputs = { "MoveFileWithProgressWLoggingTest1.txt", "MoveFileWithProgressWLoggingTest2.txt" };
            string[] inputs = { "MoveFileWithProgressWLoggingTest1.txt" };

            // Outputs untracked since the source file is deleted.
            return ProcessWindowsCallHelper("MoveFileWithProgressW", extraDependencies: inputs, extraOutputs: outputs, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsMoveFileWithProgressA()
        {
            string[] outputs = { "MoveFileWithProgressALoggingTest1.txt", "MoveFileWithProgressALoggingTest2.txt" };
            string[] inputs = { "MoveFileWithProgressALoggingTest1.txt" };

            // Outputs untracked since the source file is deleted.
            return ProcessWindowsCallHelper("MoveFileWithProgressA", extraDependencies: inputs, extraOutputs: outputs, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsReplaceFileW()
        {
            string[] deleted = { "ReplaceFileWLoggingTestIn.txt", "ReplaceFileWLoggingTestOut.txt" };
            return ProcessWindowsCallHelper("ReplaceFileW", extraOutputs: deleted, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsReplaceFileA()
        {
            string[] deleted = { "ReplaceFileALoggingTestIn.txt", "ReplaceFileALoggingTestOut.txt" };
            return ProcessWindowsCallHelper("ReplaceFileA", extraOutputs: deleted, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsDeleteFileW()
        {
            string[] outputs = { "DeleteFileWLoggingTest.txt" };
            string[] inputs = { "DeleteFileWLoggingTest.txt" };
            return ProcessWindowsCallHelper("DeleteFileW", extraDependencies: inputs, extraOutputs: outputs, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsDeleteFileA()
        {
            string[] outputs = { "DeleteFileALoggingTest.txt" };
            string[] inputs = { "DeleteFileALoggingTest.txt" };
            return ProcessWindowsCallHelper("DeleteFileA", extraDependencies: inputs, extraOutputs: outputs, untrackedOutputs: true);
        }

        [Fact]
        public Task ProcessWindowsCreateHardLinkW()
        {
            string[] outputs = { "CreateHardLinkWLoggingTest1.txt" };
            string[] inputs = { "CreateHardLinkWLoggingTest2.txt" };
            return ProcessWindowsCallHelper("CreateHardLinkW", extraDependencies: inputs, extraOutputs: outputs);
        }

        [Fact]
        public Task ProcessWindowsCreateHardLinkA()
        {
            string[] outputs = { "CreateHardLinkALoggingTest1.txt" };
            string[] inputs = { "CreateHardLinkALoggingTest2.txt" };
            return ProcessWindowsCallHelper("CreateHardLinkA", extraDependencies: inputs, extraOutputs: outputs);
        }

        [Fact(Skip = "Disable while t-doilij works on fix to not write to drop directory")]
        public Task ProcessWindowsCreateSymbolicLinkW()
        {
            return ProcessWindowsCallHelper("CreateSymbolicLinkW");
        }

        [Fact(Skip = "Disable while t-doilij works on fix to not write to drop directory")]
        public Task ProcessWindowsCreateSymbolicLinkA()
        {
            return ProcessWindowsCallHelper("CreateSymbolicLinkA");
        }

        [Fact]
        public Task ProcessWindowsFindFirstFileW()
        {
            return ProcessWindowsCallHelper("FindFirstFileW");
        }

        [Fact]
        public Task ProcessWindowsFindFirstFileA()
        {
            return ProcessWindowsCallHelper("FindFirstFileA");
        }

        [Fact]
        public Task ProcessWindowsFindFirstFileExW()
        {
            return ProcessWindowsCallHelper("FindFirstFileExW");
        }

        [Fact]
        public Task ProcessWindowsFindFirstFileExA()
        {
            return ProcessWindowsCallHelper("FindFirstFileExA");
        }

        [Fact]
        public Task ProcessWindowsGetFileInformationByHandleEx()
        {
            return ProcessWindowsCallHelper("GetFileInformationByHandleEx");
        }

        [Fact]
        public Task ProcessWindowsSetFileInformationByHandle()
        {
            return ProcessWindowsCallHelper("SetFileInformationByHandle");
        }

        [Fact]
        public Task ProcessWindowsOpenFileMappingW()
        {
            return ProcessWindowsCallHelper("OpenFileMappingW");
        }

        [Fact]
        public Task ProcessWindowsOpenFileMappingA()
        {
            return ProcessWindowsCallHelper("OpenFileMappingA");
        }

        [Fact]
        public Task ProcessWindowsGetTempFileNameW()
        {
            return ProcessWindowsCallHelper("GetTempFileNameW");
        }

        [Fact]
        public Task ProcessWindowsGetTempFileNameA()
        {
            return ProcessWindowsCallHelper("GetTempFileNameA");
        }

        [Fact]
        public Task ProcessWindowsCreateDirectoryW()
        {
            string[] inputs = { "CreateDirectoryWLoggingTest" };
            return ProcessWindowsCallHelper("CreateDirectoryW", extraDependencies: inputs, readsAndWritesDirectories: true);
        }

        [Fact]
        public Task ProcessWindowsCreateDirectoryA()
        {
            string[] inputs = { "CreateDirectoryALoggingTest" };
            return ProcessWindowsCallHelper("CreateDirectoryA", extraDependencies: inputs, readsAndWritesDirectories: true);
        }

        [Fact]
        public Task ProcessWindowsCreateDirectoryExW()
        {
            return ProcessWindowsCallHelper("CreateDirectoryExW");
        }

        [Fact]
        public Task ProcessWindowsCreateDirectoryExA()
        {
            return ProcessWindowsCallHelper("CreateDirectoryExA");
        }

        [Fact]
        public Task ProcessWindowsRemoveDirectoryW()
        {
            string[] inputs = { "RemoveDirectoryWLoggingTest" };
            string[] outputs = { "RemoveDirectoryWLoggingTest" };
            return ProcessWindowsCallHelper("RemoveDirectoryW", extraDependencies: inputs, extraOutputs: outputs, readsAndWritesDirectories: true);
        }

        [Fact]
        public Task ProcessWindowsRemoveDirectoryA()
        {
            string[] inputs = { "RemoveDirectoryALoggingTest" };
            string[] outputs = { "RemoveDirectoryALoggingTest" };
            return ProcessWindowsCallHelper("RemoveDirectoryA", extraDependencies: inputs, extraOutputs: outputs, readsAndWritesDirectories: true);
        }

        [Fact]
        public Task ProcessWindowsDecryptFileW()
        {
            return ProcessWindowsCallHelper("DecryptFileW");
        }

        [Fact]
        public Task ProcessWindowsDecryptFileA()
        {
            return ProcessWindowsCallHelper("DecryptFileA");
        }

        [Fact]
        public Task ProcessWindowsEncryptFileW()
        {
            return ProcessWindowsCallHelper("EncryptFileW");
        }

        [Fact]
        public Task ProcessWindowsEncryptFileA()
        {
            return ProcessWindowsCallHelper("EncryptFileA");
        }

        [Fact]
        public Task ProcessWindowsOpenEncryptedFileRawW()
        {
            return ProcessWindowsCallHelper("OpenEncryptedFileRawW");
        }

        [Fact]
        public Task ProcessWindowsOpenEncryptedFileRawA()
        {
            return ProcessWindowsCallHelper("OpenEncryptedFileRawA");
        }

        [Fact]
        public Task ProcessWindowsOpenFileById()
        {
            return ProcessWindowsCallHelper("OpenFileById");
        }

        #endregion

        #region Failure Cases
        [Fact]
        public Task ProcessWindowsCopyFileFailsWithAllowedSourceButDisallowedDestination()
        {
            string[] inputs = { "CopyFileALoggingTest1.txt" };

            return ProcessWindowsCallHelper(
                "CopyFileA",
                new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = true },
                extraDependencies: inputs,
                expectedErrorStrings: new[] { "failed with exit code 1" });
        }
        #endregion
    }
}
