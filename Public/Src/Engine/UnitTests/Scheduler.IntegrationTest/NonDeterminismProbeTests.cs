// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class NonDeterminismProbeTests : SchedulerIntegrationTestBase
    {

        public NonDeterminismProbeTests(ITestOutputHelper output) : base(output)
        {
        }

        [Feature(Features.OpaqueDirectory)]
	    [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NonDeterminismOpaqueDirectoryOutput(bool fileListedAsNormalOutput)
        {
            // Set up PipA => opaqueDirectory
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "opaquedir"));

            var builderA = CreatePipBuilder(new Operation[]
            {
                // writes a guid to the file when there is no content specified (makes it nondeterministic)
                // the argument, doNotInfer, means that pipA will not declare outputInOpaque as an
                // output (so BuildXL will not expect a write to that file).
                Operation.WriteFile(CreateOutputFileArtifact(opaqueDirPath), doNotInfer: !fileListedAsNormalOutput)
            });

            builderA.AddOutputDirectory(opaqueDirPath);
            SchedulePipBuilder(builderA);

            RunScheduler().AssertSuccess();

            // Ensure no determinism probe events for first build where determinism probe is not enabled
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicOutput, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredProcessThatCannotRunFromCache, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredPipFailure, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeDetectedUnexpectedMismatch, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUncacheablePip, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredOutputDirectoryDifferentFiles, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicDirectoryOutput, 0);

            Configuration.Cache.DeterminismProbe = true;
            RunScheduler().AssertSuccess();

            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredProcessThatCannotRunFromCache, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredPipFailure, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeDetectedUnexpectedMismatch, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUncacheablePip, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredOutputDirectoryDifferentFiles, 0);

            // Here we are testing that the changed file content in an opaque dir is detected
            if (fileListedAsNormalOutput)
            {
                AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicOutput, 1);
            }
            else
            {
                AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicOutput, 0);
            }

            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicDirectoryOutput, 1);
        }

        [Feature(Features.OpaqueDirectory)]
	    [Fact]
        public void NonDeterminismOpaqueDirectoryOutputDifferentFiles()
        {
            string untracked = Path.Combine(ObjectRoot, "untracked.txt");

            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "opaquedir"));

            // all outputs have deterministc content, the first and third file will be written
            // depending on the content of the untracked file.
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFileIfInputEqual(CreateOutputFileArtifact(opaqueDirPath, prefix: "write-if-1"), untracked, "1", "deterministic-content"),
                Operation.WriteFile(CreateOutputFileArtifact(opaqueDirPath, prefix: "write-always"), "deterministic-content", doNotInfer: true),
                Operation.WriteFileIfInputEqual(CreateOutputFileArtifact(opaqueDirPath, prefix: "write-if-2"), untracked, "2", "deterministic-content"),
            });

            builderA.AddOutputDirectory(opaqueDirPath);
            builderA.AddUntrackedFile(AbsolutePath.Create(Context.PathTable, untracked));
            SchedulePipBuilder(builderA);

            // set untracked to 1 so the first and second file are written by the pip
            File.WriteAllText(untracked, "1");

            RunScheduler().AssertSuccess();

            // Ensure no determinism probe events for first build where determinism probe is not enabled
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicOutput, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredProcessThatCannotRunFromCache, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredPipFailure, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeDetectedUnexpectedMismatch, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUncacheablePip, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredOutputDirectoryDifferentFiles, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicDirectoryOutput, 0);


            // set untracked to 2 so the second and third file are written by the pip
            File.WriteAllText(untracked, "2");

            Configuration.Cache.DeterminismProbe = true;
            RunScheduler().AssertSuccess();

            // Here we are testing that the set of file paths
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicOutput, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredProcessThatCannotRunFromCache, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredPipFailure, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeDetectedUnexpectedMismatch, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredUncacheablePip, 0);
            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredNondeterministicDirectoryOutput, 0);

            AssertInformationalEventLogged(EventId.DeterminismProbeEncounteredOutputDirectoryDifferentFiles, 1);
        }
    }
}
