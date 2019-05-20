// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using BuildXL.Native.IO;

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

                    AssertErrorEventLogged(EventId.PipProcessExpectedMissingOutputs);
                    AssertErrorEventLogged(EventId.PipProcessError);
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

            AssertVerboseEventLogged(EventId.PipProcessFinishedFailed);
            AssertErrorEventLogged(EventId.PipProcessError);
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
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
            AssertVerboseEventLogged(EventId.PipProcessDisallowedTempFileAccess);
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
            AssertVerboseEventLogged(EventId.PipProcessDisallowedTempFileAccess, count: 3);
        }

        [Fact]
        public async Task ProcessUnexpectedFileAccessWhitelistedByValue()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
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
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "WhitelistedValue")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid),
                    StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var fileAccessWhitelist = new FileAccessWhitelist(context);

                FullSymbol whitelistedValue;
                int characterWithError;
                if (FullSymbol.TryCreate(symbolTable, "WhitelistedValue", out whitelistedValue, out characterWithError) !=
                    FullSymbol.ParseResult.Success)
                {
                    XAssert.Fail("Could not construct a FullSymbol from a string.");
                }

                fileAccessWhitelist.Add(
                    new ValuePathFileAccessWhitelistEntry(
                        whitelistedValue,
                        FileAccessWhitelist.RegexWithProperties(Regex.Escape(Path.GetFileName(undeclaredFileName))),
                        allowsCaching: false,
                        name: "name"));

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false },
                    pip,
                    fileAccessWhitelist);
            }

            AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable);
        }

        [Fact]
        public async Task ProcessUnexpectedFileAccessWhitelistedByValueWithRegex()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
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
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "WhitelistedValue")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var fileAccessWhitelist = new FileAccessWhitelist(context);

                FullSymbol whitelistedValue;
                int characterWithError;
                if (FullSymbol.TryCreate(symbolTable, "WhitelistedValue", out whitelistedValue, out characterWithError) !=
                    FullSymbol.ParseResult.Success)
                {
                    XAssert.Fail("Could not construct a FullSymbol from a string.");
                }

                fileAccessWhitelist.Add(
                    new ValuePathFileAccessWhitelistEntry(
                        whitelistedValue,
                        FileAccessWhitelist.RegexWithProperties(Regex.Escape(PathSuffix) + '$'),
                        allowsCaching: false,
                        name: "name"));

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false },
                    pip,
                    fileAccessWhitelist);
            }

            AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable);
        }

        [Fact]
        public async Task ProcessUnexpectedFileAccessWhitelistedByExecutable()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
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
                        FullSymbol.Invalid.Combine(context.SymbolTable, SymbolAtom.CreateUnchecked(context.StringTable, "WhitelistedValue")),
                        LocationData.Invalid,
                        QualifierId.Unqualified,
                        PipData.Invalid),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                var fileAccessWhitelist = new FileAccessWhitelist(context);

                fileAccessWhitelist.Add(
                    new ExecutablePathWhitelistEntry(
                        executableFileArtifact.Path,
                        FileAccessWhitelist.RegexWithProperties(Regex.Escape(Path.GetFileName(undeclaredFileName))),
                        allowsCaching: false,
                        name: "name"));

                await AssertProcessSucceedsAsync(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false },
                    pip,
                    fileAccessWhitelist);
            }

            AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable);
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
                        PipData.Invalid),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context, 
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true, FailUnexpectedFileAccesses = false }, 
                    pip, 
                    fileAccessWhitelist: null, 
                    new Dictionary<string, string>(), 
                    SemanticPathExpander.Default, 
                    new TestDirectoryArtifactContext(
                            SealDirectoryKind.SharedOpaque,
                            new FileArtifact[] { FileArtifact.CreateOutputFile(AbsolutePath.Create(context.PathTable, inputUndersharedOpaqueRoot)) }));

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, result.Status);

                // There should be a single reported file access: The attempt to read 'input/in.txt'. The accesses related to outputs (creating the nested output
                // directory and writing the file) should not be reported here
                ObservedFileAccess access = result.ObservedFileAccesses.Single();
                XAssert.AreEqual(AbsolutePath.Create(context.PathTable, inputUndersharedOpaqueRoot), access.Path);
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
                if (EventListener.GetEventCount(EventId.PipFailedToCreateDumpFile) == 1 &&
                    (EventListener.GetLog().Contains("-2147024597")) // -2147024597 && 0x0FFF == 0x12B (ERROR_PARTIAL_COPY)
                    || EventListener.GetLog().Contains("80070018")) // win32 error code 18 is (ERROR_BAD_LENGTH)
                {
                    failedToCreateDump = true;
                }
            }

            if (failedToCreateDump)
            {
                SetExpectedFailures(2, 1, "DX0016", "DX0064", "DX2210");
            }
            else
            {
                SetExpectedFailures(2, 0, "DX0016", "DX0064");
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
                arguments.Add("/d /c start " + CmdHelper.CmdX64 + " /k");  // Starts child process that hangs on console input and has to be killed by job object.

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

                SandboxedProcessPipExecutionResult result = await RunProcess(
                    context,
                    new SandboxConfiguration { FileAccessIgnoreCodeCoverage = true },
                    pip,
                    null,
                    new Dictionary<string, string>(),
                    SemanticPathExpander.Default);

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.ExecutionFailed, result.Status);
            }

            // conhost.exe may or may not be started within the pip depending on
            // the current machine state. And each cmd might have its own conhost.
            int numChildrenSurvivedErrors = EventListener.GetEventCount(EventId.PipProcessChildrenSurvivedError);
            SetExpectedFailures(1 + numChildrenSurvivedErrors, 0,
                "DX0041",  // EventId.PipProcessChildrenSurvivedError
                "DX0064");  // EventId.PipProcessChildrenSurvivedKilled
            XAssert.IsTrue(numChildrenSurvivedErrors >= 1 && numChildrenSurvivedErrors <= 3, $"Child processes: {numChildrenSurvivedErrors}");
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
                arguments.Add("/d /c start " + CmdHelper.CmdX64 + " /k");  // Starts child process that hangs on console input and has to be killed by job object.

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

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.PreparationFailed, result.Status);
                XAssert.AreEqual(SandboxedProcessPipExecutor.ProcessLaunchRetryCountMax, result.NumberOfProcessLaunchRetries);
                AssertVerboseEventLogged(EventId.RetryStartPipDueToErrorPartialCopyDuringDetours, SandboxedProcessPipExecutor.ProcessLaunchRetryCountMax);
            }

            SetExpectedFailures(1, 0, "DX0011");
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

        private static Task AssertProcessFailsExecution(
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessWhitelist fileAccessWhitelist = null,
            SandboxedProcessPipExecutionResultWrapper resultWrapper = null)
        {
            return AssertProcessCompletesWithStatus(SandboxedProcessPipExecutionStatus.ExecutionFailed, context, config, process, fileAccessWhitelist, resultWrapper);
        }

        private static Task AssertProcessFailsPreparation(
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessWhitelist fileAccessWhitelist = null)
        {
            return AssertProcessCompletesWithStatus(SandboxedProcessPipExecutionStatus.PreparationFailed, context, config, process, fileAccessWhitelist);
        }

        private static Task AssertProcessSucceedsAsync(
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessWhitelist fileAccessWhitelist = null,
            IDirectoryArtifactContext directoryArtifactContext = null)
        {
            return AssertProcessCompletesWithStatus(SandboxedProcessPipExecutionStatus.Succeeded, context, config, process, fileAccessWhitelist, directoryArtifactContext: directoryArtifactContext);
        }

        private static async Task AssertProcessCompletesWithStatus(
            SandboxedProcessPipExecutionStatus status,
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessWhitelist fileAccessWhitelist,
            SandboxedProcessPipExecutionResultWrapper resultWrapper = null,
            IDirectoryArtifactContext directoryArtifactContext = null)
        {
            SandboxedProcessPipExecutionResult result = await RunProcess(context, config, process, fileAccessWhitelist, new Dictionary<string, string>(), SemanticPathExpander.Default, directoryArtifactContext);

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

        private static Task<SandboxedProcessPipExecutionResult> RunProcess(
            BuildXLContext context,
            ISandboxConfiguration config,
            Process process,
            FileAccessWhitelist fileAccessWhitelist,
            IReadOnlyDictionary<string, string> rootMap,
            SemanticPathExpander expander,
            IDirectoryArtifactContext directoryArtifactContext = null)
        {
            Func<FileArtifact, Task<bool>> dummyMakeOutputPrivate = artifact => Task.FromResult(true);
            var loggingContext = CreateLoggingContextForTest();

            return new SandboxedProcessPipExecutor(
                context,
                loggingContext,
                process,
                config,
                null, // Not full instantiation of the engine. For such cases this is null.
                null, // Not full instantiation of the engine. For such cases this is null.
                rootMap,
                new ProcessInContainerManager(loggingContext, context.PathTable),
                fileAccessWhitelist,
                null,
                process.AllowPreserveOutputs ? dummyMakeOutputPrivate : null,
                expander,
                false,
                new PipEnvironment(),
                validateDistribution: false,
                directoryArtifactContext: directoryArtifactContext ?? TestDirectoryArtifactContext.Empty).RunAsync(sandboxedKextConnection: GetSandboxedKextConnection());
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
