// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
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
            FileArtifact inputFile = CreateSourceFile();

            var opsA = new Operation[]
            {
                Operation.ReadFile(inputFile),
                Operation.WriteFile(outputFile1, doNotInfer: true),
                Operation.WriteFile(outputFile2, doNotInfer: true),
            };

            var builderA = CreatePipBuilder(opsA);
            builderA.AddOutputFile(outputFile1, FileExistence.Required);
            builderA.AddOutputFile(outputFile2, FileExistence.Required);

            Process pipA = SchedulePipBuilder(builderA).Process;
            RunScheduler().AssertCacheMiss(pipA.PipId);

            File.Delete(ArtifactToString(outputFile1));
            File.Delete(ArtifactToString(outputFile2));

            ResetPipGraphBuilder();

            var opsB = new Operation[]
            {
                Operation.ReadFile(inputFile),
                Operation.WriteFile(outputFile1, doNotInfer: true),
                Operation.WriteFile(outputFile2, doNotInfer: true),
            };

            var builderB = CreatePipBuilder(opsB);
            builderB.AddOutputFile(outputFile2, FileExistence.Required);
            builderB.AddOutputFile(outputFile1, FileExistence.Required);

            var pipB = SchedulePipBuilder(builderB).Process;
            RunScheduler().AssertCacheHit(pipB.PipId);
        }
    }
}
