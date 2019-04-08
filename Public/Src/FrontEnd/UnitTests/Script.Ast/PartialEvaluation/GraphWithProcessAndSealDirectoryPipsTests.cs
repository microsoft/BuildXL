// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
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
    /// this test class producess the following pip graph (where 'P' stands for Process pip, and 'SD' for SealDirectory pip)
    ///
    ///        P0
    ///        |
    ///       SD0
    ///        |
    ///        P1    P5
    ///        |     |
    ///       SD1   SD5
    ///     /  |  \ /
    ///    P2  P3  P4
    ///    |   |   |
    ///   SD2 SD3 SD4
    ///   
    /// All process-to-process pip dependencies are realized via output directories, which places a SealDirectory pip in between.
    /// </summary>
    /// <remarks>
    /// All edges in the graph diagrams above are directed, and they point upward.  In the spec-to-spec diagram, they represent
    /// spec usage dependencies (i.e., spec1 uses something defined in spec0); in the pip graph diagram, they represent
    /// pip dependencies (e.g., pip SD0 is a depends on pip P0, i.e., process P0 produces seal directory SD0).
    /// </remarks>
    [Trait("Category", "PartialEvaluation")]
    public sealed class GraphWithProcessAndSealDirectoryPipsTests : GraphBasedTestBase
    {
        public GraphWithProcessAndSealDirectoryPipsTests(ITestOutputHelper output) : base(output)
        {
        }

        protected override string GetHelperSpecContent()
        {
            const string HelperSpecContent = @"
import {Transformer} from 'Sdk.Transformers';

export function runProcessWithDirectoryInputs(tag: string, inputs: StaticDirectory[]): StaticDirectory {
    const outDir = Context.getNewOutputDirectory('partial-eval-test-dirs');
    const outputDirName = `outdir-${tag}`;
    const outputDirToProduce = d`${outDir}/${outputDirName}`;
    const result = Transformer.execute({
        tool: <Transformer.ToolDefinition>{ exe: f`dummy.exe` },
        tags: [ tag ],
        arguments: [
            ...inputs.map(dir => cmdArgument(createArtifact(dir, ArtifactKind.input))),
            cmdArgument(createArtifact(outputDirToProduce, ArtifactKind.output))
        ],
        workingDirectory: outDir,
    });
    return result.getOutputDirectory(outputDirToProduce);
}

function cmdArgument(value: any): Argument {
    return {
        name: undefined,
        value: value
    };
}

function createArtifact(value: Transformer.InputArtifact | Transformer.OutputArtifact, kind: ArtifactKind, original?: File): Artifact {
    return <Artifact>{
        path: value,
        kind: kind,
        original: original
    };
}";
            return HelperSpecContent;
        }

        /// <summary>
        /// Each generated spec looks something like:
        ///
        /// export const x{i} = runProcessWithDirectoryInputs('p{i}', [ NS{j1}.x{j1}, ..., NS{jn}.x{jn} ];
        /// </summary>
        protected override string GenerateSpec(int specIndex, List<int> specDependencies)
        {
            var commaSeparatedImports = string.Join(", ", specDependencies.Select(specIdx => $"{GetSpecNamespace(specIdx)}.x{specIdx}"));
            return $"namespace NS{specIndex} {{ export const x{specIndex} = runProcessWithDirectoryInputs('{GetProcTag(specIndex)}', [ {commaSeparatedImports} ]); }}";
        }

        protected override void AssertPipGraphContent(PipGraph pipGraph, SimpleGraph file2file, StringTable stringTable)
        {
            AssertPipGraphCounts(pipGraph, new Dictionary<PipType, int>
            {
                [PipType.Process] = file2file.NodeCount,
                [PipType.SealDirectory] = file2file.NodeCount,
            });
            var processPips = pipGraph.RetrievePipsOfType(PipType.Process).ToList();

            // assert edges exist
            Assert.All(
                file2file.Edges,
                edge =>
                {
                    var errPrefix = $"Edge ({edge.Src})->({edge.Dest}) not found: ";
                    var srcPip = FindPipByTag(processPips, GetProcTag(edge.Src), stringTable);
                    var destPip = FindPipByTag(processPips, GetProcTag(edge.Dest), stringTable);
                    var producedSealDirectoryPips = pipGraph.RetrievePipImmediateDependents(destPip).Where(pip => pip.PipType == PipType.SealDirectory).ToList();
                    XAssert.AreEqual(1, producedSealDirectoryPips.Count, $"{errPrefix} expected to find exactly one SealDirectory dependency of Process Pip {destPip}");
                    var producedSealDirectoryPip = producedSealDirectoryPips.First();
                    var deps = pipGraph.RetrievePipImmediateDependents(producedSealDirectoryPip);
                    if (!deps.Contains(srcPip))
                    {
                        XAssert.Fail($"{errPrefix} expected edges between {srcPip} <-- {producedSealDirectoryPip} <-- {destPip}; dependencies of Pip {producedSealDirectoryPip} are: {XAssert.SetToString(deps)}");
                    }
                });
        }
    }
}
