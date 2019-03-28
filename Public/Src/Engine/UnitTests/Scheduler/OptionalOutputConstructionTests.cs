// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Builders;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.Scheduler.SchedulerTestHelper;

namespace Test.BuildXL.Scheduler
{
    [Feature(Features.OptionalOutput)]
    public class OptionalOutputConstructionTest : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public OptionalOutputConstructionTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void PipCanDependOnOptionalOutput()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath outputPath = env.Paths.CreateAbsolutePath(A("x", "test.optional"));

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputFile(outputPath, FileExistence.Optional);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                outputs1.TryGetOutputFile(outputPath, out var outputFile);
                pip2.AddInputFile(outputFile);
                pip2.AddOutputFile(env.Paths.CreateAbsolutePath(A("x", "dummyOutput")));
                env.PipConstructionHelper.AddProcess(pip2);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Theory]
        [InlineData(FileExistence.Required)]
        [InlineData(FileExistence.Optional)]
        public void PipCanRewriteOptionalOutput(FileExistence rewrittenFileExistence)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath outputPath = env.Paths.CreateAbsolutePath(A("x", "test"));

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputFile(outputPath, FileExistence.Optional);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                outputs1.TryGetOutputFile(outputPath, out var outputFile);
                pip2.AddInputFile(outputFile);
                pip2.AddOutputFile(outputFile, rewrittenFileExistence);
                env.PipConstructionHelper.AddProcess(pip2);

                AssertSuccessGraphBuilding(env);
            }
        }
    }
}
