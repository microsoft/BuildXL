// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

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
        public void CallBoostObserverUtilitiesTests()
        {
            RunTest("observer_utilities_test");
        }

        private SandboxedProcessResult RunTest(string testExeName)
        {
            var testExecutable = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, Path.Combine(TestBinRoot, "LinuxTestProcesses", testExeName)));
            XAssert.IsTrue(File.Exists(testExecutable.Path.ToString(Context.PathTable)), $"Test executable '{testExecutable.Path.ToString(Context.PathTable)}' not found.");

            using TempFileStorage tempFileStorage = new TempFileStorage(canGetFileNames: true);

            var workingDirectoryAbsolutePath = AbsolutePath.Create(Context.PathTable, tempFileStorage.RootDirectory);
            var arguments = new PipDataBuilder(Context.PathTable.StringTable);
            var allDependencies = new List<FileArtifact> { testExecutable };
            var allOutputs = new List<FileArtifactWithAttributes>(1);

            var dummyOutputArtifact = FileArtifact.CreateSourceFile(tempFileStorage.GetFileName(Context.PathTable, "dummy"));
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
                tempFileStorage.GetUniqueDirectory(Context.PathTable),
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

            var processInfo = ToProcessInfo(pip);
            var result = RunProcess(processInfo).Result;

            XAssert.AreEqual(result.ExitCode, 0);

            return result;
        }
    }
}
