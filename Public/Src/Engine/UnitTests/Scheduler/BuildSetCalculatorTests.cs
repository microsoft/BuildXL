// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using FileId = System.Int32;
using PathId = System.Int32;

namespace Test.BuildXL.Scheduler
{
    public class BuildSetCalculatorTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public BuildSetCalculatorTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void BasicBuild()
        {
            MutableDirectedGraph graph;
            NodeId[] n;

            CreateGraph(out graph, out n);
            var buildSetCalculator = new TestBuildSetCalculator(graph);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n[2], n[6], n[9], n[11], n[5], n[7], n[14] },
                explicitlyScheduledNodes: new[] { n[2], n[5] },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: false);
        }

        [Fact]
        public void BasicIncrementalScheduling()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            var dirtyRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanMaterializedNotRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanNotMaterializedNotRequested = buildSetCalculator.CreateNode(PipType.Process);

            // Graph: dirtyRequested -> cleanMaterializedNotRequested -> cleanNotMaterializedNotRequested.
            graph.AddEdge(cleanNotMaterializedNotRequested, cleanMaterializedNotRequested);
            graph.AddEdge(cleanMaterializedNotRequested, dirtyRequested);

            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested);

            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(cleanMaterializedNotRequested);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested,

                    // Need to be scheduled so that the dirtyRequested can execute properly.
                    cleanMaterializedNotRequested
                },
                explicitlyScheduledNodes: new[] { dirtyRequested },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void BasicIncrementalSchedulingWithNotMaterializedInTheMiddle()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            var dirtyRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanMaterializedNotRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanNotMaterializedNotRequested = buildSetCalculator.CreateNode(PipType.Process);

            // Graph: dirtyRequested -> cleanNotMaterializedNotRequested -> cleanMaterializedNotRequested.
            graph.AddEdge(cleanMaterializedNotRequested, cleanNotMaterializedNotRequested);
            graph.AddEdge(cleanNotMaterializedNotRequested, dirtyRequested);

            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested);

            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(cleanMaterializedNotRequested);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested,
                    cleanNotMaterializedNotRequested,
                    
                    // Need to be scheduled so that the cleanNotMaterializedNotRequested can execute properly.
                    cleanMaterializedNotRequested
                },
                explicitlyScheduledNodes: new[] { dirtyRequested },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void BasicIncrementalSchedulingWithSealedDirectoryInTheMiddle()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            var dirtyRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanMaterializedNotRequestedSealedDirectory = buildSetCalculator.CreateNode(PipType.SealDirectory);
            var cleanNotMaterializedNotRequested = buildSetCalculator.CreateNode(PipType.Process);

            // Graph: dirtyRequested -> cleanMaterializedNotRequestedSealDirectory -> cleanNotMaterializedNotRequested.
            graph.AddEdge(cleanNotMaterializedNotRequested, cleanMaterializedNotRequestedSealedDirectory);
            graph.AddEdge(cleanMaterializedNotRequestedSealedDirectory, dirtyRequested);

            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested);

            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(cleanMaterializedNotRequestedSealedDirectory);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested,

                    // Although clean-materialized, need to be scheduled.
                    cleanMaterializedNotRequestedSealedDirectory
                },
                explicitlyScheduledNodes: new[] { dirtyRequested },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void IncrementalSchedulingWithForkGraph()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            var dirtyRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanMaterializedNotRequestedCopyFile = buildSetCalculator.CreateNode(PipType.CopyFile);
            var cleanNotMaterializedNotRequested = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyNotRequested2 = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyNotRequestedDepOf2 = buildSetCalculator.CreateNode(PipType.Process);

            // Graph:
            //    dirtyRequested                                           dirtyNotRequested2
            //            |                                                   |    |
            //            +------> cleanMaterializedNotRequestedCopyFile <----+    +-------------> dirtyNotRequestedDepOf2
            //                                   |
            //                                   V
            //                        cleanNotMaterializedNotRequested

            graph.AddEdge(cleanNotMaterializedNotRequested, cleanMaterializedNotRequestedCopyFile);
            graph.AddEdge(cleanMaterializedNotRequestedCopyFile, dirtyRequested);
            graph.AddEdge(cleanMaterializedNotRequestedCopyFile, dirtyNotRequestedDepOf2);
            graph.AddEdge(dirtyNotRequestedDepOf2, dirtyNotRequested2);

            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested);
            dirtyNodeTracker.MarkNodeDirty(dirtyNotRequestedDepOf2);

            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(cleanMaterializedNotRequestedCopyFile);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested,

                    // Need to be added so dirtyRequested can execute properly.
                    cleanMaterializedNotRequestedCopyFile
                },
                explicitlyScheduledNodes: new[] { dirtyRequested },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void IncrementalSchedulingWithForkGraphAndNotMaterializedNodeInTheJunction()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            var dirtyRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanMaterializedNotRequestedCopyFile = buildSetCalculator.CreateNode(PipType.CopyFile);
            var cleanNotMaterializedNotRequested = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyNotRequested2 = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyNotRequestedDepOf2 = buildSetCalculator.CreateNode(PipType.Process);

            // Graph:
            //    dirtyRequested                                           dirtyNotRequested2
            //            |                                                   |    |
            //            +------> cleanNotMaterializedNotRequested <---------+    +-------------> dirtyNotRequestedDepOf2
            //                                   |
            //                                   V
            //                      cleanMaterializedNotRequestedCopyFile

            graph.AddEdge(cleanMaterializedNotRequestedCopyFile, cleanNotMaterializedNotRequested);
            graph.AddEdge(cleanNotMaterializedNotRequested, dirtyRequested);
            graph.AddEdge(cleanNotMaterializedNotRequested, dirtyNotRequestedDepOf2);
            graph.AddEdge(dirtyNotRequestedDepOf2, dirtyNotRequested2);

            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested);
            dirtyNodeTracker.MarkNodeDirty(dirtyNotRequestedDepOf2);

            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(cleanMaterializedNotRequestedCopyFile);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested,

                    // Need to be scheduled because not materialized.
                    cleanNotMaterializedNotRequested,
                    cleanMaterializedNotRequestedCopyFile
                },
                explicitlyScheduledNodes: new[] { dirtyRequested },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void IncrementalSchedulingWithForkGraphAndMultipleRequestedNodes()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            var dirtyRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanMaterializedNotRequestedCopyFile = buildSetCalculator.CreateNode(PipType.CopyFile);
            var cleanNotMaterializedNotRequested = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyNotRequested2 = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyRequestedDepOf2 = buildSetCalculator.CreateNode(PipType.Process);

            // Graph:
            //    dirtyRequested                                           dirtyNotRequested2
            //            |                                                   |    |
            //            +------> cleanMaterializedNotRequestedCopyFile <----+    +-------------> dirtyRequestedDepOf2
            //                                   |
            //                                   V
            //                        cleanNotMaterializedNotRequested

            graph.AddEdge(cleanNotMaterializedNotRequested, cleanMaterializedNotRequestedCopyFile);
            graph.AddEdge(cleanMaterializedNotRequestedCopyFile, dirtyRequested);
            graph.AddEdge(cleanMaterializedNotRequestedCopyFile, dirtyNotRequested2);
            graph.AddEdge(dirtyRequestedDepOf2, dirtyNotRequested2);

            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequestedDepOf2);

            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(cleanMaterializedNotRequestedCopyFile);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested,
                    dirtyRequestedDepOf2,
                    dirtyNotRequested2,

                    // Need to be added so dirtyRequested and dirtyNotRequested2 can execute properly.
                    cleanMaterializedNotRequestedCopyFile
                },
                explicitlyScheduledNodes: new[] { dirtyRequested, dirtyRequestedDepOf2 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void IncrementalSchedulingWithXandYGraph()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker); 

             var dirtyRequested1 = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyRequested2 = buildSetCalculator.CreateNode(PipType.Process);
            var cleanMaterializedXCenterNotRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanNotMaterializedXBottomLeft = buildSetCalculator.CreateNode(PipType.Process);
            var cleanNotMaterializedXBottomRight = buildSetCalculator.CreateNode(PipType.Process);
            var cleanNotMaterializedYCenterNotRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanMaterializedYBottomNotRequested = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyNotRequested = buildSetCalculator.CreateNode(PipType.Process);

            // Graph:
            //        dirtyRequested1                                     dirtyRequested2                                                  dirtyNotRequested
            //               |                                                  |  |                                                            |
            //               +--> cleanMaterializedXCenterNotRequested <--------+  +---------> cleanNotMaterializedYCenterNotRequested <--------+
            //                                    |                                                              |
            //                   +----------------+-------------------+                                          |
            //                   |                                    |                                          |
            //                   V                                    V                                          V
            //  cleanNotMaterializedXBottomLeft        cleanNotMaterializedXBottomRight            cleanMaterializedYBottomNotRequested 

            graph.AddEdge(cleanNotMaterializedXBottomLeft, cleanMaterializedXCenterNotRequested);
            graph.AddEdge(cleanNotMaterializedXBottomRight, cleanMaterializedXCenterNotRequested);
            graph.AddEdge(cleanMaterializedXCenterNotRequested, dirtyRequested1);
            graph.AddEdge(cleanMaterializedXCenterNotRequested, dirtyRequested2);
            graph.AddEdge(cleanMaterializedYBottomNotRequested, cleanNotMaterializedYCenterNotRequested);
            graph.AddEdge(cleanNotMaterializedYCenterNotRequested, dirtyRequested2);
            graph.AddEdge(cleanNotMaterializedYCenterNotRequested, dirtyNotRequested);

            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested1);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested2);
            dirtyNodeTracker.MarkNodeDirty(dirtyNotRequested);

            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(cleanMaterializedXCenterNotRequested);
            materializedNodeSet.Add(cleanMaterializedYBottomNotRequested);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested1,
                    dirtyRequested2,

                    // Need to be scheduled so that dirtyRequested1 and dirtyRequested2 can execute properly.
                    cleanMaterializedXCenterNotRequested,

                    cleanNotMaterializedYCenterNotRequested,
                    cleanMaterializedYBottomNotRequested
                },
                explicitlyScheduledNodes: new[] { dirtyRequested1, dirtyRequested2 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void IncrementalScheduling()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph, 
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            var dirtyRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanRequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanRequested2 = buildSetCalculator.CreateNode(PipType.CopyFile);
            var cleanSharedDependency = buildSetCalculator.CreateNode(PipType.Process);
            var cleanDependency = buildSetCalculator.CreateNode(PipType.Process);
            var dirtyUnrequested = buildSetCalculator.CreateNode(PipType.Process);
            var cleanRequestedDirtyUnrequestedDependency2 = buildSetCalculator.CreateNode(PipType.Process);

            var dependencyWhichIsNotDownstreamOfAProcess = buildSetCalculator.CreateNode(PipType.CopyFile);

            var dependencyWhichIsDownstreamOfAProcess = buildSetCalculator.CreateNode(PipType.CopyFile);
            var upstreamProcess = buildSetCalculator.CreateNode(PipType.Process);
            var requestedReachableWhichIsDownstreamOfAProcess = buildSetCalculator.CreateNode(PipType.CopyFile);

            // NOTE: Normally nodes reachable from dirty nodes must be built. But that's only if reachable
            // by a path in the explicitly requested nodes dependency closure and downstream of a process pip.
            var cleanRequestedDirtyUnrequestedDependency = buildSetCalculator.CreateNode(PipType.Process);

            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            dirtyNodeTracker.MarkNodeDirty(dirtyRequested);

            // All nodes are assumed to be materialized.
            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Fill();

            // Dependencies: 
            // dirtyRequested -> cleanSharedDependency
            // cleanRequested -> cleanSharedDependency
            // cleanRequested -> cleanDependency
            // dirtyUnrequested -> dirtyRequested
            // dirtyUnrequested -> cleanRequestedDirtyUnrequestedDependency
            // cleanRequestedDirtyUnrequestedDependency -> dependencyWhichIsNotDownstreamOfAProcess
            // dirtyRequested -> dependencyWhichIsNotDownstreamOfAProcess
            graph.AddEdge(cleanSharedDependency, dirtyRequested);
            graph.AddEdge(cleanSharedDependency, cleanRequested);
            graph.AddEdge(cleanDependency, cleanRequested);
            graph.AddEdge(dirtyRequested, dirtyUnrequested);
            graph.AddEdge(cleanRequestedDirtyUnrequestedDependency, dirtyUnrequested);
            graph.AddEdge(dependencyWhichIsNotDownstreamOfAProcess, cleanRequestedDirtyUnrequestedDependency);
            graph.AddEdge(dependencyWhichIsNotDownstreamOfAProcess, dirtyRequested);

            // cleanRequested2 -> cleanSharedDependency
            // cleanRequested2 -> dependencyWhichIsDownstreamOfAProcess
            // requestedReachableWhichIsDownstreamOfAProcess -> dependencyWhichIsDownstreamOfAProcess
            // dependencyWhichIsDownstreamOfAProcess -> upstreamProcess
            graph.AddEdge(cleanSharedDependency, cleanRequested2);
            graph.AddEdge(dependencyWhichIsDownstreamOfAProcess, cleanRequested2);
            graph.AddEdge(dependencyWhichIsDownstreamOfAProcess, requestedReachableWhichIsDownstreamOfAProcess);
            graph.AddEdge(upstreamProcess, dependencyWhichIsDownstreamOfAProcess);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested,

                    // The following pips are included although they are clean and materialized.
                    // We need to schedule them so that the execution of dirtyRequested works properly.
                    // This is OK these pips will not do any execution, but only assert their outputs.
                    cleanSharedDependency,
                    dependencyWhichIsNotDownstreamOfAProcess
                },
                explicitlyScheduledNodes:
                    new[]
                    {
                        dirtyRequested, cleanRequested, cleanRequested2, cleanRequestedDirtyUnrequestedDependency,
                        requestedReachableWhichIsDownstreamOfAProcess
                    },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: false);

            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(dependencyWhichIsNotDownstreamOfAProcess);
            materializedNodeSet.Add(cleanRequestedDirtyUnrequestedDependency);
            materializedNodeSet.Add(cleanDependency);
            materializedNodeSet.Add(cleanRequested);
            materializedNodeSet.Add(cleanRequested2);
            materializedNodeSet.Add(dependencyWhichIsDownstreamOfAProcess);
            materializedNodeSet.Add(requestedReachableWhichIsDownstreamOfAProcess);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[]
                {
                    dirtyRequested,

                    // Scheduled because it is not materialized, although clean.
                    cleanSharedDependency,

                    // Need to be scheduled so dirtyRequested can execute properly.
                    dependencyWhichIsNotDownstreamOfAProcess,

                    // Scheduled because it becomes dirty after adding cleanSharedDependency.
                    cleanRequested,

                    // Need to be scheduled so that cleanRequested can execute properly.
                    cleanDependency,

                    // Scheduled because it becomes dirty after adding cleanSharedDependency.
                    cleanRequested2,

                    // Needs to be scheduled so that cleanRequested2 can execute properly.
                    dependencyWhichIsDownstreamOfAProcess,
                },
                explicitlyScheduledNodes:
                    new[]
                    {
                        dirtyRequested, cleanRequested, cleanRequested2, cleanRequestedDirtyUnrequestedDependency,
                        requestedReachableWhichIsDownstreamOfAProcess
                    },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: false);
        }

        [Fact]
        public void IncrementalSchedulingSpecial()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            NodeId[] n = new NodeId[10];
            n[0] = buildSetCalculator.CreateNode(PipType.Process);
            n[1] = buildSetCalculator.CreateNode(PipType.Process);
            n[2] = buildSetCalculator.CreateNode(PipType.Process);
            n[3] = buildSetCalculator.CreateNode(PipType.Process);
            n[4] = buildSetCalculator.CreateNode(PipType.SealDirectory);
            n[5] = buildSetCalculator.CreateNode(PipType.Process);

            // This should make all nodes get added.
            n[6] = buildSetCalculator.CreateNode(PipType.Process);

            n[7] = buildSetCalculator.CreateNode(PipType.Process);
            n[8] = buildSetCalculator.CreateNode(PipType.Process);
            n[9] = buildSetCalculator.CreateNode(PipType.Process);

            // All nodes are assumed to be clean.
            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);

            // But only 1 & 3 have materialized its outputs.
            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(n[1]);
            materializedNodeSet.Add(n[3]);

            // Dependencies: 

            // 0 -> 1M -> 2 -> 3M -> 4[SD] -> 6 <- 7 <- 8 <- 9 
            //                       ^
            //                       |
            //                       5
            graph.AddEdge(n[1], n[0]);
            graph.AddEdge(n[2], n[1]);
            graph.AddEdge(n[3], n[2]);
            graph.AddEdge(n[4], n[3]);
            graph.AddEdge(n[6], n[4]);
            graph.AddEdge(n[6], n[7]);
            graph.AddEdge(n[7], n[8]);
            graph.AddEdge(n[8], n[9]);
            graph.AddEdge(n[4], n[5]);

            buildSetCalculator.ComputeAndValidate(
                    expectedNodes: n.ToArray(),
                    explicitlyScheduledNodes: new[] { n[0], n[5], n[9] },
                    forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                    scheduleDependents: true);
        }

        [Fact]
        public void IncrementalSchedulingCopyAndWriteFileDoNotDirtyItsDependents()
        {
            MutableDirectedGraph graph = new MutableDirectedGraph();

            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);
            NodeId[] n = new NodeId[8];
            n[0] = buildSetCalculator.CreateNode(PipType.Process);
            n[1] = buildSetCalculator.CreateNode(PipType.Process);
            n[2] = buildSetCalculator.CreateNode(PipType.Process);
            n[3] = buildSetCalculator.CreateNode(PipType.SealDirectory);
            n[4] = buildSetCalculator.CreateNode(PipType.CopyFile);
            n[5] = buildSetCalculator.CreateNode(PipType.WriteFile);
            n[6] = buildSetCalculator.CreateNode(PipType.Process);
            n[7] = buildSetCalculator.CreateNode(PipType.Process);

            // Nodes 0 & 7 are dirty, and they are requested.
            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            buildSetCalculator.MarkProcessNodeDirty(n[0]);
            buildSetCalculator.MarkProcessNodeDirty(n[7]);

            // Nodes 2 & 5 have materialized their outputs.
            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Add(n[1]);
            materializedNodeSet.Add(n[5]);

            // Graph:
            // 0,D -> 1,M -> 2 -> 3/SD <- 6 <- 7,D
            //                     |
            //                     +-> 4/CP -> 5/WF,M
            graph.AddEdge(n[5], n[4]);
            graph.AddEdge(n[4], n[3]);
            graph.AddEdge(n[3], n[6]);
            graph.AddEdge(n[6], n[7]);
            graph.AddEdge(n[3], n[2]);
            graph.AddEdge(n[2], n[1]);
            graph.AddEdge(n[1], n[0]);

            // Node 2 should not be scheduled.
            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n[0], n[1], n[3], n[4], n[5], n[6], n[7] },
                explicitlyScheduledNodes: new[] { n[0], n[7] },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void NonMaterializedPips()
        {
            var graph = new MutableDirectedGraph();
            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();
            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            // n1 --> n3 --> n4
            //           ^
            //           |
            //    n2 ----+

            var n1 = buildSetCalculator.CreateNode(PipType.Process);
            var n2 = buildSetCalculator.CreateNode(PipType.Process);
            var n3 = buildSetCalculator.CreateNode(PipType.Process);
            var n4 = buildSetCalculator.CreateNode(PipType.Process);

            graph.AddEdge(n1, n3);
            graph.AddEdge(n2, n3);
            graph.AddEdge(n3, n4);

            // n3 is clean but non materialized, only n4 is materialized.
            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.ClearAndSetRange(graph.NodeRange);

            materializedNodeSet.Add(n4);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n1, n2, n3, n4 },
                explicitlyScheduledNodes: new[] { n3 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact]
        public void DirtyBuildBuildsDependents()
        {
            var graph = new MutableDirectedGraph();
            var buildSetCalculator = new TestBuildSetCalculator(graph);
            var n1 = buildSetCalculator.CreateNode(PipType.Process);
            var n2 = buildSetCalculator.AddSealedDirectory(new Directory(), SealDirectoryKind.Full);
            var n3 = buildSetCalculator.CreateNode(PipType.Process);
            var n4 = buildSetCalculator.CreateNode(PipType.Process);

            // Dependencies:
            // n4 -> n3
            // n3 -> n2
            // n2 -> n1
            graph.AddEdge(n1, n2);
            graph.AddEdge(n2, n3);
            graph.AddEdge(n3, n4);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n1, n2, n3, n4 },
                explicitlyScheduledNodes: new[] { n1, n4 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Always,
                scheduleDependents: false);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n1, n2, n3 },
                explicitlyScheduledNodes: new[] { n1, n3 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Always,
                scheduleDependents: false);
        }

        [Fact]
        public void BasicDirtyBuild()
        {
            var graph = new MutableDirectedGraph();
            var buildSetCalculator = new TestBuildSetCalculator(graph);
            var n1 = buildSetCalculator.CreateNode(PipType.Process);
            var n2 = buildSetCalculator.AddProcess(new[] { buildSetCalculator.CreateFile(exists: true) });
            var n3 = buildSetCalculator.AddProcess(new[] { buildSetCalculator.CreateFile(exists: false, sourceFile: false, producer: n2) });
            var n4 = buildSetCalculator.CreateNode(PipType.Process);

            // n2 depends n1
            graph.AddEdge(n1, n2);

            // n3 depends n2
            graph.AddEdge(n2, n3);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n2 }, 
                explicitlyScheduledNodes: new[] { n2 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Always,
                scheduleDependents: false);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n2, n3 }, 
                explicitlyScheduledNodes: new[] { n3 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Always,
                scheduleDependents: false);
        }

        [Fact]
        public void BasicModuleDirtyBuild()
        {
            var graph = new MutableDirectedGraph();
            var buildSetCalculator = new TestBuildSetCalculator(graph);
            var m1 = ModuleId.UnsafeCreate(1);
            var m2 = ModuleId.UnsafeCreate(2);
            var n1 = buildSetCalculator.AddProcess(moduleId: m1);
            var n2 = buildSetCalculator.AddProcess(moduleId: m1, files: new[] { buildSetCalculator.CreateFile(exists: true) });
            var n3 = buildSetCalculator.AddProcess(moduleId: m2, files: new[] { buildSetCalculator.CreateFile(exists: false, sourceFile: false, producer: n2) });
            var n4 = buildSetCalculator.AddProcess(moduleId: m2, files: new[] { buildSetCalculator.CreateFile(exists: true) });
            var n5 = buildSetCalculator.AddProcess(moduleId: m1, files: new[] { buildSetCalculator.CreateFile(exists: true) });

            // n2 depends n1
            graph.AddEdge(n1, n2);

            // n3 depends n2
            graph.AddEdge(n2, n3);

            // n4 depends n3
            graph.AddEdge(n3, n4);

            // Even though n2's inputs are present, we need to schedule its dependency, n1 as they are from the same module.
            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n1, n2 },
                explicitlyScheduledNodes: new[] { n2 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Module,
                scheduleDependents: false);

            // n2 is the producer of the n3's missing input. Even though n2's module (m1) is not the requested module, 
            // we need to schedule n2 as well due to missing input
            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n2, n3 },
                explicitlyScheduledNodes: new[] { n3 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Module,
                scheduleDependents: false);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n2, n3, n4 },
                explicitlyScheduledNodes: new[] { n4 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Module,
                scheduleDependents: false);

            // If normal dirty build is used, then BuildXL should only schedule n4 because there is no inputs missing.
            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n4 },
                explicitlyScheduledNodes: new[] { n4 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Always,
                scheduleDependents: false);

            // When requested multiple modules, the pips belonging to those modules cannot be skipped.
            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { n1, n2, n3, n4, n5 },
                explicitlyScheduledNodes: new[] { n4, n5 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Module,
                scheduleDependents: false);
        }

        [Fact]
        public void DirtyBuildWithAllMaterializedPips()
        {
            var graph = new MutableDirectedGraph();
            var dirtyNodeSet = new RangedNodeSet();
            var materializedNodeSet = new RangedNodeSet();

            var dirtyNodeTracker = new DirtyNodeTracker(
                graph: graph,
                dirtyNodes: dirtyNodeSet,
                perpetualDirtyNodes: new RangedNodeSet(),
                dirtyNodesChanged: false,
                materializedNodes: materializedNodeSet);
            var buildSetCalculator = new TestBuildSetCalculator(graph, dirtyNodeTracker);

            // n1 --> n3 --> n4
            //           ^
            //           |
            //    n2 ----+

            var n1 = buildSetCalculator.CreateNode(PipType.Process);
            var n2 = buildSetCalculator.CreateNode(PipType.Process);
            var n3 = buildSetCalculator.CreateNode(PipType.Process);
            var n4 = buildSetCalculator.CreateNode(PipType.Process);

            graph.AddEdge(n1, n3);
            graph.AddEdge(n2, n3);
            graph.AddEdge(n3, n4);

            // All clean, all materialized.
            dirtyNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.ClearAndSetRange(graph.NodeRange);
            materializedNodeSet.Fill();

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new NodeId[0],
                explicitlyScheduledNodes: new[] { n3 },
                forceSkipDepsMode: ForceSkipDependenciesMode.Disabled,
                scheduleDependents: true);
        }

        [Fact(DisplayName = nameof(DirtyBuildCopyPipsAreTransparent))]
        public void DirtyBuildCopyPipsAreTransparent()
        {
            var graph = new MutableDirectedGraph();
            var buildSetCalculator = new TestBuildSetCalculator(graph);

            var inputsMissingTransitiveDependency = buildSetCalculator.AddProcess(
                new[] { buildSetCalculator.CreateFile(exists: false) });

            var inputPresentDependency = buildSetCalculator.AddProcess(
                new[] { buildSetCalculator.CreateFile(exists: true) });
            var copyNode = buildSetCalculator.AddCopy(buildSetCalculator.CreateFile(exists: false, sourceFile: false, producer: inputPresentDependency));
            var requested = buildSetCalculator.AddProcess(new[] { buildSetCalculator.CreateFile(exists: false, sourceFile: false, producer: copyNode) });
            var unrequested = graph.CreateNode();

            // These two nodes shouldn't be built because they aren't in the closure
            var notInRequestedClosure = buildSetCalculator.CreateNode(PipType.Process);
            var unrelated = buildSetCalculator.CreateNode(PipType.Process);

            // Dependencies: 
            // inputsMissingTransitiveDependency will not be included because the only
            // connection it has to the dependency closure is through inputPresentDependency
            // which has all inputs present
            // inputPresentDependency -> inputsMissingTransitiveDependency
            // requested -> copyNode -> inputPresentDependency
            // requested -> unrequested -> inputPresentDependency
            // notInRequestedClosure -> unrequested
            graph.AddEdge(inputPresentDependency, copyNode);
            graph.AddEdge(copyNode, requested); // requested depends on copynode
            graph.AddEdge(unrequested, requested);
            graph.AddEdge(inputPresentDependency, unrequested);
            graph.AddEdge(unrequested, notInRequestedClosure);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { inputPresentDependency, copyNode, requested, unrequested},
                explicitlyScheduledNodes: new[] { requested },
                forceSkipDepsMode: ForceSkipDependenciesMode.Always,
                scheduleDependents: false);
        }

        [Fact]
        public void DirtyBuildWithDirectoryDependency()
        {
            var graph = new MutableDirectedGraph();
            var buildSetCalculator = new TestBuildSetCalculator(graph);

            var inputsMissingTransitiveDependency = buildSetCalculator.AddProcess(
                new[] { buildSetCalculator.CreateFile(exists: false) });

            var inputPresentDependency = buildSetCalculator.AddProcess(
                new[] { buildSetCalculator.CreateFile(exists: true) });

            var sealDir = new Directory(new[] { buildSetCalculator.CreateFile(exists: false, sourceFile: false, producer: inputPresentDependency) });
            var sealNode = buildSetCalculator.AddSealedDirectory(sealDir, SealDirectoryKind.Full);
            var dirInputMissingDependency = buildSetCalculator.AddProcess(
                files: new[] { buildSetCalculator.CreateFile(exists: true) }, 
                dirs: new[] { sealDir });
            
            var requested = buildSetCalculator.AddProcess(new[] { buildSetCalculator.CreateFile(exists: false, sourceFile: false, producer: dirInputMissingDependency) });

            // These two nodes shouldn't be built because they aren't in the closure
            var notInRequestedClosure = buildSetCalculator.CreateNode(PipType.Process);
            var unrelated = buildSetCalculator.CreateNode(PipType.Process);

            // Dependencies: 
            // requested -> dirInputMissingDependency -> sealNode -> inputPresentDependency -> inputsMissingTransitiveDependency
            graph.AddEdge(inputsMissingTransitiveDependency, inputPresentDependency);
            graph.AddEdge(inputPresentDependency, sealNode);
            graph.AddEdge(sealNode, dirInputMissingDependency);
            graph.AddEdge(dirInputMissingDependency, requested);
            graph.AddEdge(requested, notInRequestedClosure);

            buildSetCalculator.ComputeAndValidate(
                expectedNodes: new[] { inputPresentDependency, sealNode, dirInputMissingDependency, requested },
                explicitlyScheduledNodes: new[] { requested },
                forceSkipDepsMode: ForceSkipDependenciesMode.Always,
                scheduleDependents: false);
        }

        /// <summary>
        /// Creates a test graph.
        /// </summary>
        private static void CreateGraph(out MutableDirectedGraph graph, out NodeId[] nodes)
        {
            graph = new MutableDirectedGraph();
            XAssert.AreEqual(graph.NodeCount, 0);
            XAssert.AreEqual(graph.EdgeCount, 0);

            // Test creation.
            nodes = new NodeId[20];
            for (int i = 0; i < nodes.Length; ++i)
            {
                nodes[i] = graph.CreateNode();
            }

            XAssert.AreEqual(graph.NodeCount, nodes.Length);
            XAssert.AreEqual(graph.EdgeCount, 0);

            foreach (NodeId t in nodes)
            {
                XAssert.IsTrue(graph.ContainsNode(t));
                XAssert.IsTrue(graph.IsSourceNode(t));
                XAssert.IsTrue(graph.IsSinkNode(t));
            }

            graph.AddEdge(nodes[5], nodes[0]);
            graph.AddEdge(nodes[5], nodes[1]);
            graph.AddEdge(nodes[6], nodes[1]);
            graph.AddEdge(nodes[6], nodes[2]);
            graph.AddEdge(nodes[7], nodes[5]);
            graph.AddEdge(nodes[9], nodes[5]);
            graph.AddEdge(nodes[8], nodes[4]);
            graph.AddEdge(nodes[9], nodes[6]);
            graph.AddEdge(nodes[14], nodes[7]);
            graph.AddEdge(nodes[10], nodes[8]);
            graph.AddEdge(nodes[11], nodes[8]);
            graph.AddEdge(nodes[11], nodes[9]);
        }

        /// <summary>
        /// Build set calculator using test data structures
        /// </summary>
        private sealed class TestBuildSetCalculator : BuildSetCalculator<TestProcess, PathId, FileId, Directory>
        {
            private readonly MutableDirectedGraph m_graph;

            public TestBuildSetCalculator(MutableDirectedGraph graph, DirtyNodeTracker incrementalSchedulingState = null) 
                : base(Events.StaticContext, graph, incrementalSchedulingState, new CounterCollection<PipExecutorCounter>())
            {
                m_graph = graph;
            }

            public readonly ConcurrentDenseIndex<PipType> PipTypes = new ConcurrentDenseIndex<PipType>(debug: false);
            public readonly ConcurrentDenseIndex<ModuleId> ModuleIds = new ConcurrentDenseIndex<ModuleId>(debug: false);

            public readonly ConcurrentDictionary<Directory, Tuple<SealDirectoryKind, NodeId>> SealDirectoryKinds = new ConcurrentDictionary<Directory, Tuple<SealDirectoryKind, NodeId>>();
            public readonly ConcurrentDictionary<NodeId, Directory> SealDirectories = new ConcurrentDictionary<NodeId, Directory>();
            public readonly ConcurrentDictionary<NodeId, int> CopyFiles = new ConcurrentDictionary<NodeId, int>();
            public readonly ConcurrentDictionary<int, NodeId> Producers = new ConcurrentDictionary<int, NodeId>();

            public readonly ConcurrentDenseIndex<TestProcess> Processes = new ConcurrentDenseIndex<TestProcess>(debug: false);
            public readonly ConcurrentBitArray ExistentFiles = new ConcurrentBitArray(100, defaultValue: true);

            private static readonly TestProcess DummyProcess = new TestProcess();

            private int m_nextFile = 0;

            public int CreateFile(bool exists = true, bool sourceFile = true, NodeId producer = default(NodeId))
            {
                var file = m_nextFile;
                m_nextFile++;

                ExistentFiles[file] = exists;

                if (!sourceFile)
                {
                    Contract.Assert(producer.IsValid);
                    Producers.Add(file, producer);
                }

                return file;
            }

            public NodeId CreateNode(PipType pipType, ModuleId moduleId = default(ModuleId))
            {
                var node = m_graph.CreateNode();
                PipTypes[node.Value] = pipType;

                ModuleIds[node.Value] = moduleId;

                return node;
            }

            public NodeId AddCopy(FileId file, ModuleId moduleId = default(ModuleId))
            {
                var node = CreateNode(PipType.CopyFile, moduleId);
                CopyFiles.Add(node, file);
                return node;
            }

            public NodeId AddSealedDirectory(Directory directory, SealDirectoryKind kind, ModuleId moduleId = default(ModuleId))
            {
                var node = CreateNode(PipType.SealDirectory, moduleId);
                SealDirectoryKinds.Add(directory, Tuple.Create(kind, node));
                SealDirectories.Add(node, directory);
                return node;
            }

            public NodeId AddProcess(FileId[] files = null, Directory[] dirs = null, ModuleId moduleId = default(ModuleId))
            {
                var node = CreateNode(PipType.Process, moduleId);
                Processes[node.Value] = new TestProcess()
                {
                    Dependencies = files,
                    Directories = dirs
                };

                return node;
            }

            protected override bool ExistsAsFile(FileId path)
            {
                return ExistentFiles[path];
            }

            protected override ReadOnlyArray<Directory> GetDirectoryDependencies(TestProcess process)
            {
                return process.Directories != null ?
                    ReadOnlyArray<Directory>.FromWithoutCopy(process.Directories) :
                    ReadOnlyArray<Directory>.Empty;
            }

            protected override ReadOnlyArray<FileId> GetFileDependencies(TestProcess process)
            {
                return process.Dependencies != null ?
                    ReadOnlyArray<FileId>.FromWithoutCopy(process.Dependencies) :
                    ReadOnlyArray<FileId>.Empty;
            }

            protected override PathId GetPath(FileId file)
            {
                return file;
            }

            protected override PipType GetPipType(NodeId node)
            {
                return PipTypes[node.Value];
            }

            protected override TestProcess GetProcess(NodeId node)
            {
                return Processes[node.Value] ?? DummyProcess;
            }

            protected override Directory GetSealDirectoryArtifact(NodeId node)
            {
                Directory result = null;
                SealDirectories.TryGetValue(node, out result);
                return result;
            }

            protected override ReadOnlyArray<FileId> ListSealedDirectoryContents(Directory directory)
            {
                return ReadOnlyArray<FileId>.FromWithoutCopy(directory.Dependencies ?? new FileId[0]);
            }

            public void ComputeAndValidate(NodeId[] explicitlyScheduledNodes, NodeId[] expectedNodes, ForceSkipDependenciesMode forceSkipDepsMode = ForceSkipDependenciesMode.Always, bool scheduleDependents = false)
            {
                var scheduledNodesResult = GetNodesToSchedule(scheduleDependents, explicitlyScheduledNodes, forceSkipDepsMode, scheduleMetaPips: false);

                // Transform to array index to align with indices when nodes are created into an array
                Assert.Equal(NormalizeNodes(expectedNodes), NormalizeNodes(scheduledNodesResult.ScheduledNodes));
            }

            private RangedNodeSet GetNodeSet(IEnumerable<NodeId> nodes)
            {
                var nodeSet = new RangedNodeSet();
                nodeSet.ClearAndSetRange(m_graph.NodeRange);

                foreach (var node in nodes)
                {
                    nodeSet.Add(node);
                }

                return nodeSet;
            }

            private uint[] NormalizeNodes(IEnumerable<NodeId> nodes)
            {
                return nodes.Select(n => n.Value - 1).Distinct().OrderBy(id => id).ToArray();
            }

            protected override bool IsFileRequiredToExist(int file)
            {
                // For testing purposes, require all files to exist
                return true;
            }

            protected override int GetCopyFile(NodeId node)
            {
                return CopyFiles[node];
            }

            protected override ModuleId GetModuleId(NodeId node)
            {
                return ModuleIds[node.Value];
            }

            protected override NodeId GetProducer(int file)
            {
                return Producers[file];
            }

            protected override NodeId GetProducer(Directory directory)
            {
                foreach (var entry in SealDirectories)
                {
                    if (entry.Value == directory)
                    {
                        return entry.Key;
                    }
                }

                return NodeId.Invalid;
            }

            protected override bool IsDynamicKindDirectory(NodeId node)
            {
                var dir = SealDirectories[node];
                return SealDirectoryKinds[dir].Item1.IsDynamicKind();
            }

            protected override SealDirectoryKind GetSealedDirectoryKind(NodeId node)
            {
                if (SealDirectories.TryGetValue(node, out var dir))
                {
                    return SealDirectoryKinds[dir].Item1;
                }

                return default(SealDirectoryKind);
            }

            protected override string GetModuleName(ModuleId moduleId)
            {
                return moduleId.Value + string.Empty;
            }

            protected override string GetPathString(FileId file)
            {
                return GetPath(file) + string.Empty;
            }
        }

        private class FileSetBase
        {
            public FileId[] Dependencies;
        }

        private class TestProcess : FileSetBase
        {
            public Directory[] Directories;
        }

        private class Directory : FileSetBase
        {
            public Directory(FileId[] content = null)
            {
                Dependencies = content;
            }
        }
    }
}
