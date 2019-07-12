// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using System.IO;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    /// <summary>
    /// Incremental build tests for <see cref="DirectoryArtifact" /> dependencies.
    /// </summary>
    [Trait("Category", "DeterminismProbeTests")]
    [Trait("Category", "WindowsOSOnly")] // heavily depends on cmd.exe
    public sealed class DeterminismProbeTests : IncrementalBuildTestBase
    {
        public DeterminismProbeTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        [Fact]
        public void DetectNonDeterminism()
        {
            SetupTestState();

            Configuration.Sandbox.UnsafeSandboxConfiguration = new UnsafeSandboxConfiguration(Configuration.Sandbox.UnsafeSandboxConfiguration) { UnexpectedFileAccessesAreErrors = false };
            string undeclaredFilePath = Path.Combine(Configuration.Layout.SourceDirectory.ToString(PathTable), "undeclared.cpp");

            var snapshot = EventListener.SnapshotEventCounts();

            var paths = GetBuildPaths(Configuration, PathTable);
            File.Delete(paths.NonDeterministicTool_ReadAdditionalFileIndicator);
            EagerCleanBuild("Build #1");

            // Ensure no determinism probe events for first build where determinism probe is not enabled
            XAssert.AreEqual(0, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeEncounteredNondeterministicOutput, snapshot));
            XAssert.AreEqual(0, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeEncounteredProcessThatCannotRunFromCache, snapshot));
            XAssert.AreEqual(0, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch, snapshot));
            XAssert.AreEqual(0, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeDetectedUnexpectedMismatch, snapshot));

            // Write the indicator file so the second execution will probe an additional file path
            // thereby adding an absent path probe and changing the strong fingerprint
            File.WriteAllText(paths.NonDeterministicTool_ReadAdditionalFileIndicator, "Read additional file");

            Configuration.Cache.DeterminismProbe = true;
            CounterCollection<PipExecutorCounter> counters = null;
            Build("Build #2", scheduler =>
            {
                counters = scheduler.PipExecutionCounters;
            });

            XAssert.AreEqual(1, counters.GetCounterValue(PipExecutorCounter.ProcessPipDeterminismProbeDifferentFiles));
            XAssert.AreEqual(3, counters.GetCounterValue(PipExecutorCounter.ProcessPipDeterminismProbeSameFiles));

            // Verify determinism events were logged
            XAssert.AreEqual(1, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeEncounteredNondeterministicOutput, snapshot));
            XAssert.AreEqual(0, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeEncounteredProcessThatCannotRunFromCache, snapshot));
            XAssert.AreEqual(1, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch, snapshot));
            XAssert.AreEqual(0, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeDetectedUnexpectedMismatch, snapshot));


            // Now perform a build where the pip is uncacheable
            File.WriteAllText(undeclaredFilePath, "whatever");
            Build("Build #3");
            File.Delete(undeclaredFilePath);
            // Warnings for the file access violation, uncacheable pip, and dependency analyzer
            AssertWarningEventLogged(EventId.FileMonitoringWarning);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 2);

            // Determinism validation cannot be performed
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUncacheablePip);

            // Perform a build where the pip fails
            
            File.Delete(paths.NonDeterministicTool_ProbedFile);

            // The absence of the above file causes the pip to produce fewer files than expected. The
            // test executes with this condition in order to ensure that the determinism probe properly
            // handles pip failures.
            Configuration.Cache.DeterminismProbe = true;

            FailedBuild("Build #4");
            AssertVerboseEventLogged(EventId.PipProcessMissingExpectedOutputOnCleanExit);
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessExpectedMissingOutputs);
            AssertErrorEventLogged(EventId.PipProcessError);

            // Verify DeterminismProbeEncounteredPipFailure was logged
            XAssert.AreEqual(1, EventListener.GetEventCountSinceSnapshot(EventId.DeterminismProbeEncounteredPipFailure, snapshot));
        }

        protected override string GetSpecContents()
        {
            return @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';

const inc = Transformer.sealDirectory(
    d`inc`, 
    [
        f`inc/a.h`,
        f`inc/b.h`,
    ]);

const runNonDeterministic = Transformer.execute({
    tool: {
        exe: cmd,
        dependsOnWindowsDirectories: true,
        untrackedFiles: [
            f`NonDeterministicStrongFingerprintIndicator.txt`,
            f`NonDeterministicToolProbedFile.txt`,
        ],
    },
    arguments: [
        Cmd.rawArgument('/d /c call NonDeterminism.bat'),
    ],
    workingDirectory: d`.`,
    dependencies: [
        f`NonDeterminism.bat`,
        inc,
    ],
    outputs: [
        p`obj/det.txt`,
        p`obj/nondet.txt`,
        p`obj/notalways.txt`,
    ],
});

const runDeterministic = Transformer.execute({
    tool: {
        exe: cmd,
        dependsOnWindowsDirectories: true,
    },
    arguments: [
        Cmd.rawArgument('/d /c echo constantvalue > '),
        Cmd.argument(Artifact.output(p`obj\const.txt`)),
    ],
    workingDirectory: d`.`,
});
";
        }

        /// <summary>
        /// Number of pips that would run on a clean build.
        /// </summary>
        protected override int TotalPips => 2;

        /// <summary>
        /// Number of outputs that would be copied from the cache on a fully-cached build.
        /// </summary>
        protected override int TotalPipOutputs => 4;

        private static Paths GetBuildPaths(IConfiguration config, PathTable pathTable)
        {
            var sourceDirectoryPath = config.Layout.SourceDirectory.ToString(pathTable);
            var objectDirectoryPath = config.Layout.ObjectDirectory.ToString(pathTable);

            return new Paths
            {
                NonDeterministicTool_ReadAdditionalFileIndicator = Path.Combine(sourceDirectoryPath, @"NonDeterministicStrongFingerprintIndicator.txt"),
                NonDeterministicTool_DeterministicOutput = Path.Combine(sourceDirectoryPath, @"obj\det.txt"),
                NonDeterministicTool_NonDeterministicOutput = Path.Combine(sourceDirectoryPath, @"obj\nondet.txt"),
                DeterministicToolOutput = Path.Combine(sourceDirectoryPath, @"obj\const.txt"),
                NonDeterministicTool_ProbedFile = Path.Combine(sourceDirectoryPath, @"NonDeterministicToolProbedFile.txt"),
            };
        }

        protected override void WriteInitialSources()
        {
            var nonDeterministicToolScript = @"
REM Write non-deterministic outputs
echo %TIME% > obj\nondet.txt

REM Write deterministic output
echo xyz > obj\det.txt

IF EXIST NonDeterministicStrongFingerprintIndicator.txt (
    IF EXIST inc\a.h (
        REM Do nothing, this is only here to probe
        echo foundHeader > obj\nondet.txt
    )
    IF EXIST undeclared.cpp (
       echo noOp
    )
)

IF EXIST NonDeterministicToolProbedFile.txt (
   echo inconsistentOutput > obj\notalways.txt
)

exit /b 0
";
            AddFile("NonDeterminism.bat", nonDeterministicToolScript);
            AddFile(@"inc\a.h", nonDeterministicToolScript);
            AddFile(@"inc\b.h", nonDeterministicToolScript);
            AddFile(@"NonDeterministicToolProbedFile.txt", "Trigger inconsistent PIP behavior");
        }

        protected override void VerifyOutputsAfterBuild(IConfiguration config, PathTable pathTable)
        {
        }

        private sealed class Paths
        {
            public string NonDeterministicTool_ReadAdditionalFileIndicator;
            public string NonDeterministicTool_DeterministicOutput;
            public string NonDeterministicTool_NonDeterministicOutput;
            public string DeterministicToolOutput;
            public string NonDeterministicTool_ProbedFile;
        }
    }
}
