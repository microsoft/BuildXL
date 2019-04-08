// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    [Feature(Features.RewrittenFile)]
    public class RewritingProcessGraphConstructionTest : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public RewritingProcessGraphConstructionTest(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 3, MemberType = typeof(TruthTable))]
        public void WarnWhenPreservedOutputsAreRewritten(bool pip1PreserveOutput, bool pip2PreserveOutput, bool inPlaceRewriteForPip2)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath input = env.Paths.CreateAbsolutePath(@"\\dummyPath\input");
                AbsolutePath output = env.Paths.CreateAbsolutePath(@"\\dummyPath\output");
                
                var pip1 = CreatePipBuilder(env);
                pip1.AddInputFile(FileArtifact.CreateSourceFile(input));
                pip1.AddOutputFile(output);

                if (pip1PreserveOutput)
                {
                    pip1.Options |= Process.Options.AllowPreserveOutputs;
                }

                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilder(env);

                outputs1.TryGetOutputFile(output, out var outputFile);
                if (inPlaceRewriteForPip2)
                {
                    pip2.AddRewrittenFileInPlace(outputFile);
                }
                else
                {
                    pip2.AddInputFile(FileArtifact.CreateSourceFile(input));
                    pip2.AddOutputFile(outputFile, FileExistence.Required);
                }

                if (pip2PreserveOutput)
                {
                    pip2.Options |= Process.Options.AllowPreserveOutputs;
                }

                bool hasPreserveOutput = pip1PreserveOutput || pip2PreserveOutput;

                env.PipConstructionHelper.AddProcess(pip2);
                AssertSuccessGraphBuilding(env);

                if (hasPreserveOutput)
                {
                    AssertVerboseEventLogged(EventId.RewritingPreservedOutput, count: 1);
                }
            }
        }

        private ProcessBuilder CreatePipBuilder(TestEnv env)
        {
            var processBuilder = ProcessBuilder.Create(env.PathTable, env.PipDataBuilderPool.GetInstance());

            var exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(env.Context.PathTable, @"\\dummyPath\DummyFile.exe"));

            processBuilder.Executable = exe;
            processBuilder.AddInputFile(exe);
            return processBuilder;
        }

        private PipGraph AssertSuccessGraphBuilding(TestEnv env)
        {
            var builder = env.PipGraph as PipGraph.Builder;

            XAssert.IsNotNull(builder);
            var pipGraph = builder.Build();
            XAssert.IsNotNull(pipGraph);
            return pipGraph;
        }
    }
}
