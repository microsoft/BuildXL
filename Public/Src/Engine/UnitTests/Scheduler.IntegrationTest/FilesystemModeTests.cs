// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Pips.Operations;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.Executables.TestProcess;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class FilesystemModeTests : SchedulerIntegrationTestBase
    {
        public FilesystemModeTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void EventualFilesystemUsedForProbe()
        {
            // pipA enumerates the directory where its output and pipB's output goes. It is forced to run before B because
            // B consumes its output
            var outA = CreateOutputFileArtifact(ObjectRoot);
            var opsA = new Operation[]
            {
                Operation.Probe(CreateSourceFile()),
                Operation.EnumerateDir(CreateOutputDirectoryArtifact(ObjectRoot)),
                Operation.WriteFile(outA)
            };
            Process pipA = CreateAndSchedulePipBuilder(opsA).Process;

            var opsB = new Operation[]
            {
                Operation.Probe(outA),
                Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot))
            };
            Process pipB = CreateAndSchedulePipBuilder(opsB).Process;

            // Perform build with full graph filesystem. Both output files should be produced
            ScheduleRunResult result = RunScheduler().AssertSuccess();
            result.AssertCacheMiss(pipA.PipId);
            result.AssertCacheMiss(pipB.PipId);

            // Perform build with full graph filesystem. Both processes should be a cache hit, even though the directory
            // that got enumerated changed state
            ScheduleRunResult result2 = RunScheduler().AssertSuccess();

            XAssert.AreEqual(PipResultStatus.UpToDate, result2.PipResults[pipA.PipId]);
            XAssert.AreEqual(PipResultStatus.UpToDate, result2.PipResults[pipB.PipId]);
        }
    }
}
