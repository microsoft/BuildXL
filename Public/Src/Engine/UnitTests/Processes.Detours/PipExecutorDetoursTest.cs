// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;
using ProcessLogEventId = BuildXL.Processes.Tracing.LogEventId;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Processes.Detours
{
    public sealed partial class SandboxedProcessPipExecutorTest
    {
        private const int ErrorPrivilegeNotHeld = 1314;
        private const string ExtraFileNameInDirectory = "foo.txt";

        private bool IsNotEnoughPrivilegesError(SandboxedProcessPipExecutionResult result)
        {
            if (result.Status == SandboxedProcessPipExecutionStatus.ExecutionFailed && result.ExitCode == ErrorPrivilegeNotHeld)
            {
                SetExpectedFailures(1, 0);
                return true;
            }

            return false;
        }

        private static void EstablishJunction(string junctionPath, string targetPath)
        {
            if (!Directory.Exists(junctionPath))
            {
                Directory.CreateDirectory(junctionPath);
            }

            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            FileUtilities.CreateJunction(junctionPath, targetPath);
        }

        private Task<SandboxedProcessPipExecutionResult> RunProcessAsync(
            PathTable pathTable,
            bool ignoreSetFileInformationByHandle,
            bool ignoreZwRenameFileInformation,
            bool monitorNtCreate,
            bool ignoreRepPoints,
            BuildXLContext context,
            Process pip,
            out string errorString,
            bool existingDirectoryProbesAsEnumerations = false,
            bool disableDetours = false,
            AbsolutePath binDirectory = default(AbsolutePath),
            bool unexpectedFileAccessesAreErrors = true,
            List<TranslateDirectoryData> directoriesToTranslate = null,
            bool ignoreGetFinalPathNameByHandle = false,
            bool ignoreZwOtherFileInformation = true,
            bool monitorZwCreateOpenQueryFile = false,
            bool ignoreNonCreateFileReparsePoints = true,
            bool ignoreZwCreateOpenQuesryFile = true,
            bool isQuickBuildIntegrated = false,
            bool ignorePreloadedDlls = true,
            bool enforceAccessPoliciesOnDirectoryCreation = false)
        {
            errorString = null;

            var directoryTranslator = new DirectoryTranslator();

            if (TryGetSubstSourceAndTarget(out string substSource, out string substTarget))
            {
                directoryTranslator.AddTranslation(substSource, substTarget);
            }

            if (directoriesToTranslate != null)
            {
                foreach (var translateDirectoryData in directoriesToTranslate)
                {
                    directoryTranslator.AddTranslation(translateDirectoryData.FromPath, translateDirectoryData.ToPath, context.PathTable);
                }
            }

            directoryTranslator.Seal();

            SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
            {
                FileAccessIgnoreCodeCoverage = true,
                LogFileAccessTables = true,
                LogObservedFileAccesses = true,
                UnsafeSandboxConfigurationMutable =
                {
                    UnexpectedFileAccessesAreErrors = unexpectedFileAccessesAreErrors,
                    IgnoreReparsePoints = ignoreRepPoints,
                    ExistingDirectoryProbesAsEnumerations = existingDirectoryProbesAsEnumerations,
                    IgnoreZwRenameFileInformation = ignoreZwRenameFileInformation,
                    IgnoreZwOtherFileInformation = ignoreZwOtherFileInformation,
                    IgnoreNonCreateFileReparsePoints = ignoreNonCreateFileReparsePoints,
                    IgnoreSetFileInformationByHandle = ignoreSetFileInformationByHandle,
                    SandboxKind = disableDetours ? SandboxKind.None : SandboxKind.Default,
                    MonitorNtCreateFile = monitorNtCreate,
                    IgnoreGetFinalPathNameByHandle = ignoreGetFinalPathNameByHandle,
                    MonitorZwCreateOpenQueryFile = monitorZwCreateOpenQueryFile,
                    IgnorePreloadedDlls = ignorePreloadedDlls
                },
                EnforceAccessPoliciesOnDirectoryCreation = enforceAccessPoliciesOnDirectoryCreation,
                FailUnexpectedFileAccesses = unexpectedFileAccessesAreErrors
            };

            var loggingContext = CreateLoggingContextForTest();

            return new SandboxedProcessPipExecutor(
                context,
                loggingContext,
                pip,
                sandboxConfiguration,
                null,
                null,
                new Dictionary<string, string>(),
                new ProcessInContainerManager(loggingContext, context.PathTable),
                null,
                null,
                null,
                SemanticPathExpander.Default,
                false,
                pipEnvironment: new PipEnvironment(),
                validateDistribution: false,
                directoryArtifactContext: TestDirectoryArtifactContext.Empty,
                buildEngineDirectory: binDirectory,
                directoryTranslator: directoryTranslator,
                isQbuildIntegrated: isQuickBuildIntegrated).RunAsync(sandboxedKextConnection: GetSandboxedKextConnection());
        }

        [Fact]
        public async Task CallCreateFileOnNtEscapedPath()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testFilePath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory(Path.GetDirectoryName(testFilePath));
                File.WriteAllText(testFilePath, "input!");

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallCreateFileOnNtEscapedPath");

                var environmentVariables = new List<EnvironmentVariable>();

                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);
                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[] { executableFileArtifact }),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,

                    // We want to have accessed under the working directory explicitly reported. The process will acces \\?\<working directory here>\input
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new[] { DirectoryArtifact.CreateWithZeroPartialSealId(workingDirectoryAbsolutePath) }),
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = true },
                    pip);
            }

            // The \\?\ escaped path should not have failed parsing.
            AssertWarningEventLogged(ProcessLogEventId.PipProcessFailedToParsePathOfFileAccess, count: 0);
        }

        [Flags]
        private enum AddFileOrDirectoryKinds
        {
            None,
            AsDependency,
            AsOutput
        }

        // TODO: This setup method is so convoluted because many hands have touched it and simply add things on top of the others. Need a task to tidy it up.
        private static Process SetupDetoursTests(
            BuildXLContext context,
            TempFileStorage tempFileStorage,
            PathTable pathTable,
            string firstFileName,
            string secondFileOrDirectoryName,
            string nativeTestName,
            bool isDirectoryTest = false,
            bool createSymlink = false,
            bool addCreateFileInDirectoryToDependencies = false,
            bool createFileInDirectory = false,
            AddFileOrDirectoryKinds addFirstFileKind = AddFileOrDirectoryKinds.AsOutput,
            AddFileOrDirectoryKinds addSecondFileOrDirectoryKind = AddFileOrDirectoryKinds.AsDependency,
            bool makeSecondUntracked = false,
            Dictionary<string, AbsolutePath> createdInputPaths = null,
            List<AbsolutePath> untrackedPaths = null,
            List<AbsolutePath> additionalTempDirectories = null,
            List<DirectoryArtifact> outputDirectories = null)
        {
            // Get the executable "DetoursTests.exe".
            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            XAssert.IsTrue(!string.IsNullOrWhiteSpace(currentCodeFolder), "Current code folder is unknown");

            Contract.Assert(currentCodeFolder != null);

            string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");
            XAssert.IsTrue(File.Exists(executable));

            FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

            // Get the working directory.
            Contract.Assume(!string.IsNullOrWhiteSpace(tempFileStorage.RootDirectory));
            AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, tempFileStorage.RootDirectory);

            // Create a clean test directory.
            AbsolutePath testDirectoryAbsolutePath = tempFileStorage.GetDirectory(pathTable, "input");
            string testDirectoryExpandedPath = testDirectoryAbsolutePath.ToString(pathTable);

            XAssert.IsTrue(Directory.Exists(testDirectoryExpandedPath), "Test directory must successfully be created");
            XAssert.IsFalse(Directory.EnumerateFileSystemEntries(testDirectoryExpandedPath).Any(), "Test directory must be empty");

            // Create a file artifact for the the first file, and ensure that the first file does not exist.
            AbsolutePath firstFileOrDirectoryAbsolutePath = tempFileStorage.GetFileName(pathTable, testDirectoryAbsolutePath, firstFileName);
            FileArtifact firstFileArtifact = FileArtifact.CreateSourceFile(firstFileOrDirectoryAbsolutePath);
            DirectoryArtifact firstDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(firstFileOrDirectoryAbsolutePath);

            string firstFileExpandedPath = firstFileOrDirectoryAbsolutePath.ToString(pathTable);

            if (File.Exists(firstFileExpandedPath))
            {
                ExceptionUtilities.HandleRecoverableIOException(() => File.Delete(firstFileExpandedPath), exception => { });
            }

            if (Directory.Exists(firstFileExpandedPath))
            {
                ExceptionUtilities.HandleRecoverableIOException(() => Directory.Delete(firstFileExpandedPath, true), exception => { });
            }

            XAssert.IsFalse(File.Exists(firstFileExpandedPath));

            if (createdInputPaths != null)
            {
                createdInputPaths[firstFileName] = firstFileOrDirectoryAbsolutePath;
            }

            // Set second artifact, depending on whether we are testing directory or not.
            FileArtifact secondFileArtifact = FileArtifact.Invalid;
            FileArtifact extraFileArtifact = FileArtifact.Invalid;
            DirectoryArtifact secondDirectoryArtifact = DirectoryArtifact.Invalid;
            string secondFileOrDirectoryExpandedPath = null;
            AbsolutePath secondFileOrDirectoryAbsolutePath = AbsolutePath.Invalid;

            if (!string.IsNullOrWhiteSpace(secondFileOrDirectoryName))
            {
                if (isDirectoryTest)
                {
                    secondFileOrDirectoryAbsolutePath = tempFileStorage.GetDirectory(pathTable, testDirectoryAbsolutePath, secondFileOrDirectoryName);
                    secondFileOrDirectoryExpandedPath = secondFileOrDirectoryAbsolutePath.ToString(pathTable);

                    secondDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(secondFileOrDirectoryAbsolutePath);
                }
                else
                {
                    secondFileOrDirectoryAbsolutePath = tempFileStorage.GetFileName(pathTable, testDirectoryAbsolutePath, secondFileOrDirectoryName);
                    secondFileOrDirectoryExpandedPath = secondFileOrDirectoryAbsolutePath.ToString(pathTable);

                    if (File.Exists(secondFileOrDirectoryExpandedPath))
                    {
                        ExceptionUtilities.HandleRecoverableIOException(() => File.Delete(secondFileOrDirectoryExpandedPath), exception => { });
                    }

                    if (Directory.Exists(secondFileOrDirectoryExpandedPath))
                    {
                        ExceptionUtilities.HandleRecoverableIOException(() => Directory.Delete(secondFileOrDirectoryExpandedPath, true), exception => { });
                    }

                    XAssert.IsFalse(File.Exists(secondFileOrDirectoryExpandedPath));
                    XAssert.IsFalse(Directory.Exists(secondFileOrDirectoryExpandedPath));

                    secondFileArtifact = FileArtifact.CreateSourceFile(secondFileOrDirectoryAbsolutePath);

                    ExceptionUtilities.HandleRecoverableIOException(
                        () =>
                        {
                            using (FileStream fs = File.Create(secondFileOrDirectoryExpandedPath))
                            {
                                byte[] info = new UTF8Encoding(true).GetBytes("aaa");

                                // Add some information to the file.
                                fs.Write(info, 0, info.Length);
                                fs.Close();
                            }
                        },
                        exception => { });
                    XAssert.IsTrue(File.Exists(secondFileOrDirectoryExpandedPath));
                }

                if (createdInputPaths != null)
                {
                    createdInputPaths[secondFileOrDirectoryName] = secondFileOrDirectoryAbsolutePath;
                }
            }

            bool addCreatedFileToDirectory = false;

            if (isDirectoryTest && createFileInDirectory && secondFileOrDirectoryAbsolutePath.IsValid)
            {
                XAssert.IsTrue(!string.IsNullOrWhiteSpace(secondFileOrDirectoryExpandedPath));

                AbsolutePath extraFileAbsolutePath = tempFileStorage.GetFileName(pathTable, secondFileOrDirectoryAbsolutePath, ExtraFileNameInDirectory);
                extraFileArtifact = FileArtifact.CreateSourceFile(extraFileAbsolutePath);

                string extraFileExtendedPath = extraFileAbsolutePath.ToString(pathTable);
                if (File.Exists(extraFileExtendedPath))
                {
                    ExceptionUtilities.HandleRecoverableIOException(() => File.Delete(extraFileExtendedPath), exception => { });
                }

                XAssert.IsFalse(File.Exists(extraFileExtendedPath));

                ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        using (FileStream fs = File.Create(extraFileExtendedPath))
                        {
                            byte[] info = new UTF8Encoding(true).GetBytes("bbb");

                            // Add some information to the file.
                            fs.Write(info, 0, info.Length);
                            fs.Close();
                        }
                    },
                    exception => { });

                XAssert.IsTrue(File.Exists(extraFileExtendedPath));
                addCreatedFileToDirectory = true;

                if (createdInputPaths != null)
                {
                    Contract.Assert(!string.IsNullOrWhiteSpace(secondFileOrDirectoryName));
                    createdInputPaths[Path.Combine(secondFileOrDirectoryName, ExtraFileNameInDirectory)] = extraFileAbsolutePath;
                    createdInputPaths[Path.Combine(firstFileName, ExtraFileNameInDirectory)] = firstFileOrDirectoryAbsolutePath.Combine(
                        pathTable,
                        ExtraFileNameInDirectory);
                }
            }

            var arguments = new PipDataBuilder(pathTable.StringTable);
            arguments.Add(nativeTestName);

            var untrackedList = new List<AbsolutePath>(CmdHelper.GetCmdDependencies(pathTable));

            if (untrackedPaths != null)
            {
                foreach (AbsolutePath up in untrackedPaths)
                {
                    untrackedList.Add(up);
                }
            }

            var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);

            if (makeSecondUntracked && secondFileOrDirectoryAbsolutePath.IsValid)
            {
                untrackedList.Add(secondFileOrDirectoryAbsolutePath);
            }

            if (createSymlink && secondFileOrDirectoryAbsolutePath.IsValid)
            {
                XAssert.IsTrue(!string.IsNullOrWhiteSpace(secondFileOrDirectoryExpandedPath));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(firstFileExpandedPath, secondFileOrDirectoryExpandedPath, !isDirectoryTest));
            }

            var allDependencies = new List<FileArtifact> { executableFileArtifact };
            var allDirectoryDependencies = new List<DirectoryArtifact>(2);
            var allOutputs = new List<FileArtifactWithAttributes>(2);
            var allDirectoryOutputs = new List<DirectoryArtifact>();

            if (secondFileOrDirectoryAbsolutePath.IsValid)
            {
                if (addSecondFileOrDirectoryKind.HasFlag(AddFileOrDirectoryKinds.AsDependency))
                {
                    if (isDirectoryTest)
                    {
                        allDirectoryDependencies.Add(secondDirectoryArtifact);
                    }
                    else
                    {
                        allDependencies.Add(secondFileArtifact);
                    }
                }

                if (addSecondFileOrDirectoryKind.HasFlag(AddFileOrDirectoryKinds.AsOutput) && !isDirectoryTest)
                {
                    // Rewrite.
                    allOutputs.Add(secondFileArtifact.CreateNextWrittenVersion().WithAttributes(FileExistence.Required));
                }
            }

            if (addCreatedFileToDirectory && addCreateFileInDirectoryToDependencies)
            {
                allDependencies.Add(extraFileArtifact);

                if (createSymlink)
                {
                    // If symlink is created, then add the symlink via first directory as dependency.
                    allDependencies.Add(FileArtifact.CreateSourceFile(firstFileOrDirectoryAbsolutePath.Combine(pathTable, ExtraFileNameInDirectory)));
                }
            }

            if (addFirstFileKind.HasFlag(AddFileOrDirectoryKinds.AsDependency))
            {
                if (isDirectoryTest)
                {
                    allDirectoryDependencies.Add(firstDirectoryArtifact);
                }
                else
                {
                    allDependencies.Add(firstFileArtifact);
                }
            }

            if (addFirstFileKind.HasFlag(AddFileOrDirectoryKinds.AsOutput))
            {
                if (isDirectoryTest)
                {
                    allDirectoryOutputs.Add(firstDirectoryArtifact);
                }
                else
                {
                    allOutputs.Add(firstFileArtifact.CreateNextWrittenVersion().WithAttributes(FileExistence.Required));
                }
            }

            if (outputDirectories != null)
            {
                foreach (DirectoryArtifact da in outputDirectories)
                {
                    allDirectoryOutputs.Add(da);
                }
            }

            return new Process(
                executableFileArtifact,
                workingDirectoryAbsolutePath,
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
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(allOutputs.ToArray()),
                directoryDependencies:
                    isDirectoryTest
                        ? ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(allDirectoryDependencies.ToArray<DirectoryArtifact>())
                        : ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(allDirectoryOutputs.ToArray<DirectoryArtifact>()),
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedList),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                tags: ReadOnlyArray<StringId>.Empty,

                // We expect the CreateFile call to fail, but with no monitoring error logged.
                successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(0),
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: additionalTempDirectories == null ? ReadOnlyArray<AbsolutePath>.Empty : ReadOnlyArray<AbsolutePath>.From(additionalTempDirectories));
        }
        
        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public async Task CallDetouredSetFileInformationFileLink(bool ignorePreloadedDlls, bool useFileLinkInformationEx)
        {
            if (useFileLinkInformationEx)
            {
                // FileLinkInformationEx is only available starting RS5 (ver 1809, OS build 17763)
                // skip the test if it's running on a machine that does not support it. 
                var versionString = OperatingSystemHelper.GetOSVersion();

                int build = 0;
                var match = Regex.Match(versionString, @"^Windows\s\d+\s\w+\s(?<buildId>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    build = Convert.ToInt32(match.Groups["buildId"].Value);
                }

                if (build < 17763)
                {
                    return;
                }
            }

            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "SetFileInformationFileLinkTest1.txt",
                    "SetFileInformationFileLinkTest2.txt",
                    useFileLinkInformationEx
                        ? "CallDetouredSetFileInformationFileLinkEx" 
                        : "CallDetouredSetFileInformationFileLink",
                    isDirectoryTest: false,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: true,
                    ignoreZwRenameFileInformation: true,
                    monitorNtCreate: true,
                    ignoreRepPoints: true,
                    ignoreZwOtherFileInformation: false,
                    ignorePreloadedDlls: ignorePreloadedDlls,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                if (!ignorePreloadedDlls)
                {
                    // The count includes extra preloaded Dlls.
                    // The preloaded Dlls should be bigger or equal to 5.
                    // In the cloud, the extra preloaded Dlls may not be included.
                    XAssert.IsTrue(
                        result.AllReportedFileAccesses.Count >= 5,
                        "Number of reported accesses: " + result.AllReportedFileAccesses.Count + Environment.NewLine
                        + string.Join(Environment.NewLine + "\t", result.AllReportedFileAccesses.Select(rfs => rfs.Describe())));
                }

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["SetFileInformationFileLinkTest2.txt"], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (createdInputPaths["SetFileInformationFileLinkTest1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed),
                    });
            }
        }

        [Fact]
        public async Task CallDetouredZwCreateFile()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "ZwCreateFileTest1.txt",
                    "ZwCreateFileTest2.txt",
                    "CallDetouredZwCreateFile",
                    isDirectoryTest: false,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,

                    // The second file will be opened with a write access in order for SetFileInformationByHandle works.
                    // However, the second file will be renamed into the first file, and so the second file does not fall into
                    // rewrite category, and thus cannot be specified as output. This forces us to make it untracked.
                    makeSecondUntracked: true,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: true,
                    ignoreZwRenameFileInformation: true,
                    monitorNtCreate: true,
                    ignoreRepPoints: true,
                    ignoreZwOtherFileInformation: false,
                    monitorZwCreateOpenQueryFile: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                if (result.Status == SandboxedProcessPipExecutionStatus.ExecutionFailed)
                {
                    // When we build in the cloud or in the release pipeline, this test can suffer from 'unclear' file system limitation or returns weird error.
                    SetExpectedFailures(1, 0);
                }
                else
                {
                    VerifyNormalSuccess(context, result);
                }

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["ZwCreateFileTest1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed),
                    });
            }
        }

        [Fact]
        public async Task CallDetouredCreateFileWWithGenericAllAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateFileWWithGenericAllAccess1.txt",
                    "CreateFileWWithGenericAllAccess2.txt",
                    "CallDetouredCreateFileWWithGenericAllAccess",
                    isDirectoryTest: false,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: true,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: true,
                    ignoreZwRenameFileInformation: true,
                    monitorNtCreate: true,
                    ignoreRepPoints: true,
                    ignoreZwOtherFileInformation: true,
                    monitorZwCreateOpenQueryFile: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateFileWWithGenericAllAccess1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed),
                    });
            }
        }

        [Fact]
        public async Task CallDetouredZwOpenFile()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "ZwOpenFileTest1.txt",
                    "ZwOpenFileTest2.txt",
                    "CallDetouredZwOpenFile",
                    isDirectoryTest: false,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsDependency,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: true,
                    ignoreZwRenameFileInformation: true,
                    monitorNtCreate: true,
                    ignoreRepPoints: true,
                    ignoreZwOtherFileInformation: false,
                    monitorZwCreateOpenQueryFile: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["ZwOpenFileTest2.txt"], RequestedAccess.Read, FileAccessStatus.Allowed),
                    });
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CallDetouredSetFileInformationByHandle(bool ignoreSetFileInformationByHandle)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "SetFileInformationByHandleTest1.txt",
                    "SetFileInformationByHandleTest2.txt",
                    "CallDetouredSetFileInformationByHandle",
                    isDirectoryTest: false,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,

                    // The second file will be opened with a write access in order for SetFileInformationByHandle works.
                    // However, the second file will be renamed into the first file, and so the second file does not fall into
                    // rewrite category, and thus cannot be specified as output. This forces us to make it untracked.
                    makeSecondUntracked: true,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: ignoreSetFileInformationByHandle,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                var accesses = new List<(AbsolutePath, RequestedAccess, FileAccessStatus)>();

                // Although ignored, we still have write request on SetFileInformationByHandleTest2.txt because we open handle of it by calling CreateFile.
                accesses.Add((createdInputPaths["SetFileInformationByHandleTest2.txt"], RequestedAccess.Write, FileAccessStatus.Allowed));

                if (!ignoreSetFileInformationByHandle)
                {
                    accesses.Add((createdInputPaths["SetFileInformationByHandleTest1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed));
                }

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    accesses.ToArray());
            }
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CallDetouredGetFinalPathNameByHandle(bool ignoreGetFinalPathNameByHandle)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = tempFiles.GetDirectory("input");
                string testTargetDirPath = tempFiles.GetDirectory("inputTarget");
                
                // Create file input\GetFinalPathNameByHandleTest.txt, which essentially is inputTarget\GetFinalPathNameByHandleTest.txt
                string testFile = Path.Combine(testDirPath, "GetFinalPathNameByHandleTest.txt");
                AbsolutePath firstAbsPath = AbsolutePath.Create(pathTable, testFile);
                createdInputPaths[testFile] = firstAbsPath;
                FileArtifact testFileArtifact = FileArtifact.CreateSourceFile(firstAbsPath);

                string testTargetFile = Path.Combine(testTargetDirPath, "GetFinalPathNameByHandleTest.txt");
                AbsolutePath firstTargetAbsPath = AbsolutePath.Create(pathTable, testTargetFile);
                createdInputPaths[testTargetFile] = firstTargetAbsPath;

                if (File.Exists(testTargetFile))
                {
                    File.Delete(testTargetFile);
                }

                using (FileStream fs = File.Create(testTargetFile))
                {
                    byte[] info = new UTF8Encoding(true).GetBytes("aaa");

                    // Add some information to the file.
                    await fs.WriteAsync(info, 0, info.Length);
                    fs.Close();
                }

                XAssert.IsTrue(File.Exists(testTargetFile));

                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(testFile, testTargetFile, true));

                var allDependencies = new List<FileArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                // Only add input\GetFinalPathNameByHandleTest.txt as dependency.
                allDependencies.Add(testFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDetouredGetFinalPathNameByHandle");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString,
                    ignoreGetFinalPathNameByHandle: ignoreGetFinalPathNameByHandle,
                    directoriesToTranslate:
                        new List<TranslateDirectoryData>
                        {
                            new TranslateDirectoryData(
                                testTargetDirPath + @"\<" + testDirPath + @"\", 
                                AbsolutePath.Create(context.PathTable, testTargetDirPath),
                                AbsolutePath.Create(context.PathTable, testDirPath))
                        });

                VerifyExecutionStatus(
                    context,
                    result,
                    ignoreGetFinalPathNameByHandle
                    ? SandboxedProcessPipExecutionStatus.ExecutionFailed
                    : SandboxedProcessPipExecutionStatus.Succeeded);
                VerifyExitCode(context, result, ignoreGetFinalPathNameByHandle ? -1 : 0);

                SetExpectedFailures(ignoreGetFinalPathNameByHandle ? 1 : 0, 0);
            }
        }

        [Fact]
        public async Task TestDeleteTempDirectory()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = tempFiles.GetDirectory("input");
                AbsolutePath inputDirPath = AbsolutePath.Create(pathTable, testDirPath);
                createdInputPaths[testDirPath] = inputDirPath;

                XAssert.IsTrue(Directory.Exists(testDirPath));

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDeleteDirectoryTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(new[] { inputDirPath }),
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(new[] { inputDirPath }));

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths[testDirPath], RequestedAccess.Write, FileAccessStatus.Allowed),
                    });
            }
        }

        [Fact]
        public async Task TestDeleteTempDirectoryNoFileAccessError()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = tempFiles.GetDirectory("input");
                AbsolutePath inputDirPath = AbsolutePath.Create(pathTable, testDirPath);
                createdInputPaths[testDirPath] = inputDirPath;

                XAssert.IsTrue(Directory.Exists(testDirPath));

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDeleteDirectoryTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(new[] { inputDirPath }),
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(new[] { inputDirPath }));

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    unexpectedFileAccessesAreErrors: false,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths[testDirPath], RequestedAccess.Write, FileAccessStatus.Allowed),
                    });
            }
        }


        [Fact]
        public async Task TestCreateExistingDirectoryFileAccessError()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = tempFiles.GetDirectory("input");
                AbsolutePath inputDirPath = AbsolutePath.Create(pathTable, testDirPath);
                createdInputPaths[testDirPath] = inputDirPath;

                XAssert.IsTrue(Directory.Exists(testDirPath));

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallCreateDirectoryTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString,
                    // with this flag set to 'true', Detours should not interpret CreateDirectory as a read-only probe => CreateDirectory should be denied
                    enforceAccessPoliciesOnDirectoryCreation: true);

                SetExpectedFailures(1, 0);
                AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);

                VerifyAccessDenied(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths[testDirPath], RequestedAccess.Write, FileAccessStatus.Denied),
                    });
            }
        }

        /// <summary>
        /// Tests that directories of output files are created.
        /// </summary>
        [Fact]
        public async Task CreateDirectoriesNoAllow()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = tempFiles.RootDirectory;
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(context.PathTable, destination);
                DirectoryArtifact destinationFileArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(destinationAbsolutePath);

                string envVarName = "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty);

                string destFile = Path.Combine(destination, "Foo", "bar.txt");
                string destDirectory = Path.Combine(destination, "Foo");
                AbsolutePath destFileAbsolutePath = AbsolutePath.Create(context.PathTable, destFile);
                FileArtifact destFileArtifact = FileArtifact.CreateOutputFile(destFileAbsolutePath);

                if (File.Exists(destFile))
                {
                    File.Delete(destFile);
                }

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("mkdir");
                    arguments.Add(destinationAbsolutePath);
                    arguments.Add("&");
                    arguments.Add("echo");
                    arguments.Add("aaaaa");
                    arguments.Add(">");
                    arguments.Add(destFileAbsolutePath);
                }

                List<AbsolutePath> untrackedPaths = new List<AbsolutePath>();

                foreach (AbsolutePath ap in CmdHelper.GetCmdDependencies(context.PathTable))
                {
                    untrackedPaths.Add(ap);
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(
                        new EnvironmentVariable[]
                        {
                            new EnvironmentVariable(
                                StringId.Create(context.PathTable.StringTable, envVarName),
                                PipDataBuilder.CreatePipData(
                                    context.PathTable.StringTable,
                                    " ",
                                    PipDataFragmentEscaping.CRuntimeArgumentRules,
                                    "Success"))
                        }),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(new[]
                                                                {
                                                                    destFileArtifact.WithAttributes(),
                                                                }),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.Succeeded);

                // TODO(imnarasa): Check the exit code.
                XAssert.IsFalse(result.ExitCode == 1);

                XAssert.IsTrue(File.Exists(destFile));
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredFileCreateWithSymlinkAndIgnore()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymbolicLinkTest1.txt",
                    "CreateSymbolicLinkTest2.txt",
                    "CallDetouredFileCreateWithSymlink",
                    isDirectoryTest: false,

                    // Setup doesn't create symlink, but the C++ method CallDetouredFileCreateWithSymlink does.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,

                    // Ignore reparse point.
                    ignoreRepPoints: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // CallDetouredFileCreateWithSymlink calls CreateFileW with Read access on CreateSymbolicLinkTest2.txt.
                        (createdInputPaths["CreateSymbolicLinkTest2.txt"], RequestedAccess.Read, FileAccessStatus.Allowed),
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredFileCreateWithSymlinkAndIgnoreFail()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymbolicLinkTest1.txt",
                    "CreateSymbolicLinkTest2.txt",
                    "CallDetouredFileCreateWithSymlink",
                    isDirectoryTest: false,

                    // Setup doesn't create symlink, but the C++ method CallDetouredFileCreateWithSymlink does.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,

                    // Ignore reparse point.
                    ignoreRepPoints: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // CallDetouredFileCreateWithSymlink calls CreateFileW with Read access on CreateSymbolicLinkTest2.txt.
                        (createdInputPaths["CreateSymbolicLinkTest2.txt"], RequestedAccess.Read, FileAccessStatus.Allowed),
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredFileCreateWithSymlinkAndNoIgnore()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymbolicLinkTest1.txt",
                    "CreateSymbolicLinkTest2.txt",
                    "CallDetouredFileCreateWithSymlink",
                    isDirectoryTest: false,

                    // Setup doesn't create symlink, but the C++ method CallDetouredFileCreateWithSymlink does.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,

                    // Don't ignore reparse point.
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateSymbolicLinkTest2.txt"], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (createdInputPaths["CreateSymbolicLinkTest1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    });
            }
        }

        [Fact]
        public async Task CallDetouredFileCreateWithNoSymlinkAndIgnore()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateNoSymbolicLinkTest1.txt",
                    "CreateNoSymbolicLinkTest2.txt",
                    "CallDetouredFileCreateWithNoSymlink",
                    isDirectoryTest: false,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateNoSymbolicLinkTest2.txt"], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (createdInputPaths["CreateNoSymbolicLinkTest1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    });
            }
        }

        [Fact]
        public async Task CallDetouredFileCreateWithNoSymlinkAndNoIgnore()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateNoSymbolicLinkTest1.txt",
                    "CreateNoSymbolicLinkTest2.txt",
                    "CallDetouredFileCreateWithNoSymlink",
                    isDirectoryTest: false,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true,
                    UnsafeSandboxConfigurationMutable =
                    {
                        UnexpectedFileAccessesAreErrors = true,
                        IgnoreReparsePoints = false,
                        IgnoreSetFileInformationByHandle = false,
                        IgnoreZwRenameFileInformation = false,
                        IgnoreZwOtherFileInformation = false,
                        IgnoreNonCreateFileReparsePoints = false,
                        MonitorZwCreateOpenQueryFile = false,
                        MonitorNtCreateFile = true,
                    }
                };

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateNoSymbolicLinkTest2.txt"], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (createdInputPaths["CreateNoSymbolicLinkTest1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallAccessSymLinkOnFilesWithGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "AccessSymLinkOnFiles1.txt",
                    "AccessSymLinkOnFiles2.txt",
                    "CallAccessSymLinkOnFiles",
                    isDirectoryTest: false,
                    createSymlink: true,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsDependency,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["AccessSymLinkOnFiles2.txt"], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (createdInputPaths["AccessSymLinkOnFiles1.txt"], RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallAccessSymLinkOnFilesWithNoGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "AccessSymLinkOnFiles1.txt",
                    "AccessSymLinkOnFiles2.txt",
                    "CallAccessSymLinkOnFiles",
                    isDirectoryTest: false,
                    createSymlink: true,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsDependency,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.None,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                // Error exit code and access denied.
                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["AccessSymLinkOnFiles2.txt"], RequestedAccess.Read, FileAccessStatus.Denied),
                        (createdInputPaths["AccessSymLinkOnFiles1.txt"], RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        [Fact]
        public async Task TestDirectoryEnumeration()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory("input");

                string firstTestFile = Path.Combine(testDirPath, "Test1.txt");
                XAssert.IsFalse(File.Exists(firstTestFile));
                AbsolutePath firstAbsPath = AbsolutePath.Create(pathTable, firstTestFile);
                FileArtifact firstFileArtifact = FileArtifact.CreateSourceFile(firstAbsPath);
                if (File.Exists(firstTestFile))
                {
                    File.Delete(firstTestFile);
                }

                // Create the file.
                using (FileStream fs = File.Create(firstTestFile))
                {
                    byte[] info = new System.Text.UTF8Encoding(true).GetBytes("aaa");

                    // Add some information to the file.
                    await fs.WriteAsync(info, 0, info.Length);
                    fs.Close();
                }

                XAssert.IsTrue(File.Exists(firstTestFile));

                string secondTestFile = Path.Combine(testDirPath, "Test2.txt");
                XAssert.IsFalse(File.Exists(secondTestFile));
                AbsolutePath secondAbsPath = AbsolutePath.Create(pathTable, secondTestFile);
                FileArtifact secondFileArtifact = FileArtifact.CreateSourceFile(secondAbsPath);
                if (File.Exists(secondTestFile))
                {
                    File.Delete(secondTestFile);
                }

                // Create the file.
                using (FileStream fs = File.Create(secondTestFile))
                {
                    byte[] info = new System.Text.UTF8Encoding(true).GetBytes("bbb");

                    // Add some information to the file.
                    await fs.WriteAsync(info, 0, info.Length);
                    fs.Close();
                }

                XAssert.IsTrue(File.Exists(secondTestFile));

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDirectoryEnumerationTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                XAssert.AreEqual(1, result.ObservedFileAccesses.Count());
                XAssert.AreEqual(testDirPath, result.ObservedFileAccesses[0].Path.ToString(pathTable));
                XAssert.AreEqual(RequestedAccess.Enumerate, result.ObservedFileAccesses[0].Accesses.First().RequestedAccess);
            }
        }

        [Fact]
        public async Task TestDeleteFile()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory("input");

                string firstTestFile = Path.Combine(testDirPath, "Test1.txt");
                XAssert.IsFalse(File.Exists(firstTestFile));
                AbsolutePath firstAbsPath = AbsolutePath.Create(pathTable, firstTestFile);
                createdInputPaths[firstTestFile] = firstAbsPath;

                FileArtifact firstFileArtifact = FileArtifact.CreateSourceFile(firstAbsPath);
                if (File.Exists(firstTestFile))
                {
                    File.Delete(firstTestFile);
                }

                // Create the file.
                using (FileStream fs = File.Create(firstTestFile))
                {
                    byte[] info = new System.Text.UTF8Encoding(true).GetBytes("aaa");

                    // Add some information to the file.
                    await fs.WriteAsync(info, 0, info.Length);
                    fs.Close();
                }

                XAssert.IsTrue(File.Exists(firstTestFile));

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDeleteFileTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                XAssert.IsTrue(File.Exists(firstTestFile));

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths[firstTestFile], RequestedAccess.Write, FileAccessStatus.Denied),
                    });
            }
        }

        [Fact]
        public async Task TestDeleteFileStdRemove()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory("input");

                string firstTestFile = Path.Combine(testDirPath, "Test1.txt");
                XAssert.IsFalse(File.Exists(firstTestFile));
                AbsolutePath firstAbsPath = AbsolutePath.Create(pathTable, firstTestFile);
                createdInputPaths[firstTestFile] = firstAbsPath;

                FileArtifact firstFileArtifact = FileArtifact.CreateSourceFile(firstAbsPath);
                if (File.Exists(firstTestFile))
                {
                    File.Delete(firstTestFile);
                }

                // Create the file.
                using (FileStream fs = File.Create(firstTestFile))
                {
                    byte[] info = new System.Text.UTF8Encoding(true).GetBytes("aaa");

                    // Add some information to the file.
                    await fs.WriteAsync(info, 0, info.Length);
                    fs.Close();
                }

                XAssert.IsTrue(File.Exists(firstTestFile));

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDeleteFileStdRemoveTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                XAssert.IsTrue(File.Exists(firstTestFile));

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths[firstTestFile], RequestedAccess.Write, FileAccessStatus.Denied),
                    });
            }
        }

        [Fact]
        public async Task TestDeleteFileStdRemovenNoFile()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory("input");

                string firstTestFile = Path.Combine(testDirPath, "Test1.txt");
                XAssert.IsFalse(File.Exists(firstTestFile));
                AbsolutePath firstAbsPath = AbsolutePath.Create(pathTable, firstTestFile);
                createdInputPaths[firstTestFile] = firstAbsPath;

                FileArtifact firstFileArtifact = FileArtifact.CreateSourceFile(firstAbsPath);
                if (File.Exists(firstTestFile))
                {
                    File.Delete(firstTestFile);
                }

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDeleteFileStdRemoveTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
                VerifyExitCode(context, result, 2);

                XAssert.IsFalse(File.Exists(firstTestFile));

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths[firstTestFile], RequestedAccess.Probe, FileAccessStatus.Allowed),
                    });
            }
        }

        [Fact]
        public async Task TestDeleteDirectory()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory("input");
                AbsolutePath inputDirPath = AbsolutePath.Create(pathTable, testDirPath);

                string firstTestFile = Path.Combine(testDirPath, "Test1.txt");
                XAssert.IsFalse(File.Exists(firstTestFile));
                AbsolutePath firstAbsPath = AbsolutePath.Create(pathTable, firstTestFile);
                createdInputPaths[testDirPath] = inputDirPath;

                FileArtifact firstFileArtifact = FileArtifact.CreateSourceFile(firstAbsPath);
                if (File.Exists(firstTestFile))
                {
                    File.Delete(firstTestFile);
                }

                // Create the file.
                using (FileStream fs = File.Create(firstTestFile))
                {
                    byte[] info = new System.Text.UTF8Encoding(true).GetBytes("aaa");

                    // Add some information to the file.
                    await fs.WriteAsync(info, 0, info.Length);
                    fs.Close();
                }

                XAssert.IsTrue(File.Exists(firstTestFile));

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDeleteDirectoryTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);
                AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);

                VerifyAccessDenied(context, result);

                XAssert.IsTrue(File.Exists(firstTestFile));

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths[testDirPath], RequestedAccess.Write, FileAccessStatus.Denied),
                        });
            }
        }

        [Fact]
        public async Task TestDeleteDirectoryWithAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);
                List<AbsolutePath> untrackedPaths = new List<AbsolutePath>();

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory("input");
                AbsolutePath inputDirPath = AbsolutePath.Create(pathTable, testDirPath);

                string firstTestFile = Path.Combine(testDirPath, "Test1.txt");
                XAssert.IsFalse(File.Exists(firstTestFile));
                AbsolutePath firstAbsPath = AbsolutePath.Create(pathTable, firstTestFile);
                createdInputPaths[testDirPath] = inputDirPath;
                untrackedPaths.Add(inputDirPath);

                FileArtifact firstFileArtifact = FileArtifact.CreateSourceFile(firstAbsPath);
                if (File.Exists(firstTestFile))
                {
                    File.Delete(firstTestFile);
                }

                // Create the file.
                using (FileStream fs = File.Create(firstTestFile))
                {
                    byte[] info = new UTF8Encoding(true).GetBytes("aaa");

                    // Add some information to the file.
                    await fs.WriteAsync(info, 0, info.Length);
                    fs.Close();
                }

                XAssert.IsTrue(File.Exists(firstTestFile));

                var allDependencies = new List<FileArtifact>(2);
                var allDirectoryDependencies = new List<DirectoryArtifact>(2);
                allDependencies.Add(executableFileArtifact);

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallDeleteDirectoryTest");

                var environmentVariables = new List<EnvironmentVariable>();
                Process pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(allDependencies.ToArray<FileArtifact>()),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 0 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
                VerifyExitCode(context, result, 145);

                XAssert.IsTrue(File.Exists(firstTestFile));

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths[testDirPath], RequestedAccess.Write, FileAccessStatus.Allowed),
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallAccessSymLinkOnDirectoriesWithGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "AccessSymLinkOnDirectories1.dir",
                    "AccessSymLinkOnDirectories2.dir",
                    "CallAccessSymLinkOnDirectories",
                    isDirectoryTest: true,
                    createSymlink: true,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: true,
                    addFirstFileKind: AddFileOrDirectoryKinds.None,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.None,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // TODO: Currently BuildXL does not handle directory junction, so the access via AccessSymLinkOnDirectories2.dir is not recognized.
                        // (createdInputPaths[Path.Combine("AccessSymLinkOnDirectories2.dir", ExtraFileNameInDirectory)], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (
                            createdInputPaths[Path.Combine("AccessSymLinkOnDirectories1.dir", ExtraFileNameInDirectory)],
                            RequestedAccess.Read,
                            FileAccessStatus.Allowed)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallAccessSymLinkOnDirectoriesWithNoGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "AccessSymLinkOnDirectories1.dir",
                    "AccessSymLinkOnDirectories2.dir",
                    "CallAccessSymLinkOnDirectories",
                    isDirectoryTest: true,
                    createSymlink: true,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: true,
                    addFirstFileKind: AddFileOrDirectoryKinds.None,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.None,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // TODO: Currently BuildXL does not handle directory junction, so the access via AccessSymLinkOnDirectories2.dir is not recognized.
                        // (createdInputPaths[Path.Combine("AccessSymLinkOnDirectories2.dir", ExtraFileNameInDirectory)], RequestedAccess.Read, FileAccessStatus.Denied),
                        (
                            createdInputPaths[Path.Combine("AccessSymLinkOnDirectories1.dir", ExtraFileNameInDirectory)],
                            RequestedAccess.Read,
                            FileAccessStatus.Denied)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateSymLinkOnFilesWithGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnFiles1.txt",
                    "CreateSymLinkOnFiles2.txt",
                    "CallCreateSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateSymLinkOnFiles1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    pathsToFalsify: new[]
                    {
                        createdInputPaths["CreateSymLinkOnFiles2.txt"]
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateAndDeleteSymLinkOnFilesWithGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "SymlinkToIrrelevantExistingFile.lnk",
                    "IrrelevantExistingFile.txt",
                    "CallCreateAndDeleteSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.None,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                // Create target file and ensure that it exists afterwards.
                AbsolutePath targetPath = createdInputPaths["IrrelevantExistingFile.txt"];
                WriteFile(context.PathTable, targetPath, "Irrelevant");
                XAssert.IsTrue(File.Exists(targetPath.ToString(context.PathTable)));

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["SymlinkToIrrelevantExistingFile.lnk"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    pathsToFalsify: new[]
                    {
                        createdInputPaths["IrrelevantExistingFile.txt"]
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallMoveSymLinkOnFilesWithGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                // Create an irrelevant file and ensure that it exists.
                var irrelevantFilePath = tempFiles.GetFileName(pathTable, "IrrelevantExistingFile.txt");
                WriteFile(pathTable, irrelevantFilePath, "Irrelevant");
                XAssert.IsTrue(File.Exists(irrelevantFilePath.ToString(pathTable)));

                // Create OldSymlink -> IrrelevantFile
                var oldSymlink = tempFiles.GetFileName(pathTable, "OldSymlinkToIrrelevantExistingFile.lnk");
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(oldSymlink.ToString(pathTable), irrelevantFilePath.ToString(pathTable), true));

                var newSymlink = tempFiles.GetFileName(pathTable, "NewSymlinkToIrrelevantExistingFile.lnk");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallMoveSymLinkOnFilesNotEnforceChainSymLinkAccesses",
                    inputFiles: ReadOnlyArray<FileArtifact>.FromWithoutCopy(FileArtifact.CreateSourceFile(oldSymlink)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                        FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(oldSymlink), FileExistence.Optional),
                        FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(newSymlink), FileExistence.Required)),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                XAssert.IsTrue(File.Exists(newSymlink.ToString(pathTable)));

                var toVerify = new []
                {
                    (oldSymlink, RequestedAccess.Read | RequestedAccess.Write, FileAccessStatus.Allowed),
                    (newSymlink, RequestedAccess.Write, FileAccessStatus.Allowed)
                };

                var toFalsify = new[] { irrelevantFilePath };

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    toVerify,
                    toFalsify);
            }
        }

        [Theory]
        [InlineData("CallDetouredMoveFileExWForRenamingDirectory")]
        [InlineData("CallDetouredSetFileInformationByHandleForRenamingDirectory")]
        [InlineData("CallDetouredZwSetFileInformationByHandleForRenamingDirectory")]
        public async Task CallMoveDirectoryReportAllAccesses(string callArgument)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                // Create OldDirectory\, OldDirectory\fileImplicit.txt, and OldDirectory\fileExplicit.txt.
                AbsolutePath oldDirectory = tempFiles.GetDirectory(pathTable, "OldDirectory");
                AbsolutePath oldDirectoryFileImplicit = oldDirectory.Combine(pathTable, "fileImplicit.txt");
                AbsolutePath oldDirectoryFileExplicit = oldDirectory.Combine(pathTable, "fileExplicit.txt");
                WriteFile(pathTable, oldDirectoryFileImplicit, "implicit");
                WriteFile(pathTable, oldDirectoryFileExplicit, "explicit");

                // Create OldDirectory\Nested, OldDirectory\Nested\fileImplicit.txt, OldDirectory\Nested\fileExplicit.txt.
                AbsolutePath oldDirectoryNested = tempFiles.GetDirectory(pathTable, oldDirectory, "Nested");
                
                AbsolutePath oldDirectoryNestedFileImplicit = oldDirectoryNested.Combine(pathTable, "fileImplicit.txt");
                AbsolutePath oldDirectoryNestedFileExplicit = oldDirectoryNested.Combine(pathTable, "fileExplicit.txt");               
                WriteFile(pathTable, oldDirectoryNestedFileImplicit, "implicit");
                WriteFile(pathTable, oldDirectoryNestedFileExplicit, "explicit");

                // Create OldDirectory\Nested\Nested, OldDirectory\Nested\Nested\fileImplicit.txt, OldDirectory\Nested\Nested\fileExplicit.txt.
                AbsolutePath oldDirectoryNestedNested = tempFiles.GetDirectory(pathTable, oldDirectoryNested, "Nested");

                AbsolutePath oldDirectoryNestedNestedFileImplicit = oldDirectoryNestedNested.Combine(pathTable, "fileImplicit.txt");
                AbsolutePath oldDirectoryNestedNestedFileExplicit = oldDirectoryNestedNested.Combine(pathTable, "fileExplicit.txt");
                WriteFile(pathTable, oldDirectoryNestedNestedFileImplicit, "implicit");
                WriteFile(pathTable, oldDirectoryNestedNestedFileExplicit, "explicit");

                var oldDirectories = new AbsolutePath[]
                {
                    oldDirectory,
                    oldDirectoryNested,
                    oldDirectoryNestedNested,
                };

                var oldFiles = new AbsolutePath[]
                {
                    oldDirectoryFileExplicit,
                    oldDirectoryFileImplicit,
                    oldDirectoryNestedFileExplicit,
                    oldDirectoryNestedFileImplicit,
                    oldDirectoryNestedNestedFileExplicit,
                    oldDirectoryNestedNestedFileImplicit
                };

                AbsolutePath outputDirectory = oldDirectory.GetParent(pathTable).Combine(pathTable, "OutputDirectory");
                AbsolutePath tempDirectory = oldDirectory.GetParent(pathTable).Combine(pathTable, "TempDirectory");
                AbsolutePath newDirectory = outputDirectory.Combine(pathTable, "NewDirectory");
                var newDirectories = oldDirectories.Select(p => p.Relocate(pathTable, oldDirectory, newDirectory)).ToArray();
                var newFiles = oldFiles.Select(p => p.Relocate(pathTable, oldDirectory, newDirectory)).ToArray();

                FileUtilities.CreateDirectory(outputDirectory.ToString(pathTable));

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: callArgument,
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(tempDirectory));

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    enforceAccessPoliciesOnDirectoryCreation: true,
                    unexpectedFileAccessesAreErrors: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                foreach (var path in oldDirectories)
                {
                    XAssert.IsFalse(Directory.Exists(path.ToString(pathTable)));
                }

                foreach (var path in oldFiles)
                {
                    XAssert.IsFalse(File.Exists(path.ToString(pathTable)));
                }

                foreach (var path in newDirectories)
                {
                    XAssert.IsTrue(Directory.Exists(path.ToString(pathTable)));
                }

                foreach (var path in newFiles)
                {
                    XAssert.IsTrue(File.Exists(path.ToString(pathTable)));
                }

                var toVerify = newDirectories
                    .Concat(newFiles)
                    .Concat(oldDirectories)
                    .Concat(oldFiles)
                    .Select(p => (p, RequestedAccess.Write, FileAccessStatus.Denied)).ToArray();

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    toVerify,
                    new AbsolutePath[0]);
            }
        }

        [Theory]
        [InlineData("CallDetouredMoveFileExWForRenamingDirectory")]
        [InlineData("CallDetouredSetFileInformationByHandleForRenamingDirectory")]
        [InlineData("CallDetouredZwSetFileInformationByHandleForRenamingDirectory")]
        public async Task CallMoveDirectoryReportSelectiveAccesses(string callArgument)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                // Create OldDirectory\, OldDirectory\fileImplicit.txt, and OldDirectory\fileExplicit.txt.
                AbsolutePath oldDirectory = tempFiles.GetDirectory(pathTable, "OldDirectory");
                AbsolutePath oldDirectoryFileImplicit = oldDirectory.Combine(pathTable, "fileImplicit.txt");
                AbsolutePath oldDirectoryFileExplicit = oldDirectory.Combine(pathTable, "fileExplicit.txt");
                WriteFile(pathTable, oldDirectoryFileImplicit, "implicit");
                WriteFile(pathTable, oldDirectoryFileExplicit, "explicit");

                // Create OldDirectory\Nested, OldDirectory\Nested\fileImplicit.txt, OldDirectory\Nested\fileExplicit.txt.
                AbsolutePath oldDirectoryNested = tempFiles.GetDirectory(pathTable, oldDirectory, "Nested");

                AbsolutePath oldDirectoryNestedFileImplicit = oldDirectoryNested.Combine(pathTable, "fileImplicit.txt");
                AbsolutePath oldDirectoryNestedFileExplicit = oldDirectoryNested.Combine(pathTable, "fileExplicit.txt");
                WriteFile(pathTable, oldDirectoryNestedFileImplicit, "implicit");
                WriteFile(pathTable, oldDirectoryNestedFileExplicit, "explicit");

                // Create OldDirectory\Nested\Nested, OldDirectory\Nested\Nested\fileImplicit.txt, OldDirectory\Nested\Nested\fileExplicit.txt.
                AbsolutePath oldDirectoryNestedNested = tempFiles.GetDirectory(pathTable, oldDirectoryNested, "Nested");

                AbsolutePath oldDirectoryNestedNestedFileImplicit = oldDirectoryNestedNested.Combine(pathTable, "fileImplicit.txt");
                AbsolutePath oldDirectoryNestedNestedFileExplicit = oldDirectoryNestedNested.Combine(pathTable, "fileExplicit.txt");
                WriteFile(pathTable, oldDirectoryNestedNestedFileImplicit, "implicit");
                WriteFile(pathTable, oldDirectoryNestedNestedFileExplicit, "explicit");

                var oldDirectories = new AbsolutePath[]
                {
                    oldDirectory,
                    oldDirectoryNested,
                    oldDirectoryNestedNested,
                };

                var oldFiles = new AbsolutePath[]
                {
                    oldDirectoryFileExplicit,
                    oldDirectoryFileImplicit,
                    oldDirectoryNestedFileExplicit,
                    oldDirectoryNestedFileImplicit,
                    oldDirectoryNestedNestedFileExplicit,
                    oldDirectoryNestedNestedFileImplicit
                };

                AbsolutePath tempDirectory = oldDirectory.GetParent(pathTable).Combine(pathTable, "TempDirectory");
                AbsolutePath outputDirectory = oldDirectory.GetParent(pathTable).Combine(pathTable, "OutputDirectory");
                AbsolutePath newDirectory = outputDirectory.Combine(pathTable, "NewDirectory");
                var newDirectoryNested = oldDirectoryNested.Relocate(pathTable, oldDirectory, newDirectory);
                var newDirectoryNestedNested = oldDirectoryNestedNested.Relocate(pathTable, oldDirectory, newDirectory);

                var newDirectories = new AbsolutePath[]
                {
                    newDirectory,
                    newDirectoryNested,
                    newDirectoryNestedNested
                };

                var newDirectoryFileExplicit = oldDirectoryFileExplicit.Relocate(pathTable, oldDirectory, newDirectory);
                var newDirectoryNestedFileExplicit = oldDirectoryNestedFileExplicit.Relocate(pathTable, oldDirectory, newDirectory);
                var newDirectoryNestedNestedFileExplicit = oldDirectoryNestedNestedFileExplicit.Relocate(pathTable, oldDirectory, newDirectory);
                var newDirectoryFileImplicit = oldDirectoryFileImplicit.Relocate(pathTable, oldDirectory, newDirectory);
                var newDirectoryNestedFileImplicit = oldDirectoryNestedFileImplicit.Relocate(pathTable, oldDirectory, newDirectory);
                var newDirectoryNestedNestedFileImplicit = oldDirectoryNestedNestedFileImplicit.Relocate(pathTable, oldDirectory, newDirectory);

                var newExplicitFiles = new AbsolutePath[]
                {
                    newDirectoryFileExplicit,
                    newDirectoryNestedFileExplicit,
                    newDirectoryNestedNestedFileExplicit,
                };

                var newImplicitFiles = new AbsolutePath[]
                {
                    newDirectoryFileImplicit,
                    newDirectoryNestedFileImplicit,
                    newDirectoryNestedNestedFileImplicit,
                };

                var newFiles = newExplicitFiles.Concat(newImplicitFiles).ToArray();

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: callArgument,
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.From(
                        newExplicitFiles.Select(f => FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(f), FileExistence.Required))),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(DirectoryArtifact.CreateWithZeroPartialSealId(outputDirectory)),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(oldDirectory, tempDirectory));

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    enforceAccessPoliciesOnDirectoryCreation: true,
                    unexpectedFileAccessesAreErrors: true,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                foreach (var path in oldDirectories)
                {
                    XAssert.IsFalse(Directory.Exists(path.ToString(pathTable)));
                }

                foreach (var path in oldFiles)
                {
                    XAssert.IsFalse(File.Exists(path.ToString(pathTable)));
                }

                foreach (var path in newDirectories)
                {
                    XAssert.IsTrue(Directory.Exists(path.ToString(pathTable)));
                }

                foreach (var path in newFiles)
                {
                    XAssert.IsTrue(File.Exists(path.ToString(pathTable)));
                }

                var toVerify = newExplicitFiles.Select(p => (p, RequestedAccess.Write, FileAccessStatus.Allowed)).ToArray();
                var toFalsify = newImplicitFiles.Concat(newDirectories).Concat(oldDirectories).Concat(oldFiles).ToArray();

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses.Where(f => f.ExplicitlyReported).ToList(),
                    toVerify,
                    toFalsify);
            }
        }

        [Fact]
        public async Task CallCreateFileWReportAllAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                AbsolutePath createdFile = tempFiles.GetFileName(pathTable, "CreateFile");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallDetouredCreateFileWWrite",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    enforceAccessPoliciesOnDirectoryCreation: true,
                    unexpectedFileAccessesAreErrors: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[] { (createdFile, RequestedAccess.Write, FileAccessStatus.Denied) },
                    new AbsolutePath[0]);
            }
        }

        [Fact]
        public async Task CallOpenFileById()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                // Create Directory\, Directory\fileF.txt
                AbsolutePath directory = tempFiles.GetDirectory(pathTable, "Directory");
                AbsolutePath fileF = directory.Combine(pathTable, "fileF.txt");
                AbsolutePath fileG = directory.Combine(pathTable, "fileG.txt");
                WriteFile(pathTable, fileF, "f");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallOpenFileById",
                    inputFiles: ReadOnlyArray<FileArtifact>.FromWithoutCopy(FileArtifact.CreateSourceFile(fileF)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                        FileArtifactWithAttributes.FromFileArtifact(FileArtifact.CreateOutputFile(fileG), FileExistence.Required)),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: true,
                    enforceAccessPoliciesOnDirectoryCreation: true,
                    unexpectedFileAccessesAreErrors: true,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[] 
                    {
                        (fileF, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (fileG, RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    new AbsolutePath[0]);
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateSymLinkOnFilesWithGrantedAccessAndIgnoreNoTempNoUntracked()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnFiles1.txt",
                    "CreateSymLinkOnFiles2.txt",
                    "CallCreateSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateSymLinkOnFiles1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    pathsToFalsify: new[]
                    {
                        createdInputPaths["CreateSymLinkOnFiles2.txt"]
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateSymLinkOnFilesWithGrantedAccessAndNoIgnoreTempNoUntracked()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                AbsolutePath testDirectoryAbsolutePath = tempFiles.GetDirectory(pathTable, "input");
                AbsolutePath testFilePath = tempFiles.GetFileName(pathTable, testDirectoryAbsolutePath, "CreateSymLinkOnFiles1.txt");
                List<AbsolutePath> testDirList = new List<AbsolutePath>();
                testDirList.Add(testFilePath);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnFiles1.txt",
                    "CreateSymLinkOnFiles2.txt",
                    "CallCreateSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths,
                    additionalTempDirectories: testDirList);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateSymLinkOnFiles1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    pathsToFalsify: new[]
                    {
                        createdInputPaths["CreateSymLinkOnFiles2.txt"]
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateSymLinkOnFilesWithGrantedAccessAndIgnoreTempNoUntracked()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                AbsolutePath testDirectoryAbsolutePath = tempFiles.GetDirectory(pathTable, "input");
                AbsolutePath testFilePath = tempFiles.GetFileName(pathTable, testDirectoryAbsolutePath, "CreateSymLinkOnFiles1.txt");
                List<AbsolutePath> testDirList = new List<AbsolutePath>();
                testDirList.Add(testFilePath);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnFiles1.txt",
                    "CreateSymLinkOnFiles2.txt",
                    "CallCreateSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths,
                    additionalTempDirectories: testDirList);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateSymLinkOnFiles1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    pathsToFalsify: new[]
                    {
                        createdInputPaths["CreateSymLinkOnFiles2.txt"]
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateSymLinkOnFilesWithGrantedAccessAndNoIgnoreNoTempUntracked()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                AbsolutePath testDirectoryAbsolutePath = tempFiles.GetDirectory(pathTable, "input");
                AbsolutePath testFilePath = tempFiles.GetFileName(pathTable, testDirectoryAbsolutePath, "CreateSymLinkOnFiles1.txt");
                List<AbsolutePath> testDirList = new List<AbsolutePath>();
                testDirList.Add(testFilePath);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnFiles1.txt",
                    "CreateSymLinkOnFiles2.txt",
                    "CallCreateSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths,
                    untrackedPaths: testDirList);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateSymLinkOnFiles1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    pathsToFalsify: new[]
                    {
                        createdInputPaths["CreateSymLinkOnFiles2.txt"]
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateSymLinkOnFilesWithGrantedAccessAndIgnoreNoTempUntracked()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                AbsolutePath testDirectoryAbsolutePath = tempFiles.GetDirectory(pathTable, "input");
                AbsolutePath testFilePath = tempFiles.GetFileName(pathTable, testDirectoryAbsolutePath, "CreateSymLinkOnFiles1.txt");
                List<AbsolutePath> testDirList = new List<AbsolutePath>();
                testDirList.Add(testFilePath);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnFiles1.txt",
                    "CreateSymLinkOnFiles2.txt",
                    "CallCreateSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths,
                    untrackedPaths: testDirList);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateSymLinkOnFiles1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    pathsToFalsify: new[]
                    {
                        createdInputPaths["CreateSymLinkOnFiles2.txt"]
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateSymLinkOnFilesWithGrantedAccessAndIgnoreNoTempUntrackedOpaque()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
                AbsolutePath testDirectoryAbsolutePath = tempFiles.GetDirectory(pathTable, "input");
                AbsolutePath testFilePath = AbsolutePath.Create(pathTable, Path.Combine(testDirectoryAbsolutePath.ToString(pathTable), "CreateSymLinkOnFiles1.txt"));
                DirectoryArtifact dirArt = DirectoryArtifact.CreateWithZeroPartialSealId(testFilePath);
                List<DirectoryArtifact> dirArtifactList = new List<DirectoryArtifact>();
                dirArtifactList.Add(dirArt);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnFiles1.txt",
                    "CreateSymLinkOnFiles2.txt",
                    "CallCreateSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.None,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths,
                    outputDirectories: dirArtifactList);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateSymLinkOnFiles1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                    },
                    pathsToFalsify: new[]
                    {
                        createdInputPaths["CreateSymLinkOnFiles2.txt"]
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallCreateSymLinkOnFilesWithNoGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnFiles1.txt",
                    "CreateSymLinkOnFiles2.txt",
                    "CallCreateSymLinkOnFiles",
                    isDirectoryTest: false,

                    // The C++ part will create the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.None,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.None,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[] { (createdInputPaths["CreateSymLinkOnFiles1.txt"], RequestedAccess.Write, FileAccessStatus.Denied) },
                    pathsToFalsify: new[] { createdInputPaths["CreateSymLinkOnFiles2.txt"] });
            }
        }

        [Fact(Skip = "No support for directory junctions as outputs")]
        public async Task CallCreateSymLinkOnDirectoriesWithGrantedAccess()
        {
            // TODO:
            // If output directory junction is marked as an output file, then the post verification that checks for file existence will fail.
            // If output directory junction is marked as an output directory, then there's no write policy for that output directory because BuildXL
            // creates output directories prior to executing the process.
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnDirectories1.dir",
                    "CreateSymLinkOnDirectories2.dir",
                    "CallCreateSymLinkOnDirectories",
                    isDirectoryTest: true,

                    // The C++ part creates the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);
            }
        }

        [Fact(Skip = "No support for directory junctions as outputs")]
        public async Task CallCreateSymLinkOnDirectoriesWithNoGrantedAccess()
        {
            // TODO:
            // If output directory junction is marked as an output file, then the post verification that checks for file existence will fail.
            // If output directory junction is marked as an output directory, then there's no write policy for that output directory because BuildXL
            // creates output directories prior to executing the process.
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymLinkOnDirectories1.dir",
                    "CreateSymLinkOnDirectories2.dir",
                    "CallCreateSymLinkOnDirectories",
                    isDirectoryTest: true,

                    // The C++ part creates the symlink.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(2, 0);

                VerifyAccessDenied(context, result);
            }
        }

        [Fact]
        public async Task CreateFileWithZeroAccessOnDirectory()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string testDirPath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory("input");

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("CallCreateFileWithZeroAccessOnDirectory");

                var environmentVariables = new List<EnvironmentVariable>();

                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);
                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[] { executableFileArtifact }),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                    tags: ReadOnlyArray<StringId>.Empty,

                    // We expect the CreateFile call to fail, but with no monitoring error logged.
                    successExitCodes: ReadOnlyArray<int>.FromWithoutCopy(new[] { 5 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = true;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
            }
        }

        [Fact]
        public Task TimestampsNormalize()
        {
            return Timestamps(normalize: true);
        }

        [Fact]
        public Task TimestampsNoNormalize()
        {
            return Timestamps(normalize: false);
        }

        public async Task Timestamps(bool normalize)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                FileArtifact inputArtifact = WriteFile(pathTable, tempFiles.GetFileName(pathTable, workingDirectoryAbsolutePath, "input"), "Useful data");
                FileArtifact outputToRewrite = WriteFile(pathTable, tempFiles.GetFileName(pathTable, workingDirectoryAbsolutePath, "rewrittenOutput"), "Useful data");
                FileArtifact outputAfterRewrite = outputToRewrite.CreateNextWrittenVersion();

                string inputSubdirectoryPath = Path.Combine(workingDirectory, "subdir");
                tempFiles.GetDirectory("subdir");
                var inputSubdirectory = DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(pathTable, inputSubdirectoryPath));

                // subdirInput1 and subdirInput2 are brought in via a directory scope (inputSubdirectory) so as to exercise extension of the
                // Read+Report policy of the directory to the wildcard matches (a 'truncated' policy search cursor; see PolicySearch.h
                FileArtifact subdirInput1 = WriteFile(pathTable, tempFiles.GetFileName(pathTable, AbsolutePath.Create(pathTable, inputSubdirectoryPath), "input1"), "Useful data");
                FileArtifact subdirInput2 = WriteFile(pathTable, tempFiles.GetFileName(pathTable, AbsolutePath.Create(pathTable, inputSubdirectoryPath), "input2"), "Useful data");
                FileArtifact subdirRewrittenOutput1BeforeWrite = WriteFile(pathTable, tempFiles.GetFileName(pathTable, AbsolutePath.Create(pathTable, inputSubdirectoryPath), "rewrittenOutput1"), "Useful data");
                FileArtifact subdirRewrittenOutput1AfterWrite = subdirRewrittenOutput1BeforeWrite.CreateNextWrittenVersion();
                FileArtifact subdirRewrittenOutput2BeforeWrite = WriteFile(pathTable, tempFiles.GetFileName(pathTable, AbsolutePath.Create(pathTable, inputSubdirectoryPath), "rewrittenOutput2"), "Useful data");
                FileArtifact subdirRewrittenOutput2AfterWrite = subdirRewrittenOutput2BeforeWrite.CreateNextWrittenVersion();

                // Create artifacts to be contained in a shared opaque

                string sharedOpaqueSubdirectoryPath = Path.Combine(workingDirectory, "sharedOpaque");
                tempFiles.GetDirectory("sharedOpaque");
                tempFiles.GetDirectory("sharedOpaque\\subdir");
                tempFiles.GetDirectory("sharedOpaque\\subdir\\nested");
                tempFiles.GetDirectory("sharedOpaque\\anothersubdir");
                tempFiles.GetDirectory("sharedOpaque\\anothersubdir\\nested");
                AbsolutePath sharedOpaqueSubdirectoryAbsolutePath = AbsolutePath.Create(pathTable, sharedOpaqueSubdirectoryPath);
                var sharedOpaqueSubdirectory = new DirectoryArtifact(AbsolutePath.Create(pathTable, sharedOpaqueSubdirectoryPath), partialSealId: 1, isSharedOpaque: true);

                // This is a directory with one source file to become a source seal under a shared opaque
                string sourceSealInsharedOpaqueSubdirectoryPath = Path.Combine(sharedOpaqueSubdirectoryPath, "sourceSealInSharedOpaque");
                tempFiles.GetDirectory("sharedOpaque\\sourceSealInSharedOpaque");
                var sourceSealSubdirectory = DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(pathTable, sourceSealInsharedOpaqueSubdirectoryPath));
                AbsolutePath sourceSealInsharedOpaqueSubdirectoryAbsolutePath = AbsolutePath.Create(pathTable, sourceSealInsharedOpaqueSubdirectoryPath);
                FileArtifact sourceInSourceSealInSharedOpaque = WriteFile(pathTable, tempFiles.GetFileName(pathTable, sourceSealInsharedOpaqueSubdirectoryAbsolutePath, "inputInSourceSealInSharedOpaque"), "Useful data");
                // A static input in a shared opaque (that is, explicitly declared as an input even if it is under a shared opaque)
                FileArtifact staticInputInSharedOpaque = WriteFile(pathTable,
                    tempFiles.GetFileName(pathTable, sharedOpaqueSubdirectoryAbsolutePath.Combine(pathTable, "subdir").Combine(pathTable, "nested"), "staticInputInSharedOpaque"), "Useful data");
                // Dynamic inputs in a shared opaque (that is, not explicitly declared as inputs, the shared opaque serves as the artifact that the pip depends on)
                FileArtifact dynamicInputInSharedOpaque1 = WriteFile(pathTable,
                    tempFiles.GetFileName(pathTable, sharedOpaqueSubdirectoryAbsolutePath.Combine(pathTable, "anothersubdir").Combine(pathTable, "nested"), "dynamicInputInSharedOpaque1"), "Useful data");
                FileArtifact dynamicInputInSharedOpaque2 = WriteFile(pathTable,
                    tempFiles.GetFileName(pathTable, sharedOpaqueSubdirectoryAbsolutePath.Combine(pathTable, "anothersubdir"), "dynamicInputInSharedOpaque2"), "Useful data");
                FileArtifact dynamicInputInSharedOpaque3 = WriteFile(pathTable,
                    tempFiles.GetFileName(pathTable, sharedOpaqueSubdirectoryAbsolutePath, "dynamicInputInSharedOpaque3"), "Useful data");
                // A static rewritten output in a shared opaque
                FileArtifact outputInSharedOpaqueToRewrite = WriteFile(pathTable, tempFiles.GetFileName(pathTable, sharedOpaqueSubdirectoryAbsolutePath, "rewrittenOutputInSharedOpaque"), "Useful data");
                FileArtifact outputInSharedOpaqueAfterRewrite = outputInSharedOpaqueToRewrite.CreateNextWrittenVersion();

                var arguments = new PipDataBuilder(pathTable.StringTable);
                if (normalize)
                {
                    arguments.Add("TimestampsNormalize");
                }
                else
                {
                    arguments.Add("TimestampsNoNormalize");
                }

                var environmentVariables = new List<EnvironmentVariable>();

                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);
                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                        new[]
                        {
                            executableFileArtifact,
                            inputArtifact,
                            outputToRewrite,
                            subdirRewrittenOutput1BeforeWrite,
                            subdirRewrittenOutput2BeforeWrite,
                            staticInputInSharedOpaque, // the dynamic input is explicitly not included in this list
                            outputInSharedOpaqueToRewrite,
                        }),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                        new[]
                        {
                            outputAfterRewrite.WithAttributes(),
                            subdirRewrittenOutput1AfterWrite.WithAttributes(),
                            subdirRewrittenOutput2AfterWrite.WithAttributes(),
                            outputInSharedOpaqueAfterRewrite.WithAttributes()
                        }),
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new[] {inputSubdirectory, sourceSealSubdirectory, sharedOpaqueSubdirectory}),
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new[] { new DirectoryArtifact(sharedOpaqueSubdirectory.Path, sharedOpaqueSubdirectory.PartialSealId + 1, isSharedOpaque: true) }),
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true,
                    NormalizeReadTimestamps = normalize
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = true;
                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip,
                    // There is no file content manager available, we need to manually tell which files belong to the shared opaque
                    directoryArtifactContext: new TestDirectoryArtifactContext(new[] { dynamicInputInSharedOpaque1, dynamicInputInSharedOpaque2, dynamicInputInSharedOpaque3 }));
            }
        }

        [Fact]
        public async Task TestUseLargeNtClosePreallocatedList()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = CmdHelper.CmdX64;

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                FileArtifact inputArtifact = WriteFile(pathTable, tempFiles.GetFileName(pathTable, workingDirectoryAbsolutePath, "input"), "Useful data");
                FileArtifact outputToRewrite = WriteFile(pathTable, tempFiles.GetFileName(pathTable, workingDirectoryAbsolutePath, "rewrittenOutput"), "Useful data");

                FileArtifact outputAfterRewrite = outputToRewrite.CreateNextWrittenVersion();

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("echo");
                arguments.Add("bar");

                var environmentVariables = new List<EnvironmentVariable>();

                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);
                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[]
                                                                              {
                                                                                  executableFileArtifact,
                                                                              }),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true,
                    UseLargeNtClosePreallocatedList = true
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = true;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
            }
        }

        [Fact]
        public async Task TestProbeForDirectory()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                AbsolutePath inputFile = tempFiles.GetFileName(pathTable, "input.txt\\");
                FileArtifact inputArtifact = WriteFile(pathTable, inputFile, "Some content");
                var environmentVariables = new List<EnvironmentVariable>();
                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallProbeForDirectory",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyExitCode(context, result, 267); // Invalid directory name

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (inputFile, RequestedAccess.Probe, FileAccessStatus.Allowed),
                    });

                SetExpectedFailures(1, 0);
            }
        }

        [Fact]
        public async Task TestGetFileAttributeOnFileWithPipeChar()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallFileAttributeOnFileWithPipeChar",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNoFileAccesses(result);
            }
        }

        [Fact]
        public async Task TestGetFileAttributeQuestion()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallGetAttributeQuestion",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNoFileAccesses(result);
            }
        }

        [Fact]
        public async Task TestGetAttributeNonExistentDeclared()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);

                string inputFile = Path.Combine(workingDirectory, "GetAttributeNonExistent.txt");
                FileArtifact inputArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, inputFile));
                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallGetAttributeNonExistent",
                    inputFiles: ReadOnlyArray<FileArtifact>.FromWithoutCopy(inputArtifact),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (inputArtifact.Path, RequestedAccess.Probe, FileAccessStatus.Allowed)
                    });
            }
        }

        [Fact]
        public async Task TestNetworkPathNotDeclared()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);

                string inputFile = @"\\daddev\office\16.0\7923.1000\shadow\store\X64\Debug\airspace\x-none\inc\airspace.etw.man";
                FileArtifact inputArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, inputFile));

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallAccessNetworkDrive",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    disableDetours: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                bool inputNotFound = false;

                foreach (ReportedFileAccess raf in result.AllReportedFileAccesses)
                {
                    // 3 - FileNotFound. Detours allow file accesses for non existing files.
                    // This could happen if the network acts weird and the file is not accessible through the network.
                    if (raf.GetPath(pathTable) == @"\\daddev\office\16.0\7923.1000\shadow\store\X64\Debug\airspace\x-none\inc\airspace.etw.man"
                        && raf.Error == 3)
                    {
                        inputNotFound = true;
                    }
                }

                if (!inputNotFound)
                {
                    VerifyNoObservedFileAccessesAndUnexpectedFileAccesses(
                        result,
                        new[]
                        {
                        inputFile
                        },
                        pathTable);
                }
            }
        }

        [Fact]
        public async Task TestInvalidFileNameNotDeclared()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);

                // Input file on the native side is: @"@:\office\16.0\7923.1000\shadow\store\X64\Debug\airspace\x-none\inc\airspace.etw.man";
                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallAccessInvalidFile",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    disableDetours: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                SetExpectedFailures(1, 1);

                VerifyExitCode(context, result, 3);
            }
        }

        [Fact]
        public async Task TestNetworkPathDeclared()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);

                string inputFile = @"\\daddev\office\16.0\7923.1000\shadow\store\X64\Debug\airspace\x-none\inc\airspace.etw.man";
                FileArtifact inputArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, inputFile));

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallAccessNetworkDrive",
                    inputFiles: ReadOnlyArray<FileArtifact>.FromWithoutCopy(inputArtifact),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    disableDetours: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNoFileAccesses(result);
            }
        }

        [Fact]
        public async Task TestDisableDetours()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);

                string inputFile = Path.Combine(workingDirectory, "CallGetAttributeNonExistent.txt");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallGetAttributeNonExistent",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    disableDetours: true,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNoFileAccesses(result);
            }
        }

        [Fact]
        public async Task TestGetAttributeNonExistentNotDeclared()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);

                string inputFile = Path.Combine(workingDirectory, "GetAttributeNonExistent.txt");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallGetAttributeNonExistent",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                XAssert.AreEqual(inputFile, result.ObservedFileAccesses[0].Path.ToString(pathTable));
                XAssert.AreEqual(1, result.ObservedFileAccesses.Length);
                XAssert.AreEqual(0, (result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted == null) ?
                    0 :
                    result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted.Count);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestGetAttributeNonExistentUnderDeclaredDirectoryDependency(bool declareNonExistentFile)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                string depDirFilePath = Path.Combine(workingDirectory, "input");
                tempFiles.GetDirectory("input");

                string inputFile = Path.Combine(workingDirectory, "input", "GetAttributeNonExistent.txt");
                FileArtifact inputArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, inputFile));

                AbsolutePath depDirAbsolutePath = AbsolutePath.Create(pathTable, depDirFilePath);
                DirectoryArtifact depDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(depDirAbsolutePath);

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallGetAttributeNonExistentInDepDirectory",
                    inputFiles: !declareNonExistentFile ? ReadOnlyArray<FileArtifact>.Empty : ReadOnlyArray<FileArtifact>.FromWithoutCopy(inputArtifact),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(depDirArtifact),
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                if (!declareNonExistentFile)
                {
                    XAssert.AreEqual(1, result.ObservedFileAccesses.Length);
                    XAssert.AreEqual(
                        0,
                        (result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted == null)
                        ? 0
                        : result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted.Count);
                }
                else
                {
                    VerifyNoFileAccesses(result);
                }

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (inputArtifact.Path, RequestedAccess.Probe, FileAccessStatus.Allowed)
                    });
            }
        }

        [Fact]
        public async Task TestUseExtraThreadToDrainNtClose()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = CmdHelper.CmdX64;

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                FileArtifact inputArtifact = WriteFile(pathTable, tempFiles.GetFileName(pathTable, workingDirectoryAbsolutePath, "input"), "Useful data");
                FileArtifact outputToRewrite = WriteFile(pathTable, tempFiles.GetFileName(pathTable, workingDirectoryAbsolutePath, "rewrittenOutput"), "Useful data");

                FileArtifact outputAfterRewrite = outputToRewrite.CreateNextWrittenVersion();

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("echo");
                arguments.Add("bar");

                var environmentVariables = new List<EnvironmentVariable>();

                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);
                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[]
                                                                              {
                                                                                  executableFileArtifact,
                                                                              }),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true,
                    UseExtraThreadToDrainNtClose = false
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = true;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
            }
        }

        [Fact]
        public async Task TestUseLargeNtClosePreallocatedListAndUseExtraThreadToDrainNtClose()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = CmdHelper.CmdX64;

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                FileArtifact inputArtifact = WriteFile(pathTable, tempFiles.GetFileName(pathTable, workingDirectoryAbsolutePath, "input"), "Useful data");
                FileArtifact outputToRewrite = WriteFile(pathTable, tempFiles.GetFileName(pathTable, workingDirectoryAbsolutePath, "rewrittenOutput"), "Useful data");
                FileArtifact outputAfterRewrite = outputToRewrite.CreateNextWrittenVersion();

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("echo");
                arguments.Add("bar");

                var environmentVariables = new List<EnvironmentVariable>();

                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);
                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[]
                                                                              {
                                                                                  executableFileArtifact,
                                                                              }),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true,
                    UseExtraThreadToDrainNtClose = false,
                    UseLargeNtClosePreallocatedList = true
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = true;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
            }
        }

        /// <summary>
        /// Tests that short names of files cannot be discovered with e.g. <c>GetShortPathName</c> or <c>FindFirstFile</c>.
        /// </summary>
        [Fact]
        public async Task CmdMove()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = tempFiles.RootDirectory;
                string envVarName = "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty);

                // Create the input file.
                AbsolutePath sourceAbsolutePath = tempFiles.GetFileName(pathTable, "a1.txt");
                string sourceExpandedPath = sourceAbsolutePath.ToString(pathTable);

                if (File.Exists(sourceExpandedPath))
                {
                    ExceptionUtilities.HandleRecoverableIOException(() => File.Delete(sourceExpandedPath), exception => { });
                }

                XAssert.IsFalse(File.Exists(sourceExpandedPath));

                using (FileStream fs = File.Create(sourceExpandedPath))
                {
                    Random rnd = new Random();
                    byte[] b = new byte[10];
                    rnd.NextBytes(b);

                    // Add some information to the file.
                    await fs.WriteAsync(b, 0, b.Length);
                    fs.Close();
                }

                // Set up target path.
                AbsolutePath targetAbsolutePath = tempFiles.GetFileName(pathTable, "a2.txt");
                string targetExpandedPath = targetAbsolutePath.ToString(pathTable);

                if (File.Exists(targetExpandedPath))
                {
                    ExceptionUtilities.HandleRecoverableIOException(() => File.Delete(targetExpandedPath), exception => { });
                }

                XAssert.IsFalse(File.Exists(targetExpandedPath));

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("move");
                    arguments.Add(sourceAbsolutePath);
                    arguments.Add(targetAbsolutePath);
                }

                List<AbsolutePath> untrackedPaths = new List<AbsolutePath>(CmdHelper.GetCmdDependencies(context.PathTable)) { sourceAbsolutePath };

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(
                        new EnvironmentVariable(
                            StringId.Create(context.PathTable.StringTable, envVarName),
                            PipDataBuilder.CreatePipData(
                                context.PathTable.StringTable,
                                " ",
                                PipDataFragmentEscaping.CRuntimeArgumentRules,
                                "Success"))),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact, FileArtifact.CreateSourceFile(sourceAbsolutePath)),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
                SetExpectedFailures(1, 0);

                VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
                VerifyExitCode(context, result, 1);

                XAssert.IsFalse(File.Exists(targetExpandedPath));
            }
        }

        /// <summary>
        /// This test makes sure we are adding AllowRead access to the directory that contains the current executable. Negative case.
        /// </summary>
        [Fact]
        public async Task TestAccessToExecutableTestNoAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string exeAssembly = AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly());
                string outsidePath = Path.Combine(Path.GetDirectoryName(exeAssembly), "TestProcess", "Win", "Test.BuildXL.Executables.TestProcess.exe");

                XAssert.IsTrue(AbsolutePath.TryCreate(pathTable, outsidePath, out AbsolutePath outsideAbsPath));

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("dir ");
                    arguments.Add(outsideAbsPath);
                }

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.Empty,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[] { executableFileArtifact }),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);
                AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);

                VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
                VerifyExitCode(context, result, 1);
            }
        }

        /// <summary>
        /// This test makes sure we are adding AllowRead access to the directory that contains the current executable.
        /// </summary>
        [Fact]
        public async Task TestAccessToExecutableTest()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                AbsolutePath exePath;
                string localExePath = string.Empty;
                try
                {
                    localExePath = new System.Uri(System.Reflection.Assembly.GetEntryAssembly().CodeBase).LocalPath;
                }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
                catch
                {
                    localExePath = string.Empty;
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                XAssert.IsTrue(!string.IsNullOrEmpty(localExePath));
                bool gotten = AbsolutePath.TryCreate(pathTable, localExePath, out exePath);
                XAssert.IsTrue(gotten);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("dir ");
                    arguments.Add(exePath);
                }

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);

                var environmentVariables = new List<EnvironmentVariable>();
                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[] { executableFileArtifact }),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    binDirectory: exePath,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);
            }
        }

        /// <summary>
        /// Tests that short names of files cannot be discovered with e.g. <c>GetShortPathName</c> or <c>FindFirstFile</c>.
        /// </summary>
        [Fact]
        public async Task ShortNames()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
                Contract.Assume(currentCodeFolder != null);

                string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");

                XAssert.IsTrue(File.Exists(executable));
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));

                string workingDirectory = tempFiles.RootDirectory;
                Contract.Assume(workingDirectory != null);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(pathTable, workingDirectory);
                var combinedDir = Path.Combine(workingDirectory, "directoryWithAVeryLongName");
                tempFiles.GetDirectory(combinedDir);
                FileArtifact inputArtifact = WriteFile(pathTable, tempFiles.GetFileName(pathTable, AbsolutePath.Create(pathTable, combinedDir), "fileWithAVeryLongName"), "Useful data");

                var arguments = new PipDataBuilder(pathTable.StringTable);
                arguments.Add("ShortNames");

                var environmentVariables = new List<EnvironmentVariable>();

                var untrackedPaths = CmdHelper.GetCmdDependencies(pathTable);
                var untrackedScopes = CmdHelper.GetCmdDependencyScopes(pathTable);
                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.From(environmentVariables),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(pathTable),
                    null,
                    null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[]
                                                                              {
                                                                                  executableFileArtifact,
                                                                                  inputArtifact,
                                                                              }),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true,
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = true;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
            }
        }

        [Fact]
        public async Task CallDetouredAccessesChainOfJunctions()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceOfSymlink = tempFiles.GetDirectory(pathTable, "SourceOfSymLink.link");
                var intermediateSymlink = tempFiles.GetDirectory(pathTable, "IntermediateSymLink.link");
                var intermediateSymlink1 = tempFiles.GetDirectory(pathTable, "IntermediateSymLink1.link");

                EstablishJunction(sourceOfSymlink.ToString(pathTable), intermediateSymlink.ToString(pathTable));
                EstablishJunction(intermediateSymlink.ToString(pathTable), intermediateSymlink1.ToString(pathTable));

                var targetFileStr = Path.Combine(intermediateSymlink1.ToString(pathTable), "Target.txt");
                var targetFile = AbsolutePath.Create(pathTable, targetFileStr);
                WriteFile(pathTable, targetFile, "target content");

                var srcFileStr = Path.Combine(sourceOfSymlink.ToString(pathTable), "Target.txt");
                var srcFile = AbsolutePath.Create(pathTable, srcFileStr);

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallAccessOnChainOfJunctions",
                    inputFiles:
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(srcFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (srcFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredAccessesCreateSymlinkForQBuild()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateSymbolicLinkTest1.txt",
                    "CreateSymbolicLinkTest2.txt",
                    "CallDetouredAccessesCreateSymlinkForQBuild",
                    isDirectoryTest: false,

                    // Setup doesn't create symlink, but the C++ method CallDetouredFileCreateWithSymlink does.
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: true,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsOutput,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString,
                    isQuickBuildIntegrated: true);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                   context,
                   result.AllReportedFileAccesses,
                   new[]
                   {
                        (createdInputPaths["CreateSymbolicLinkTest1.txt"], RequestedAccess.Write, FileAccessStatus.Allowed)
                   },
                   pathsToFalsify: new[]
                   {
                        createdInputPaths["CreateSymbolicLinkTest2.txt"]
                   });
            }
        }

        [Fact]
        public async Task CallAccessJunctionOnDirectoriesWithGrantedAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "AccessSymLinkOnDirectories1.dir",
                    "AccessJunctionOnDirectories2.dir",
                    "CallAccessSymLinkOnDirectories",
                    isDirectoryTest: true,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: true,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsDependency,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string junctionPath = createdInputPaths["AccessSymLinkOnDirectories1.dir"].ToString(pathTable);
                string targetPath = createdInputPaths["AccessJunctionOnDirectories2.dir"].ToString(pathTable);

                EstablishJunction(junctionPath, targetPath);

                string errorString;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // TODO: Currently BuildXL does not handle directory junction, so the access via AccessSymLinkOnDirectories2.dir is not recognized.
                        // (createdInputPaths[Path.Combine("AccessSymLinkOnDirectories2.dir", ExtraFileNameInDirectory)], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (
                            createdInputPaths[Path.Combine("AccessSymLinkOnDirectories1.dir", ExtraFileNameInDirectory)],
                            RequestedAccess.Read,
                            FileAccessStatus.Allowed)
                    });
            }
        }

        [Fact]
        public async Task CallAccessJunctionOnDirectoriesWithNoGrantedAccessNoTranslation()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "AccessSymLinkOnDirectories1.dir",
                    "AccessJunctionOnDirectories2.dir",
                    "CallAccessSymLinkOnDirectories",
                    isDirectoryTest: true,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: true,
                    addFirstFileKind: AddFileOrDirectoryKinds.None,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.AsDependency,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                string junctionPath = createdInputPaths["AccessSymLinkOnDirectories1.dir"].ToString(pathTable);
                string targetPath = createdInputPaths["AccessJunctionOnDirectories2.dir"].ToString(pathTable);

                EstablishJunction(junctionPath, targetPath);

                string errorString;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // TODO: Currently BuildXL does not handle directory junction, so the access via AccessSymLinkOnDirectories2.dir is not recognized.
                        // (createdInputPaths[Path.Combine("AccessSymLinkOnDirectories2.dir", ExtraFileNameInDirectory)], RequestedAccess.Read, FileAccessStatus.Allowed),
                       (
                            createdInputPaths[Path.Combine("AccessSymLinkOnDirectories1.dir", ExtraFileNameInDirectory)],
                            RequestedAccess.Read,
                            FileAccessStatus.Denied)
                    });
            }
        }

        [Fact]
        public async Task CallAccessJunctionOnDirectoriesWithGrantedAccessWithTranslation()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                // CAUTION: Unlike other tests, this test swaps the order of directories.
                // In this test, the native is accessing AccessSymLinkOnDirectories1.dir\foo, such that
                // there is a junction from AccessJunctionOnDirectories2.dir to AccessSymLinkOnDirectories1.dir,
                // and the dependency is specified in terms of AccessJunctionOnDirectories2.dir.
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "AccessJunctionOnDirectories2.dir",
                    "AccessSymLinkOnDirectories1.dir",
                    "CallAccessSymLinkOnDirectories",
                    isDirectoryTest: true,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: true,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsDependency,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.None,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                var targetPath = createdInputPaths["AccessSymLinkOnDirectories1.dir"];
                var junctionPath = createdInputPaths["AccessJunctionOnDirectories2.dir"];
                string targetPathStr = targetPath.ToString(pathTable);
                string junctionPathStr = junctionPath.ToString(pathTable);

                EstablishJunction(junctionPathStr, targetPathStr);

                string errorString;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString,
                    directoriesToTranslate:
                        new List<TranslateDirectoryData>
                        {
                            new TranslateDirectoryData(targetPathStr + @"\<" + junctionPathStr + @"\", targetPath, junctionPath)
                        });

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // TODO: Currently BuildXL does not handle directory junction, so the access via AccessSymLinkOnDirectories2.dir is not recognized.
                        // (createdInputPaths[Path.Combine("AccessSymLinkOnDirectories2.dir", ExtraFileNameInDirectory)], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (
                            createdInputPaths[Path.Combine("AccessJunctionOnDirectories2.dir", ExtraFileNameInDirectory)],
                            RequestedAccess.Read,
                            FileAccessStatus.Allowed)
                    });
            }
        }

        [Fact]
        public async Task CallAccessJunctionOnDirectoriesWithGrantedAccessWithTranslationGetLongestPath()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                // CAUTION: Unlike other tests, this test swaps the order of directories.
                // In this test, the native is accessing AccessSymLinkOnDirectories1.dir\foo, such that
                // there is a junction from AccessJunctionOnDirectories2.dir to AccessSymLinkOnDirectories1.dir,
                // and the dependency is specified in terms of AccessJunctionOnDirectories2.dir.
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "AccessJunctionOnDirectories2.dir",
                    "AccessSymLinkOnDirectories1.dir",
                    "CallAccessSymLinkOnDirectories",
                    isDirectoryTest: true,
                    createSymlink: false,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: true,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsDependency,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.None,
                    makeSecondUntracked: false,
                    createdInputPaths: createdInputPaths);

                var targetPath = createdInputPaths["AccessSymLinkOnDirectories1.dir"];
                var junctionPath = createdInputPaths["AccessJunctionOnDirectories2.dir"];
                string targetPathStr = targetPath.ToString(pathTable);
                string junctionPathStr = junctionPath.ToString(pathTable);

                EstablishJunction(junctionPathStr, targetPathStr);

                string errorString;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: pip,
                    errorString: out errorString,
                    directoriesToTranslate: new List<TranslateDirectoryData>
                                            {
                                                new TranslateDirectoryData(
                                                    targetPathStr.Substring(0, targetPathStr.Length - 4) + @"\<" + junctionPathStr + @"\",
                                                    AbsolutePath.Create(pathTable, targetPathStr.Substring(0, targetPathStr.Length - 4)),
                                                    junctionPath),
                                                new TranslateDirectoryData(targetPathStr + @"\<" + junctionPathStr + @"\", targetPath, junctionPath),
                                                new TranslateDirectoryData(
                                                    targetPathStr.Substring(0, targetPathStr.Length - 3) + @"\<" + junctionPathStr + @"\",
                                                    AbsolutePath.Create(pathTable, targetPathStr.Substring(0, targetPathStr.Length - 3)),
                                                    junctionPath),
                                            });

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // TODO: Currently BuildXL does not handle directory junction, so the access via AccessSymLinkOnDirectories2.dir is not recognized.
                        // (createdInputPaths[Path.Combine("AccessSymLinkOnDirectories2.dir", ExtraFileNameInDirectory)], RequestedAccess.Read, FileAccessStatus.Allowed),
                        (
                            createdInputPaths[Path.Combine("AccessJunctionOnDirectories2.dir", ExtraFileNameInDirectory)],
                            RequestedAccess.Read,
                            FileAccessStatus.Allowed)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredFileCreateThatAccessesChainOfSymlinks()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceOfSymlink = tempFiles.GetFileName(pathTable, "SourceOfSymLink.link");
                var intermediateSymlink = tempFiles.GetFileName(pathTable, "IntermediateSymLink.link");
                var targetFile = tempFiles.GetFileName(pathTable, "Target.txt");
                WriteFile(pathTable, targetFile, "target content");

                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(intermediateSymlink.ToString(pathTable), targetFile.ToString(pathTable), true));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceOfSymlink.ToString(pathTable), intermediateSymlink.ToString(pathTable), true));

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallDetouredFileCreateThatAccessesChainOfSymlinks",
                    inputFiles:
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(sourceOfSymlink),
                            FileArtifact.CreateSourceFile(intermediateSymlink),
                            FileArtifact.CreateSourceFile(targetFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (intermediateSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task CallDetouredFileCreateThatCopiesChainOfSymlinks(bool followChainOfSymlinks)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceOfSymlink = tempFiles.GetFileName(pathTable, "SourceOfSymLink.link");
                var intermediateSymlink = tempFiles.GetFileName(pathTable, "IntermediateSymLink.link");
                var targetFile = tempFiles.GetFileName(pathTable, "Target.txt");
                WriteFile(pathTable, targetFile, "target content");

                var copiedFile = tempFiles.GetFileName(pathTable, "CopiedFile.txt");

                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(intermediateSymlink.ToString(pathTable), targetFile.ToString(pathTable), true));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceOfSymlink.ToString(pathTable), intermediateSymlink.ToString(pathTable), true));

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: followChainOfSymlinks
                    ? "CallDetouredCopyFileFollowingChainOfSymlinks"
                    : "CallDetouredCopyFileNotFollowingChainOfSymlinks",
                    inputFiles:
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(sourceOfSymlink),
                            FileArtifact.CreateSourceFile(intermediateSymlink),
                            FileArtifact.CreateSourceFile(targetFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                        FileArtifactWithAttributes.Create(
                            FileArtifact.CreateOutputFile(copiedFile), FileExistence.Required)),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    ignoreNonCreateFileReparsePoints: false,
                    monitorZwCreateOpenQueryFile: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                if (!followChainOfSymlinks && IsNotEnoughPrivilegesError(result))
                {
                    // When followChainOfSymlinks is false, this test calls CopyFileExW with COPY_FILE_COPY_SYMLINK.
                    // With this flag, CopyFileExW essentially creates a symlink that points to the same target
                    // as the source of copy file. However, the symlink creation is not via CreateSymbolicLink, and
                    // thus SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE cannot be specified.
                    return;
                }

                VerifyNormalSuccess(context, result);

                XAssert.IsTrue(File.Exists(copiedFile.ToString(pathTable)));

                var toVerify = new List<(AbsolutePath, RequestedAccess, FileAccessStatus)>
                {
                    (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                    (copiedFile, RequestedAccess.Write, FileAccessStatus.Allowed)
                };

                var toVerifyOrFalsify = new List<(AbsolutePath, RequestedAccess, FileAccessStatus)>
                {
                    (intermediateSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                    (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                };

                if (followChainOfSymlinks)
                {
                    toVerify.AddRange(toVerifyOrFalsify);
                }

                var toFalsify = new List<(AbsolutePath absolutePath, RequestedAccess requestedAccess, FileAccessStatus fileAccessStatus)>();
                if (!followChainOfSymlinks)
                {
                    toFalsify.AddRange(toVerifyOrFalsify);
                }

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    toVerify.ToArray(),
                    toFalsify.Select(a => a.absolutePath).ToArray());
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredFileCreateThatAccessesChainOfSymlinksFailDueToNoAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceOfSymlink = tempFiles.GetFileName(pathTable, "SourceOfSymLink.link");
                var intermediateSymlink = tempFiles.GetFileName(pathTable, "IntermediateSymLink.link");
                var targetFile = tempFiles.GetFileName(pathTable, "Target.txt");
                WriteFile(pathTable, targetFile, "target content");

                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(intermediateSymlink.ToString(pathTable), targetFile.ToString(pathTable), true));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceOfSymlink.ToString(pathTable), intermediateSymlink.ToString(pathTable), true));

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallDetouredFileCreateThatAccessesChainOfSymlinks",
                    inputFiles:

                    // Intermediate symlink is not specified as an input.
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(sourceOfSymlink),
                            FileArtifact.CreateSourceFile(targetFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (intermediateSymlink, RequestedAccess.Read, FileAccessStatus.Denied),
                        (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredNtCreateFileThatAccessesChainOfSymlinks()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceOfSymlink = tempFiles.GetFileName(pathTable, "SourceOfSymLink.link");
                var intermediateSymlink = tempFiles.GetFileName(pathTable, "IntermediateSymLink.link");
                var targetFile = tempFiles.GetFileName(pathTable, "Target.txt");
                WriteFile(pathTable, targetFile, "target content");

                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(intermediateSymlink.ToString(pathTable), targetFile.ToString(pathTable), true));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceOfSymlink.ToString(pathTable), intermediateSymlink.ToString(pathTable), true));

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallDetouredNtCreateFileThatAccessesChainOfSymlinks",
                    inputFiles:
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(sourceOfSymlink),
                            FileArtifact.CreateSourceFile(intermediateSymlink),
                            FileArtifact.CreateSourceFile(targetFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (intermediateSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredNtCreateFileThatAccessesChainOfSymlinksFailDueToNoAccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceOfSymlink = tempFiles.GetFileName(pathTable, "SourceOfSymLink.link");
                var intermediateSymlink = tempFiles.GetFileName(pathTable, "IntermediateSymLink.link");
                var targetFile = tempFiles.GetFileName(pathTable, "Target.txt");
                WriteFile(pathTable, targetFile, "target content");

                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(intermediateSymlink.ToString(pathTable), targetFile.ToString(pathTable), true));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceOfSymlink.ToString(pathTable), intermediateSymlink.ToString(pathTable), true));

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallDetouredNtCreateFileThatAccessesChainOfSymlinks",
                    inputFiles:

                        // Intermediate symlink is not specified as an input.
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(sourceOfSymlink),
                            FileArtifact.CreateSourceFile(targetFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                SetExpectedFailures(1, 0);

                VerifyAccessDenied(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (intermediateSymlink, RequestedAccess.Read, FileAccessStatus.Denied),
                        (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallAccessNestedSiblingSymLinkOnFiles()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var sourceDir = tempFiles.GetDirectory(pathTable, @"imports\x64");
                var sourceOfSymlink = tempFiles.GetFileName(pathTable, sourceDir, "symlink.imports.link");

                var intermediateDir = tempFiles.GetDirectory(pathTable, @"icache\x64");
                var intermediateSymlink = tempFiles.GetFileName(pathTable, intermediateDir, "symlink.icache.link");

                var targetDir = tempFiles.GetDirectory(pathTable, @"targets\x64");
                var targetFile = tempFiles.GetFileName(pathTable, targetDir, "hello.txt");

                WriteFile(pathTable, targetFile, "aaa");

                // Force creation of relative symlinks.
                var currentDirectory = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(intermediateDir.ToString(pathTable));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink("symlink.icache.link", @"..\..\targets\x64\hello.txt", true));

                Directory.SetCurrentDirectory(sourceDir.ToString(pathTable));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink("symlink.imports.link", @"..\..\icache\x64\symlink.icache.link", true));

                Directory.SetCurrentDirectory(currentDirectory);

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallAccessNestedSiblingSymLinkOnFiles",
                    inputFiles:
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(sourceOfSymlink),
                            FileArtifact.CreateSourceFile(intermediateSymlink),
                            FileArtifact.CreateSourceFile(targetFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString;

                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (intermediateSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true,  @"..\..\..\targets\x64\hello.txt")]
        [InlineData(false, @"..\..\..\targets\x64\hello.txt")]
        [InlineData(true,  @"..\..\targets\x64\hello.txt")]
        [InlineData(false, @"..\..\targets\x64\hello.txt")]
        public async Task CallAccessNestedSiblingSymLinkOnFilesThroughDirectorySymlinkOrJunction(bool useJunction, string symlinkRelativeTarget)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                // File and directory layout:
                //    Enlist
                //    |
                //    +---icache
                //    |   \---current
                //    |       \---x64
                //    |              symlink.imports.link ==> ..\..\..\targets\x64\hello.txt, or
                //    |                                   ==> ..\..\targets\x64\hello.txt
                //    +---imports
                //    |   \---x64 ==> ..\icache\current\x64
                //    |
                //    \---targets
                //        \---x64
                //               hello.txt

                // access: imports\x64\symlink.imports.link

                var sourceDir = tempFiles.GetDirectory(pathTable, @"imports\x64");
                var sourceOfSymlink = tempFiles.GetFileName(pathTable, sourceDir, "symlink.imports.link");

                var intermediateDir = tempFiles.GetDirectory(pathTable, @"icache\current\x64");
                var intermediateSymlink = tempFiles.GetFileName(pathTable, intermediateDir, "symlink.imports.link");

                var targetDir = tempFiles.GetDirectory(pathTable, @"targets\x64");
                var targetFile = tempFiles.GetFileName(pathTable, targetDir, "hello.txt");

                WriteFile(pathTable, targetFile, "aaa");

                // Force creation of relative symlinks.
                var currentDirectory = Directory.GetCurrentDirectory();

                Directory.SetCurrentDirectory(intermediateDir.ToString(pathTable));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink("symlink.imports.link", symlinkRelativeTarget, true));

                if (useJunction)
                {
                    EstablishJunction(sourceDir.ToString(pathTable), intermediateDir.ToString(pathTable));
                }
                else
                {
                    Directory.SetCurrentDirectory(sourceDir.GetParent(pathTable).ToString(pathTable));
                    FileUtilities.DeleteDirectoryContents("x64", deleteRootDirectory: true);
                    XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink("x64", @"..\icache\current\x64", false));
                }

                Directory.SetCurrentDirectory(currentDirectory);

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallAccessNestedSiblingSymLinkOnFiles",
                    inputFiles:
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(sourceOfSymlink),
                            FileArtifact.CreateSourceFile(intermediateSymlink),
                            FileArtifact.CreateSourceFile(targetFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString;

                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                if (useJunction)
                {
                    // We access imports\x64\symlink.imports.link and imports\x64 --> icache\current\x64 is a junction.
                    // The access path imports\x64\symlink.imports.link is not resolved to icache\current\x64\symlink.imports.link.

                    if (string.Equals(symlinkRelativeTarget, @"..\..\..\targets\x64\hello.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        // When symlink.imports.link is replaced with '..\..\..\targets\x64\hello.txt', the resulting path is a non-existent path.
                        VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
                        VerifyExitCode(context, result, NativeIOConstants.ErrorPathNotFound);
                        SetExpectedFailures(1, 0);
                    }
                    else if (string.Equals(symlinkRelativeTarget, @"..\..\targets\x64\hello.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        // When symlink.imports.link is replaced with '..\..\targets\x64\hello.txt', the resulting path is 'target\x64\hello.txt', which is an existing path.
                        VerifyNormalSuccess(context, result);

                        VerifyFileAccesses(
                            context,
                            result.AllReportedFileAccesses,
                            new[]
                            {
                                // We only report
                                // - imports\x64\symlink.import.link
                                // - target\x64\hello.txt
                                (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                                (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                            });
                    }
                }
                else
                {
                    // We access imports\x64\symlink.imports.link and imports\x64 --> icache\current\x64 is a directory symlink.
                    // The accessed path imports\x64\symlink.imports.link will be resolved first to icache\current\x64\symlink.imports.link before
                    // symlink.import.link is replaced by the relative target. That is for directory symlink, Windows use the target path for resolution.
                    if (string.Equals(symlinkRelativeTarget, @"..\..\..\targets\x64\hello.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        VerifyNormalSuccess(context, result);

                        VerifyFileAccesses(
                            context,
                            result.AllReportedFileAccesses,
                            new[]
                            {
                                // Since we access imports\x64\symlink.imports.link and imports\x64 --> icache\current\x64 is a directory symlink,
                                // we only report imports\x64\symlink.imports.link and targets\x64\hello.txt. Note that we do not report
                                // all possible forms of path when enforcing chain of symlinks. In this case, we do not report icache\current\x64\symlink.imports.link.
                                (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                                (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                            });
                    }
                    else if (string.Equals(symlinkRelativeTarget, @"..\..\targets\x64\hello.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        // When symlink.imports.link is replaced with '..\..\targets\x64\hello.txt', the resulting path is a non-existent path.
                        VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
                        VerifyExitCode(context, result, NativeIOConstants.ErrorPathNotFound);
                        SetExpectedFailures(1, 0);
                    }
                }
            }
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CallAccessNestedSiblingSymLinkOnFilesThroughMixedDirectorySymlinkAndJunction(bool relativeDirectorySymlinkTarget)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                // File and directory layout:
                //    Enlist
                //    |
                //    +---icache
                //    |   \---current
                //    |       \---x64
                //    |              symlink.imports.link ==> ..\..\..\targets\x64\hello.txt
                //    +---data
                //    |   \---imports
                //    |
                //    +---imports ==> \Enlist\data\imports (junction)
                //    |   \---x64 ==> ..\icache\current\x64 (or \Enlist\icache\current\x64) (directory symlink)
                //    |
                //    \---targets
                //        \---x64
                //               hello.txt

                // access: imports\x64\symlink.imports.link

                var dataImports = tempFiles.GetDirectory(pathTable, @"data\imports");
                var imports = tempFiles.GetDirectory(pathTable, "imports");
                EstablishJunction(imports.ToString(pathTable), dataImports.ToString(pathTable));

                var sourceDir = tempFiles.GetDirectory(pathTable, @"imports\x64");
                var sourceOfSymlink = tempFiles.GetFileName(pathTable, sourceDir, "symlink.imports.link");

                var intermediateDir = tempFiles.GetDirectory(pathTable, @"icache\current\x64");
                var intermediateSymlink = tempFiles.GetFileName(pathTable, intermediateDir, "symlink.imports.link");

                var targetDir = tempFiles.GetDirectory(pathTable, @"targets\x64");
                var targetFile = tempFiles.GetFileName(pathTable, targetDir, "hello.txt");

                WriteFile(pathTable, targetFile, "aaa");

                // Force creation of relative symlinks.
                string currentDirectory = Directory.GetCurrentDirectory();

                Directory.SetCurrentDirectory(intermediateDir.ToString(pathTable));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink("symlink.imports.link", @"..\..\..\targets\x64\hello.txt", true));

                Directory.SetCurrentDirectory(currentDirectory);

                if (relativeDirectorySymlinkTarget)
                {
                    // Force creation of relative symlinks.
                    currentDirectory = Directory.GetCurrentDirectory();
                    Directory.SetCurrentDirectory(sourceDir.GetParent(pathTable).ToString(pathTable));
                    FileUtilities.DeleteDirectoryContents("x64", deleteRootDirectory: true);
                    XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink("x64", @"..\icache\current\x64", false));
                    Directory.SetCurrentDirectory(currentDirectory);
                }
                else
                {
                    FileUtilities.DeleteDirectoryContents(sourceDir.ToString(pathTable), deleteRootDirectory: true);
                    XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceDir.ToString(pathTable), intermediateDir.ToString(pathTable), false));
                }

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallAccessNestedSiblingSymLinkOnFiles",
                    inputFiles:
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(
                            FileArtifact.CreateSourceFile(sourceOfSymlink),
                            FileArtifact.CreateSourceFile(intermediateSymlink),
                            FileArtifact.CreateSourceFile(targetFile)),
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString;

                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        // Since we access imports\x64\symlink.imports.link and imports\x64 --> icache\current\x64 is a directory symlink,
                        // we only report imports\x64\symlink.imports.link and targets\x64\hello.txt. Note that we do not report
                        // all possible forms of path when enforcing chain of symlinks. In this case, we do not report icache\current\x64\symlink.imports.link.
                        (sourceOfSymlink, RequestedAccess.Read, FileAccessStatus.Allowed),
                        (targetFile, RequestedAccess.Read, FileAccessStatus.Allowed)
                    });
            }
        }

        private async Task AccessSymlinkAndVerify(BuildXLContext context, TempFileStorage tempFiles, List<TranslateDirectoryData> translateDirectoryData, string function, AbsolutePath[] paths)
        {
            var process = CreateDetourProcess(
                context,
                context.PathTable,
                tempFiles,
                argumentStr: function,
                inputFiles:
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(paths.Select(FileArtifact.CreateSourceFile).ToArray()),
                inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

            string errorString;

            SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                pathTable: context.PathTable,
                ignoreSetFileInformationByHandle: false,
                ignoreZwRenameFileInformation: false,
                monitorNtCreate: true,
                ignoreRepPoints: false,
                context: context,
                pip: process,
                errorString: out errorString,
                directoriesToTranslate: translateDirectoryData);

            VerifyNormalSuccess(context, result);

            VerifyFileAccesses(
                context,
                result.AllReportedFileAccesses,
                paths.Select(path => (path, RequestedAccess.Read, FileAccessStatus.Allowed)).ToArray());
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallAccessJunctionSymlink()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var realDirectory = tempFiles.GetDirectory(pathTable, @"real\subdir");
                var realTargetDirectory = tempFiles.GetDirectory(pathTable, @"real\target");
                var realTarget = tempFiles.GetFileName(pathTable, realTargetDirectory, "hello.txt");
                WriteFile(pathTable, realTarget, "real");

                var junctionDirectory = tempFiles.GetDirectory(pathTable, @"junction\subdir");
                var junctionTargetDirectory = tempFiles.GetDirectory(pathTable, @"junction\target");
                var junctionTarget = tempFiles.GetFileName(pathTable, junctionTargetDirectory, "hello.txt");
                WriteFile(pathTable, junctionTarget, "junction");

                EstablishJunction(junctionDirectory.ToString(pathTable), realDirectory.ToString(pathTable));

                // Force creation of relative symlinks.
                var currentDirectory = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(realDirectory.ToString(pathTable));
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink("symlink.link", @"..\target\hello.txt", true));

                Directory.SetCurrentDirectory(currentDirectory);

                // We now have the following file structure:
                // real
                //   |
                //   +- target
                //   |   |
                //   |   + - hello.txt
                //   |
                //   +- subdir
                //        |
                //        + - symlink.link ==> ..\target\hello.txt
                // junction
                //   |
                //   +- target
                //   |   |
                //   |   + - hello.txt
                //   |
                //   +- subdir ==> real\subdir

                var realSymlink = tempFiles.GetFileName(pathTable, realDirectory, "symlink.link");
                var translateToReal = new TranslateDirectoryData(
                    $"{junctionDirectory.ToString(pathTable)}<{realDirectory.ToString(pathTable)}",
                    junctionDirectory,
                    realDirectory);

                // Access real\subdir\symlink.link
                // The final file to access is real\target\hello.txt.
                await AccessSymlinkAndVerify(context, tempFiles, new List<TranslateDirectoryData>(), "CallAccessJunctionSymlink_Real",
                    new[]
                    {
                        // Specify as inputs in manifest
                        // - real\subdir\symlink.link
                        // - real\target\hello.txt
                        realSymlink,
                        realTarget

                        // Chain of symlinks:
                        // 1. real\subdir\symlink.link
                        // 2. real\target\hello.txt

                        // Test does not need directory translation because all paths in the chain is covered by the manifest.
                    });

                // Access junction\subdir\symlink.link
                // The final file to access is junction\target\hello.txt, and not real\target\hello.txt. Although junction\subdir points to real\subdir,
                // the resolution of junction doesn't expand it to the target.
                await AccessSymlinkAndVerify(context, tempFiles, new List<TranslateDirectoryData>() { translateToReal }, "CallAccessJunctionSymlink_Junction",
                    new[]
                    {
                        realSymlink,
                        junctionTarget

                        // Chain of symlinks:
                        // 1. junction\subdir\symlink.link -- needs translation from junction\subdir to real\subdir
                        // 2. junction\target\hello.txt -- covered by manifest
                    });

                var junctionSymlink = tempFiles.GetFileName(pathTable, junctionDirectory, "symlink.link");
                var translateToJunction = new TranslateDirectoryData(
                    $"{realDirectory.ToString(pathTable)}<{junctionDirectory.ToString(pathTable)}",
                    realDirectory,
                    junctionDirectory);

                // Access real\subdir\symlink.link
                // The final file to access is real\target\hello.txt
                await AccessSymlinkAndVerify(context, tempFiles, new List<TranslateDirectoryData>() { translateToJunction }, "CallAccessJunctionSymlink_Real",
                    new[]
                    {
                        // Specify as inputs in manifest
                        // - junction\subdir\symlink.link
                        // - real\target\hello.txt
                        junctionSymlink,
                        realTarget

                        // Chain of symlinks:
                        // 1. real\subdir\symlink.link -- needs translation from real\subdir to junction\subdir
                        // 2. real\target\hello.txt -- covered by manifest
                    });

                // Access junction\subdir\symlink.link
                // The final file to access is junction\target\hello.txt; see the reason above when accessing junction\subdir\symlink.link.
                await AccessSymlinkAndVerify(context, tempFiles, new List<TranslateDirectoryData>(), "CallAccessJunctionSymlink_Junction",
                    new[]
                    {
                        // Specify as inputs in manifest
                        // - junction\subdir\symlink.link
                        // - junction\target\hello.txt
                        junctionSymlink,
                        junctionTarget

                        // Chain of symlinks:
                        // 1. junction\subdir\symlink.link -- covered by manifest
                        // 2. junction\target\hello.txt -- covered by manifest
                    });
            }
        }

        [Fact]
        public async Task TestPipeCreation()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallPipeTest",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    disableDetours: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNoFileAccesses(result);
            }
        }

        [Fact]
        public void TestPathWithTrailingPathSeparator()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            AbsolutePath path = AbsolutePath.Create(pathTable, "C:\\foo\\bar\\");
            XAssert.AreEqual(path.ToString(pathTable), "C:\\foo\\bar");
        }

        private static FileArtifact WriteFile(PathTable pathTable, AbsolutePath filePath, string content)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(filePath.IsValid);
            Contract.Requires(content != null);

            string expandedPath = filePath.ToString(pathTable);

            if (File.Exists(expandedPath))
            {
                ExceptionUtilities.HandleRecoverableIOException(() => File.Delete(expandedPath), exception => { });
            }

            XAssert.IsFalse(File.Exists(expandedPath));

            ExceptionUtilities.HandleRecoverableIOException(
                        () =>
                        {
                            using (FileStream fs = File.Create(expandedPath))
                            {
                                byte[] info = new UTF8Encoding(true).GetBytes(content);

                                // Add some information to the file.
                                fs.Write(info, 0, info.Length);
                                fs.Close();
                            }
                        },
                        exception => { });

            XAssert.IsTrue(File.Exists(expandedPath));

            return FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, expandedPath));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CallDeleteWithoutSharing(bool untracked)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var untrackedFile = tempFiles.GetFileName(pathTable, "untracked.txt");
                WriteFile(pathTable, untrackedFile, "real");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallDeleteWithoutSharing",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: untracked 
                        ? ReadOnlyArray<FileArtifactWithAttributes>.Empty 
                        : ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(FileArtifactWithAttributes.FromFileArtifact(FileArtifact.CreateSourceFile(untrackedFile), FileExistence.Optional)),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: untracked ? ReadOnlyArray<AbsolutePath>.FromWithoutCopy(untrackedFile.GetParent(pathTable)) : ReadOnlyArray<AbsolutePath>.Empty);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    disableDetours: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                if (untracked)
                {
                    VerifyNoFileAccesses(result);
                    VerifySharingViolation(context, result);
                    SetExpectedFailures(1, 0);
                }
                else
                {
                    VerifyNormalSuccess(context, result);
                }
            }
        }

        [Fact]
        public async Task CallDeleteOnOpenedHardlink()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var untrackedDirectory = tempFiles.GetDirectory(pathTable, "untracked");
                WriteFile(pathTable, untrackedDirectory.Combine(pathTable, "file.txt"), "real");
                var outputFile = tempFiles.GetFileName(pathTable, "output.txt");

                var process = CreateDetourProcess(
                    context,
                    pathTable,
                    tempFiles,
                    argumentStr: "CallDeleteOnOpenedHardlink",
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(FileArtifactWithAttributes.FromFileArtifact(FileArtifact.CreateSourceFile(outputFile), FileExistence.Required)),
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(untrackedDirectory));

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: false,
                    ignoreZwRenameFileInformation: false,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    disableDetours: false,
                    context: context,
                    pip: process,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public async Task CallDetouredCreateFileWForProbingOnly()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                var createdInputPaths = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

                var pip = SetupDetoursTests(
                    context,
                    tempFiles,
                    pathTable,
                    "CreateFileWForProbingOnly.lnk",
                    "CreateFileWForProbingOnly.txt",
                    "CallDetouredCreateFileWForProbingOnly",
                    isDirectoryTest: false,
                    createSymlink: true,
                    addCreateFileInDirectoryToDependencies: false,
                    createFileInDirectory: false,
                    addFirstFileKind: AddFileOrDirectoryKinds.AsDependency,
                    addSecondFileOrDirectoryKind: AddFileOrDirectoryKinds.None,
                    makeSecondUntracked: true,
                    createdInputPaths: createdInputPaths);

                string errorString = null;
                SandboxedProcessPipExecutionResult result = await RunProcessAsync(
                    pathTable: pathTable,
                    ignoreSetFileInformationByHandle: true,
                    ignoreZwRenameFileInformation: true,
                    monitorNtCreate: true,
                    ignoreRepPoints: false,
                    ignoreZwOtherFileInformation: true,
                    monitorZwCreateOpenQueryFile: true,
                    context: context,
                    pip: pip,
                    errorString: out errorString);

                VerifyNormalSuccess(context, result);

                VerifyFileAccesses(
                    context,
                    result.AllReportedFileAccesses,
                    new[]
                    {
                        (createdInputPaths["CreateFileWForProbingOnly.lnk"], RequestedAccess.Probe, FileAccessStatus.Allowed),
                    });
            }
        }

        private static Process CreateDetourProcess(
            BuildXLContext context,
            PathTable pathTable,
            TempFileStorage tempFileStorage,
            string argumentStr,
            ReadOnlyArray<FileArtifact> inputFiles,
            ReadOnlyArray<DirectoryArtifact> inputDirectories,
            ReadOnlyArray<FileArtifactWithAttributes> outputFiles,
            ReadOnlyArray<DirectoryArtifact> outputDirectories,
            ReadOnlyArray<AbsolutePath> untrackedScopes)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(tempFileStorage != null);
            Contract.Requires(argumentStr != null);
            Contract.Requires(inputFiles != null && Contract.ForAll(inputFiles, artifact => artifact.IsValid));
            Contract.Requires(inputDirectories != null && Contract.ForAll(inputDirectories, artifact => artifact.IsValid));
            Contract.Requires(outputFiles != null && Contract.ForAll(outputFiles, artifact => artifact.IsValid));
            Contract.Requires(outputDirectories != null && Contract.ForAll(outputDirectories, artifact => artifact.IsValid));

            // Get the executable "DetoursTests.exe".
            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            XAssert.IsTrue(!string.IsNullOrWhiteSpace(currentCodeFolder), "Current code folder is unknown");

            Contract.Assert(currentCodeFolder != null);

            string executable = Path.Combine(currentCodeFolder, DetourTestFolder, "DetoursTests.exe");
            XAssert.IsTrue(File.Exists(executable));

            FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, executable));
            var untrackedList = new List<AbsolutePath>(CmdHelper.GetCmdDependencies(pathTable));
            var allUntrackedScopes = new List<AbsolutePath>(untrackedScopes);
            allUntrackedScopes.AddRange(CmdHelper.GetCmdDependencyScopes(pathTable));

            var inputFilesWithExecutable = new List<FileArtifact>(inputFiles) { executableFileArtifact };

            var arguments = new PipDataBuilder(pathTable.StringTable);
            arguments.Add(argumentStr);

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
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);
        }

        private static void VerifyFileAccesses(
            BuildXLContext context,
            IReadOnlyList<ReportedFileAccess> reportedFileAccesses,
            (AbsolutePath abosultePath, RequestedAccess requestedAccess, FileAccessStatus fileAccessStatus)[] observationsToVerify,
            AbsolutePath[] pathsToFalsify = null,
            (AbsolutePath abosultePath, RequestedAccess requestedAccess, FileAccessStatus fileAccessStatus)[] observationsToFalsify = null)
        {
            PathTable pathTable = context.PathTable;
            var pathsToReportedFileAccesses = new Dictionary<AbsolutePath, List<ReportedFileAccess>>();

            foreach (var reportedFileAccess in reportedFileAccesses)
            {
                AbsolutePath reportedPath = AbsolutePath.Invalid;

                if (reportedFileAccess.ManifestPath.IsValid)
                {
                    reportedPath = reportedFileAccess.ManifestPath;
                }

                if (!string.IsNullOrWhiteSpace(reportedFileAccess.Path))
                {
                    AbsolutePath temp;

                    if (AbsolutePath.TryCreate(pathTable, reportedFileAccess.Path, out temp))
                    {
                        reportedPath = temp;
                    }
                }

                if (reportedPath.IsValid)
                {
                    List<ReportedFileAccess> list;

                    if (!pathsToReportedFileAccesses.TryGetValue(reportedPath, out list))
                    {
                        list = new List<ReportedFileAccess>();
                        pathsToReportedFileAccesses.Add(reportedPath, list);
                    }

                    list.Add(reportedFileAccess);
                }
            }

            foreach (var observation in observationsToVerify)
            {
                List<ReportedFileAccess> pathSpecificAccesses;
                bool getFileAccess = pathsToReportedFileAccesses.TryGetValue(observation.abosultePath, out pathSpecificAccesses);
                XAssert.IsTrue(
                    getFileAccess,
                    "Expected path '{0}' is missing from the reported file accesses; reported accesses are as follows: {1}{2}",
                    observation.abosultePath.ToString(pathTable),
                    Environment.NewLine,
                    string.Join(Environment.NewLine, pathsToReportedFileAccesses.Keys.Select(p => "--- " + p.ToString(pathTable))));

                Contract.Assert(pathSpecificAccesses != null);

                bool foundExpectedAccess = false;

                foreach (var pathSpecificAccess in pathSpecificAccesses)
                {
                    if (pathSpecificAccess.RequestedAccess == observation.Item2 && pathSpecificAccess.Status == observation.Item3)
                    {
                        foundExpectedAccess = true;
                        break;
                    }
                }

                XAssert.IsTrue(
                    foundExpectedAccess,
                    "Expected access for path '{0}' with requested access '{1}' and access status '{2}' is missing from the reported file accesses; reported accesses are as follows: {3}{4}",
                    observation.abosultePath.ToString(pathTable),
                    observation.requestedAccess.ToString(),
                    observation.fileAccessStatus.ToString(),
                    Environment.NewLine,
                    string.Join(
                        Environment.NewLine,
                        pathSpecificAccesses.Select(r => "--- " + r.RequestedAccess.ToString() + " | " + r.Status.ToString())));
            }

            if (pathsToFalsify != null)
            {
                foreach (var absolutePath in pathsToFalsify)
                {
                    XAssert.IsFalse(
                        pathsToReportedFileAccesses.ContainsKey(absolutePath),
                        "Unexpected path '{0}' exists in the reported file accesses",
                        absolutePath.ToString(pathTable));
                }
            }

            if (observationsToFalsify != null)
            {
                foreach (var observation in observationsToFalsify)
                {
                    List<ReportedFileAccess> pathSpecificAccesses;
                    var getFileAccess = pathsToReportedFileAccesses.TryGetValue(observation.abosultePath, out pathSpecificAccesses);
                    if (!getFileAccess)
                    {
                        continue;
                    }

                    Contract.Assert(pathSpecificAccesses != null);

                    bool foundExpectedAccess = false;

                    foreach (var pathSpecificAccess in pathSpecificAccesses)
                    {
                        if (pathSpecificAccess.RequestedAccess == observation.Item2 && pathSpecificAccess.Status == observation.Item3)
                        {
                            foundExpectedAccess = true;
                            break;
                        }
                    }

                    XAssert.IsFalse(
                        foundExpectedAccess,
                        "Unexpected access for path '{0}' with requested access '{1}' and access status '{2}' exists in the reported file accesses",
                        observation.abosultePath.ToString(pathTable),
                        observation.requestedAccess.ToString(),
                        observation.fileAccessStatus.ToString());
                }
            }
        }

        private static void VerifyNoFileAccesses(SandboxedProcessPipExecutionResult result)
        {
            XAssert.AreEqual(0, result.ObservedFileAccesses.Length);
            XAssert.AreEqual(
                0, 
                (result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted == null) 
                ? 0 
                : result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted.Count);
        }

        private static void VerifyNoObservedFileAccessesAndUnexpectedFileAccesses(SandboxedProcessPipExecutionResult result, string[] unexpectedFileAccesses, PathTable pathTable)
        {
            XAssert.AreEqual(0, result.ObservedFileAccesses.Length);
            XAssert.AreEqual(unexpectedFileAccesses.Length, (result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted == null) ?
                0 :
                result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted.Count);

            foreach (string unexpectedFileAccessExpected in unexpectedFileAccesses)
            {
                bool exitInner = false;
                foreach (ReportedFileAccess unexpectedFileAccess in result.UnexpectedFileAccesses.FileAccessViolationsNotWhitelisted)
                {
                    if (exitInner)
                    {
                        break;
                    }

                    string unexpectedFileAccessString = unexpectedFileAccess.GetPath(pathTable);
                    if (unexpectedFileAccessExpected == unexpectedFileAccessString)
                    {
                        exitInner = true;
                        continue;
                    }

                    XAssert.Fail("Unexpected file access on file {0} not registered.", unexpectedFileAccessString);
                }
            }
        }
    }
}
