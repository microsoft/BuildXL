// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.PartialEvaluation
{
    /// <summary>
    /// For a spec2spec dependency graph that looks like, for example:
    ///
    ///      0
    ///      |
    ///      1    5
    ///    / | \ /
    ///   2  3  4 
    ///   
    /// this test class producess the following pip graph (where 'P' stands for process pip)
    ///
    ///      P0
    ///      |
    ///      P1   P5
    ///    / | \ /
    ///   P2 P3 P4
    ///   
    /// All process-to-process pip dependencies are realized via output files.
    /// </summary>
    /// <remarks>
    /// All edges in the graph diagrams above are directed, and they point upward.  In the spec-to-spec diagram, they represent
    /// spec usage dependencies (i.e., spec1 uses something defined in spec0); in the pip graph diagram, they represent
    /// pip dependencies (e.g., pip SD0 is a depends on pip P0, i.e., process P0 produces seal directory SD0).
    /// </remarks>
    [Trait("Category", "PartialEvaluation")]
    public sealed class GraphWithProcessPipsOnlyTests : GraphBasedTestBase
    {
        public GraphWithProcessPipsOnlyTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// Each generated spec looks something like:
        ///
        /// export const x{i} = runProcess('p{i}', [ NS{j1}.x{j1}, ..., NS{jn}.x{jn} ];
        /// </summary>
        protected override string GenerateSpec(int specIndex, List<int> specDependencies)
        {
            var commaSeparatedImports = string.Join(", ", specDependencies.Select(specIdx => $"{GetSpecNamespace(specIdx)}.x{specIdx}"));
            return $"namespace NS{specIndex} {{ export const x{specIndex} = runProcess('{GetProcTag(specIndex)}', [ {commaSeparatedImports} ]); }}";
        }

        protected override void AssertPipGraphContent(PipGraph pipGraph, SimpleGraph file2file, StringTable stringTable)
        {
            AssertPipGraphCounts(pipGraph, new Dictionary<PipType, int>
            {
                [PipType.Process] = file2file.NodeCount,
            });
            var processPips = pipGraph.RetrievePipsOfType(PipType.Process).ToList();
            AssertEdges(pipGraph, file2file, processPips, stringTable);
        }
    }
}
