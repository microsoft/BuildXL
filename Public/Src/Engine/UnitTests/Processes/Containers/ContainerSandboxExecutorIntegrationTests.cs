// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    [Trait("Category", "WindowsOSOnly")]
    public sealed class ContainerSandboxExecutorIntegrationTests : XunitBuildXLTest
    {
        public ContainerSandboxExecutorIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        /// <summary>
        /// Verifies that after the sandboxed process executor is done running an in-container pip,
        /// the expected result is valid
        /// </summary>
        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public async Task ProcessInContainerPassesAllSandboxValidations()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pathTable = context.PathTable;

                var outputFile = tempFiles.GetUniqueFileName();
                var outputFilePath = AbsolutePath.Create(pathTable, outputFile);

                var pip = CreateOutputFileProcess(context, tempFiles, outputFilePath);
                var pipExecutionResult = await RunProcess(context, pip);

                // Observe that the fact the execution result succeeds means that the expected outputs are in their expected place
                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, pipExecutionResult.Status);
            }
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public async Task ProcessInContainerGeneratingNestedOutputsPassesAllSandboxValidations()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pathTable = context.PathTable;

                var outputFile = tempFiles.GetUniqueFileName(@"outputs\");
                var outputFileNested = tempFiles.GetUniqueFileName(@"outputs\nested\");

                var outputFilePath = AbsolutePath.Create(pathTable, outputFile);
                var outputFileNestedPath = AbsolutePath.Create(pathTable, outputFileNested);

                FileUtilities.CreateDirectory(outputFileNestedPath.GetParent(pathTable).ToString(pathTable));

                // Arguments to create two output files, one nested under the other
                var arguments = $"echo hi > {outputFile} && echo bye > {outputFileNested}";
                var outputs = ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                    new[] 
                    {
                        FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(outputFilePath), FileExistence.Required),
                        FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(outputFileNestedPath), FileExistence.Required)
                    });

                var pip = CreateConsoleProcessInContainer(context, tempFiles, pathTable, arguments, outputs, CollectionUtilities.EmptyArray<DirectoryArtifact>().ToReadOnlyArray());
                var pipExecutionResult = await RunProcess(context, pip);

                // Observe that the fact the execution result succeeds means that the expected outputs are in their expected place
                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, pipExecutionResult.Status);
            }
        }


        /// <summary>
        /// The isolation levels provided are assumed to be unique values (and not a combination of values). It wouldn't be hard to generalize it
        /// but it is not worth testing at that granularity level
        /// </summary>
        [TheoryIfSupported(requiresHeliumDriversAvailable: true)]
        [InlineData(ContainerIsolationLevel.IsolateOutputFiles)]
        [InlineData(ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories)]
        [InlineData(ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories)]
        public async Task IsolationLevelControlsWriteRedirection(ContainerIsolationLevel containerIsolationLevel)
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pathTable = context.PathTable;

                var outputFile = tempFiles.GetUniqueFileName(@"fileOutputs\");
                var opaqueOutputFile = tempFiles.GetUniqueFileName(@"directoryOutputs\");

                var outputFilePath = AbsolutePath.Create(pathTable, outputFile);
                var opaqueOutputFilePath = AbsolutePath.Create(pathTable, opaqueOutputFile);

                var arguments = $"echo hi > {outputFile} && echo bye > {opaqueOutputFile}";
                var outputs = ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                    new[]
                    {
                        FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(outputFilePath), FileExistence.Required),
                    });

                var opaqueOutputs = ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(
                    new[] 
                    {
                        new DirectoryArtifact(opaqueOutputFilePath.GetParent(pathTable), 1, isSharedOpaque: containerIsolationLevel == ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories),
                    }
                );

                var pip = CreateConsoleProcessInContainer(context, tempFiles, pathTable, arguments, outputs, opaqueOutputs, containerIsolationLevel);
                var pipExecutionResult = await RunProcess(context, pip);

                XAssert.AreEqual(SandboxedProcessPipExecutionStatus.Succeeded, pipExecutionResult.Status);

                var redirectedDirForFiles = pip.UniqueRedirectedDirectoryRoot.Combine(pathTable, "fileOutputs");
                var redirectedFile = redirectedDirForFiles.Combine(pathTable, outputFilePath.GetName(pathTable)).ToString(pathTable);

                var redirectedDirForDirectories = pip.UniqueRedirectedDirectoryRoot.Combine(pathTable, "directoryOutputs");
                var redirectedOpaqueFile = redirectedDirForDirectories.Combine(pathTable, opaqueOutputFilePath.GetName(pathTable)).ToString(pathTable);

                // Make sure outputs got redirected based on the configured isolation level
                switch (containerIsolationLevel)
                {
                    case ContainerIsolationLevel.IsolateOutputFiles:
                        XAssert.IsTrue(File.Exists(redirectedFile));
                        break;
                    case ContainerIsolationLevel.IsolateExclusiveOpaqueOutputDirectories:
                    case ContainerIsolationLevel.IsolateSharedOpaqueOutputDirectories:
                        XAssert.IsTrue(File.Exists(redirectedOpaqueFile));
                        break;
                }
            }
        }

        [FactIfSupported(requiresHeliumDriversAvailable: true)]
        public async Task TombstoneFileDoesNotRepresentARequiredOutput()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pathTable = context.PathTable;

                var outputFile = tempFiles.GetUniqueFileName();
                var outputFilePath = AbsolutePath.Create(pathTable, outputFile);

                var renamedFile = $"{outputFile}.renamed";
                var renamedFilePath = AbsolutePath.Create(pathTable, renamedFile);

                // Arguments to create an output file that gets immediately renamed.
                // However, both files are marked as required, even though the original one 
                // is not there after the rename happens
                var arguments = $"echo hi > {outputFile} && move {outputFile} {renamedFile}";
                var outputs = ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                    new[]
                    {
                        FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(outputFilePath), FileExistence.Required),
                        FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(renamedFilePath), FileExistence.Required)
                    });

                var pip = CreateConsoleProcessInContainer(context, tempFiles, pathTable, arguments, outputs, CollectionUtilities.EmptyArray<DirectoryArtifact>().ToReadOnlyArray());
                // the move command under the console seems to have some issue with the detours policy
                // This is orthogonal to the test, and we don't care about detours at this point
                var pipExecutionResult = await RunProcess(context, pip, failUnexpectedFileAccesses: false);

                // The redirected output is created as a tombstone file, but the sandboxed pip executor should report it as an absent file
                AssertErrorEventLogged(EventId.PipProcessMissingExpectedOutputOnCleanExit);
                AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessExpectedMissingOutputs);
            }
        }

        private Task<SandboxedProcessPipExecutionResult> RunProcess(BuildXLContext context, Process pip, bool failUnexpectedFileAccesses = true)
        {
            var loggingContext = CreateLoggingContextForTest();

            return new SandboxedProcessPipExecutor(
                            context,
                            loggingContext,
                            pip,
                            new SandboxConfiguration { FailUnexpectedFileAccesses = failUnexpectedFileAccesses },
                            layoutConfig: null,
                            loggingConfig: null,
                            rootMappings: new Dictionary<string, string>(),
                            processInContainerManager: new ProcessInContainerManager(loggingContext, context.PathTable),
                            whitelist: null,
                            makeInputPrivate: null,
                            makeOutputPrivate: null,
                            semanticPathExpander: SemanticPathExpander.Default,
                            disableConHostSharing: false,
                            pipEnvironment: new PipEnvironment(),
                            validateDistribution: false,
                            tempDirectoryCleaner: new TestMoveDeleteCleaner(TestOutputDirectory),
                            directoryArtifactContext: TestDirectoryArtifactContext.Empty).RunAsync();
        }

        private static Process CreateOutputFileProcess(BuildXLContext context, TempFileStorage tempFiles, AbsolutePath outputFilePath)
        {
            // Arguments to create an output file
            var arguments = $"echo hi > {outputFilePath.ToString(context.PathTable)}";
            var outputs = ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                new[] { FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(outputFilePath), FileExistence.Required) });

            var pip = CreateConsoleProcessInContainer(context, tempFiles, context.PathTable, arguments, outputs, CollectionUtilities.EmptyArray<DirectoryArtifact>().ToReadOnlyArray());
            return pip;
        }

        private static Process CreateConsoleProcessInContainer(
            BuildXLContext context, 
            TempFileStorage tempFiles, 
            PathTable pt, 
            string arguments, 
            ReadOnlyArray<FileArtifactWithAttributes> outputFiles, 
            ReadOnlyArray<DirectoryArtifact> directoryOutputs,
            ContainerIsolationLevel containerIsolationLevel = ContainerIsolationLevel.IsolateAllOutputs)
        {
            var executableFileArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, CmdHelper.CmdX64));

            var argumentBuilder = new PipDataBuilder(context.PathTable.StringTable);
            argumentBuilder.Add("/d");
            argumentBuilder.Add("/c");
            using (argumentBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
            {
                foreach(var arg in arguments.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    argumentBuilder.Add(arg);
                }
            }

            string workingDirectory = tempFiles.GetUniqueDirectory();
            var workingDirectoryAbsolutePath = AbsolutePath.Create(context.PathTable, workingDirectory);

            string uniqueOutputDirectory = tempFiles.GetUniqueDirectory();
            var uniqueOutputDirectoryPath = AbsolutePath.Create(context.PathTable, uniqueOutputDirectory);

            string uniqueRedirectedOutputDirectory = tempFiles.GetUniqueDirectory("redirected");
            var uniqueRedirectedOutputDirectoryPath = AbsolutePath.Create(context.PathTable, uniqueRedirectedOutputDirectory);

            var pip = new Process(
                executableFileArtifact,
                workingDirectoryAbsolutePath,
                argumentBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                FileArtifact.Invalid,
                PipData.Invalid,
                ReadOnlyArray<EnvironmentVariable>.FromWithoutCopy(),
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                FileArtifact.Invalid,
                tempFiles.GetUniqueDirectory(pt),
                null,
                null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[] { executableFileArtifact }),
                outputs: outputFiles,
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: directoryOutputs,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                options: Process.Options.NeedsToRunInContainer,
                uniqueOutputDirectory: uniqueOutputDirectoryPath,
                uniqueRedirectedDirectoryRoot: uniqueRedirectedOutputDirectoryPath,
                containerIsolationLevel: containerIsolationLevel);

            return pip;
        }
    }
}
