// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public sealed class PartialGraphTest : PipTestBase
    {
        public PartialGraphTest(ITestOutputHelper output)
            : base(output)
        {
            BaseSetup();
        }

        [Fact]
        public void TestNoAffectedFiles()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2");
            var reloadedGraph = ReloadGraph(pips);
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "0, 1, 2", expectedEdgesAsString: "0->1, 0->2");
        }

        [Fact]
        public void TestLeafFileAffected1()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: 1);
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "0, 2", expectedEdgesAsString: "0->2");
        }

        [Fact]
        public void TestLeafFileAffected2()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: 2);
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "0, 1", expectedEdgesAsString: "0->1");
        }

        [Fact]
        public void TestMultipleLeafFilesAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 1, 2 });
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "0", expectedEdgesAsString: string.Empty);
        }

        [Fact]
        public void TestAllFilesAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 0, 1, 2 });
            ExpectEmptyGraph(reloadedGraph);
        }

        [Fact]
        public void TestAffectedSpecsDoNotFormAClosure()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2");
            var exception = XAssert.ThrowsAny(() => ReloadGraph(pips, affectedIndexes: new[] { 0, 1 }));
            var innerException = (exception as AggregateException)?.InnerException ?? exception;
            XAssert.AreEqual(typeof(BuildXLException), innerException?.GetType());
        }

        [Fact]
        public void TestTriagleGraphLeafAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2, 1->2");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 2 });
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "0, 1", expectedEdgesAsString: "0->1");
        }

        [Fact]
        public void TestTriagleGraphMiddleAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2, 1->2");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 1, 2 });
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "0", expectedEdgesAsString: string.Empty);
        }

        [Fact]
        public void TestTriagleGraphAllAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1, 0->2, 1->2");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 0, 1, 2 });
            ExpectEmptyGraph(reloadedGraph);
        }
        
        [Fact]
        public void TestDisconnectedGraphLoneNodeAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 2 });
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "0, 1", expectedEdgesAsString: "0->1");
        }

        [Fact]
        public void TestDisconnectedGraphLeafNodeAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 1 });
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "0, 2", expectedEdgesAsString: string.Empty);
        }

        [Fact]
        public void TestDisconnectedGraphRootNodeAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 0, 1 });
            ExpectGraph(reloadedGraph, pips, expectedPipIndexesAsString: "2", expectedEdgesAsString: string.Empty);
        }

        [Fact]
        public void TestDisconnectedGraphAllNodesAffected()
        {
            Pip[] pips = CreateGraph(3, "0->1");
            var reloadedGraph = ReloadGraph(pips, affectedIndexes: new[] { 0, 1, 2 });
            ExpectEmptyGraph(reloadedGraph);
        }

        [Fact]
        public void TestAddSealDirectory()
        {
            var root = CreateUniqueSourcePath("root");
            var seal = CreateSealDirectory(root, SealDirectoryKind.Partial);
            PipGraphBuilder.AddSealDirectory(seal);
            var builder = new PatchablePipGraph(
                oldPipGraph: PipGraphBuilder.DataflowGraph,
                oldPipTable: PipTable,
                graphBuilder: CreatePipGraphBuilder(),
                maxDegreeOfParallelism: Environment.ProcessorCount);
            var stats = builder.PartiallyReloadGraph(new HashSet<AbsolutePath>());
            Assert.Equal(1, stats.NumPipsReloaded);
        }

        private void ExpectEmptyGraph(GraphReloadResult reloadResult)
        {
            var newGraph = reloadResult.PipGraph.DataflowGraph;
            Assert.Equal(0, newGraph.NodeCount);
            Assert.Equal(0, newGraph.EdgeCount);
            Assert.Equal(0, reloadResult.Stats.NumPipsReloaded);
        }

        private void ExpectGraph(GraphReloadResult reloadResult, Pip[] oldPips, string expectedPipIndexesAsString, string expectedEdgesAsString)
        {
            int[] expectedPipIndexes = expectedPipIndexesAsString.Split(',').Select(i => int.Parse(i)).ToArray();
            var graph = SimpleGraph.Parse(oldPips.Length, expectedEdgesAsString);

            var newPipGraph = reloadResult.PipGraph;
            var newPipTable = newPipGraph.PipTable;
            var newGraph = newPipGraph.DataflowGraph;

            // check that the new pip table contains expected number of relevant pips
            var allPipTypes = new HashSet<PipType>(oldPips.Select(pip => pip.PipType).Distinct());
            IEnumerable<Pip> newRelevantPips = HydratePipsByType(newPipTable, relevantTypes: allPipTypes);
            Assert.Equal(expectedPipIndexes.Length, newRelevantPips.Count());
            
            // check that for all expected pips there is a node in the new graph
            Assert.All(
                expectedPipIndexes,
                idx =>
                {
                    Assert.True(newGraph.ContainsNode(oldPips[idx].PipId.ToNodeId()), $"No graph node found for Pip{idx}");
                });

            // check edges
            var newRelevantPipIdValues = new HashSet<uint>(newRelevantPips.Select(pip => pip.PipId.Value));
            Assert.All(
                expectedPipIndexes, 
                idx =>
                {
                    var nodeId = oldPips[idx].PipId.ToNodeId();
                    var expectedOutgoingEdges = graph
                        .Edges
                        .Where(e => e.Src == idx)
                        .Select(e => oldPips[e.Dest].PipId.ToNodeId().Value)
                        .OrderBy(v => v)
                        .ToArray();
                    var actualOutgoingEdges = newGraph
                        .GetOutgoingEdges(nodeId)
                        .Select(e => e.OtherNode.Value)
                        .Where(v => newRelevantPipIdValues.Contains(v))
                        .OrderBy(v => v)
                        .ToArray();
                    XAssert.AreArraysEqual(expectedOutgoingEdges, actualOutgoingEdges, expectedResult: true);
                });

            // check stats
            Assert.Equal(expectedPipIndexes.Length, reloadResult.Stats.NumPipsReloaded);
        }

        private IEnumerable<Pip> HydrateAllPips(PipTable pipTable)
        {
            return pipTable.Keys.Select(pipId => pipTable.HydratePip(pipId, PipQueryContext.Test)).ToList(); 
        }

        private IEnumerable<Pip> HydratePipsByType(PipTable pipTable, IEnumerable<PipType> relevantTypes)
        {
            return pipTable
                .Keys
                .Where(pipId => relevantTypes.Any(pipType => pipType == pipTable.GetPipType(pipId)))
                .Select(pipId => pipTable.HydratePip(pipId, PipQueryContext.Test))
                .ToList();
        }

        private GraphReloadResult ReloadGraph(Pip[] procs, params int[] affectedIndexes)
        {
            Assert.All(affectedIndexes, i => Assert.True(i >= 0 && i < procs.Length));
            
            // add meta pips only for non-affected processes, because they should be present in the reloaded graph
            var nonAffectedIndexes = Enumerable.Range(0, procs.Length).Except(affectedIndexes);
            var nonAffectedProcs = nonAffectedIndexes.Select(i => procs[i]).ToArray();

            // partially reload graph into the newly created PipGraph.Builder
            var builder = new PatchablePipGraph(
                oldPipGraph: PipGraphBuilder.DataflowGraph,
                oldPipTable: PipTable,
                graphBuilder: CreatePipGraphBuilder(),
                maxDegreeOfParallelism: Environment.ProcessorCount);
            var affectedSpecs = affectedIndexes.Select(i => procs[i].Provenance.Token.Path);
            var reloadingStats = builder.PartiallyReloadGraph(new HashSet<AbsolutePath>(affectedSpecs));

            // build and return the new PipGraph together with the statistics of graph reloading
            return new GraphReloadResult()
            {
                PipGraph = builder.Build(),
                Stats = reloadingStats
            };
        }

        private PipGraph.Builder CreatePipGraphBuilder()
        {
            return new PipGraph.Builder(
                CreatePipTable(),
                Context,
                global::BuildXL.Scheduler.Tracing.Logger.Log,
                LoggingContext,
                new ConfigurationImpl(), 
                Expander);
        }

        private PipTable CreatePipTable()
        {
            return new PipTable(
                Context.PathTable,
                Context.SymbolTable,
                initialBufferSize: 16,
                maxDegreeOfParallelism: Environment.ProcessorCount,
                debug: true);
        }

        private Pip[] CreateGraph(int numNodes, string graphAsString)
        {
            // parse edges
            var graph = SimpleGraph.Parse(numNodes, graphAsString);

            // for each node create a single output file
            var outFiles = Enumerable
                .Range(0, numNodes)
                .Select(_ => CreateOutputFileArtifact())
                .ToArray();

            // for each node create a Process pip with a single output file and dependencies according to 'edges'
            var processes = Enumerable
                .Range(0, numNodes)
                .Select(procIdx =>
                {
                    var dependencies = graph
                        .Edges
                        .Where(e => e.Dest == procIdx)
                        .Select(e => outFiles[e.Src])
                        .ToArray();
                    var processBuilder = new ProcessBuilder();
                    var arguments = SchedulerTestBase.CreateCmdArguments(
                        stringTable: Context.StringTable,
                        dependencies: dependencies,
                        outputs: new[] { outFiles[procIdx] });
                    return processBuilder
                        .WithContext(Context)
                        .WithWorkingDirectory(GetWorkingDirectory())
                        .WithStandardDirectory(GetStandardDirectory())
                        .WithExecutable(CmdExecutable)
                        .WithArguments(arguments)
                        .WithDependencies(dependencies)
                        .WithOutputs(outFiles[procIdx])
                        .WithProvenance(CreateProvenance())
                        .Build();
                })
                .ToArray();

            // add created processes to PipGraphBuilder
            foreach (var proc in processes)
            {
                PipGraphBuilder.AddProcess(proc);
            }

            // return created processes
            return processes;
        }
    }

    internal sealed class GraphReloadResult
    {
        internal PipGraph PipGraph { get; set; }
        internal GraphPatchingStatistics Stats { get; set; }
    }
}
