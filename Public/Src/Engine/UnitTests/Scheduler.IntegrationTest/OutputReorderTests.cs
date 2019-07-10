// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class OutputReorderTests : SchedulerIntegrationTestBase
    {
        public OutputReorderTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void ReorderOutputsCacheHit()
        {
            FileArtifact outputFile1 = CreateSourceFile();
            FileArtifact outputFile2 = CreateSourceFile();

            var builderA = CreatePipBuilder(new Operation[]{});
            builderA.AddOutputFile(outputFile1, FileExistence.Optional);
            builderA.AddOutputFile(outputFile2, FileExistence.Optional);

            Process pipA = SchedulePipBuilder(builderA).Process;
            RunScheduler().AssertCacheMiss(pipA.PipId);

            ResetPipGraphBuilder();

            var builderB = CreatePipBuilder(new Operation[] { });
            builderB.AddOutputFile(outputFile2, FileExistence.Optional);
            builderB.AddOutputFile(outputFile1, FileExistence.Optional);

            var pipB = SchedulePipBuilder(builderB).Process;
            RunScheduler().AssertCacheHit(pipB.PipId);
        }
    }
}
