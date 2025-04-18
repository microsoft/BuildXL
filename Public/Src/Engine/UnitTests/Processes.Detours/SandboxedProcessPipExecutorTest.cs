// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.ProcessPipExecutor;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Processes.Detours
{
    public sealed partial class SandboxedProcessPipExecutorTest
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MissingFileAccessForOutput(bool preserveOutputs)
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = Path.Combine(tempFiles.GetUniqueDirectory("sub"), "file.txt");
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(context.PathTable, destination);
                FileArtifact destinationFileArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                string envVarName = "ENV" + Guid.NewGuid().ToString("N");

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("%" + envVarName + "%");
                }

                var options = default(Process.Options);
                var sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true,
                };

                if (preserveOutputs)
                {
                    options |= Process.Options.AllowPreserveOutputs;
                    sandboxConfiguration.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;
                    File.WriteAllText(destination, string.Empty);
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(new EnvironmentVariable(
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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact /*, responseFileArtifact */),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(destinationFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    options: options);

                if (preserveOutputs)
                {
                    await AssertProcessSucceedsAsync(
                        context,
                        sandboxConfiguration,
                        pip);
                }
                else
                {
                    await AssertProcessFailsExecution(
                        context,
                        sandboxConfiguration,
                        pip);

                    AssertErrorEventLogged(ProcessesLogEventId.PipProcessExpectedMissingOutputs);
                    AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
                }
            }
        }

        [Fact]
        public async Task ProcessSuccess()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = tempFiles.GetUniqueFileName();
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(context.PathTable, destination);
                FileArtifact destinationFileArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                string envVarName = "ENV" + Guid.NewGuid().ToString("N");

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("%" + envVarName + "%");
                    arguments.Add(">");
                    arguments.Add(destinationFileArtifact);
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(new EnvironmentVariable(
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

                    // TODO:1759: Fix response file handling. Should be able to appear in the dependencies list, but should appear in the graph as a WriteFile pip.
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact /*, responseFileArtifact */),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(destinationFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var processIdListenerInvocations = new List<int>();

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true },
                    pip,
                    processIdListener: pid => processIdListenerInvocations.Add(pid));

                string actual = File.ReadAllText(destination).Trim();
                XAssert.AreEqual("Success", actual);
                XAssert.AreEqual(2, processIdListenerInvocations.Count);
                XAssert.IsTrue(processIdListenerInvocations[0] > 0);
                XAssert.IsTrue(processIdListenerInvocations[1] < 0);
                XAssert.AreEqual(processIdListenerInvocations[0], -processIdListenerInvocations[1]);
            }
        }

        [Fact]
        public async Task Bug185033()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = tempFiles.GetUniqueFileName();
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(context.PathTable, destination);
                FileArtifact destinationFileArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                string envVarName = "ENV" + Guid.NewGuid().ToString("N");

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("%" + envVarName + "%");
                    arguments.Add(">");
                    arguments.Add(destinationFileArtifact);
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(new EnvironmentVariable(
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

                    // TODO:1759: Fix response file handling. Should be able to appear in the dependencies list, but should appear in the graph as a WriteFile pip.
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact /*, responseFileArtifact */),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(destinationFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration { LogObservedFileAccesses = true };
                sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = false;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
                string actual = File.ReadAllText(destination).Trim();
                XAssert.AreEqual(
                    "Success",
                    actual);
            }
        }

        [Fact]
        public async Task ProcessRedirectStandardOutput()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = tempFiles.GetUniqueDirectory();
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string standardOutput = Path.Combine(workingDirectory, "out.txt");
                AbsolutePath standardOutputAbsolutePath = AbsolutePath.Create(context.PathTable, standardOutput);
                FileArtifact standardOutputFileArtifact = FileArtifact.CreateSourceFile(standardOutputAbsolutePath).CreateNextWrittenVersion();

                string envVarName = "ENV" + Guid.NewGuid().ToString("N");

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("%" + envVarName + "%");
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(new EnvironmentVariable(
                            StringId.Create(context.PathTable.StringTable, envVarName),
                            PipDataBuilder.CreatePipData(
                                context.PathTable.StringTable,
                                " ",
                                PipDataFragmentEscaping.CRuntimeArgumentRules,
                                "Success"))),
                    FileArtifact.Invalid,
                    standardOutputFileArtifact,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,

                    // TODO:1759: Fix response file handling. Should be able to appear in the dependencies list, but should appear in the graph as a WriteFile pip.
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact /*, responseFileArtifact */),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(standardOutputFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true },
                    pip);
                string actual = File.ReadAllText(standardOutput).Trim();
                XAssert.AreEqual(
                    "Success",
                    actual);
            }
        }

        [Fact]
        public async Task ProcessSuccessNonzeroExitCode()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("exit");
                    arguments.Add("1");
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.FromWithoutCopy(1),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true },
                    pip);
            }
        }

        [Fact]
        public async Task ProcessUnexpectedFileAccess()
        {
            const string Text2 = "Text2";
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string undeclaredFileName = tempFiles.GetUniqueFileName();
                File.WriteAllText(undeclaredFileName, Text2);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("type");
                    arguments.Add(undeclaredFileName);
                    arguments.Add("&");

                    // Continue even though type fails
                    arguments.Add("if");
                    arguments.Add("errorlevel");
                    arguments.Add("1");
                    arguments.Add("exit");
                    arguments.Add("42");
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                // The script above is supposed to reach the last statement (exit 42); we need type to actually fail.
                await AssertProcessFailsExecution(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = true },
                    pip);
            }

            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessFinishedFailed);
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccess);
            AssertLogContains(caseSensitive: true, requiredLogMessages: "exit code 42");
        }

        [Fact]
        public async Task ProcessFileTempDirectoryViolation()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add(@"echo >%TMP%\illegal.txt");
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                await AssertProcessFailsExecution(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = true },
                    pip);
            }

            SetExpectedFailures(1, 0, "DX0064");
            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedTempFileAccess);
        }

        [Fact]
        public async Task ProcessWithMultipleTempDirectories()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string tempDir1 = tempFiles.GetUniqueDirectory();
                string tempDir2 = tempFiles.GetUniqueDirectory();
                var tempDirs = new[]
                               {
                                   AbsolutePath.Create(context.PathTable, tempDir1),
                                   AbsolutePath.Create(context.PathTable, tempDir2)
                               };

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("foo");
                    arguments.Add(">");
                    arguments.Add(Path.Combine(tempDir1, "legalFoo.txt"));
                    arguments.Add("&");
                    arguments.Add("echo");
                    arguments.Add("bar");
                    arguments.Add(">");
                    arguments.Add(Path.Combine(tempDir2, "legalBar.txt"));
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable).Concat(tempDirs)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.From(tempDirs));

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true },
                    pip);
            }

            SetExpectedFailures(0, 0);
        }

        [Feature(Features.NonStandardOptions)]
        [Fact]
        public async Task ProcessFileTempDirectoryViolationReportedAsWarning()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.NoEscaping, " "))
                {
                    arguments.Add(@"echo foo > %TMP%\lawless.txt & type %TMP%\lawless.txt");
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false, };
                sandboxConfiguration.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = false;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
            }

            // 4 warnings with 2 probes collapsed into one, because they reported the same kind
            // of access. Events ignore the function used for the access.
            // However, the 2nd probe may have a different error code, and thus it could be 3 or 4 warnings in total.
            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedTempFileAccess, count: 3, allowMore: true);
        }

        [Fact]
        public async Task ProcessUnexpectedFileAccessAllowlistedByValue()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var loggingContext = CreateLoggingContextForTest();
            var symbolTable = context.SymbolTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string undeclaredFileName = tempFiles.GetUniqueFileName();

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("foo");
                    arguments.Add(">");
                    arguments.Add(undeclaredFileName);
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    new PipProvenance(
                        0,
                        ModuleId.Invalid,
                        StringId.Invalid,
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "AllowlistedValue")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid,
                        false),
                    StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var fileAccessAllowlist = new FileAccessAllowlist(context);

                FullSymbol allowlistedValue;
                int characterWithError;
                if (FullSymbol.TryCreate(symbolTable, "AllowlistedValue", out allowlistedValue, out characterWithError) !=
                    FullSymbol.ParseResult.Success)
                {
                    XAssert.Fail("Could not construct a FullSymbol from a string.");
                }

                fileAccessAllowlist.Add(
                    new ValuePathFileAccessAllowlistEntry(
                        allowlistedValue,
                        FileAccessAllowlist.RegexWithProperties(Regex.Escape(Path.GetFileName(undeclaredFileName))),
                        allowsCaching: false,
                        name: "name"));

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false },
                    pip,
                    fileAccessAllowlist);
            }

            AssertInformationalEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccessAllowlistedNonCacheable);
        }

        [Fact]
        public async Task ProcessUnexpectedFileAccessAllowlistedByValueWithRegex()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var loggingContext = CreateLoggingContextForTest();
            var symbolTable = context.SymbolTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                const string PathSuffix = ".suffix";
                string undeclaredFileName = tempFiles.GetUniqueFileName(suffix: PathSuffix);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("foo");
                    arguments.Add(">");
                    arguments.Add(undeclaredFileName);
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    new PipProvenance(
                        0,
                        ModuleId.Invalid,
                        StringId.Invalid,
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "AllowlistedValue")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid,
                        false),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var fileAccessAllowlist = new FileAccessAllowlist(context);

                FullSymbol allowlistedValue;
                int characterWithError;
                if (FullSymbol.TryCreate(symbolTable, "AllowlistedValue", out allowlistedValue, out characterWithError) !=
                    FullSymbol.ParseResult.Success)
                {
                    XAssert.Fail("Could not construct a FullSymbol from a string.");
                }

                fileAccessAllowlist.Add(
                    new ValuePathFileAccessAllowlistEntry(
                        allowlistedValue,
                        FileAccessAllowlist.RegexWithProperties(Regex.Escape(PathSuffix) + '$'),
                        allowsCaching: false,
                        name: "name"));

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false },
                    pip,
                    fileAccessAllowlist);
            }

            AssertInformationalEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccessAllowlistedNonCacheable);
        }

        /// <summary>
        /// The test checks for two scenarios of FileAccessAllowLists.
        /// a.) We specify the toolPath in the allowList. We expect the verbose event logged stating the file access.
        /// b.) We do not specify the toolPath, we expect the same results as in the first scenario.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ProcessUnexpectedFileAccessAllowlistedByExecutable(bool isToolPathSpecifiedInTheAllowList)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var loggingContext = CreateLoggingContextForTest();
            var symbolTable = context.SymbolTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string undeclaredFileName = tempFiles.GetUniqueFileName();

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("foo");
                    arguments.Add(">");
                    arguments.Add(undeclaredFileName);
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    new PipProvenance(
                        0,
                        ModuleId.Invalid,
                        StringId.Invalid,
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "AllowlistedValue")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid, 
                        false),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var fileAccessAllowlist = new FileAccessAllowlist(context);

                if (isToolPathSpecifiedInTheAllowList)
                {
                    fileAccessAllowlist.Add(
                        new ExecutablePathAllowlistEntry(
                            executableFileArtifact.Path,
                            FileAccessAllowlist.RegexWithProperties(Regex.Escape(Path.GetFileName(undeclaredFileName))),
                            allowsCaching: false,
                            name: "name"));
                }
                else
                {
                    fileAccessAllowlist.Add(
                        new ExecutablePathAllowlistEntry(
                            null,
                            FileAccessAllowlist.RegexWithProperties(Regex.Escape(Path.GetFileName(undeclaredFileName))),
                            allowsCaching: false,
                            name: "name"));
                }

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false },
                    pip,
                    fileAccessAllowlist);
            }

            AssertInformationalEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccessAllowlistedNonCacheable);
        }

        [Fact]
        public async Task ProcessFileAccessesUnderSharedOpaques()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var symbolTable = context.SymbolTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = tempFiles.GetUniqueDirectory();
                var sharedOpaqueRoot = Path.Combine(workingDirectory, "sharedOpaque");

                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, sharedOpaqueRoot);

                // Create two shared opaques with the same root. One will act as an input to the pip, the other one as an
                // output directory
                var sharedOpaqueInput = new DirectoryArtifact(AbsolutePath.Create(context.PathTable, sharedOpaqueRoot), 1, isSharedOpaque: true);
                var sharedOpaqueOutput = new DirectoryArtifact(AbsolutePath.Create(context.PathTable, sharedOpaqueRoot), 2, isSharedOpaque: true);

                // The shared opaque input contains input/in.txt
                var inputUndersharedOpaqueRoot = Path.Combine(sharedOpaqueRoot, "input", "in.txt");
                Directory.CreateDirectory(sharedOpaqueRoot);
                Directory.CreateDirectory(Path.Combine(sharedOpaqueRoot, "input"));
                File.WriteAllText(inputUndersharedOpaqueRoot, "Foo");

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    // Reads 'input/in.txt' (under the shared opaque input) and creates 'nested/out.txt' (under the shared opaque output)
                    arguments.Add("type");
                    arguments.Add(@"input\in.txt");
                    arguments.Add("&&");
                    arguments.Add("mkdir");
                    arguments.Add("nested");
                    arguments.Add("&&");
                    arguments.Add("echo");
                    arguments.Add("foo");
                    arguments.Add(">");
                    arguments.Add(@"nested\out.txt");
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new DirectoryArtifact[] { sharedOpaqueInput }),
                    ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new DirectoryArtifact[] { sharedOpaqueOutput }),
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    new PipProvenance(
                        0,
                        ModuleId.Invalid,
                        StringId.Invalid,
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "SharedOpaqueAccesses")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid,
                        false),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false },
                    pip,
                    fileAccessAllowlist: null,
                    new Dictionary<string, string>(),
                    SemanticPathExpander.Default,
                    new TestDirectoryArtifactContext(
                            SealDirectoryKind.SharedOpaque,
                            new FileArtifact[] { FileArtifact.CreateOutputFile(AbsolutePath.Create(context.PathTable, inputUndersharedOpaqueRoot)) }));

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, result.Status);

                // There should be a single reported file access that is not related to creating directories: The attempt to read 'input/in.txt'. The accesses related to writing the file should not be reported here.
                ObservedFileAccess access = result.ObservedFileAccesses.Single(fa => !fa.Accesses.All(a => a.IsDirectoryCreationOrRemoval()));
                XAssert.AreEqual(AbsolutePath.Create(context.PathTable, inputUndersharedOpaqueRoot), access.Path);
            }
        }

        /// <summary>
        /// Blocking accesses under shared opaques based on file existence is a Windows-only feature for now.
        /// </summary>
        [TheoryIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ProcessFileAccessesBlockedBasedOnFileExistenceUnderSharedOpaques(bool pipCreatesFile)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var symbolTable = context.SymbolTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.OsShellExe;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = tempFiles.GetUniqueDirectory();
                var sharedOpaqueRoot = Path.Combine(workingDirectory, "sharedOpaque");

                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, sharedOpaqueRoot);

                // Create a shared opaque.
                var sharedOpaque = new DirectoryArtifact(AbsolutePath.Create(context.PathTable, sharedOpaqueRoot), 1, isSharedOpaque: true);

                // The shared opaque input contains input/in.txt
                var inputUndersharedOpaqueRoot = Path.Combine(sharedOpaqueRoot, "input", "in.txt");
                Directory.CreateDirectory(sharedOpaqueRoot);
                Directory.CreateDirectory(Path.Combine(sharedOpaqueRoot, "input"));

                // If the test is configured so the pip does not create the file, we create it before the pip runs
                if (!pipCreatesFile)
                {
                    File.WriteAllText(inputUndersharedOpaqueRoot, "Foo");
                }

                var arguments = new PipDataBuilder(context.PathTable.StringTable);

                if (OperatingSystemHelper.IsWindowsOS)
                {
                    arguments.Add("/d");
                    arguments.Add("/c");
                    using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                    {
                        // Writes into 'input/in.txt' (under the shared opaque input) twice
                        addCommonArguments(arguments);
                    }
                }
                else
                {
                    arguments.Add("-c");
                    addCommonArguments(arguments);
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new DirectoryArtifact[] { sharedOpaque }),
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    new PipProvenance(
                        0,
                        ModuleId.Invalid,
                        StringId.Invalid,
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "SharedOpaqueAccesses")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid,
                        false),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    // Write on existing files are for now blocked only when allow undeclared source reads are on
                    options: Process.Options.AllowUndeclaredSourceReads);

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    new SandboxConfiguration
                    {
                        FileAccessIgnoreCodeCoverage = true,
                        FailUnexpectedFileAccesses = false,
                        UnsafeSandboxConfiguration = new UnsafeSandboxConfiguration { IgnoreUndeclaredAccessesUnderSharedOpaques = false },
                        LogObservedFileAccesses = true
                    },
                    pip,
                    fileAccessAllowlist: null,
                    new Dictionary<string, string>(),
                    SemanticPathExpander.Default,
                    new TestDirectoryArtifactContext(
                            SealDirectoryKind.SharedOpaque,
                            new FileArtifact[] { }));

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, result.Status);

                if (!pipCreatesFile)
                {
                    // There should be two denied accesses, both based on file existence
                    var deniedAccessesBasedOnExistence = result.AllReportedFileAccesses.Where(a => a.Status == FileAccessStatus.Denied && a.Method == FileAccessStatusMethod.FileExistenceBased);
                    XAssert.AreEqual(2, deniedAccessesBasedOnExistence.Count());
                }
                else
                {
                    // No violations when the pip is the one creating the file
                    XAssert.AreEqual(null, result.UnexpectedFileAccesses.FileAccessViolationsNotAllowlisted);
                }
            }

            static void addCommonArguments(PipDataBuilder arguments)
            {
                arguments.Add("echo");
                arguments.Add("foo");
                arguments.Add(">");
                arguments.Add(@"input\in.txt");
                arguments.Add("&&");
                arguments.Add("echo");
                arguments.Add("bar");
                arguments.Add(">");
                arguments.Add(@"input\in.txt");
            }
        }

        [Fact]
        public async Task ProcessTooLargeCommandLine()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                arguments.Add(new string('X', SandboxedProcessInfo.MaxCommandLineLength));

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                await AssertProcessFailsPreparation(context, new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true }, pip);
            }

            SetExpectedFailures(1, 0, "DX0032");
        }

        [Fact]
        public async Task ProcessFileAccessNoMonitoring()
        {
            const string Text2 = @"Text2";
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string undeclaredFileName = tempFiles.GetUniqueFileName();
                File.WriteAllText(undeclaredFileName, Text2);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("type");
                    arguments.Add(undeclaredFileName);
                    arguments.Add("&&");
                    arguments.Add("echo");
                    arguments.Add("Text2");
                    arguments.Add("1>&2");
                    arguments.Add("&&");
                    arguments.Add("echo");
                    arguments.Add("Text1");
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    LogFileAccessTables = true,
                    FileAccessIgnoreCodeCoverage = true
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = false;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
            }

            SetExpectedFailures(0, 0);
        }

        [Fact]
        public async Task ProcessBadRegex()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.Empty,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    PipProvenance.CreateDummy(context),
                    StringId.Invalid,
                    ReadOnlyArray<AbsolutePath>.Empty,
                    new RegexDescriptor(StringId.Create(context.PathTable.StringTable, "("), RegexOptions.None)); // this is not a legal pattern

                await AssertProcessFailsPreparation(
                    context,
                   new SandboxConfiguration
                   {
                       FailUnexpectedFileAccesses = false,
                       FileAccessIgnoreCodeCoverage = true
                   },
                    pip);
            }

            SetExpectedFailures(1, 0, "DX0039");
        }

        [Fact]
        public async Task Bug21916()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = Path.Combine(tempFiles.GetUniqueDirectory(), "sub", "file.txt");
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(context.PathTable, destination);
                FileArtifact destinationFileArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                string envVarName = "ENV" + Guid.NewGuid().ToString("N");

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("%" + envVarName + "%");
                    arguments.Add(">");
                    arguments.Add(destinationFileArtifact);
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(new EnvironmentVariable(
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

                    // TODO:1759: Fix response file handling. Should be able to appear in the dependencies list, but should appear in the graph as a WriteFile pip.
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact /*, responseFileArtifact */),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(destinationFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = false;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
                string actual = File.ReadAllText(destination).Trim();
                XAssert.AreEqual(
                    "Success",
                    actual);
            }
        }

        [Fact]
        public async Task Bug64631()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = Path.Combine(tempFiles.GetUniqueDirectory(), "sub", "file.txt");
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(context.PathTable, destination);
                FileArtifact destinationFileArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                string envVarName = "ENV" + Guid.NewGuid().ToString("N");

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("%" + envVarName + "%");
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(new EnvironmentVariable(
                            StringId.Create(context.PathTable.StringTable, envVarName),
                            PipDataBuilder.CreatePipData(
                                context.PathTable.StringTable,
                                " ",
                                PipDataFragmentEscaping.CRuntimeArgumentRules,
                                "Success"))),
                    FileArtifact.Invalid,
                    destinationFileArtifact,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,

                    // TODO:1759: Fix response file handling. Should be able to appear in the dependencies list, but should appear in the graph as a WriteFile pip.
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact /*, responseFileArtifact */),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(destinationFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = false;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
                string actual = File.ReadAllText(destination).Trim();
                XAssert.AreEqual(
                    "Success",
                    actual);
            }
        }

        [Fact]
        public async Task Bug53535()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string response = Path.Combine(tempFiles.GetUniqueDirectory(), "file.txt");
                AbsolutePath responseAbsolutePath = AbsolutePath.Create(context.PathTable, response);
                FileArtifact responseFileArtifact = FileArtifact.CreateSourceFile(responseAbsolutePath).CreateNextWrittenVersion();

                string responseFileText = "AAA\r\nBBB";
                var responseFileArguments = new PipDataBuilder(context.PathTable.StringTable);
                responseFileArguments.Add(responseFileText);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/c");

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    responseFileArtifact,
                    responseFileArguments.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping),
                    ReadOnlyArray<EnvironmentVariable>.Empty,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact, responseFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = false;

                await AssertProcessSucceedsAsync(
                    context,
                    sandboxConfiguration,
                    pip);
            }
        }

        [Fact]
        public async Task Timeout()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            // When we timeout-kill a process if the kill happens while Detours is copying the payload to the child process,
            // we can get a very rare ERROR_PARTIAL_COPY from the OS. This is non recoverable error and the process is in a weird half started state.
            // In such case the process can't be dumped.
            bool failedToCreateDump = false;

            string expectedDumpPath = null;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, TestOutputDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d /c FOR /L %i IN (0,0,1) DO @rem");

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
                    1.MillisecondsToTimeSpan(),  // warning after 1ms
                    2.MillisecondsToTimeSpan(),  // termination after 2ms
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    uniqueOutputDirectory: AbsolutePath.Create(context.PathTable, TestOutputDirectory));

                SandboxedProcessPipExecutionResult wrappedResult = null;
                SandboxedProcessPipExecutionResultWrapper wrapper = new SandboxedProcessPipExecutionResultWrapper(wrappedResult);
                SandboxConfiguration sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true,
                    OutputReportingMode = OutputReportingMode.FullOutputOnError,
                };

                sandboxConfiguration.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = false;

                await AssertProcessFailsExecution(
                    context,
                    sandboxConfiguration,
                    pip,
                    null,
                    wrapper);

                expectedDumpPath = Path.Combine(TestOutputDirectory, pip.FormattedSemiStableHash);

                // Very occasionally the child dump fails to be collected due to ERROR_PARTIAL_COPY (0x12B) or ERROR_BAD_LENGTH
                // Check for the masked version of this in the native error code of the process dump message and ignore when it is hit
                if (EventListener.GetEventCount((int)ProcessesLogEventId.PipFailedToCreateDumpFile) == 1 &&
                    (EventListener.GetLog().Contains("-2147024597")) // -2147024597 && 0x0FFF == 0x12B (ERROR_PARTIAL_COPY)
                    || EventListener.GetLog().Contains("80070018")) // win32 error code 18 is (ERROR_BAD_LENGTH)
                {
                    failedToCreateDump = true;
                }
            }

            if (failedToCreateDump)
            {
                SetExpectedFailures(1, 1, "DX0016", "DX2210");
            }
            else
            {
                SetExpectedFailures(1, 0, "DX0016");
                var files = Directory.GetFiles(expectedDumpPath);
                Assert.True(files.Length >= 1, "Did not find dump file at: " + TestOutputDirectory);
            }
        }

        [Fact]
        public async Task PipChildProcessSurvives()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, TestOutputDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d /c start /B " + CmdHelper.CmdX64 + " /C timeout 1000 > NUL");  // Starts child process that 'hangs' and has to be killed by job object.

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    nestedProcessTerminationTimeout: TimeSpan.Zero);

                AbsolutePath.TryCreate(context.PathTable, TemporaryDirectory, out AbsolutePath dumpDir);

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    new SandboxConfiguration
                    {
                        FileAccessIgnoreCodeCoverage = true,
                        SurvivingPipProcessChildrenDumpDirectory = dumpDir.Combine(context.PathTable, LogFileExtensions.SurvivingPipProcessChildrenDumpDirectory)
                    },
                    pip,
                    null,
                    new Dictionary<string, string>(),
                    SemanticPathExpander.Default);

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.ExecutionFailed, result.Status);
            }

            // conhost.exe may or may not be started within the pip depending on
            // the current machine state. And each cmd might have its own conhost.
            int numChildrenSurvivedErrors = EventListener.GetEventCount((int)ProcessesLogEventId.PipProcessChildrenSurvivedError);
            SetExpectedFailures(1 + numChildrenSurvivedErrors, 0,
                "DX0041",   // ProcessesLogEventId.PipProcessChildrenSurvivedError
                "DX0064");  // ProcessesLogEventId.PipProcessChildrenSurvivedKilled
            XAssert.IsTrue(numChildrenSurvivedErrors >= 1 && numChildrenSurvivedErrors <= 3, $"Child processes: {numChildrenSurvivedErrors}");
            AssertVerboseEventLogged(ProcessesLogEventId.DumpSurvivingPipProcessChildrenStatus, count: numChildrenSurvivedErrors, allowMore: true);
        }

        [Fact]
        public async Task PipChildProcessSurvivesButAllowed()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, TestOutputDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d /c start /B " + CmdHelper.CmdX64 + " /C timeout 1000 > NUL");  // Starts child process that 'hangs' and has to be killed by job object.

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    allowedSurvivingChildProcessNames: ReadOnlyArray<PathAtom>.FromWithoutCopy(
                        PathAtom.Create(context.StringTable, "cmd.exe"),
                        PathAtom.Create(context.StringTable, "conhost.exe")),
                    nestedProcessTerminationTimeout: TimeSpan.Zero
                );

                var sandboxConfiguration = new SandboxConfiguration
                {
                    FileAccessIgnoreCodeCoverage = true
                };

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    sandboxConfiguration,
                    pip,
                    null,
                    new Dictionary<string, string>(),
                    SemanticPathExpander.Default);

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, result.Status);
            }
        }

        [Fact]
        public async Task PipRetry()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string standardOut = tempFiles.GetUniqueFileName();
                AbsolutePath standardOutAbsolutePath = AbsolutePath.Create(context.PathTable, standardOut);
                FileArtifact standardOutFileArtifact = FileArtifact.CreateSourceFile(standardOutAbsolutePath).CreateNextWrittenVersion();

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("Success");
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(),
                    FileArtifact.Invalid,
                    standardOutFileArtifact,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(standardOutFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    testRetries: true);

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true },
                    pip,
                    null,
                    new Dictionary<string, string>(),
                    SemanticPathExpander.Default);

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.ExecutionFailed, result.Status);
                XAssert.AreEqual(RetryReason.ProcessStartFailure, result.RetryInfo?.RetryReason);
                AssertVerboseEventLogged(ProcessesLogEventId.RetryStartPipDueToErrorPartialCopyDuringDetours, SandboxedProcessPipExecutor.ProcessLaunchRetryCountMax);
                AssertWarningEventLogged(LogEventId.PipProcessStartFailed, 1);
            }
        }

        [Fact]
        public async Task CreateDirectoryAllowed()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = tempFiles.GetUniqueDirectory();

                // Make sure the directory is deleted since we want to test that our process creates it
                Directory.Delete(destination);
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(context.PathTable, destination);

                string standardOut = tempFiles.GetUniqueFileName();
                AbsolutePath standardOutAbsolutePath = AbsolutePath.Create(context.PathTable, standardOut);
                FileArtifact standardOutFileArtifact = FileArtifact.CreateSourceFile(standardOutAbsolutePath).CreateNextWrittenVersion();

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("mkdir");
                    arguments.Add(destinationAbsolutePath);
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(),
                    FileArtifact.Invalid,
                    standardOutFileArtifact,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(standardOutFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                List<AbsolutePath> writableRoots = new List<AbsolutePath> { AbsolutePath.Create(context.PathTable, tempFiles.RootDirectory) };

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, EnforceAccessPoliciesOnDirectoryCreation = false },
                    pip,
                    null,
                    new Dictionary<string, string>(),
                    new TestSemanticPathExpander(writableRoots));

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, result.Status);
            }
        }

        [Fact]
        public async Task ProcessUsingUntrackedEnvironmentVariable()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                string destination = tempFiles.GetUniqueFileName();
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(context.PathTable, destination);
                FileArtifact destinationFileArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                string envVarName = "ENV" + Guid.NewGuid().ToString("N");
                Environment.SetEnvironmentVariable(envVarName, "Success", EnvironmentVariableTarget.Process);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("echo");
                    arguments.Add("%" + envVarName + "%");
                    arguments.Add(">");
                    arguments.Add(destinationFileArtifact);
                }

                var pip = new Process(
                    executableFileArtifact,
                    workingDirectoryAbsolutePath,
                    arguments.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                    FileArtifact.Invalid,
                    PipData.Invalid,
                    ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(new EnvironmentVariable(
                        StringId.Create(context.PathTable.StringTable, envVarName),
                        PipData.Invalid,
                        isPassThrough: true)),
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    FileArtifact.Invalid,
                    tempFiles.GetUniqueDirectory(context.PathTable),
                    null,
                    null,
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact /*, responseFileArtifact */),
                    ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(destinationFileArtifact.WithAttributes()),
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true },
                    pip);
                string actual = File.ReadAllText(destination).Trim();
                XAssert.AreEqual(
                    "Success",
                    actual);
            }
        }

        [Fact]
        public async Task UpdatePipProperties()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                // Make sure to handle the case of a pip emitting duplicate properties
                arguments.Add("echo PipProperty_Foo_EndProperty");
                arguments.Add("echo PipProperty_Foo_EndProperty");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    arguments.Add("exit");
                    arguments.Add("1");
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.Empty,
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.FromWithoutCopy(1),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true },
                    pip,
                    null,
                    new Dictionary<string, string>(),
                    SemanticPathExpander.Default,
                    null);

                XAssert.AreEqual(1, result.PipProperties["Foo"]);
                VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
                SetExpectedFailures(1, 0, "DX0064");
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true, requiresWindowsBasedOperatingSystem: true)]
        public async Task DirSymlinksAreProperlyResolvedAsync()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var symbolTable = context.SymbolTable;

            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory))
            {
                string executable = CmdHelper.CmdX64;
                FileArtifact executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, executable));

                string workingDirectory = tempFiles.GetUniqueDirectory();
                string symlinkDir = Path.Combine(workingDirectory, "symlinkDir");
                string targetDirectory = Path.Combine(workingDirectory, "targetDir");
                FileUtilities.CreateDirectory(targetDirectory);

                var success = FileUtilities.TryCreateSymbolicLink(symlinkDir, targetDirectory, isTargetFile: false);
                XAssert.IsTrue(success.Succeeded);

                var sharedOpaqueRoot = Path.Combine(symlinkDir, "sharedOpaque");
                var sharedResolvedOpaqueRoot = Path.Combine(targetDirectory, "sharedOpaque");

                AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, sharedOpaqueRoot);

                // Create two shared opaques with the same root. One will act as an input to the pip, the other one as an
                // output directory
                var sharedOpaqueInput = new DirectoryArtifact(AbsolutePath.Create(context.PathTable, sharedOpaqueRoot), 1, isSharedOpaque: true);
                var sharedOpaqueOutput = new DirectoryArtifact(AbsolutePath.Create(context.PathTable, sharedOpaqueRoot), 2, isSharedOpaque: true);

                var sharedResolvedOpaqueInput = new DirectoryArtifact(AbsolutePath.Create(context.PathTable, sharedResolvedOpaqueRoot), 1, isSharedOpaque: true);
                var sharedResolvedOpaqueOutput = new DirectoryArtifact(AbsolutePath.Create(context.PathTable, sharedResolvedOpaqueRoot), 2, isSharedOpaque: true);

                // The shared opaque input contains input/in.txt
                var inputUndersharedOpaqueRoot = Path.Combine(sharedOpaqueRoot, "input", "in.txt");
                Directory.CreateDirectory(sharedOpaqueRoot);
                Directory.CreateDirectory(Path.Combine(sharedOpaqueRoot, "input"));
                File.WriteAllText(inputUndersharedOpaqueRoot, "Foo");

                var targetDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, targetDirectory);
                var symlinkDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, symlinkDir);
                var sharedOpaqueRootViaRealPath = targetDirectoryAbsolutePath.Combine(context.PathTable, "sharedOpaque");
                var inputViaRealPath = sharedOpaqueRootViaRealPath.Combine(context.PathTable, RelativePath.Create(context.StringTable, "input/in.txt"));
                var outputViaRealPath = sharedOpaqueRootViaRealPath.Combine(context.PathTable, RelativePath.Create(context.StringTable, "nested/out.txt"));

                var arguments = new PipDataBuilder(context.PathTable.StringTable);
                arguments.Add("/d");
                arguments.Add("/c");
                using (arguments.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    // Reads 'input/in.txt' (under the shared opaque input) and creates 'nested/out.txt' (under the shared opaque output)
                    arguments.Add("type");
                    arguments.Add(@"input\in.txt");
                    arguments.Add("&&");
                    arguments.Add("mkdir");
                    arguments.Add("nested");
                    arguments.Add("&&");
                    arguments.Add("echo");
                    arguments.Add("foo");
                    arguments.Add(">");
                    arguments.Add(@"nested\out.txt");
                }

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
                    ReadOnlyArray<FileArtifact>.FromWithoutCopy(executableFileArtifact),
                    ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new DirectoryArtifact[] { sharedOpaqueInput, sharedResolvedOpaqueInput }),
                    ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new DirectoryArtifact[] { sharedOpaqueOutput }),
                    ReadOnlyArray<PipId>.Empty,
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(context.PathTable)),
                    ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(context.PathTable)),
                    ReadOnlyArray<StringId>.Empty,
                    ReadOnlyArray<int>.Empty,
                    ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    new PipProvenance(
                        0,
                        ModuleId.Invalid,
                        StringId.Invalid,
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "SharedOpaqueAccesses")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid,
                        false),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var inputUnderSharedOpaqueAbsolutePath = AbsolutePath.Create(context.PathTable, inputUndersharedOpaqueRoot);

                DirectoryTranslator translator = null;
                if (TryGetSubstSourceAndTarget(out string substSource, out string substTarget))
                {
                    translator = new DirectoryTranslator();
                    translator.AddTranslation(substSource, substTarget);
                    translator.Seal();
                }

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    new SandboxConfiguration
                    {
                        FileAccessIgnoreCodeCoverage = true,
                        FailUnexpectedFileAccesses = false,
                        LogObservedFileAccesses = true,
                        UnsafeSandboxConfiguration = new UnsafeSandboxConfiguration
                        {
                            IgnoreFullReparsePointResolving = false
                        }
                    },
                    pip,
                    fileAccessAllowlist: null,
                    new Dictionary<string, string>(),
                    SemanticPathExpander.Default,
                    new TestDirectoryArtifactContext(
                            SealDirectoryKind.SharedOpaque,
                            new FileArtifact[] { FileArtifact.CreateOutputFile(inputUnderSharedOpaqueAbsolutePath) }),
                    reparsePointResolver: new ReparsePointResolver(context.PathTable, translator));

                var allReportedFileAccesses = result.ObservedFileAccesses.SelectMany(fa => fa.Accesses).ToList();
                allReportedFileAccesses.AddRange(result.UnexpectedFileAccesses.FileAccessViolationsNotAllowlisted);

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, result.Status);

                // There shouldn't be any file access that points to in.txt via the symlink
                XAssert.ContainsNot(allReportedFileAccesses.Select(fa => fa.Path), inputUnderSharedOpaqueAbsolutePath.ToString(context.PathTable));

                // There should be at least one access that points to in.txt via the resolved path
                var inputAccessViaRealPath = allReportedFileAccesses.First(oa => oa.Path == inputViaRealPath.ToString(context.PathTable));
                // The manifest path also has to be resolved (this is a declared input, so the manifest matches the path)
                XAssert.AreEqual(sharedOpaqueRootViaRealPath.ToString(context.PathTable), inputAccessViaRealPath.ManifestPath.ToString(context.PathTable));

                // There should be an access that points to out.txt via the resolved path
                var outputAccessViaRealPath = allReportedFileAccesses.Single(oa => oa.Path == outputViaRealPath.ToString(context.PathTable));
                // The manifest path also has to be resolved (this is an output under a shared opaque, so the manifest is the root of the opaque)
                XAssert.AreEqual(sharedOpaqueRootViaRealPath.ToString(context.PathTable), outputAccessViaRealPath.ManifestPath.ToString(context.PathTable));

                // There should be a read access on the symlink itself, representing the intermediate result of the resolving process
                var symlinkRead = allReportedFileAccesses.First(oa => (oa.Path ?? oa.ManifestPath.ToString(context.PathTable)) == symlinkDirectoryAbsolutePath.ToString(context.PathTable));
                XAssert.IsTrue(symlinkRead.DesiredAccess.HasFlag(DesiredAccess.GENERIC_READ));
            }
        }

        private class TestSemanticPathExpander : SemanticPathExpander
        {
            private readonly IEnumerable<AbsolutePath> m_writableRoots;

            public TestSemanticPathExpander(IEnumerable<AbsolutePath> writableRoots)
            {
                m_writableRoots = writableRoots;
            }

            public override IEnumerable<AbsolutePath> GetWritableRoots()
            {
                return m_writableRoots;
            }
        }

        internal sealed class SandboxedProcessPipExecutionResultWrapper
        {
            internal SandboxedProcessPipExecutionResult Result;

            internal SandboxedProcessPipExecutionResultWrapper(SandboxedProcessPipExecutionResult result)
            {
                Result = result;
            }
        }

        private Task AssertProcessFailsExecution(
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessAllowlist fileAccessAllowlist = null,
            SandboxedProcessPipExecutionResultWrapper resultWrapper = null)
        {
            return AssertProcessCompletesWithStatusAsync(SandboxedProcessPipExecutionStatus.ExecutionFailed, context, config, process, fileAccessAllowlist, resultWrapper);
        }

        private Task AssertProcessFailsPreparation(
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessAllowlist fileAccessAllowlist = null)
        {
            return AssertProcessCompletesWithStatusAsync(SandboxedProcessPipExecutionStatus.PreparationFailed, context, config, process, fileAccessAllowlist);
        }

        private Task AssertProcessSucceedsAsync(
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessAllowlist fileAccessAllowlist = null,
            IDirectoryArtifactContext directoryArtifactContext = null,
            Action<int> processIdListener = null)
        {
            return AssertProcessCompletesWithStatusAsync(
                SandboxedProcessPipExecutionStatus.Succeeded, context, config, process, fileAccessAllowlist,
                directoryArtifactContext: directoryArtifactContext,
                processIdListener: processIdListener);
        }

        private async Task AssertProcessCompletesWithStatusAsync(
            SandboxedProcessPipExecutionStatus status,
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessAllowlist fileAccessAllowlist,
            SandboxedProcessPipExecutionResultWrapper resultWrapper = null,
            IDirectoryArtifactContext directoryArtifactContext = null,
            Action<int> processIdListener = null)
        {
            SandboxedProcessPipExecutionResult result = await RunProcess(context, config, process, fileAccessAllowlist, new Dictionary<string, string>(), SemanticPathExpander.Default, directoryArtifactContext, processIdListener: processIdListener);

            if (resultWrapper != null)
            {
                resultWrapper.Result = result;
            }

            if (result.Status != status)
            {
                VerifyExecutionStatus(context, result, status);
            }
        }

        private static string GetProcessOutput(BuildXLContext context, Tuple<AbsolutePath, Encoding> maybeOutput)
        {
            if (maybeOutput == null)
            {
                return "<stream not saved>";
            }

            string path = maybeOutput.Item1.ToString(context.PathTable);
            if (!File.Exists(path))
            {
                return "<file missing: " + path + ">";
            }

            return File.ReadAllText(path, maybeOutput.Item2);
        }

        private Task<SandboxedProcessPipExecutionResult> RunProcess(
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessAllowlist fileAccessAllowlist,
            IReadOnlyDictionary<string, string> rootMap,
            SemanticPathExpander expander,
            IDirectoryArtifactContext directoryArtifactContext = null,
            Action<int> processIdListener = null,
            ReparsePointResolver reparsePointResolver = null)
        {
            Func<string, Task<bool>> dummyMakeOutputPrivate = pathStr => Task.FromResult(true);
            var loggingContext = CreateLoggingContextForTest();

            var directoryTranslator = new DirectoryTranslator();
            if (TryGetSubstSourceAndTarget(out string substSource, out string substTarget))
            {
                directoryTranslator.AddTranslation(substSource, substTarget);
            }

            directoryTranslator.AddDirectoryTranslationFromEnvironment();
            directoryTranslator.Seal();

            var configuration = new ConfigurationImpl()
            {
                Sandbox = (SandboxConfiguration) config,
                Distribution = new DistributionConfiguration { ValidateDistribution = false },
                Engine = new EngineConfiguration { DisableConHostSharing = false },
                Layout = new LayoutConfiguration { ObjectDirectory = AbsolutePath.Create(context.PathTable, TemporaryDirectory)}
            };

            return new SandboxedProcessPipExecutor(
                context,
                loggingContext,
                process,
                configuration,
                rootMap,
                fileAccessAllowlist,
                null,
                process.AllowPreserveOutputs ? dummyMakeOutputPrivate : null,
                expander,
                new PipEnvironment(loggingContext),
                sidebandState: null,
                directoryArtifactContext: directoryArtifactContext ?? TestDirectoryArtifactContext.Empty,
                tempDirectoryCleaner: MoveDeleteCleaner,
                processIdListener: processIdListener,
                directoryTranslator: directoryTranslator,
                reparsePointResolver: reparsePointResolver).RunAsync(sandboxConnection: GetSandboxConnection());
        }

        private static void VerifyExitCode(BuildXLContext context, SandboxedProcessPipExecutionResult result, int expectedExitCode)
        {
            XAssert.AreEqual(
                expectedExitCode,
                result.ExitCode,
                "Sandboxed process result has the wrong exit code."
                + Environment.NewLine + "Standard error:"
                + Environment.NewLine + "{0}"
                + Environment.NewLine + "Standard output:"
                + Environment.NewLine + "{1}"
                + Environment.NewLine,
                GetProcessOutput(context, result.EncodedStandardError),
                GetProcessOutput(context, result.EncodedStandardOutput));
        }

        private static void VerifyExecutionStatus(
            BuildXLContext context,
            SandboxedProcessPipExecutionResult result,
            SandboxedProcessPipExecutionStatus expectedStatus)
        {
            XAssert.AreEqual(
                expectedStatus,
                result.Status,
                "Sandboxed process result has the wrong status."
                + Environment.NewLine + "Standard error:"
                + Environment.NewLine + "{0}"
                + Environment.NewLine + "Standard output:"
                + Environment.NewLine + "{1}"
                + Environment.NewLine,
                GetProcessOutput(context, result.EncodedStandardError),
                GetProcessOutput(context, result.EncodedStandardOutput));
        }

        private static void VerifyNormalSuccess(BuildXLContext context, SandboxedProcessPipExecutionResult result)
        {
            VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.Succeeded);
            VerifyExitCode(context, result, NativeIOConstants.ErrorSuccess);
        }

        private static void VerifyAccessDenied(BuildXLContext context, SandboxedProcessPipExecutionResult result)
        {
            VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
            VerifyExitCode(context, result, NativeIOConstants.ErrorAccessDenied);
        }

        private static void VerifySharingViolation(BuildXLContext context, SandboxedProcessPipExecutionResult result)
        {
            VerifyExecutionStatus(context, result, SandboxedProcessPipExecutionStatus.ExecutionFailed);
            VerifyExitCode(context, result, NativeIOConstants.ErrorSharingViolation);
        }
    }
}
