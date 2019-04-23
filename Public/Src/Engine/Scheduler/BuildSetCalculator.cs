// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Computes the set of nodes to build from a graph and dirty node tracker states
    /// </summary>
    internal abstract class BuildSetCalculator<TProcess, TPath, TFile, TDirectory>
    {
        /// <summary>
        /// The dataflow graph of nodes
        /// </summary>
        private readonly DirectedGraph m_graph;

        /// <summary>
        /// The node visitor for the graph
        /// </summary>
        private readonly NodeVisitor m_visitor;

        /// <summary>
        /// Tracks dirty state of nodes
        /// </summary>
        private readonly DirtyNodeTracker m_dirtyNodeTracker;

        /// <summary>
        /// PipExecutor counters
        /// </summary>
        private readonly CounterCollection<PipExecutorCounter> m_counters;

        /// <summary>
        /// Logging context.
        /// </summary>
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Constructor for build set calculator.
        /// </summary>
        /// <param name="loggingContext">Logging context.</param>
        /// <param name="graph">Pip graph.</param>
        /// <param name="dirtyNodeTracker">Dirty node tracker.</param>
        /// <param name="counters">Counter collection.</param>
        protected BuildSetCalculator(
            LoggingContext loggingContext,
            DirectedGraph graph,
            DirtyNodeTracker dirtyNodeTracker,
            CounterCollection<PipExecutorCounter> counters)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(graph != null);
            Contract.Requires(counters != null);

            m_loggingContext = loggingContext;
            m_graph = graph;
            m_visitor = new NodeVisitor(graph);
            m_dirtyNodeTracker = dirtyNodeTracker;
            m_counters = counters;
        }

        #region AbstractMembers

        /// <summary>
        /// Gets the pip type of the node
        /// </summary>
        protected abstract PipType GetPipType(NodeId node);

        /// <summary>
        /// Gets the process data for the node
        /// </summary>
        protected abstract TProcess GetProcess(NodeId node);

        /// <summary>
        /// Gets the module id of the node
        /// </summary>
        protected abstract ModuleId GetModuleId(NodeId node);

        /// <summary>
        /// Gets the module name of the node
        /// </summary>
        protected abstract string GetModuleName(ModuleId moduleId);

        /// <summary>
        /// Gets the copy file for the CopyFile pip.
        /// </summary>
        protected abstract TFile GetCopyFile(NodeId node);

        /// <summary>
        /// Gets the process data for the node
        /// </summary>
        protected abstract TDirectory GetSealDirectoryArtifact(NodeId node);

        /// <summary>
        /// Gets the process file dependencies
        /// </summary>
        protected abstract ReadOnlyArray<TFile> GetFileDependencies(TProcess process);

        /// <summary>
        /// Gets the process directory dependencies
        /// </summary>
        protected abstract ReadOnlyArray<TDirectory> GetDirectoryDependencies(TProcess process);

        /// <summary>
        /// Gets the files contained sealed the directory
        /// </summary>
        protected abstract ReadOnlyArray<TFile> ListSealedDirectoryContents(TDirectory directory);

        /// <summary>
        /// Gets the path for the file
        /// </summary>
        protected abstract TPath GetPath(TFile file);

        /// <summary>
        /// Gets the path string for the file
        /// </summary>
        protected abstract string GetPathString(TPath path);

        /// <summary>
        /// Gets whether the file exists
        /// </summary>
        protected abstract bool ExistsAsFile(TPath path);

        /// <summary>
        /// Gets whether the file is required to exist for the purpose of skipping dependencies
        /// </summary>
        protected abstract bool IsFileRequiredToExist(TFile file);

        /// <summary>
        /// Gets the producer
        /// </summary>
        protected abstract NodeId GetProducer(TFile file);

        /// <summary>
        /// Gets the producer
        /// </summary>
        protected abstract NodeId GetProducer(TDirectory directory);

        /// <summary>
        /// Whether it is opaque or shared opaque
        /// </summary>
        protected abstract bool IsDynamicKindDirectory(NodeId node);

        /// <nodoc/>
        protected abstract SealDirectoryKind GetSealedDirectoryKind(NodeId node);

        /// <summary>
        /// Gets pip description for debugging purpose.
        /// </summary>
        protected virtual string GetDescription(NodeId node)
        {
            return string.Empty;
        }

        /// <summary>
        /// Checks if the output of a node is rewritten.
        /// </summary>
        protected virtual bool IsRewrittenPip(NodeId node)
        {
            return false;
        }

        #endregion

        /// <summary>
        /// Result of <see cref="BuildSetCalculator{TProcess,TPath,TFile,TDirectory}.GetNodesToSchedule(bool,System.Collections.Generic.IEnumerable{BuildXL.Scheduler.Graph.NodeId},ForceSkipDependenciesMode,bool)"/>
        /// </summary>
        public sealed class GetScheduledNodesResult
        {
            /// <summary>
            /// Scheduled nodes.
            /// </summary>
            public readonly IEnumerable<NodeId> ScheduledNodes;

            /// <summary>
            /// Must-executed nodes used by dirty build.
            /// </summary>
            public readonly HashSet<NodeId> MustExecuteNodes;

            /// <summary>
            /// The count of processes that were determined to be cache hits via incremental scheduling. This set
            /// includes <see cref="CleanMaterializedProcessFrontierCount"/>. So a subset of these pips will still be
            /// run through the standard scheduler algorithm
            /// </summary>
            public readonly int IncrementalSchedulingCacheHitProcesses;

            /// <summary>
            /// The count of processes that are clean and materialized, but must be run through the traditional scheduler
            /// so it is aware of their output content
            /// </summary>
            public readonly int CleanMaterializedProcessFrontierCount;

            /// <summary>
            /// Constructor to build a dummy result when there are no dirty nodes to schedule.
            /// </summary>
            /// <param name="incrementalSchedulingCacheHitProcesses">
            /// The number of nodes that were skipped by incremental scheduling.
            /// </param>
            public static GetScheduledNodesResult CreateForNoOperationBuild(int incrementalSchedulingCacheHitProcesses)
            {
                return new GetScheduledNodesResult(incrementalSchedulingCacheHitProcesses);
            }

            /// <summary>
            /// Constructor to build a dummy result when there are no dirty nodes to schedule.
            /// </summary>
            /// <param name="incrementalSchedulingCacheHitProcesses">
            /// The number of nodes that were skipped by incremental scheduling.
            /// </param>
            private GetScheduledNodesResult(int incrementalSchedulingCacheHitProcesses)
            {
                ScheduledNodes = new NodeId[0];
                MustExecuteNodes = new HashSet<NodeId>();
                IncrementalSchedulingCacheHitProcesses = incrementalSchedulingCacheHitProcesses;
            }

            /// <nodoc/>
            public GetScheduledNodesResult(
                IEnumerable<NodeId> scheduledNodes, 
                HashSet<NodeId> mustExecuteNodes,
                int incrementalSchedulingCacheHitProcesses, 
                int cleanMaterializedProcessFrontierCount)
            {
                ScheduledNodes = scheduledNodes;
                MustExecuteNodes = mustExecuteNodes;
                IncrementalSchedulingCacheHitProcesses = incrementalSchedulingCacheHitProcesses;
                CleanMaterializedProcessFrontierCount = cleanMaterializedProcessFrontierCount;
            }
        }

        /// <summary>
        /// Gets nodes to schedule.
        /// </summary>
        /// <param name="scheduleDependents">If true, then include all transitive dependents of the explicitly scheduled nodes.</param>
        /// <param name="explicitlyScheduledNodes">Explicitly scheduled nodes.</param>
        /// <param name="forceSkipDepsMode">If not disabled, then skip dependencies. This corresponds to "dirty" build.</param>
        /// <param name="scheduleMetaPips">If true, metapips will be scheduled</param>
        /// <returns>Nodes to schedule.</returns>
        public GetScheduledNodesResult GetNodesToSchedule(
            bool scheduleDependents,
            IEnumerable<NodeId> explicitlyScheduledNodes,
            ForceSkipDependenciesMode forceSkipDepsMode,
            bool scheduleMetaPips)
        {
            int explicitlySelectedNodeCount;
            int explicitlySelectedProcessCount;
            int dirtyNodeCount;
            int dirtyProcessCount;
            int nonMaterializedNodeCount;
            int nonMaterializedProcessCount;
            int processesInBuildCone = 0;

            HashSet<NodeId> nodesToSchedule;
            VisitationTracker transitiveDependencyNodeFilter;

            using (m_counters.StartStopwatch(PipExecutorCounter.BuildSetCalculatorComputeBuildCone))
            {
                var visitedNodes = new VisitationTracker(m_graph);
                nodesToSchedule = new HashSet<NodeId>(explicitlyScheduledNodes);
                explicitlySelectedNodeCount = nodesToSchedule.Count;
                explicitlySelectedProcessCount = nodesToSchedule.Count(IsProcess);

                // 1. Calculate dirty nodes.
                // The filter-passing set may include nodes which are dirty/clean and schedulable/not-schedulable (w.r.t. state).
                // We want stats on dirty vs. not-dirty, and want to drop anything not schedulable.
                // This step also marks dirty non-materialized nodes.
                CalculateDirtyNodes(
                    nodesToSchedule,
                    out dirtyNodeCount,
                    out dirtyProcessCount,
                    out nonMaterializedNodeCount,
                    out nonMaterializedProcessCount);

                if (dirtyNodeCount == 0)
                {
                    int duration = (int) m_counters.GetElapsedTime(PipExecutorCounter.BuildSetCalculatorComputeBuildCone).TotalMilliseconds;

                    // Build cone is the same as the explicitly selected processes.
                    Logger.Log.BuildSetCalculatorProcessStats(
                        m_loggingContext,
                        m_graph.Nodes.Count(IsProcess),
                        explicitlySelectedProcessCount,
                        explicitlySelectedProcessCount,
                        explicitlySelectedProcessCount,
                        0,
                        duration);
                    Logger.Log.BuildSetCalculatorStats(
                        m_loggingContext,
                        0,
                        0,
                        explicitlySelectedNodeCount,
                        explicitlySelectedProcessCount,
                        nonMaterializedNodeCount,
                        nonMaterializedProcessCount,
                        duration,
                        0,
                        0,
                        0,
                        0);
                    return GetScheduledNodesResult.CreateForNoOperationBuild(explicitlySelectedProcessCount);
                }

                // 2. Add transitive dependents of explicitly scheduled nodes (if requested).
                if (scheduleDependents)
                {
                    m_visitor.VisitTransitiveDependents(
                        nodesToSchedule,
                        visitedNodes,
                        node =>
                        {
                            // Don't schedule dependents that are meta pips. These may artificially connect unrequested
                            // pips since we will later schedule their dependencies. For example, this would cause
                            // everything referenced by a spec file pip to be scheduled as a single unit.
                            PipType pipType = GetPipType(node);
                            if (!pipType.IsMetaPip())
                            {
                                nodesToSchedule.Add(node);

                                if (pipType == PipType.Process)
                                {
                                    ++processesInBuildCone;
                                }

                                return true;
                            }

                            return false;
                        });
                }

                // At this point nodesToSchedule contains
                // (1) all nodes that are explicitly scheduled (explicitlyScheduledNodes), and
                // (2) if scheduleDependents is true, all dependents of (1) transitively.
                transitiveDependencyNodeFilter = visitedNodes;

                // 3. Collect/visit transitive dependencies, but don't put it in nodesToSchedule.
                transitiveDependencyNodeFilter.UnsafeReset();

                // The code below essentially does m_visitor.VisitTransitiveDependencies(nodesToSchedule, transitiveDependencyNodeFilter, node => true), but in parallel.
                foreach (var nodeId in nodesToSchedule)
                {
                    if (transitiveDependencyNodeFilter.MarkVisited(nodeId))
                    {
                        if (IsProcess(nodeId))
                        {
                            ++processesInBuildCone;
                        }
                    }
                }

                ParallelAlgorithms.WhileNotEmpty(
                    nodesToSchedule,
                    (node, add) =>
                    {
                        foreach (Edge inEdge in m_graph.GetIncomingEdges(node))
                        {
                            if (visitedNodes.MarkVisited(inEdge.OtherNode))
                            {
                                add(inEdge.OtherNode);
                                if (IsProcess(inEdge.OtherNode))
                                {
                                    Interlocked.Increment(ref processesInBuildCone);
                                }
                            }
                        }
                    });

                // At this point nodesToSchedule hasn't change from step 2.
                // But now, transitiveDependencyNodeFilter have already marked all nodes in nodesToSchedule, plus
                // their dependencies transitively.
            }

            IEnumerable<NodeId> scheduledNodes;
            var mustExecute = new HashSet<NodeId>();
            var stats = new BuildSetCalculatorStats();
            var metaPipCount = 0;

            using (m_counters.StartStopwatch(PipExecutorCounter.BuildSetCalculatorGetNodesToSchedule))
            {
                scheduledNodes = GetNodesToSchedule(
                    nodesToSchedule,
                    transitiveDependencyNodeFilter,
                    forceSkipDepsMode,
                    scheduleMetaPips,
                    mustExecute,
                    stats,
                    ref metaPipCount);
            }

            int buildConeDuration = (int) m_counters.GetElapsedTime(PipExecutorCounter.BuildSetCalculatorComputeBuildCone).TotalMilliseconds;
            int getScheduledNodesDuration = (int) m_counters.GetElapsedTime(PipExecutorCounter.BuildSetCalculatorGetNodesToSchedule).TotalMilliseconds;
            int scheduledProcessCount = scheduledNodes.Count(IsProcess);

            Logger.Log.BuildSetCalculatorProcessStats(
                m_loggingContext,
                m_graph.Nodes.Count(IsProcess),
                explicitlySelectedProcessCount,
                processesInBuildCone,
                (processesInBuildCone - scheduledProcessCount) + stats.CleanMaterializedProcessFrontierCount,
                scheduledProcessCount,
                buildConeDuration + getScheduledNodesDuration);

            Logger.Log.BuildSetCalculatorStats(
                m_loggingContext,
                dirtyNodeCount,
                dirtyProcessCount,
                explicitlySelectedNodeCount,
                explicitlySelectedProcessCount,
                nonMaterializedNodeCount,
                nonMaterializedProcessCount,
                buildConeDuration,
                scheduledNodes.Count(),
                scheduledProcessCount,
                metaPipCount,
                getScheduledNodesDuration);

            int incrementalSchedulingCacheHits = forceSkipDepsMode == ForceSkipDependenciesMode.Disabled
                ? (processesInBuildCone - scheduledProcessCount + stats.CleanMaterializedProcessFrontierCount)
                : 0;

            return new GetScheduledNodesResult(
                scheduledNodes: scheduledNodes,
                mustExecuteNodes: mustExecute,
                incrementalSchedulingCacheHitProcesses: incrementalSchedulingCacheHits,
                cleanMaterializedProcessFrontierCount: forceSkipDepsMode == ForceSkipDependenciesMode.Disabled ? stats.CleanMaterializedProcessFrontierCount : 0);
        }

        private IEnumerable<NodeId> GetNodesToSchedule(
            HashSet<NodeId> nodesToSchedule,
            VisitationTracker transitiveDependencyNodeFilter,
            ForceSkipDependenciesMode forceSkipDepsMode,
            bool scheduleMetaPips,
            HashSet<NodeId> mustExecute,
            BuildSetCalculatorStats stats,
            ref int metaPipCount)
        {
            if (forceSkipDepsMode == ForceSkipDependenciesMode.Disabled)
            {
                ScheduleDependenciesUntilCleanAndMaterialized(nodesToSchedule, transitiveDependencyNodeFilter, stats);
            }
            else
            {
                int numExplicitlySelectedProcesses, numScheduledProcesses, numExecutedProcesses, numExecutedProcessesWithoutDirty = 0;
                Func<NodeId, bool> isProcess = (node) => GetPipType(node) == PipType.Process;

                using (m_counters.StartStopwatch(PipExecutorCounter.ForceSkipDependenciesScheduleDependenciesUntilInputsPresentDuration))
                {
                    numExplicitlySelectedProcesses = nodesToSchedule.Count(isProcess);

                    // Calculate how many process pips are in the transitive dependency closure of the filtered pips
                    foreach (var node in m_graph.Nodes)
                    {
                        if (transitiveDependencyNodeFilter.WasVisited(node) && isProcess(node))
                        {
                            numExecutedProcessesWithoutDirty++;
                        }
                    }

                    ScheduleDependenciesUntilRequiredInputsPresent(nodesToSchedule, transitiveDependencyNodeFilter, mustExecute, forceSkipDepsMode);

                    numScheduledProcesses = nodesToSchedule.Where(isProcess).Count();
                    numExecutedProcesses = mustExecute.Where(isProcess).Count();
                }

                Logger.Log.DirtyBuildStats(
                    m_loggingContext,
                    (long)m_counters.GetElapsedTime(PipExecutorCounter.ForceSkipDependenciesScheduleDependenciesUntilInputsPresentDuration).TotalMilliseconds,
                    forceSkipDepsMode == ForceSkipDependenciesMode.Module,
                    numExplicitlySelectedProcesses,
                    numScheduledProcesses,
                    numExecutedProcesses,
                    numExecutedProcessesWithoutDirty - numExecutedProcesses);
            }

            int scheduledMetaPipCount = 0;

            if (scheduleMetaPips)
            {
                using (m_counters.StartStopwatch(PipExecutorCounter.BuildSetCalculatorComputeAffectedMetaPips))
                {
                    // We compute the affected meta pips from the scheduled nodes. Simply traversing the graph from
                    // the scheduled nodes is expensive, so we split the process by first computing the meta-pip frontiers,
                    // i.e., the meta-pips that directly depend on a scheduled node. This computation can be done in parallel.
                    // Assumption: the dependents of meta pips are always meta pips.

                    // (1) Compute the meta-pips frontiers from the scheduled nodes.
                    VisitationTracker visitedNodes = new VisitationTracker(m_graph);
                    ConcurrentDictionary<NodeId, Unit> metaPipFrontier = new ConcurrentDictionary<NodeId, Unit>();

                    Parallel.ForEach(
                        nodesToSchedule,
                        node =>
                        {
                            foreach (var oe in m_graph.GetOutgoingEdges(node))
                            {
                                if (GetPipType(oe.OtherNode).IsMetaPip())
                                {
                                    metaPipFrontier.TryAdd(oe.OtherNode, Unit.Void);
                                }
                            }
                        });

                    // (2) Traverse the graph from the frontiers.
                    m_visitor.VisitTransitiveDependents(
                        metaPipFrontier.Keys,
                        visitedNodes,
                        node =>
                        {
                            nodesToSchedule.Add(node);
                            ++scheduledMetaPipCount;
                            return true;
                        });
                }

                metaPipCount = scheduledMetaPipCount;
            }

            return nodesToSchedule;
        }

        private void ScheduleDependenciesUntilCleanAndMaterialized(
            HashSet<NodeId> nodesToSchedule,
            VisitationTracker buildCone,
            BuildSetCalculatorStats stats)
        {
            int initialNodesToScheduleCount = nodesToSchedule.Count;
            int initialProcessesToScheduleCount = nodesToSchedule.Count(IsProcess);
            int nodesAddedDueToNotCleanMaterializedCount = 0;
            int processesAddedDueToNotCleanMaterializedCount = 0;
            int nodesAddedDueToCollateralDirtyCount = 0;
            int processesAddedDueToCollateralDirtyCount = 0;

            // TODO: If this method turns out to be the bottleneck, we can make it parallel later.
            var cleanMaterializedNodes = new HashSet<NodeId>();

            using (m_counters.StartStopwatch(PipExecutorCounter.BuildSetCalculatorScheduleDependenciesUntilCleanAndMaterialized))
            {
                // Add all dirty nodes from nodesToSchedule. From those dirty nodes, we try to add to nodesToSchedule
                // their dependencies that need to be built.
                // Nodes that are not materialized should have been marked dirty by CalculateDirtyNodes.
                nodesToSchedule.RemoveWhere(node => !IsNodeDirty(node));

                var nodeQueue = new Queue<NodeId>(nodesToSchedule);

                Action<NodeId> addNode =
                    markedNode =>
                    {
                        if (buildCone.WasVisited(markedNode) && !GetPipType(markedNode).IsMetaPip() &&
                            nodesToSchedule.Add(markedNode))
                        {
                            // Node is in the build cone, and has not been scheduled yet.
                            nodeQueue.Enqueue(markedNode);
                            ++nodesAddedDueToCollateralDirtyCount;

                            if (IsProcess(markedNode))
                            {
                                ++processesAddedDueToCollateralDirtyCount;
                            }
                        }
                    };

                Action<NodeId> scheduleDependencyNode =
                    node =>
                    {
                        if (buildCone.WasVisited(node))
                        {
                            // The node is in the build cone.
                            var pipType = GetPipType(node);

                            // The node is clean if it's (1) marked as clean and materialized and (2) none of its outputs are rewritten.
                            // Condition (2) is conservative, and is needed for correctness in the presence of rewritten files.
                            bool isCleanMaterialized = IsNodeCleanAndMaterialized(node) && !IsRewrittenPip(node);

                            if (!isCleanMaterialized && nodesToSchedule.Add(node))
                            {
                                // (1) Node is dirty or has not materialized its outputs.
                                // (2) Node has not been scheduled yet.

                                // Mark process node dirty, and add its dependents so that the dependencies of its dependents can be added later.
                                MarkProcessNodeDirtyAndAddItsDependents(node, addNode);
                                ++nodesAddedDueToNotCleanMaterializedCount;

                                if (pipType != PipType.HashSourceFile)
                                {
                                    nodeQueue.Enqueue(node);
                                }

                                if (IsProcess(node))
                                {
                                    ++processesAddedDueToNotCleanMaterializedCount;
                                }
                            }

                            if (isCleanMaterialized)
                            {
                                cleanMaterializedNodes.Add(node);
                            }
                        }
                    };

                while (nodeQueue.Count > 0)
                {
                    NodeId node = nodeQueue.Dequeue();

                    foreach (Edge inEdge in m_graph.GetIncomingEdges(node))
                    {
                        scheduleDependencyNode(inEdge.OtherNode);
                    }
                }

                nodesToSchedule.UnionWith(cleanMaterializedNodes);
            }

            stats.CleanMaterializedNodeFrontierCount = cleanMaterializedNodes.Count;
            stats.CleanMaterializedProcessFrontierCount = cleanMaterializedNodes.Count(IsProcess);

            Logger.Log.BuildSetCalculatorScheduleDependenciesUntilCleanAndMaterializedStats(
                m_loggingContext,
                initialNodesToScheduleCount,
                initialProcessesToScheduleCount,
                nodesAddedDueToNotCleanMaterializedCount,
                processesAddedDueToNotCleanMaterializedCount,
                nodesAddedDueToCollateralDirtyCount,
                processesAddedDueToCollateralDirtyCount,
                stats.CleanMaterializedNodeFrontierCount,
                stats.CleanMaterializedProcessFrontierCount,
                (int)m_counters.GetElapsedTime(PipExecutorCounter.BuildSetCalculatorScheduleDependenciesUntilCleanAndMaterialized).TotalMilliseconds);
        }

        /// <summary>
        /// General statistics for build set calculator.
        /// </summary>
        private class BuildSetCalculatorStats
        {
            #region ScheduleDependenciesUntilCleanAndMaterialized

            /// <summary>
            /// Number of clean-materialized nodes in the build frontier.
            /// </summary>
            public int CleanMaterializedNodeFrontierCount { get; set; } = 0;

            /// <summary>
            /// Number of clean-materialized processes in the build frontier.
            /// </summary>
            public int CleanMaterializedProcessFrontierCount { get; set; } = 0;

            #endregion
        }

        private enum DirtyBuildExecuteReason
        {
            ExplicitlyFiltered,
            DynamicDirectory,
            DynamicDirectoryProducer,
            SameModule,
            SealContentsMissing,
            IpcPipDependent,
            MissingOutputs,
        }

        private sealed class DirtyBuildState
        {
            private readonly BuildSetCalculator<TProcess, TPath, TFile, TDirectory> m_context;

            private readonly ConcurrentBigMap<TFile, (NodeId nodeId, ExistenceState existenceState)> m_fileExistenceMap =
                new ConcurrentBigMap<TFile, (NodeId nodeId, ExistenceState existenceState)>();

            private readonly ConcurrentBigMap<TDirectory, (NodeId nodeId, ExistenceState existenceState)> m_directoryFilesExistenceMap =
                new ConcurrentBigMap<TDirectory, (NodeId nodeId, ExistenceState existenceState)>();

            private readonly Action<NodeId> m_newScheduledPipFunc;

            public readonly ConcurrentBigSet<NodeId> Nodes = new ConcurrentBigSet<NodeId>();
            public readonly ConcurrentBigMap<NodeId, DirtyBuildExecuteReason> ExecuteReasonMap = new ConcurrentBigMap<NodeId, DirtyBuildExecuteReason>();

            public readonly ConcurrentBigMap<NodeId, (TPath path, NodeId id)> MissingOutputs =
                new ConcurrentBigMap<NodeId, (TPath, NodeId)>(); // producer to <path, consumer>

            public DirtyBuildState(BuildSetCalculator<TProcess, TPath, TFile, TDirectory> buildSetCalculator, Action<NodeId> newScheduledPipFunc)
            {
                m_context = buildSetCalculator;
                m_newScheduledPipFunc = newScheduledPipFunc;
            }

            public void ReportScheduledPip(NodeId node, DirtyBuildExecuteReason reason)
            {
                if (Nodes.Add(node))
                {
                    m_newScheduledPipFunc(node);
                    ExecuteReasonMap.TryAdd(node, reason);
                }
            }

            public void ReportNotSkippedPips(IEnumerable<NodeId> nodes, DirtyBuildExecuteReason reason)
            {
                foreach (var node in nodes)
                {
                    ReportScheduledPip(node, reason);
                }
            }

            public bool CheckFileExistence(NodeId consumer, TFile file, bool isReport)
            {
                var result = m_fileExistenceMap.GetOrAdd(file, m_context, CheckFileExistenceCore).Item.Value;
                if (result.Item2 == ExistenceState.Missing)
                {
                    var pipId = result.nodeId;
                    Contract.Assert(pipId.IsValid);

                    if (isReport)
                    {
                        ReportScheduledPip(pipId, DirtyBuildExecuteReason.MissingOutputs);
                        MissingOutputs.TryAdd(pipId, (m_context.GetPath(file), consumer));
                    }

                    return false;
                }

                return true;
            }

            private static (NodeId, ExistenceState) CheckFileExistenceCore(
                TFile file,
                BuildSetCalculator<TProcess, TPath, TFile, TDirectory> context)
            {
                return context.ExistsAsFile(context.GetPath(file))
                    ? (NodeId.Invalid, ExistenceState.Exists)
                    : (context.GetProducer(file), ExistenceState.Missing);
            }

            public void CheckDirectoryExistence(TDirectory dir)
            {
                var result = m_directoryFilesExistenceMap.GetOrAdd(dir, m_context, CheckDirectoryExistenceCore).Item.Value;
                if (result.Item2 == ExistenceState.Missing)
                {
                    var pipId = result.nodeId;
                    Contract.Assert(pipId.IsValid);

                    ReportScheduledPip(
                        pipId,
                        m_context.IsDynamicKindDirectory(pipId) ? DirtyBuildExecuteReason.DynamicDirectory : DirtyBuildExecuteReason.SealContentsMissing);
                }
            }

            private (NodeId, ExistenceState) CheckDirectoryExistenceCore(
                TDirectory directory,
                BuildSetCalculator<TProcess, TPath, TFile, TDirectory> context)
            {
                ExistenceState existence;
                var producer = context.GetProducer(directory);
                var isDynamicDirectory = context.IsDynamicKindDirectory(producer);

                // Dynamic seal directories will be always scheduled as we are not aware of the contents during scheduling.
                if (isDynamicDirectory)
                {
                    existence = ExistenceState.Missing;
                }
                else
                {
                    // We do not care the producers of missing seal contents. We will add those when we process the seal directory (see FindMissingDepsForSeal())
                    // We only care whether there is at least one missing content. If so, we need to schedule the seal directory. Then, we can decide which dependencies
                    // need to be scheduled based on the producers of missing content.
                    var numMissingFiles = context.FindMissingDependenciesFromFiles(
                        this,
                        context.ListSealedDirectoryContents(directory),
                        producer,
                        isReport: false);
                    existence = numMissingFiles == 0 ? ExistenceState.Exists : ExistenceState.Missing;
                }

                return (existence == ExistenceState.Exists ? NodeId.Invalid : producer, existence);
            }
        }

        private void ScheduleDependenciesUntilRequiredInputsPresent(
            HashSet<NodeId> nodesToSchedule,
            VisitationTracker nodeFilter,
            HashSet<NodeId> mustExecute,
            ForceSkipDependenciesMode forceSkipDepsMode)
        {
            using (BlockingCollection<NodeId> nodeQueue = new BlockingCollection<NodeId>())
            {
                int pendingNodeCount = 0;
                var state = new DirtyBuildState(this, (node) =>
                    {
                        Interlocked.Increment(ref pendingNodeCount);
                        nodeQueue.Add(node);
                    });

                HashSet<ModuleId> explicitlyScheduledModules = new HashSet<ModuleId>();

                foreach (var node in nodesToSchedule)
                {
                    state.ReportScheduledPip(node, DirtyBuildExecuteReason.ExplicitlyFiltered);

                    ModuleId moduleId;
                    if (forceSkipDepsMode == ForceSkipDependenciesMode.Module && (moduleId = GetModuleId(node)).IsValid)
                    {
                        explicitlyScheduledModules.Add(moduleId);
                    }
                }

                if (forceSkipDepsMode == ForceSkipDependenciesMode.Module)
                {
                    Logger.Log.DirtyBuildExplicitlyRequestedModules(Events.StaticContext, string.Join(",", explicitlyScheduledModules.Select(m => GetModuleName(m))));
                }

                Task[] tasks = new Task[Environment.ProcessorCount];
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = Task.Run(() =>
                    {
                        while (!nodeQueue.IsCompleted)
                        {
                            NodeId node;
                            if (nodeQueue.TryTake(out node, Timeout.Infinite))
                            {
                                try
                                {
                                    // Dependencies which belong to the explicitly scheduled modules will be always scheduled.
                                    if (forceSkipDepsMode == ForceSkipDependenciesMode.Module)
                                    {
                                        foreach (var dep in GetDependencies(node))
                                        {
                                            var moduleId = GetModuleId(dep);
                                            if (!explicitlyScheduledModules.Contains(moduleId))
                                            {
                                                continue;
                                            }

                                            state.ReportScheduledPip(dep, DirtyBuildExecuteReason.SameModule);
                                        }
                                    }

                                    var pipType = GetPipType(node);
                                    switch (pipType)
                                    {
                                        case PipType.Process:
                                            FindMissingDependenciesForProcess(state, node);
                                            break;
                                        case PipType.SealDirectory:
                                            // We still need to check the inputs of SealDirectory pips even though we check the directory dependencies of process pips.
                                            // Think about the following scenario: we schedule the dependencies of a process pip due to the unexistence of some inputs.
                                            // Then, we do not need to schedule all dependencies of the SealDirectory, which is a dependency of that process pip.
                                            // The inputs of the SealDirectory might already exist so we can skip all dependencies of the SealDirectory.
                                            FindMissingDependenciesForSeal(state, node);
                                            break;
                                        case PipType.CopyFile:
                                            FindMissingDependenciesFromFiles(state, new[] { GetCopyFile(node) }, node);
                                            break;
                                        case PipType.WriteFile:
                                            // WriteFiles are available to be run at any point and will be materialized as part of running dependents.
                                            break;
                                        default:
                                            // Always build dependencies of IPC pips
                                            Contract.Assert(pipType == PipType.Ipc);
                                            state.ReportNotSkippedPips(GetDependencies(node), DirtyBuildExecuteReason.IpcPipDependent);
                                            break;
                                    }
                                }
                                catch (Exception)
                                {
                                    // Don't continue performing work
                                    nodeQueue.CompleteAdding();
                                    throw;
                                }
                                finally
                                {
                                    if (Interlocked.Decrement(ref pendingNodeCount) == 0)
                                    {
                                        nodeQueue.CompleteAdding();
                                    }
                                }
                            }
                        }
                    });
                }

                Task.WaitAll(tasks);

                // Update nodesToSchedule
                var nodes = state.Nodes.UnsafeGetList();
                nodesToSchedule.UnionWith(nodes);
                mustExecute.UnionWith(nodes);

                VisitationTracker dependentVisitedNodes = new VisitationTracker(m_graph);

                // Scheduled the dependents of the nodes but those will be skipped during execution.
                m_visitor.VisitTransitiveDependents(nodes, dependentVisitedNodes, visitNode: node =>
                {
                    // Only visit dependents that are in the filter set
                    if (nodeFilter.WasVisited(node))
                    {
                        nodesToSchedule.Add(node);
                        return true;
                    }

                    return false;
                });

                foreach (var entry in state.ExecuteReasonMap)
                {
                    var pipId = entry.Key;
                    var reason = entry.Value;
                    var description = GetDescription(pipId);
                    if (GetPipType(pipId) != PipType.Process)
                    {
                        continue;
                    }

                    if (reason == DirtyBuildExecuteReason.MissingOutputs)
                    {
                        var tuple = state.MissingOutputs.TryGet(pipId).Item.Value;
                        var path = GetPathString(tuple.path);
                        var consumerDescription = GetDescription(tuple.id);
                        Logger.Log.DirtyBuildProcessNotSkippedDueToMissingOutput(Events.StaticContext, description, path, consumerDescription);
                    }
                    else
                    {
                        Logger.Log.DirtyBuildProcessNotSkipped(Events.StaticContext, description, reason.ToString());
                    }
                }
            }
        }

        private void CalculateDirtyNodes(
            IEnumerable<NodeId> selectedNodes,
            out int dirtyNodeCount,
            out int dirtyProcessCount,
            out int nonMaterializedNodeCount,
            out int nonMaterializedProcessCount)
        {
            dirtyNodeCount = 0;
            dirtyProcessCount = 0;
            nonMaterializedNodeCount = 0;
            nonMaterializedProcessCount = 0;

            foreach (var node in selectedNodes)
            {
                if (IsNodeDirty(node))
                {
                    dirtyNodeCount++;

                    if (IsProcess(node))
                    {
                        ++dirtyProcessCount;
                    }
                }
                else
                {
                    if (!IsNodeMaterialized(node))
                    {
                        // Because
                        // (1) the node is selected (perhaps by filter),
                        // (2) it is marked clean, but
                        // (2) its outputs have never been materialized,
                        // consider the node dirty.
                        MarkProcessNodeDirty(node);
                        ++dirtyNodeCount;
                        ++nonMaterializedNodeCount;

                        if (IsProcess(node))
                        {
                            ++dirtyProcessCount;
                            ++nonMaterializedProcessCount;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Existence states for file
        /// </summary>
        private enum ExistenceState : byte
        {
            Exists,
            Missing,
        }

        private IEnumerable<NodeId> GetDependencies(NodeId node)
        {
            foreach (Edge inEdge in m_graph.GetIncomingEdges(node))
            {
                yield return inEdge.OtherNode;
            }
        }

        /// <summary>
        /// Finds the missing inputs of the given process and add their producing pips to the set
        /// </summary>
        /// NOTE: Node must represent a process
        private void FindMissingDependenciesForProcess(DirtyBuildState state, NodeId node)
        {
            Contract.Assert(IsProcess(node));

            var process = GetProcess(node);
            FindMissingDependenciesFromFiles(state, GetFileDependencies(process), node);
            FindMissingDependenciesFromDirectories(state, GetDirectoryDependencies(process));
        }

        /// <summary>
        /// Finds the missing contents of the given sealdirectory and add their producing pips to the set
        /// </summary>
        /// NOTE: Node must represent a seal directory
        private void FindMissingDependenciesForSeal(DirtyBuildState state, NodeId node)
        {
            // Dynamic seal directories will be always scheduled as we are not aware of the contents during scheduling.
            if (IsDynamicKindDirectory(node))
            {
                foreach (var dep in GetDependencies(node))
                {
                    state.ReportScheduledPip(dep, DirtyBuildExecuteReason.DynamicDirectoryProducer);
                }
            }
            else
            {
                var sealDir = GetSealDirectoryArtifact(node);
                FindMissingDependenciesFromFiles(state, ListSealedDirectoryContents(sealDir), node);
            }
        }

        private static void FindMissingDependenciesFromDirectories(DirtyBuildState state, ReadOnlyArray<TDirectory> dirs)
        {
            foreach (var dir in dirs)
            {
                state.CheckDirectoryExistence(dir);
            }
        }

        private int FindMissingDependenciesFromFiles<TFileList>(DirtyBuildState state, TFileList files, NodeId consumer, bool isReport = true)
            where TFileList : IReadOnlyList<TFile>
        {
            int numMissingFiles = 0;

            foreach (var file in files)
            {
                if (!IsFileRequiredToExist(file))
                {
                    continue;
                }

                var isExist = state.CheckFileExistence(consumer, file, isReport);
                if (!isExist)
                {
                    numMissingFiles++;
                }
            }

            return numMissingFiles;
        }

        /// <summary>
        /// Gets whether a node is a process
        /// </summary>
        private bool IsProcess(NodeId nodeId)
        {
            var pipType = GetPipType(nodeId);
            return pipType == PipType.Process;
        }

        /// <summary>
        /// Marks a process node dirty, as well as all of its dependents.
        /// </summary>
        /// <remarks>
        /// This function is called for a dependency of a dirty node. If it is a process node we may
        /// need to run it as the dependent dirty node will need its inputs. There is no way to tell if this pip ever
        /// ran and so its outputs may never have been materialized. We make such nodes dirty, which automatically makes
        /// its dependents dirty (which is good as the process may not be deterministic and running it may cause generated
        /// files and their hashes to change). Of course, this only needs to be done if the node is clean (which is also
        /// an indicator of an incremental scheduling). Other types of pips are deterministic and running them will not
        /// change any hashes.
        /// </remarks>
        public void MarkProcessNodeDirty(NodeId node)
        {
            if (!IsNodeDirty(node))
            {
                PipType pipType = GetPipType(node);

                if (pipType == PipType.Process)
                {
                    // Dirty this node and all dependents.
                    m_dirtyNodeTracker.MarkNodeDirty(node);
                }
            }
        }

        /// <summary>
        /// Marks a process node dirty, as well as all of its dependents, plus performs some action when a node gets marked dirty.
        /// </summary>
        private void MarkProcessNodeDirtyAndAddItsDependents(NodeId node, Action<NodeId> action)
        {
            if (IsNodeDirty(node))
            {
                return;
            }

            PipType pipType = GetPipType(node);

            if (pipType == PipType.Process)
            {
                m_dirtyNodeTracker.MarkNodeDirty(node, action);
            }
        }

        /// <summary>
        /// Checks if a node is dirty. All nodes are considered dirty for non-incremental builds.
        /// </summary>
        public bool IsNodeDirty(NodeId node)
        {
            return m_dirtyNodeTracker == null || m_dirtyNodeTracker.IsNodeDirty(node);
        }

        /// <summary>
        /// Checks if a node has ever materialized its outputs. All nodes are considered not materialized for non-incremental builds.
        /// </summary>
        public bool IsNodeMaterialized(NodeId node)
        {
            return m_dirtyNodeTracker != null && m_dirtyNodeTracker.IsNodeMaterialized(node);
        }

        private bool IsNodeCleanAndMaterialized(NodeId node)
        {
            return !IsNodeDirty(node) && IsNodeMaterialized(node);
        }
    }
}
