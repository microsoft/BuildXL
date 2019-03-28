// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Core.Incrementality;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast.Utilities;
using Test.DScript.Workspaces.Utilities;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.TestUtilities.SimpleGraph;

namespace Test.DScript.Ast.PartialEvaluation
{
    /// <remarks>
    /// All edges in all "graph diagrams" below are directed, and they all point upward.  
    /// Semantically, they represent spec-to-spec dependencies: a dependency of spec1 
    /// on spec0 (i.e., spec1 using something defined in spec0) is depicted as:
    ///
    ///   0
    ///   |
    ///   1
    /// </remarks>
    public abstract class GraphBasedTestBase : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ModuleDescriptor TestModule { get; }

        public GraphBasedTestBase(ITestOutputHelper output) : base(output)
        {
            TestModule = ModuleDescriptor.CreateForTesting("TestModule");
        }

        private static readonly SimpleGraph Graph1 = ParseDAG(5, "0<-1, 1<-2, 1<-3, 1<-4");

        /// <summary>
        ///    0
        ///    |
        ///    1
        ///  / | \
        /// 2  3  4 
        /// </summary>
        [Fact]
        public Task TestGraph1_NothingChanged()
        {
            return DoPartialEvalTest(
                file2file: Graph1,
                changed: new int[] { },
                expectedDownstream: new int[] { },
                expectedUpstream: new int[] { },
                expectedIndirectUpstream: new int[] { },
                expectedAffected: new int[] { });
        }

        /// <summary>
        ///    0*
        ///    |
        ///    1
        ///  / | \
        /// 2  3  4 
        /// </summary>
        [Fact]
        public Task TestGraph1_RootNodeChanged()
        {
            return DoPartialEvalTest(
                file2file: Graph1,
                changed: new[] { 0 },
                expectedDownstream: new[] { 1, 2, 3, 4 },
                expectedUpstream: new int[] { },
                expectedIndirectUpstream: new int[] { },
                expectedAffected: new[] { 0, 1, 2, 3, 4 });
        }

        /// <summary>
        ///    0
        ///    |
        ///    1*
        ///  / | \
        /// 2  3  4 
        /// </summary>
        [Fact]
        public Task TestGraph1_MiddleNodeChanged()
        {
            return DoPartialEvalTest(
                file2file: Graph1,
                changed: new[] { 1 },
                expectedDownstream: new[] { 2, 3, 4 },
                expectedUpstream: new[] { 0 },
                expectedIndirectUpstream: new int[] { },
                expectedAffected: new[] { 0, 1, 2, 3, 4 });
        }

        /// <summary>
        ///    0
        ///    |
        ///    1
        ///  / | \
        /// 2  3  4*
        /// </summary>
        [Fact]
        public Task TestGraph1_LeafNodeChanged()
        {
            return DoPartialEvalTest(
                file2file: Graph1,
                changed: new[] { 4 },
                expectedDownstream: new int[] { },
                expectedUpstream: new[] { 0, 1 },
                expectedIndirectUpstream: new int[] { },
                expectedAffected: new[] { 0, 1, 4 });
        }

        private static readonly SimpleGraph Graph2 = ParseDAG(6, "0<-1, 1<-2, 1<-3, 1<-4, 5<-4");

        /// <summary>
        ///    0
        ///    |
        ///    1    5
        ///  / | \ /
        /// 2  3  4 
        /// </summary>
        [Fact]
        public Task TestGraph2_NothingChanged()
        {
            return DoPartialEvalTest(
                file2file: Graph2,
                changed: new int[] { },
                expectedDownstream: new int[] { },
                expectedUpstream: new int[] { },
                expectedIndirectUpstream: new int[] { },
                expectedAffected: new int[] { });
        }

        /// <summary>
        ///    0*
        ///    |
        ///    1    5
        ///  / | \ /
        /// 2  3  4 
        /// </summary>
        [Fact]
        public Task TestGraph2_RootNodeChanged()
        {
            return DoPartialEvalTest(
                file2file: Graph2,
                changed: new[] { 0 },
                expectedDownstream: new[] { 1, 2, 3, 4 },
                expectedUpstream: new int[] { },
                expectedIndirectUpstream: new[] { 5 },
                expectedAffected: new[] { 0, 1, 2, 3, 4, 5 });
        }

        /// <summary>
        ///    0
        ///    |
        ///    1*   5
        ///  / | \ /
        /// 2  3  4 
        /// </summary>
        [Fact]
        public Task TestGraph2_MiddleNode1Changed()
        {
            return DoPartialEvalTest(
                file2file: Graph2,
                changed: new[] { 1 },
                expectedDownstream: new[] { 2, 3, 4 },
                expectedUpstream: new[] { 0 },
                expectedIndirectUpstream: new[] { 5 },
                expectedAffected: new[] { 0, 1, 2, 3, 4, 5 });
        }

        /// <summary>
        ///    0
        ///    |
        ///    1    5*
        ///  / | \ /
        /// 2  3  4 
        /// </summary>
        [Fact]
        public Task TestGraph2_MiddleNode2Changed()
        {
            return DoPartialEvalTest(
                file2file: Graph2,
                changed: new[] { 5 },
                expectedDownstream: new[] { 4 },
                expectedUpstream: new int[] { },
                expectedIndirectUpstream: new[] { 0, 1 },
                expectedAffected: new[] { 0, 1, 4, 5 });
        }

        /// <summary>
        ///    0
        ///    |
        ///    1    5
        ///  / | \ /
        /// 2* 3  4 
        /// </summary>
        [Fact]
        public Task TestGraph2_LeafNode1Changed()
        {
            return DoPartialEvalTest(
                file2file: Graph2,
                changed: new[] { 2 },
                expectedDownstream: new int[] { },
                expectedUpstream: new int[] { 0, 1 },
                expectedIndirectUpstream: new int[] { },
                expectedAffected: new[] { 0, 1, 2 });
        }

        /// <summary>
        ///    0
        ///    |
        ///    1    5
        ///  / | \ /
        /// 2  3  4*
        /// </summary>
        [Fact]
        public Task TestGraph2_LeafNode2Changed()
        {
            return DoPartialEvalTest(
                file2file: Graph2,
                changed: new[] { 4 },
                expectedDownstream: new int[] { },
                expectedUpstream: new int[] { 0, 1, 5 },
                expectedIndirectUpstream: new int[] { },
                expectedAffected: new[] { 0, 1, 4, 5 });
        }

        [Theory]
        [MemberData(nameof(TestSpec2SpecMapGeneratedByTypeCheckerData))]
        public async Task TestSpec2SpecMapGeneratedByTypeChecker(SimpleGraph file2file)
        {
            var helper = new WorkspaceEvaluationHelper(TestOutputDirectory,null, forTesting: true);
            var repo = GenerateFullWorkspaceRepo(helper, file2file);
            var workspace = await helper.ParseAsync(repo);
            var semanticModel = helper.Typecheck(workspace);
            
            Func<int, AbsolutePath> specIdxToSpecPath = (specIdx) => SpecIdxToSpecPath(repo, specIdx);
            var relevantSpecPaths = file2file.Nodes.Select(specIdxToSpecPath).ToList();
            Func<RoaringBitSet, IEnumerable<AbsolutePath>> materializeRelevant = (bitSet) =>
            {
                bitSet.MaterializeSetIfNeeded(string.Empty, (s, i) => workspace.GetAllSourceFiles()[i].GetAbsolutePath(helper.PathTable));
                return bitSet.MaterializedSetOfPaths.Intersect(relevantSpecPaths);
            };
            
            // test the spec2spec map generated by TypeChecker
            Assert.All(
                file2file.Nodes,
                specIdx =>
                {
                    var specSourceFile = workspace.GetSourceFile(SpecIdxToSpecPath(repo, specIdx));
                    var computedDependencies = materializeRelevant(semanticModel.GetFileDependenciesOf(specSourceFile));
                    var computedDependents = materializeRelevant(semanticModel.GetFileDependentFilesOf(specSourceFile));
                    var expectedDependents = file2file.OutgoingEdges(specIdx).Select(e => specIdxToSpecPath(e.Dest));
                    var expectedDependencies = file2file.IncomingEdges(specIdx).Select(e => specIdxToSpecPath(e.Src));

                    XAssert.SetEqual(expectedDependencies, computedDependents);
                    XAssert.SetEqual(expectedDependents, computedDependencies);
                });
        }

        public static IEnumerable<object[]> TestSpec2SpecMapGeneratedByTypeCheckerData()
        {
            yield return new object[] { Graph1 };
            yield return new object[] { Graph2 };
        }

        private AbsolutePath SpecIdxToSpecPath(ModuleRepository repo, int specIdx) => repo.GetPathToModuleAndSpec(TestModule, specIdx);

        private async Task DoPartialEvalTest(SimpleGraph file2file, int[] changed,
             int[] expectedDownstream, int[] expectedUpstream, int[] expectedIndirectUpstream, int[] expectedAffected)
        {
            ComputeAndCheckSpecStates(file2file, changed, expectedDownstream, expectedUpstream, expectedIndirectUpstream, expectedAffected);

            // helpers
            var helper = new WorkspaceEvaluationHelper(TestOutputDirectory, null, forTesting: true);
            var repo = GenerateFullWorkspaceRepo(helper, file2file);
            Func<int, AbsolutePath> specIdxToSpecPath = (specIdx) => SpecIdxToSpecPath(repo, specIdx);

            // create workspace and semantic model
            var workspace = await helper.ParseAsync(repo);
            var semanticModel = helper.Typecheck(workspace);

            var changedWithDownstream = changed.Union(expectedDownstream).ToArray();
            var changedWithDownstreamPaths = changedWithDownstream.Select(specIdxToSpecPath).ToArray();

            // test workspace 'closure of dependent files' computation
            var specDepProvider = new WorkspaceBasedSpecDependencyProvider(workspace, helper.PathTable);
            var dependentPathsClosure = specDepProvider.ComputeReflectiveClosureOfDependentFiles(changed.Select(specIdxToSpecPath));
            XAssert.SetEqual(changedWithDownstreamPaths.ToStrings(helper.PathTable), dependentPathsClosure.ToStrings(helper.PathTable));

            // test workspace 'closure of dependency files' computation
            var dependencyPathsClosure = specDepProvider
                .ComputeReflectiveClosureOfDependencyFiles(changedWithDownstreamPaths)
                .Intersect(file2file.Nodes.Select(specIdxToSpecPath)); // this leaves only spec files from TestModule, i.e., filters out prelude files etc.
            var expectedAffectedPaths = expectedAffected.Select(specIdxToSpecPath);
            XAssert.SetEqual(expectedAffectedPaths.ToStrings(helper.PathTable), dependencyPathsClosure.ToStrings(helper.PathTable));

            // full evaluation
            var interpreter = await helper.ConvertAsync(workspace, oldPipGraph: null);
            var pipGraph = await helper.EvaluateAsync(workspace, interpreter);
            AssertPipGraphContent(pipGraph, file2file, helper.StringTable);

            // partial evaluation
            var helper2 = new WorkspaceEvaluationHelper(TestOutputDirectory, helper.FrontEndContext, forTesting:true);
            var partialRepo = GenerateWorkspaceRepo(helper2, file2file, expectedAffected);
            var pipGraph2 = await helper2.EvaluateWithGraphPatchingAsync(
                repo: partialRepo,
                oldPipGraph: pipGraph,
                changedSpecs: changedWithDownstreamPaths,
                specsToIgnore: expectedUpstream.Union(expectedIndirectUpstream).Select(specIdxToSpecPath));

            // assert we still get the full graph
            AssertPipGraphContent(pipGraph2, file2file, helper2.StringTable);
        }

        protected abstract string GenerateSpec(int specIndex, List<int> specDependencies);
        protected abstract void AssertPipGraphContent(PipGraph pipGraph, SimpleGraph file2file, StringTable stringTable);

        protected string GetSpecWithDefaultQualifierContent()
        {
            return "export declare const qualifier: {};";
        }

        protected virtual string GetHelperSpecContent()
        {
            const string HelperSpecContent = @"
import {Transformer} from 'Sdk.Transformers';

export function startService(tag: string): Transformer.CreateServiceResult {
    const dummyToolDef = <Transformer.ToolDefinition>{ exe: f`dummy.exe` };
    const outDir = Context.getNewOutputDirectory('partial-eval-service');
    const moniker = Transformer.getNewIpcMoniker();
    const shutdownCmd = <Transformer.ExecuteArguments>{
        tool: dummyToolDef,
        tags: [ `${tag}-shutdown` ],
        arguments: [ cmdArgument(moniker) ],
        workingDirectory: outDir,
        consoleOutput: outDir.combine(`${tag}-shutdown-stdout.txt`),
    };
    const finalizeCmd = <Transformer.ExecuteArguments>{
        tool: dummyToolDef,
        tags: [ `${tag}-finalize` ],
        arguments: [ cmdArgument(moniker) ],
        workingDirectory: outDir,
        consoleOutput: outDir.combine(`${tag}-finalize-stdout.txt`),
    };
    return Transformer.createService({
        tool: dummyToolDef,
        tags: [ `${tag}-start` ],
        arguments: [ cmdArgument(moniker) ],
        workingDirectory: outDir,
        consoleOutput: outDir.combine(`${tag}-start-stdout.txt`),
        serviceShutdownCmd: shutdownCmd,
        serviceFinalizationCmds: [ finalizeCmd ],
    }).override<Transformer.CreateServiceResult>({moniker: moniker});
}

export function ipcSend(service: Transformer.CreateServiceResult, tag: string, inputs: File[]): File {
    const outDir = Context.getNewOutputDirectory('partial-eval-ipc');
    const outFilePath = outDir.combine(`outfile-${tag}.txt`);
    const moniker = service['moniker'];
    const ipcResult = Transformer.ipcSend({
       tags: [ tag ],
       targetService: service.serviceId,
       fileDependencies: inputs,
       outputFile: outFilePath,
       moniker: moniker,
       messageBody: [ cmdArgument(moniker) ]
    });

    return ipcResult.outputFile;
}

export function runServiceAndIpcPip(tag: string, inputs: File[]): File {
    const service = startService(tag);
    return ipcSend(service, tag, inputs);
}

export function runProcess(tag: string, inputs: File[]): File {
    const outDir = Context.getNewOutputDirectory('partial-eval-test');
    const outFilePath = outDir.combine(`outfile-${tag}.txt`);
    const result = Transformer.execute({
        tool: <Transformer.ToolDefinition>{ exe: f`dummy.exe` },
        tags: [ tag ],
        arguments: [],
        workingDirectory: outDir,
        dependencies: inputs,
        implicitOutputs: [ outFilePath ]
    });
    return result.getOutputFile(outFilePath);
}

function cmdArgument(value: any): Argument {
    return {
        name: undefined,
        value: value
    };
}";
            return HelperSpecContent;
        }

        /// <summary>
        /// Given: 
        ///   - a file-to-file map (<paramref name="file2file"/>), and 
        ///   - indexes of changed nodes (<paramref name="changed"/>)
        /// computes
        ///   - downstream nodes (and asserts they match <paramref name="expectedDownstream"/>
        ///   - upsteram nodes (and asserts they match <paramref name="expectedUpstream"/>
        ///   - indirect upstream nodes (and asserts they match <paramref name="expectedIndirectUpstream"/>
        ///   - all affected nodes (and asserts they match <paramref name="expectedAffected"/>
        ///   
        /// Definitions: 
        ///   - 'downstream' nodes: all nodes reachable from <paramref name="changed"/> (excluding the 
        ///     changed nodes themselves) by following the reverse edges in <paramref name="file2file"/>
        ///   - 'upstream' nodes: all nodes reachable from <paramref name="changed"/> (excluding the 
        ///     changed nodes themselves) by following the edges in <paramref name="file2file"/>
        ///   - 'affected' nodes: upstream of 'changed' + 'downstream' nodes, including the changed and 
        ///     downstream nodes themselves
        ///   - 'indirect upstream' nodes: 'affected' nodes that are neither 'changed' or 'upstream' or 'downstream'
        /// </summary>
        /// <remarks>
        /// Some remarks about the notation in the comments below: 
        ///   - the '^' means transitive closure
        ///   - the '*' means reflexive transitive closure 
        ///   - the '.' means relational join
        /// For example
        ///   - changed.^Edges means all reachable nodes from 'changed' by following 'Edges', excluding the nodes in 'changed'
        ///   - changed.*Edges means all reachable nodes from 'changed' by following 'Edges', including the nodes in 'changed'.
        /// </remarks>
        private void ComputeAndCheckSpecStates(SimpleGraph file2file, int[] changed, int[] expectedDownstream,
             int[] expectedUpstream, int[] expectedIndirectUpstream, int[] expectedAffected)
        {
            // downstream = changed.^reverseEdges
            var downstream = file2file.ComputeDownstream(changed);
            XAssert.AreSetsEqual(expectedDownstream, downstream, expectedResult: true, format: "Downstream nodes don't match");

            // upstream = changed.^edges
            var upstream = file2file.ComputeUpstream(changed);
            XAssert.AreSetsEqual(expectedUpstream, upstream, expectedResult: true, format: "Upstream nodes don't match");

            // allAffected = (changed.*reverseEdges).*edges
            var allAffected = file2file.ComputeReflexiveUpstream(file2file.ComputeReflexiveDownstream(changed));
            XAssert.AreSetsEqual(expectedAffected, allAffected, expectedResult: true, format: "Affected nodes don't match");

            // indirectUpstream = allAffected - changed - upstream - downstream
            var indirectUpstream = allAffected.Except(changed).Except(upstream).Except(downstream);
            XAssert.AreSetsEqual(expectedIndirectUpstream, indirectUpstream, expectedResult: true, format: "Indirect upstream nodes don't match");
        }

        protected const string HelperSpecName = "helpers.dsc";
        protected const string Qualifier = "qualifier.dsc";

        private ModuleRepository GenerateWorkspaceRepo(WorkspaceEvaluationHelper helper, SimpleGraph file2file, int[] selectedFiles)
        {
            XAssert.IsTrue(file2file.IsDAG(), "Must be a DAG");
            var specs = selectedFiles
                .Select(i =>
                {
                    var specsToImport = file2file.OutgoingEdges(i).Select(e => e.Dest).ToList();
                    var specContent = GenerateSpec(i, specsToImport);
                    return new ModuleRepository.NameContentPair(GetSpecName(i), specContent);
                })
                .Concat(new[]
                        {
                            new ModuleRepository.NameContentPair(HelperSpecName, GetHelperSpecContent()),
                            new ModuleRepository.NameContentPair(Qualifier, GetSpecWithDefaultQualifierContent()),
                        })
                .ToArray();

            return helper.NewModuleRepoWithPrelude().AddContent(TestModule, specs);
        }

        protected static string GetProcTag(int procIdx) => $"p{procIdx}";
        protected static string GetSpecName(int specIdx) => $"spec{specIdx}.dsc";
        protected static string GetSpecNamespace(int specIdx) => $"NS{specIdx}";

        protected static Pip FindPipByTag(IEnumerable<Pip> pips, string tag, StringTable stringTable)
        {
            return pips.FirstOrDefault(p => p.Tags.Contains(StringId.Create(stringTable, tag)));
        }

        private ModuleRepository GenerateFullWorkspaceRepo(WorkspaceEvaluationHelper helper, SimpleGraph file2file)
        {
            return GenerateWorkspaceRepo(helper, file2file, selectedFiles: file2file.Nodes.ToArray());
        }

        internal static void AssertPipGraphCounts(PipGraph pipGraph, Dictionary<PipType, int> pipTypeCounts)
        {
            var pips = pipGraph.RetrieveScheduledPips().Where(p=> NotMetaPip(p) && p.PipType != PipType.HashSourceFile).ToArray();
            Assert.All(
                pipTypeCounts,
                pipTypeAndCount =>
                {
                    AssertPipTypeCount(pips, pipTypeAndCount.Key, pipTypeAndCount.Value);
                });
            var expectedTotalNonMetaPips = pipTypeCounts.Sum(t => t.Value);
            XAssert.AreEqual(expectedTotalNonMetaPips, pips.Count(), "Expected total number non-meta pips didn't match");
        }
        
        protected static void AssertEdges(PipGraph pipGraph, SimpleGraph file2file, List<Pip> pips, StringTable stringTable)
        {
            Assert.All(
                file2file.Edges,
                edge =>
                {
                    var srcPip = FindPipByTag(pips, GetProcTag(edge.Src), stringTable);
                    var destPip = FindPipByTag(pips, GetProcTag(edge.Dest), stringTable);
                    var deps = pipGraph.RetrievePipImmediateDependencies(srcPip).ToList();
                    if (!deps.Contains(destPip))
                    {
                        XAssert.Fail($"Edge ({edge.Src})->({edge.Dest}) not found: expected an edge between {srcPip} <-- {destPip}; dependencies of Pip {srcPip} are: {XAssert.SetToString(deps)}");
                    }
                });
        }

        private static void AssertPipTypeCount(Pip[] pips, PipType pipType, int expected)
        {
            XAssert.AreEqual(
                expected, 
                pips.Where(p => p.PipType == pipType).Count(), 
                $"Number of <{pipType}> pips doesn't match");
        }

        protected static bool NotMetaPip(Pip arg) => !arg.PipType.IsMetaPip();

        protected static StringId ExtractMonikerValueFromPipData(PipData arguments)
        {
            XAssert.AreEqual(1, arguments.FragmentCount, "expected 1 fragment");
            PipFragment fragment = arguments.First();
            XAssert.AreEqual(PipFragmentType.IpcMoniker, fragment.FragmentType);
            return fragment.GetIpcMonikerValue();
        }

        protected static void AssertMonikerConsistencyForIpcPip(IEnumerable<IpcPip> ipcPips, PipGraph pipGraph)
        {
            Assert.All(
                ipcPips,
                ipcPip =>
                {
                    var servicePip = (Process)pipGraph.RetrievePipImmediateDependencies(ipcPip).FirstOrDefault(p => (p as Process)?.IsService == true);
                    XAssert.IsNotNull(servicePip, $" could not find service pip dependency of ipc pip {ipcPip.PipId}");

                    var finalizationPip = pipGraph.PipTable.HydratePip(servicePip.ServiceInfo.ShutdownPipId, global::BuildXL.Pips.PipQueryContext.Test) as Process;
                    XAssert.IsNotNull(finalizationPip, $" could not find finalization pip for ipc pip {ipcPip.PipId}");

                    var shutdownPip = pipGraph.PipTable.HydratePip(servicePip.ServiceInfo.FinalizationPipIds.First(), global::BuildXL.Pips.PipQueryContext.Test) as Process;
                    XAssert.IsNotNull(shutdownPip, $" could not find shutdown pip for ipc pip {ipcPip.PipId}");

                    StringId servicePipMoniker = ExtractMonikerValueFromPipData(servicePip.Arguments);
                    XAssert.AreEqual(servicePipMoniker, ExtractMonikerValueFromPipData(ipcPip.MessageBody), "service pip and ipc pip monikers don't match");
                    XAssert.AreEqual(servicePipMoniker, ExtractMonikerValueFromPipData(finalizationPip.Arguments), "service pip and finalization pip monikers don't match");
                    XAssert.AreEqual(servicePipMoniker, ExtractMonikerValueFromPipData(shutdownPip.Arguments), "service pip and shutdown pip monikers don't match");
                });
        }
    }
}
