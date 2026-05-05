// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace IntegrationTest.BuildXL.Scheduler
{
    [TestClassIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
    public class MustRunOnOrchestratorTests : SchedulerIntegrationTestBase
    {
        public MustRunOnOrchestratorTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void ProcessPipWithMustRunOnOrchestratorOptionIsSet()
        {
            var outFile = CreateOutputFileArtifact();
            var pipBuilder = CreatePipBuilder(new Operation[] { Operation.WriteFile(outFile) });
            pipBuilder.Options |= Process.Options.MustRunOnOrchestrator;

            var pip = SchedulePipBuilder(pipBuilder).Process;

            // Verify the option is set on the constructed process pip
            Assert.True(pip.MustRunOnOrchestrator);
        }

        [Fact]
        public void MustRunOnOrchestratorDoesNotAffectCaching()
        {
            // First run without the flag - expect a cache miss
            var outFile = CreateOutputFileArtifact();
            var pipBuilder = CreatePipBuilder(new Operation[] { Operation.WriteFile(outFile) });
            var pip = SchedulePipBuilder(pipBuilder).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);

            // Second run still without the flag - expect a cache hit
            RunScheduler().AssertCacheHit(pip.PipId);

            // Third run: rebuild the graph with MustRunOnOrchestrator set
            // The fingerprint should remain the same, so we still get a cache hit
            ResetPipGraphBuilder();
            pipBuilder = CreatePipBuilder(new Operation[] { Operation.WriteFile(outFile) });
            pipBuilder.Options |= Process.Options.MustRunOnOrchestrator;
            pip = SchedulePipBuilder(pipBuilder).Process;
            RunScheduler().AssertCacheHit(pip.PipId);
        }
    }
}
