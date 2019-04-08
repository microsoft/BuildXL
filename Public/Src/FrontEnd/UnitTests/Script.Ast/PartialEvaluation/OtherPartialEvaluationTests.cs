// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.FrontEnd.Workspaces.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.TestUtilities.SimpleGraph;
using static Test.DScript.Ast.PartialEvaluation.GraphBasedTestBase;

namespace Test.DScript.Ast.PartialEvaluation
{
    [Trait("Category", "PartialEvaluation")]
    public sealed class OtherPartialEvaluationTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public OtherPartialEvaluationTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(1, "", true)]
        [InlineData(1, "0->0", false)]
        [InlineData(2, "0->1", true)]
        [InlineData(2, "0->1, 1->0", false)]
        [InlineData(3, "0->1, 1->2, 2->1", false)]
        [InlineData(3, "0->1, 1->2, 2->0", false)]
        [InlineData(3, "0->1, 1->2, 1->1", false)]
        [InlineData(3, "0->1, 1->2, 0->2", true)]
        public void TestDAG(int numNodes, string graphString, bool isDag)
        {
            var graph = Parse(numNodes, graphString);
            XAssert.AreEqual(isDag, graph.IsDAG());
        }

        [Fact]
        public async Task TestSimple()
        {
            const string Spec0 = "import {Transformer} from 'Sdk.Transformers';\r\nexport const x = Transformer.copyFile(f`src-x.txt`, Context.getNewOutputDirectory('test').combine('dest-x.txt'));";
            const string Spec1 = "import {Transformer} from 'Sdk.Transformers';\r\nexport const y = Transformer.copyFile(f`src-y.txt`, Context.getNewOutputDirectory('test').combine('dest-y.txt'));";

            var helperFull = new WorkspaceEvaluationHelper(TestOutputDirectory, context: null, forTesting:true);
            var testModule = ModuleDescriptor.CreateForTesting("MyModule1");

            // evaluate full
            var repo = helperFull.NewModuleRepoWithPrelude().AddContent(testModule, Spec0, Spec1);
            var pipGraph = await helperFull.EvaluateAsync(repo);
            var fullGraphPipCounts = new Dictionary<PipType, int>
            {
                [PipType.CopyFile] = 2,
            };
            AssertPipGraphCounts(pipGraph, fullGraphPipCounts);

            // evaluate partial without patching --> result is a smaller graph
            {
                var helperPartial = new WorkspaceEvaluationHelper(TestOutputDirectory, context: helperFull.FrontEndContext,  forTesting:true);
                var partialRepo = helperPartial.NewModuleRepoWithPrelude().AddContent(testModule, Spec0);
                var newPipGraph = await helperPartial.EvaluateAsync(partialRepo);
                AssertPipGraphCounts(newPipGraph, new Dictionary<PipType, int>
                {
                    [PipType.CopyFile] = 1,
                });
            }

            // evaluate partial with patching --> same as full graph
            {
                // NOTE: must start with previous PathTable, because some paths from the old build might not be seen in the partial workspace
                var helperPartial = new WorkspaceEvaluationHelper(TestOutputDirectory, context: helperFull.FrontEndContext, forTesting:true);
                var partialRepo = helperPartial.NewModuleRepoWithPrelude().AddContent(testModule, Spec0);
                var changedSpecs = new[] { repo.GetPathToModuleAndSpec(testModule, 0) };
                var newPipGraph = await helperPartial.EvaluateWithGraphPatchingAsync(partialRepo, oldPipGraph: pipGraph, changedSpecs: changedSpecs, specsToIgnore: null);
                AssertPipGraphCounts(newPipGraph, fullGraphPipCounts);
            }
        }
    }
}
