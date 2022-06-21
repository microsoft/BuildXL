// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.Fingerprints;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Scheduler.IncrementalScheduling.IncrementalSchedulingStateWriteTextHelpers;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Class implementing graph-agnostic incremental scheduling.
    /// </summary>
    /// <remarks>
    /// Graph-agnostic incremental scheduling state consists of
    /// 
    /// - graph-inagnostic state, and
    /// - graph-agnostic state.
    /// 
    /// Graph-inagnostic state is represented by <see cref="DirtyNodeTracker"/>. This allows incremental
    /// scheduling to maintain the same performance as graph-inagnostic incremental scheduling when 
    /// graph has not changed.
    /// 
    /// Graph-agnostic state tracks clean-and-materialized pips. The state is represented by the following maps,
    /// <see cref="m_cleanPips"/> and <see cref="m_cleanSourceFiles"/>. The former is for non hash-source-file pips, and
    /// the latter is for hash-source-file pips. Both maps map pips/paths to versions at which they become clean. These versions
    /// are crucial for checking is a pip is clean across multiple graph changes. For example, if a pip P is clean at version N, but
    /// one of its dependency is dirty or is clean at version M, where M > N, then P has to be considered dirty.
    /// 
    /// For space optimization, the <see cref="m_cleanPips"/> map is supported by the following maps, <see cref="m_pipOrigins"/> 
    /// and <see cref="m_pipProducers"/>. The <see cref="m_pipOrigins"/> map provides a one-to-one correspondence between 
    /// the pip stable ids in <see cref="m_cleanPips"/> to their fingerprints. This association is important because the pip graph
    /// speaks about pips in terms of their fingerprints. As the name implies, the <see cref="m_pipOrigins"/> also provides information about
    /// to which graph a pip belongs. The <see cref="m_pipProducers"/> map keeps track of clean producers by paths that they produce.
    /// This map is used when a path has a different producer when graph changes.
    /// 
    /// Pips become dirty due to two aspects:
    /// - Graph changes.
    /// - Result of journal scan.
    /// The first aspect is captured by <see cref="ProcessGraphChange"/>,
    /// while the second one is captured by the OnNext and OnCompleted methods. 
    /// </remarks>
    public sealed class GraphAgnosticIncrementalSchedulingState : IIncrementalSchedulingState
    {
        #region Graph agnostic state

        /// <summary>
        /// Pip graph sequence number.
        /// </summary>
        /// <remarks>
        /// This sequence number is incremented whenever pip graph changes.
        /// </remarks>
        private readonly PipGraphSequenceNumber m_pipGraphSequenceNumber;

        /// <summary>
        /// The id of this state.
        /// </summary>
        private readonly GraphAgnosticIncrementalSchedulingStateId m_incrementalSchedulingStateId;

        /// <summary>
        /// Mappings from paths to the <see cref="PipStableId"/>'s of their producers.
        /// </summary>
        /// <remarks>
        /// Paths can be file or directory paths.
        /// </remarks>
        private PipProducers m_pipProducers;

        /// <summary>
        /// Mappings from clean pips to the versions, denoted by <see cref="PipGraphSequenceNumber"/>, at which the pips become clean.
        /// </summary>
        private ConcurrentBigMap<PipStableId, PipGraphSequenceNumber> m_cleanPips;

        /// <summary>
        /// Set of materialized pips.
        /// </summary>
        private ConcurrentBigSet<PipStableId> m_materializedPips;

        /// <summary>
        /// Mappings from clean source files to the versions, denoted by <see cref="PipGraphSequenceNumber"/>, at which the files are considered clean.
        /// </summary>
        /// <remarks>
        /// These mappings can be viewed as mappings from <see cref="HashSourceFile"/> pips to the versions at which they become clean.
        /// </remarks>
        private ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber> m_cleanSourceFiles;

        /// <summary>
        /// Mappings from paths to the <see cref="PipStableId"/>'s of pips that dynamically read them.
        /// </summary>
        private readonly IncrementalSchedulingPathMapping<PipStableId> m_dynamicallyObservedFiles;

        /// <summary>
        /// Mappings from paths to the <see cref="PipStableId"/>'s of pips that dynamically probed them.
        /// </summary>
        private readonly IncrementalSchedulingPathMapping<PipStableId> m_dynamicallyProbedFiles;

        /// <summary>
        /// Mappings from paths to the <see cref="PipStableId"/>'s of pips that dynamically enumerate them.
        /// </summary>
        private readonly IncrementalSchedulingPathMapping<PipStableId> m_dynamicallyObservedEnumerations;

        /// <summary>
        /// Internal path table that is used by <see cref="m_dynamicallyObservedFiles"/>, <see cref="m_dynamicallyProbedFiles"/>, <see cref="m_dynamicallyObservedEnumerations"/>, and <see cref="m_cleanSourceFiles"/>.
        /// </summary>
        private readonly PathTable m_internalPathTable;

        /// <summary>
        /// Pip origins.
        /// </summary>
        private PipOrigins m_pipOrigins;

        /// <summary>
        /// Sequence of graph ids and timestamps upon which this incremental scheduling state has run.
        /// </summary>
        private readonly List<(Guid, DateTime)> m_graphLogs;

        #endregion Graph agnostic state

        #region Current graph state

        /// <summary>
        /// The current pip graph.
        /// </summary>

        /// <summary>
        /// Dirty node tracker for the current pip graph.
        /// </summary>

        /// <summary>
        /// Flag indicating if the current pip graph is different from the previous one.
        /// </summary>
        private readonly bool m_pipGraphChanged;

        /// <summary>
        /// Flag indicating if any of the dynamically observed files have changed.
        /// </summary>
        private bool m_dynamicPathsChanged;

        /// <summary>
        /// Path table belonging to the current graph.
        /// </summary>
        private PathTable PipGraphPathTable => PipGraph.Context.PathTable;

        /// <inheritdoc />
        public DirtyNodeTracker DirtyNodeTracker { get; private set; }

        /// <inheritdoc />
        public DirtyNodeTracker.PendingUpdatedState PendingUpdates => DirtyNodeTracker.PendingUpdates;

        /// <inheritdoc />
        public PipGraph PipGraph { get; private set; }

        #endregion Current graph state

        #region Runtime state

        /// <summary>
        /// Paths that the current pip graph specifies as top-only source sealed directories.
        /// </summary>
        private HashSet<AbsolutePath> m_topOnlyDirectories;

        /// <summary>
        /// Paths that the current pip graph specifies as all-recursive source sealed directories.
        /// </summary>
        private HashSet<AbsolutePath> m_allDirectories;

        /// <summary>
        /// Nodes to be dirtied and non-materialized.
        /// </summary>
        private readonly HashSet<NodeId> m_nodesToDirectDirty = new HashSet<NodeId>();

        /// <summary>
        /// Records of pips that get dirtied due to graph changed or journal scan.
        /// </summary>
        private readonly DirtiedPips m_dirtiedPips;

        /// <summary>
        /// List of node ids for temporary usages.
        /// </summary>
        private readonly List<NodeId> m_tempNodeIds = new List<NodeId>();

        #endregion

        /// <summary>
        /// Id to correlate this incremental scheduling state with the engine state.
        /// </summary>
        /// <remarks>
        /// This id is used to check if the incremental scheduling state coming from the engine state
        /// is still valid in the presence of different build server processes. For example, consider 3 builds in sequence.
        /// The 1st build uses server process X and that process keeps the engine state Ex. The 2nd build, for some reason, spawn
        /// a new server process Y and that process keeps a new engine state Ey. The 3rd build, for some unknown reason, uses
        /// the server process X, which holds the engine state Ex. Thus, the 3rd build, instead of using the incremental scheduling
        /// state built in the 2nd build, uses the incremental scheduling state from the 1st build. Apparently, using pip graph id to
        /// invalidate the incremental scheduling state is not sufficient because the 3rd build can get a graph cache hit and the graph
        /// is identical with the one constructed in the 1st build.
        /// </remarks>
        private readonly Guid m_engineStateId;

        /// <summary>
        /// Atomic save token.
        /// </summary>
        private readonly FileEnvelopeId m_atomicSaveToken;

        /// <summary>
        /// Cleaner that can register directories or files to be cleaned in the background
        /// </summary>
        private readonly ITempCleaner m_tempDirectoryCleaner;

        /// <summary>
        /// If true, don't automatically dirty downstream pips of succeed fast pips.
        /// </summary>
        private readonly bool m_stopDirtyOnSucceedFast;

        /// <summary>
        /// Envelope for serialization
        /// </summary>
        private static readonly FileEnvelope s_fileEnvelope = new FileEnvelope(name: nameof(GraphAgnosticIncrementalSchedulingState), version: (int) PipFingerprintingVersion.TwoPhaseV2 + 4);

        /// <summary>
        /// Log messages like '>>> Build file changed: X.cpp' are very useful to know what's going on internally, but only if there's not too many of them.
        /// We cap the number of messages per type of change, and log an indication of truncation if needed.
        /// </summary>
        private const int MaxMessagesPerChangeType = 50;

        /// <summary>
        /// Incremental scheduling counters.
        /// </summary>
        private readonly IncrementalSchedulingStateCounter m_stats = new IncrementalSchedulingStateCounter();

        /// <summary>
        /// Logging context.
        /// </summary>
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Index to graph logs.
        /// </summary>
        private readonly int m_indexToGraphLogs;

        /// <summary>
        /// Stopwatch for journal processing.
        /// </summary>
        private readonly Stopwatch m_journalProcessingStopwatch = new Stopwatch();

        /// <summary>
        /// Creates an instance of <see cref="GraphAgnosticIncrementalSchedulingState"/>.
        /// </summary>
        private GraphAgnosticIncrementalSchedulingState(
            LoggingContext loggingContext,
            FileEnvelopeId atomicSaveToken,
            PipGraph pipGraph,
            PathTable internalPathTable,
            GraphAgnosticIncrementalSchedulingStateId incrementalSchedulingStateId,
            DirtyNodeTracker dirtyNodeTracker,
            PipProducers pipProducers,
            ConcurrentBigMap<PipStableId, PipGraphSequenceNumber> cleanPips,
            ConcurrentBigSet<PipStableId> materializedPips,
            ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber> cleanSourceFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyProbedFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedEnumerations,
            PipOrigins pipOrigins,
            List<(Guid, DateTime)> graphLogs,
            DirtiedPips dirtiedPips,
            bool pipGraphChanged,
            PipGraphSequenceNumber pipGraphSequenceNumber,
            int indexToGraphLogs,
            Guid engineStateId,
            ITempCleaner tempDirectoryCleaner,
            bool stopDirtyOnSucceedFast)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(atomicSaveToken.IsValid);
            Contract.Requires(pipGraph != null);
            Contract.Requires(internalPathTable != null);
            Contract.Requires(incrementalSchedulingStateId != null);
            Contract.Requires(dirtyNodeTracker != null);
            Contract.Requires(pipProducers != null);
            Contract.Requires(cleanPips != null);
            Contract.Requires(materializedPips != null);
            Contract.Requires(cleanSourceFiles != null);
            Contract.Requires(dynamicallyObservedFiles != null);
            Contract.Requires(dynamicallyProbedFiles != null);
            Contract.Requires(dynamicallyObservedEnumerations != null);
            Contract.Requires(pipOrigins != null);
            Contract.Requires(graphLogs != null);
            Contract.Requires(dirtiedPips != null);

            m_loggingContext = loggingContext;
            m_engineStateId = engineStateId;
            m_atomicSaveToken = atomicSaveToken;
            PipGraph = pipGraph;
            m_internalPathTable = internalPathTable;
            m_incrementalSchedulingStateId = incrementalSchedulingStateId;
            DirtyNodeTracker = dirtyNodeTracker;
            m_pipProducers = pipProducers;
            m_cleanPips = cleanPips;
            m_materializedPips = materializedPips;
            m_cleanSourceFiles = cleanSourceFiles;
            m_dynamicallyObservedFiles = dynamicallyObservedFiles;
            m_dynamicallyProbedFiles = dynamicallyProbedFiles;
            m_dynamicallyObservedEnumerations = dynamicallyObservedEnumerations;
            m_pipOrigins = pipOrigins;
            m_graphLogs = graphLogs;
            m_dirtiedPips = dirtiedPips;
            m_pipGraphChanged = pipGraphChanged;
            m_pipGraphSequenceNumber = pipGraphSequenceNumber;
            m_indexToGraphLogs = indexToGraphLogs;
            m_tempDirectoryCleaner = tempDirectoryCleaner;
            m_stopDirtyOnSucceedFast = stopDirtyOnSucceedFast;
        }

        /// <summary>
        /// Creates a new instance of <see cref="GraphAgnosticIncrementalSchedulingState"/>.
        /// </summary>
        public static GraphAgnosticIncrementalSchedulingState CreateNew(
            LoggingContext loggingContext,
            FileEnvelopeId atomicSaveToken,
            PipGraph pipGraph,
            IConfiguration configuration,
            PreserveOutputsInfo preserveOutputSalt,
            ITempCleaner tempDirectoryCleaner = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(atomicSaveToken.IsValid);
            Contract.Requires(pipGraph != null);
            Contract.Requires(configuration != null);

            GraphAgnosticIncrementalSchedulingStateId incrementalSchedulingStateId = 
                GraphAgnosticIncrementalSchedulingStateId.Create(pipGraph.Context.PathTable, configuration, preserveOutputSalt);
            DirtyNodeTracker initialDirtyNodeTracker = CreateInitialDirtyNodeTracker(pipGraph, false, configuration.Schedule.StopDirtyOnSucceedFastPips);

            var internalPathTable = new PathTable();
            var pipProducers = PipProducers.CreateNew();
            var pipOrigins = PipOrigins.CreateNew();
            var cleanSourceFiles = new ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber>();
            var graphLogs = new List<(Guid, DateTime)>() { (pipGraph.GraphId, DateTime.UtcNow) };
            int indexToGraphLogs = 0;
            PipGraphSequenceNumber pipGraphSequenceNumber = PipGraphSequenceNumber.Zero;

            InitializeGraphAgnosticState(pipGraph, internalPathTable, cleanSourceFiles, pipGraphSequenceNumber);

            return new GraphAgnosticIncrementalSchedulingState(
                loggingContext,
                atomicSaveToken,
                pipGraph,
                internalPathTable,
                incrementalSchedulingStateId,
                initialDirtyNodeTracker,
                pipProducers,
                new ConcurrentBigMap<PipStableId, PipGraphSequenceNumber>(),
                new ConcurrentBigSet<PipStableId>(),
                cleanSourceFiles,
                new IncrementalSchedulingPathMapping<PipStableId>(),
                new IncrementalSchedulingPathMapping<PipStableId>(),
                new IncrementalSchedulingPathMapping<PipStableId>(),
                pipOrigins,
                graphLogs,
                new DirtiedPips(),
                false,
                pipGraphSequenceNumber,
                indexToGraphLogs,
                Guid.NewGuid(),
                tempDirectoryCleaner,
                configuration.Schedule.StopDirtyOnSucceedFastPips);
        }

        /// <summary>
        /// Gets initial dirty node tracker based on the given <see cref="PipGraph"/>.
        /// </summary>
        private static DirtyNodeTracker CreateInitialDirtyNodeTracker(PipGraph pipGraph, bool dirtyNodeChanged, bool stopOnSucceedFastPips)
        {
            Contract.Requires(pipGraph != null);

            RangedNodeSet dirtyNodes = CreateEmptyNodeSet(pipGraph);
            RangedNodeSet perpetualDirtyNodes = CreateEmptyNodeSet(pipGraph);
            RangedNodeSet materializedNodes = CreateEmptyNodeSet(pipGraph);

            // 1. All nodes are dirty, except hash source file nodes.
            //    We marked all nodes, except hash-source-file nodes, dirty to enforce the invariant that all
            //    dependents of a dirty node must be dirty. If a source file is inside a seal directory, but is
            //    not read by any pip that consumes the seal directory, then, if you initially mark the source file
            //    dirty, then it stays dirty in the next builds. However, because the pips consuming the seal directory
            //    (as well as the seal directory itself) have been executed, then they all are marked clean. Thus,
            //    the invariant is violated.
            //    Of course, filters can filter out those unused hash-source-file nodes, but filters are opt-in. However,
            //    the main reason here is to enforce the invariant.
            // 2. All nodes are not materialized, except hash source file nodes.
            EnsureSourceFilesCleanAndMaterialized(pipGraph, dirtyNodes, materializedNodes);

            // TODO: The constructor of DirtyNodeTracker looks unnatural, please fix.
            return new DirtyNodeTracker(
                pipGraph.DirectedGraph,
                dirtyNodes,
                perpetualDirtyNodes,
                dirtyNodeChanged,
                materializedNodes,
                CreateDirtyNodeTrackerTraversal(pipGraph, stopOnSucceedFastPips));
        }

        /// <summary>
        /// Ensures that all hash source file nodes are clean and materialized.
        /// </summary>
        private static void EnsureSourceFilesCleanAndMaterialized(
            PipGraph pipGraph, 
            RangedNodeSet dirtyNodes, 
            RangedNodeSet materializedNodes)
        {
            Contract.Requires(pipGraph != null);
            Contract.Requires(dirtyNodes != null);
            Contract.Requires(materializedNodes != null);

            dirtyNodes.Fill();

            foreach (var node in pipGraph.DirectedGraph.Nodes)
            {
                var pipId = node.ToPipId();
                Contract.Requires(pipId.IsValid, "Invalid node detected in the Directed PipGraph. Please backup your log folders and report this bug with the log folders to domdev@microsoft.com. As a temporary fix please delete the engine cache and try again.");
                if (pipGraph.PipTable.GetPipType(pipId) == PipType.HashSourceFile)
                {
                    dirtyNodes.Remove(node);
                    materializedNodes.Add(node);
                }
            }
        }

        /// <summary>
        /// Initializes agnostic states.
        /// </summary>
        private static void InitializeGraphAgnosticState(
            PipGraph pipGraph,
            PathTable internalPathTable,
            ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber> cleanSourceFiles,
            PipGraphSequenceNumber pipGraphSequenceNumber)
        {
            Contract.Requires(pipGraph != null);
            Contract.Requires(internalPathTable != null);
            Contract.Requires(cleanSourceFiles != null);

            // Initially, all source files are considered clean. Or, in other words, all hash-source-file pips
            // are initially clean and materialized.
            foreach (var fileAndProducingPip in pipGraph.AllFilesAndProducers.Where(kvp => kvp.Key.IsSourceFile))
            {
                cleanSourceFiles.TryAdd(MapPath(pipGraph, fileAndProducingPip.Key.Path, internalPathTable), pipGraphSequenceNumber);
            }
        }

        /// <summary>
        /// Creates an empty node set for <see cref="DirtyNodeTracker"/>.
        /// </summary>
        private static RangedNodeSet CreateEmptyNodeSet(PipGraph graph)
        {
            var set = new RangedNodeSet();
            set.ClearAndSetRange(graph.NodeRange);
            return set;
        }

        private void DirtyState()
        {
            DirtyGraphSpecificState();
            DirtyGraphAgnosticState();
        }

        private void DirtyGraphSpecificState() => DirtyNodeTracker = CreateInitialDirtyNodeTracker(PipGraph, true, m_stopDirtyOnSucceedFast);

        private void DirtyGraphAgnosticState()
        {
            m_cleanPips = new ConcurrentBigMap<PipStableId, PipGraphSequenceNumber>();
            m_materializedPips = new ConcurrentBigSet<PipStableId>();
            m_cleanSourceFiles = new ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber>();
            m_pipProducers = PipProducers.CreateNew();
            m_pipOrigins = PipOrigins.CreateNew();

            InitializeGraphAgnosticState(PipGraph, m_internalPathTable, m_cleanSourceFiles, m_pipGraphSequenceNumber);
        }

        #region Journal scanning

        private bool ShouldSkipProcessingNewlyPresentPath(string changedPathStr, PathChanges changeReasons)
        {
            // The pip graph and scheduling state do not indicate which nodes may be impacted
            // by creations of new files / directories (invalidations of anti-dependencies).
            // As an approximation, we utilize the property that the 'NewlyPresent'
            // reason can only be generated when some pip execution caused a path to have its absence tracked (this isn't automatic for all paths),
            // and dirty all nodes if *any* paths exhibit this change reason.
            // TODO: Add telemetry to determine how often this happens; consider adding state to map these changes to individual graph nodes.

            // This method aligns the handling of anti-dependencies in the incremental scheduling with the one in the observed input processor,
            // while maintaining the invariant that if the observed input processor determines that the pip needs to run, then
            // the journal scanning should make the pip dirty.

            // ****************** CAUTION *********************
            // The logic below is designed to replicate the logic in ObservedInputProcessingEnvironmentAdapter.TryProbeAndTrackExistence.
            // Any change there should be implemented conservatively here.
            // ************************************************

            // TODO: Not sure if creating an absolute path is right.
            var path = AbsolutePath.Create(PipGraphPathTable, changedPathStr);
            var mountInfo = PipGraph.SemanticPathExpander.GetSemanticPathInfo(path);

            if (mountInfo.IsValid && !mountInfo.AllowHashing)
            {
                // We know nothing about this path. To be safe, let the processing continue.
                return false;
            }
            else
            {
                if (changeReasons == PathChanges.NewlyPresentAsDirectory)
                {
                    if (mountInfo.IsWritable)
                    {
                        ComputeSealedSourceDirectories();

                        if (!IsReadOnlyDirectory(path))
                        {
                            // The treatment of path existence here is a bit different from the one in ObservedInputProcessor.
                            // In ObservedInputprocessor, the newly present directory is considered existent, but here,
                            // we consider it non-existent. This is safe. Consider the following case: In a run, a pip P probes
                            // a non-existent and non-read-only directory D in a writable mount, and another pip Q,
                            // that executes after P, outputs D.

                            // Case 1:
                            // If D is in the graph file-system, then P considers D existent. In the next run (no changes), without
                            // incremental scheduling, D is now existent due to Q, and P still considers D existent, and thus,
                            // P doesn’t re-run. If incremental scheduling is enabled, the incremental state will consider
                            // D non-existent and thus, P is skipped.

                            // Case 2:
                            // If D is not in the graph file-system, then P considers D non-existent. In the next run (no changes),
                            // without incremental scheduling, D is still considered non-existent by P, and thus P doesn’t re-run.
                            // If incremental scheduling is enabled, the incremental state will consider D non-existent and thus,
                            // P is skipped.

                            // Skip the processing of newly present path because based on the behavior of ObservedInputProcessor,
                            // this path can safely be considered non-existent. Note that we are not throwing away the incremental scheduling.
                            return true;
                        }
                    }
                }
            }

            // At this point we should skip processing newly present path because currently incremental scheduling
            // does not handle anti-dependencies. To this end, we need to increment NewDirectoriesCount or NewFilesCount.
            // These counters are then used later to throw away incremental scheduling.
            if (changeReasons == PathChanges.NewlyPresentAsDirectory)
            {
                ++m_stats.NewDirectoriesCount;
                if (m_stats.NewDirectoriesCount <= MaxMessagesPerChangeType)
                {
                    m_stats.Samples.AddNewDirectory(changedPathStr);
                    Tracing.Logger.Log.IncrementalSchedulingNewlyPresentDirectory(m_loggingContext, changedPathStr);
                }
            }
            else
            {
                ++m_stats.NewFilesCount;
                if (m_stats.NewFilesCount <= MaxMessagesPerChangeType)
                {
                    m_stats.Samples.AddNewFile(changedPathStr);
                    Tracing.Logger.Log.IncrementalSchedulingNewlyPresentFile(m_loggingContext, changedPathStr);
                }
            }

            return true;
        }

        private void ComputeSealedSourceDirectories()
        {
            if (m_topOnlyDirectories != null && m_allDirectories != null)
            {
                return;
            }

            m_topOnlyDirectories = new HashSet<AbsolutePath>();
            m_allDirectories = new HashSet<AbsolutePath>();

            foreach (var directoryArtifact in PipGraph.AllSealDirectories)
            {
                var node = PipGraph.GetSealedDirectoryNode(directoryArtifact);
                var kind = PipGraph.PipTable.GetSealDirectoryKind(node.ToPipId());
                var set = kind == SealDirectoryKind.SourceTopDirectoryOnly
                    ? m_topOnlyDirectories
                    : (kind == SealDirectoryKind.SourceAllDirectories ? m_allDirectories : null);

                set?.Add(directoryArtifact.Path);
            }
        }

        private bool IsReadOnlyDirectory(AbsolutePath path)
        {
            Contract.Requires(m_topOnlyDirectories != null && m_allDirectories != null);

            if (m_topOnlyDirectories.Count > 0 && m_topOnlyDirectories.Contains(path))
            {
                return true;
            }

            if (m_allDirectories.Count > 0)
            {
                for (var current = path; current.IsValid; current = current.GetParent(PipGraphPathTable))
                {
                    if (m_allDirectories.Contains(current))
                    {
                        if (current != path)
                        {
                            m_allDirectories.Add(path);
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        private void ProcessChangedPath(string changedPathStr, PathChanges changeReasons)
        {
            NodeId maybeImpactedProducer = PipGraph.TryGetOriginalProducerForPath(changedPathStr);

            if (maybeImpactedProducer.IsValid)
            {
                // A path has changed, and the change affects the producer of that path. This producer can be a hash-source-file pip.
                ++m_stats.ChangedFilesCount;
                m_stats.Samples.AddChangedPath(changedPathStr, changeReasons);

                AddNodeToDirectDirty(maybeImpactedProducer, changedPathStr, changeReasons, ChangedPathKind.StaticArtifact, m_stats.ChangedFilesCount);

                if (PipGraph.PipTable.GetPipType(maybeImpactedProducer.ToPipId()) == PipType.HashSourceFile)
                {
                    // If the source file is also a member of sealed source directory S, and the file is observed
                    // when a pip P executes by consuming S, then P needs to be made dirty.
                    ProcessDynamicallyObservedChangedPath(changedPathStr, changeReasons);
                }
            }
            else
            {
                // Changed path can affect pips that observed it dynamically during execution. 
                ProcessDynamicallyObservedChangedPath(changedPathStr, changeReasons);
            }

            // Changed path can affect clean pips of other pip graphs that this incremental scheduling
            // state has seen and tracked.
            ProcessGraphAgnosticStateChangedPath(changedPathStr, maybeImpactedProducer, changeReasons);
        }

        private void ProcessDynamicallyObservedChangedPath(string changedPathStr, PathChanges changeReasons)
        {
            if (!AbsolutePath.TryGet(m_internalPathTable, changedPathStr, out AbsolutePath path))
            {
                return;
            }

            // Determine the set to use based on the change (Enumeration or Files)
            if ((changeReasons & PathChanges.MembershipChanged) != 0)
            {
                ProcessDynamicallyObservedChangedPath(
                    changedPathStr, 
                    changeReasons, 
                    path, 
                    DynamicObservationType.Enumeration, 
                    m_dynamicallyObservedEnumerations);
            }
            else
            {
                if ((changeReasons & PathChanges.Removed) != 0)
                {
                    ProcessDynamicallyObservedChangedPath(
                        changedPathStr, 
                        changeReasons, 
                        path, 
                        DynamicObservationType.ProbedFile, 
                        m_dynamicallyProbedFiles);
                }

                ProcessDynamicallyObservedChangedPath(
                    changedPathStr, 
                    changeReasons, 
                    path, 
                    DynamicObservationType.ObservedFile, 
                    m_dynamicallyObservedFiles);
            }
        }

        private void ProcessDynamicallyObservedChangedPath(
            string changedPathStr, 
            PathChanges changeReasons, 
            AbsolutePath path, 
            DynamicObservationType observationType, 
            IncrementalSchedulingPathMapping<PipStableId> dynamicObservationsToInvalidate)
        {
            if (dynamicObservationsToInvalidate.TryGetValues(path, out IEnumerable<PipStableId> pipStableIds))
            {
                if (observationType == DynamicObservationType.ProbedFile &&
                    FileUtilities.FileExistsNoFollow(changedPathStr))
                {
                    // When a probed file is renamed to a new name and renamed back to the original name, it considered as a removal of the probed file. 
                    // We will not treat this case as a changed path
                    return;
                }

                m_tempNodeIds.Clear();

                foreach (var pipStableId in pipStableIds)
                {
                    m_dirtiedPips.PipsGetDirtiedDueToDynamicObservationAfterScan.Add((pipStableId, observationType));

                    if (TryGetGraphPipId(pipStableId, out PipId pipId))
                    {
                        m_tempNodeIds.Add(pipId.ToNodeId());
                    }
                }

                if (m_tempNodeIds.Count > 0)
                {
                    long usedChangeTypeCount = 0;
                    ChangedPathKind pathChangeKind = ChangedPathKind.DynamicallyObservedPath;

                    switch (observationType)
                    {
                        case DynamicObservationType.Enumeration:
                            usedChangeTypeCount = ++m_stats.ChangedDynamicallyObservedEnumerationMembershipsCount;
                            pathChangeKind = ChangedPathKind.DynamicallyObservedPathEnumeration;
                            break;

                        case DynamicObservationType.ObservedFile:
                            usedChangeTypeCount = ++m_stats.ChangedDynamicallyObservedFilesCount;
                            pathChangeKind = ChangedPathKind.DynamicallyObservedPath;
                            break;

                        case DynamicObservationType.ProbedFile:
                            usedChangeTypeCount = ++m_stats.ChangedDynamicallyProbedFilesCount;
                            pathChangeKind = ChangedPathKind.DynamicallyProbedPath;
                            break;
                    }

                    m_stats.Samples.AddChangedDynamicallyObservedArtifact(changedPathStr, changeReasons, observationType);

                    foreach (var node in m_tempNodeIds)
                    {
                        AddNodeToDirectDirty(node, changedPathStr, changeReasons, pathChangeKind, usedChangeTypeCount);
                    }
                }
            }
        }

        private void ProcessGraphAgnosticStateChangedPath(string changedPathStr, NodeId node, PathChanges changeReasons)
        {
            if (!AbsolutePath.TryGet(m_internalPathTable, changedPathStr, out AbsolutePath path))
            {
                return;
            }

            // If path is a source file, then mark it dirty by removing it from the clean state.
            if (m_cleanSourceFiles.TryRemove(path, out PipGraphSequenceNumber dummyValue))
            {
                if (!node.IsValid)
                {
                    LogChangeOtherGraphDuringScan(
                        m_loggingContext,
                        context =>
                        {
                            Tracing.Logger.Log.IncrementalSchedulingSourceFileOfOtherGraphIsDirtyDuringScan(
                                context,
                                changedPathStr,
                                changeReasons.ToString());
                        });

                    m_dirtiedPips.SourceFilesOfOtherPipGraphsGetDirtiedAfterScan.Add(path);
                }
            }

            // If a path is produced by a clean producer, then mark it dirty.
            if (m_pipProducers.TryGetProducer(path, out PipStableId pipStableId))
            {
                if (!node.IsValid && m_pipOrigins.TryGetFingerprint(pipStableId, out ContentFingerprint pipFingerprint))
                {
                    LogChangeOtherGraphDuringScan(
                        m_loggingContext,
                        context =>
                        {
                            Tracing.Logger.Log.IncrementalSchedulingPipOfOtherGraphIsDirtyDuringScan(
                                context,
                                pipFingerprint.ToString(),
                                changedPathStr,
                                changeReasons.ToString());
                        });

                    // Because dirtying graph-agnostic pip is considered expensive, and we don't want to
                    // slow down journal scan, we simply note about this change.
                    m_dirtiedPips.PipsOfOtherPipGraphsGetDirtiedAfterScan.Add(pipStableId);
                }

                // Note that we don't remove the dirty pip here. Dirtying pip in the graph-agnostic state
                // can be expensive and we don't want to slow down journal scan. If the pip belongs to the current graph, 
                // then the pip will be remembered by marking its node dirty, and this is done by ProcessChangedPath.
                // For a pip belonging to a different graph, we just remember the pip using m_dirtiedPips. 
                // For both cases, the pip will be marked dirty after we are done with journal scan.
            }
        }

        private void AddNodeToDirectDirty(
            NodeId maybeImpactedNode,
            string changedPath,
            PathChanges pathChangeReasons,
            ChangedPathKind pathChangeKind,
            long changeTypeCount)
        {
            if (m_nodesToDirectDirty.Add(maybeImpactedNode))
            {
                // Hash source file pips never make it into the scheduler,
                // so the immediate consumers of them must be marked as non-materialized as well to ensure they are not skipped.
                if (PipGraph.PipTable.GetPipType(maybeImpactedNode.ToPipId()) == PipType.HashSourceFile)
                {
                    foreach (var edge in PipGraph.DirectedGraph.GetOutgoingEdges(maybeImpactedNode))
                    {
                        m_nodesToDirectDirty.Add(edge.OtherNode);
                    }
                }

                if (changeTypeCount <= MaxMessagesPerChangeType)
                {
                    var dirtyPipType = PipGraph.PipTable.GetPipType(maybeImpactedNode.ToPipId());
                    var dirtyPipSemiStableHash = PipGraph.PipTable.GetPipSemiStableHash(maybeImpactedNode.ToPipId());
                    string reason = null;

                    switch (pathChangeKind)
                    {
                        case ChangedPathKind.StaticArtifact:
                            reason = I($"File/Directory '{changedPath}' changed");
                            break;
                        case ChangedPathKind.DynamicallyObservedPath:
                            reason = I($"Dynamically observed read file '{changedPath}' changed");
                            break;
                        case ChangedPathKind.DynamicallyProbedPath:
                            reason = I($"Dynamically observed probed file '{changedPath}' changed");
                            break;
                        case ChangedPathKind.DynamicallyObservedPathEnumeration:
                            reason = I($"Dynamically observed directory '{changedPath}' had membership change");
                            break;
                        default:
                            Contract.Assert(false);
                            break;
                    }

                    if (dirtyPipType == PipType.HashSourceFile)
                    {
                        Tracing.Logger.Log.IncrementalSchedulingSourceFileIsDirty(m_loggingContext, reason, pathChangeReasons.ToString());
                    }
                    else
                    {
                        Tracing.Logger.Log.IncrementalSchedulingPipIsDirty(
                            m_loggingContext,
                            dirtyPipSemiStableHash,
                            reason,
                            pathChangeReasons.ToString());
                    }
                }
            }
        }

        #endregion Journal scanning

        #region Runtime execution

        /// <summary>
        /// Records the dynamic observations
        /// </summary>
        public void RecordDynamicObservations(
            NodeId nodeId,
            IEnumerable<string> dynamicallyObservedFilePaths,
            IEnumerable<string> dynamicallyProbedFilePaths,
            IEnumerable<string> dynamicallyObservedEnumerationPaths,
            IEnumerable<string> dynamicallyObservedAbsentPathProbes,
            IEnumerable<(string directory, IEnumerable<string> fileArtifactsCollection)> outputDirectoriesContents,
            IEnumerable<string> untrackedScopes)
        {
            if (!PipGraph.TryGetPipFingerprint(nodeId.ToPipId(), out ContentFingerprint fingerprint))
            {
                return;
            }

            PipStableId pipStableId = m_pipOrigins.AddOrUpdate(
                fingerprint, 
                (PipGraph.PipTable.GetPipSemiStableHash(nodeId.ToPipId()), m_indexToGraphLogs));

            m_dynamicallyObservedFiles.ClearValue(pipStableId);
            m_dynamicallyProbedFiles.ClearValue(pipStableId);
            m_dynamicallyObservedEnumerations.ClearValue(pipStableId);

            foreach (var dynamicallyObservedFilePath in dynamicallyObservedFilePaths)
            {
                var dynamicallyObservedFile = AbsolutePath.Create(m_internalPathTable, dynamicallyObservedFilePath);
                m_dynamicallyObservedFiles.AddEntry(pipStableId, dynamicallyObservedFile);
            }

            foreach (var dynamicallyProbedFilePath in dynamicallyProbedFilePaths)
            {
                var dynamicallyProbedFile = AbsolutePath.Create(m_internalPathTable, dynamicallyProbedFilePath);
                m_dynamicallyProbedFiles.AddEntry(pipStableId, dynamicallyProbedFile);
            }

            foreach (var dynamicallyObservedEnumerationPath in dynamicallyObservedEnumerationPaths)
            {
                var dynamicallyObservedEnumeration = AbsolutePath.Create(m_internalPathTable, dynamicallyObservedEnumerationPath);
                m_dynamicallyObservedEnumerations.AddEntry(pipStableId, dynamicallyObservedEnumeration);
            }

            // TODO: Process dynamically observed absent path probes 

            foreach (var dynamicDirectoryContent in outputDirectoriesContents)
            {
                var directoryPath = AbsolutePath.Create(m_internalPathTable, dynamicDirectoryContent.directory);

                using (var untrackedScopesPool = Pools.GetAbsolutePathSet())
                using (var pools = Pools.GetAbsolutePathSet())
                {
                    var pathSet = pools.Instance;
                    var untrackedScopeSet = untrackedScopesPool.Instance;
                    untrackedScopeSet.UnionWith(
                        untrackedScopes
                            .Select(p => AbsolutePath.Create(m_internalPathTable, p))
                            .Where(p => p.IsWithin(m_internalPathTable, directoryPath)));

                    foreach (var member in dynamicDirectoryContent.fileArtifactsCollection)
                    {
                        var dynamicDirectoryContentMember = AbsolutePath.Create(m_internalPathTable, member);
                        for (
                            var currentPath = dynamicDirectoryContentMember;
                            currentPath.IsValid && currentPath != directoryPath && !pathSet.Contains(currentPath);
                            currentPath = currentPath.GetParent(m_internalPathTable))
                        {
                            if (currentPath == dynamicDirectoryContentMember)
                            {
                                m_dynamicallyObservedFiles.AddEntry(pipStableId, currentPath);
                            }
                            else if (untrackedScopeSet.Count == 0 // Typical case.
                                || (!untrackedScopeSet.Contains(currentPath)
                                    && !untrackedScopeSet.Any(u => currentPath.IsWithin(m_internalPathTable, u))))
                            {
                                m_dynamicallyObservedEnumerations.AddEntry(pipStableId, currentPath);
                            }

                            pathSet.Add(currentPath);
                        }
                    }
                }

                m_dynamicallyObservedEnumerations.AddEntry(pipStableId, directoryPath);
            }

            // This assignment is safe because m_dynamicPathChanged is only assigned to true, and its value is only read
            // at the end of build when this incremental scheduling states is about to be saved.
            m_dynamicPathsChanged = true;
        }

        #endregion Runtime execution

        #region Observer

        /// <inheritdoc />
        public void OnNext(ChangedPathInfo value)
        {
            var changeReasons = value.PathChanges;
            var changedPathStr = value.Path;

            if (value.PathChanges.ContainsNewlyPresent())
            {
                if (ShouldSkipProcessingNewlyPresentPath(changedPathStr, changeReasons))
                {
                    return;
                }
            }

            ProcessChangedPath(changedPathStr, changeReasons);
        }

        /// <inheritdoc />
        public void OnNext(ChangedFileIdInfo value)
        {
        }

        /// <inheritdoc />
        void IObserver<ChangedFileIdInfo>.OnError(Exception error)
        {
        }

        /// <inheritdoc />
        void IObserver<ChangedFileIdInfo>.OnCompleted()
        {
        }

        /// <inheritdoc />
        void IObserver<ChangedPathInfo>.OnError(Exception error)
        {
        }

        /// <inheritdoc />
        void IObserver<ChangedPathInfo>.OnCompleted()
        {
        }

        /// <inheritdoc />
        public void OnInit() => m_journalProcessingStopwatch.Restart();

        /// <inheritdoc />
        public void OnCompleted(ScanningJournalResult scanningJournalResult)
        {
            PreciseChangeReason preciseChangeReason = PreciseChangeReason.PreciseChange;
            string preciseChangeDescription = string.Empty;
            bool preciseDirtyNodes;

            m_stats.Log(m_loggingContext);

            if (!scanningJournalResult.Succeeded)
            {
                // Assume everything dirty, since we don't have a complete view of what changed since last time.
                Tracing.Logger.Log.IncrementalSchedulingAssumeAllPipsDirtyDueToFailedJournalScan(
                    m_loggingContext,
                    scanningJournalResult.Status.ToString());

                DirtyState();
                preciseDirtyNodes = false;
                preciseChangeReason = PreciseChangeReason.FailedJournalScanning;
                preciseChangeDescription = scanningJournalResult.Status.ToString();
            }
            else if (m_stats.NewArtifactsCount > 0)
            {
                // Assume everything dirty, since we don't know which nodes care about the anti-dependency.
                Tracing.Logger.Log.IncrementalSchedulingAssumeAllPipsDirtyDueToAntiDependency(m_loggingContext, m_stats.NewFilesCount);

                DirtyState();
                preciseDirtyNodes = false;
                preciseChangeReason = PreciseChangeReason.NewFilesOrDirectoriesAdded;
                preciseChangeDescription = "New files or directories were added";
            }
            else
            {
                var dirtyingNodesTransitivelyStopwatch = new StopwatchVar();

                // All changes processed; new nodes possibly dirtied.
                // Given any dirtied node, ensure that all transitive dependents are also dirty.
                // Note that we stop traversal whenever a dirty node is encountered (assuming this property already holds for them).
                if (m_nodesToDirectDirty.Count > 0)
                {
                    using (dirtyingNodesTransitivelyStopwatch.Start())
                    {
                        var transitivelyDirtiedNodes = new HashSet<NodeId>();
                        DirtyNodeTracker.MarkNodesDirectDirty(
                            m_nodesToDirectDirty,
                            node => 
                            {
                                ++m_stats.NodesTransitivelyDirtiedCount;
                                transitivelyDirtiedNodes.Add(node);
                            });

                        Parallel.ForEach(
                            m_nodesToDirectDirty,
                            node =>
                            {
                                PipId pipId = node.ToPipId();
                                if (TryGetPipStableId(pipId, out PipStableId pipStableId))
                                {
                                    DirectDirtyGraphAgnosticPip(pipStableId);
                                }
                            });

                        // Dirty graph-agnostic state corresponding to pips belonging the current pip graph.
                        Parallel.ForEach(
                            transitivelyDirtiedNodes.ToList(),
                            node =>
                            {
                                PipId pipId = node.ToPipId();

                                if (TryGetPipStableId(pipId, out PipStableId pipStableId))
                                {
                                    DirtyGraphAgnosticPip(pipStableId);
                                }
                                else if (PipGraph.PipTable.GetPipType(pipId) == PipType.HashSourceFile)
                                {
                                    var hashSourceFilePip = PipGraph.GetPipFromPipId(pipId) as HashSourceFile;
                                    m_cleanSourceFiles.TryRemove(InternGraphPath(hashSourceFilePip.Artifact.Path), out PipGraphSequenceNumber buildSequenceNumber);
                                }
                            });
                    }
                }

                preciseDirtyNodes = true;
                preciseChangeReason = PreciseChangeReason.PreciseChange;

                Tracing.Logger.Log.IncrementalSchedulingDirtyPipChanges(
                    m_loggingContext,
                    DirtyNodeTracker.HasChanged,
                    m_nodesToDirectDirty.Count,
                    m_stats.NodesTransitivelyDirtiedCount,
                    (long)dirtyingNodesTransitivelyStopwatch.TotalElapsed.TotalMilliseconds);

                // Dirty graph-agnostic state correponding to pips belonging to other pip graphs.
                if (m_dirtiedPips.PipsOfOtherPipGraphsGetDirtiedAfterScan.Count > 0)
                {
                    var dirtyingPipsOfOtherPipGraphStopwatch = new StopwatchVar();

                    using (dirtyingPipsOfOtherPipGraphStopwatch.Start())
                    {
                        Parallel.ForEach(
                            m_dirtiedPips.PipsOfOtherPipGraphsGetDirtiedAfterScan,
                            p => DirectDirtyGraphAgnosticPip(p));
                    }

                    Tracing.Logger.Log.IncrementalSchedulingPipsOfOtherPipGraphsGetDirtiedAfterScan(
                        m_loggingContext,
                        m_dirtiedPips.PipsOfOtherPipGraphsGetDirtiedAfterScan.Count,
                        (long)dirtyingPipsOfOtherPipGraphStopwatch.TotalElapsed.TotalMilliseconds);
                }

                // Removes from dynamic observation mappings pips that become dirty.
                // This removal doesn't affect correctness, but prevent the mappings
                // from growing unboundedly. Note that, the mapping can contain as well
                // pips from different pip graphs.
                if (m_dirtiedPips.PipsGetDirtiedDueToDynamicObservationAfterScan.Count > 0)
                {
                    var dirtyingPipsDueToDynamicObservationStopwatch = new StopwatchVar();

                    using (dirtyingPipsDueToDynamicObservationStopwatch.Start())
                    {
                        Parallel.ForEach(
                            m_dirtiedPips.PipsGetDirtiedDueToDynamicObservationAfterScan,
                            o =>
                            {
                                switch (o.Item2)
                                {
                                    case DynamicObservationType.ObservedFile:
                                        m_dynamicallyObservedFiles.ClearValue(o.Item1);
                                        break;
                                    case DynamicObservationType.ProbedFile:
                                        m_dynamicallyProbedFiles.ClearValue(o.Item1);
                                        break;
                                    case DynamicObservationType.Enumeration:
                                        m_dynamicallyObservedEnumerations.ClearValue(o.Item1);
                                        break;
                                }

                                DirectDirtyGraphAgnosticPip(o.Item1);
                            });
                    }

                    Tracing.Logger.Log.IncrementalSchedulingPipDirtyDueToChangesInDynamicObservationAfterScan(
                        m_loggingContext,
                        m_dirtiedPips.PipsGetDirtiedDueToDynamicObservationAfterScan.Count(e => e.Item2 == DynamicObservationType.ObservedFile),
                        m_dirtiedPips.PipsGetDirtiedDueToDynamicObservationAfterScan.Count(e => e.Item2 == DynamicObservationType.ProbedFile),
                        m_dirtiedPips.PipsGetDirtiedDueToDynamicObservationAfterScan.Count(e => e.Item2 == DynamicObservationType.Enumeration),
                        (long)dirtyingPipsDueToDynamicObservationStopwatch.TotalElapsed.TotalMilliseconds);
                }
            }

            StateStats.LogStats(this, Tracing.Logger.Log.IncrementalSchedulingStateStatsAfterScan);

            Tracing.Logger.Log.IncrementalSchedulingPreciseChange(
                m_loggingContext,
                preciseDirtyNodes,
                DirtyNodeTracker.HasChanged,
                preciseChangeReason.ToString(),
                preciseChangeDescription,
                m_journalProcessingStopwatch.ElapsedMilliseconds);
        }

        #endregion Observer

        #region Save state

        /// <summary>
        /// Saves changed incremental scheduling state.
        /// </summary>
        /// <exception cref="BuildXLException"> is thrown if there is an I/O error in saving state (such as if a requisite directory doesn't exist)</exception>
        public bool SaveIfChanged(FileEnvelopeId atomicSaveToken, string incrementalSchedulingStatePath)
        {
            Contract.Requires(atomicSaveToken.IsValid);
            Contract.Requires(!string.IsNullOrWhiteSpace(incrementalSchedulingStatePath));

            bool saved = true;
            string status = "Saved";
            string reason = string.Empty;

            var saveStopwatch = new StopwatchVar();

            using (saveStopwatch.Start())
            {
                DirtyNodeTracker.MaterializePendingUpdatedState();

                if (!DirtyNodeTracker.HasChanged &&
                    !m_dynamicPathsChanged &&
                    !m_pipGraphChanged &&
                    m_atomicSaveToken == atomicSaveToken)
                {
                    saved = false;
                    status = "Not saved";
                    reason = "State unchanged";
                }
                else
                {
                    UpdateGraphAgnosticStateBasedOnDirtyNodeTracker();

                    try
                    {
                        Save(atomicSaveToken, incrementalSchedulingStatePath);
                    }
                    catch (BuildXLException ex)
                    {
                        saved = false;
                        status = "Failed";
                        reason = ex.GetLogEventMessage();
                    }
                }
            }

            StateStats.LogStats(this, Tracing.Logger.Log.IncrementalSchedulingStateStatsEnd);

            Tracing.Logger.Log.IncrementalSchedulingSaveState(
                m_loggingContext, 
                incrementalSchedulingStatePath, 
                status, 
                reason, 
                (long)saveStopwatch.TotalElapsed.TotalMilliseconds);

            return saved;
        }

        private void UpdateGraphAgnosticStateBasedOnDirtyNodeTracker()
        {
            if (!DirtyNodeTracker.HasChanged)
            {
                return;
            }

            // Update graph-agnostic state part belonging to this current graph.

            // Step 1. Add or update the pip origins of clean pips.
            // This ensures that we have one-to-one correpondence/association between pip fingerprints and pip stable ids.
            Parallel.ForEach(
                PipGraph.AllPipStaticFingerprints.ToList(),
                pipAndFingerprint =>
                {
                    if (!DirtyNodeTracker.IsNodeDirty(pipAndFingerprint.Key.ToNodeId()))
                    {
                        m_pipOrigins.AddOrUpdate(pipAndFingerprint.Value, (PipGraph.PipTable.GetPipSemiStableHash(pipAndFingerprint.Key), m_indexToGraphLogs));
                    }
                });

            // Step 2. Update the mappings from output files to their producers for clean producers.
            // Note that we only care about the first producer.
            Parallel.ForEach(
                PipGraph.AllFilesAndProducers.Where(f => f.Key.RewriteCount == 1).ToList(),
                fileAndProducer =>
                {
                    if (!DirtyNodeTracker.IsNodeDirty(fileAndProducer.Value.ToNodeId())
                        && PipGraph.TryGetPipFingerprint(fileAndProducer.Value, out ContentFingerprint fingerprint)
                        && m_pipOrigins.TryGetPipId(fingerprint, out PipStableId pipStableId))
                    {
                        m_pipProducers.Add(MapPath(PipGraph, fileAndProducer.Key.Path, m_internalPathTable), pipStableId);
                    }
                });

            // Step 3. Update the mappings from output directories to their producers for clean-and-materialized producers.
            Parallel.ForEach(
                PipGraph.AllOutputDirectoriesAndProducers.ToList(),
                directoryAndProducer =>
                {
                    if (!DirtyNodeTracker.IsNodeDirty(directoryAndProducer.Value.ToNodeId())
                        && PipGraph.TryGetPipFingerprint(directoryAndProducer.Value, out ContentFingerprint fingerprint)
                        && m_pipOrigins.TryGetPipId(fingerprint, out PipStableId pipStableId))
                    {
                        m_pipProducers.Add(MapPath(PipGraph, directoryAndProducer.Key.Path, m_internalPathTable), pipStableId);
                    }
                });

            // Step 4. Reflect the materialized status from graph-inagnostic state to graph-agnostic state.
            Parallel.ForEach(
                DirtyNodeTracker.PendingUpdates.NodesWithChangedMaterializationStatus.ToList(),
                nodeWithMaterializationStatus =>
                {
                    PipId pipId = nodeWithMaterializationStatus.Key.ToPipId();
                    if (TryGetPipStableId(pipId, out PipStableId pipStableId))
                    {
                        // If Pip A was direct dirtied then it will not be in m_materializedPips.
                        // If Pip A was run (cache miss), then it will be marked as materialized by the DirtyNodeTracker and should be added to m_materializedPips.
                        //
                        // If Pip B was not direct dirtied but an input Pip A was, then Pip B will still be in m_materializedPips.
                        // If Pip A runs but Pip B does not due to the pip filter, then Pip B is not materialized and needs to removed from m_materializedPips.
                        bool materialized = nodeWithMaterializationStatus.Value;
                        if (materialized)
                        {
                            m_materializedPips.Add(pipStableId);
                        }
                        else
                        {
                            m_materializedPips.Remove(pipStableId);
                        }
                    }
                });

            // Step 4B. Reflect the clean status from graph-inagnostic state to graph-agnostic state.
            Parallel.ForEach(
                DirtyNodeTracker.PendingUpdates.CleanNodes.ToList(),
                cleanNode =>
                {
                    PipId pipId = cleanNode.ToPipId();

                    if (TryGetPipStableId(pipId, out PipStableId pipStableId))
                    {
                        // If pip is clean from previous run (smaller version), then no need to add it to the clean pips set.
                        // Note that we use TryAdd to add clean state if necessary.
                        // Note also that we remove pips from the clean pips set during journal scanning or graph change processing. 
                        m_cleanPips.TryAdd(pipStableId, m_pipGraphSequenceNumber);
                    }
                    else if (PipGraph.PipTable.GetPipType(pipId) == PipType.HashSourceFile)
                    {
                        // If source file pip is clean from previous run (smaller version), then no need to add it to the clean source files set.
                        // Note that we use TryAdd to add clean state if necessary.
                        // Note also that we remove source files from the clean source files set during journal scanning or graph change processing.
                        var hashSourceFilePip = PipGraph.GetPipFromPipId(pipId) as HashSourceFile;
                        m_cleanSourceFiles.TryAdd(InternGraphPath(hashSourceFilePip.Artifact.Path), m_pipGraphSequenceNumber);
                    }
                });
        }

        private void Save(FileEnvelopeId atomicSaveToken, string incrementalSchedulingStatePath)
        {
            Contract.Requires(atomicSaveToken.IsValid);
            Contract.Requires(!string.IsNullOrWhiteSpace(incrementalSchedulingStatePath));

            FileUtilities.DeleteFile(incrementalSchedulingStatePath, tempDirectoryCleaner: m_tempDirectoryCleaner);
            FileUtilities.CreateDirectory(Path.GetDirectoryName(incrementalSchedulingStatePath));

            using (var stream = FileUtilities.CreateFileStream(
                incrementalSchedulingStatePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Delete,
                // Do not write the file with SequentialScan since it will be reread in the subsequent build
                FileOptions.None))
            {
                ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        FileEnvelopeId correlationId = FileEnvelopeId.Create();
                        s_fileEnvelope.WriteHeader(stream, correlationId);

                        using (var writer = new BuildXLWriter(debug: false, leaveOpen: true, logStats: false, stream: stream))
                        {
                            // Engine state id must come first for quick reusability check.
                            writer.Write(m_engineStateId);

                            atomicSaveToken.Serialize(writer);
                            
                            // Write the id of the current graph for optimization if graph doesn't change.
                            writer.Write(PipGraph.GraphId);

                            // TODO: We may want to split these into small files so saving can be parallelized.
                            // TODO: Or we can split the files into graph-agnostic and graph-specific ones.

                            m_internalPathTable.StringTable.Serialize(writer);
                            m_internalPathTable.Serialize(writer);
                            m_incrementalSchedulingStateId.Serialize(writer);

                            m_pipProducers.Serialize(writer);
                            m_cleanPips.Serialize(writer, kvp => { writer.Write(kvp.Key); kvp.Value.Serialize(writer); });
                            m_materializedPips.Serialize(writer, v => writer.Write(v));
                            m_cleanSourceFiles.Serialize(writer, kvp => { writer.Write(kvp.Key); kvp.Value.Serialize(writer); });
                            m_dynamicallyObservedFiles.Serialize(writer, (w, id) => w.Write(id));
                            m_dynamicallyProbedFiles.Serialize(writer, (w, id) => w.Write(id));
                            m_dynamicallyObservedEnumerations.Serialize(writer, (w, id) => w.Write(id));
                            m_pipOrigins.Serialize(writer);
                            writer.WriteReadOnlyList(m_graphLogs, (w, l) => { w.Write(l.Item1); w.Write(l.Item2); });
                            m_dirtiedPips.Serialize(writer);
                            m_pipGraphSequenceNumber.Serialize(writer);
                            writer.Write(m_indexToGraphLogs);

                            // Serialize the dirty node of the current graph for optimization if graph doesn't change.
                            DirtyNodeTracker.CreateSerializedState().Serialize(writer);
                        }

                        s_fileEnvelope.FixUpHeader(stream, correlationId);
                    },
                    ex => { throw new BuildXLException(I($"Failed to save incremental scheduling state to '{incrementalSchedulingStatePath}'"), ex); });
            }
        }

        #endregion Save state

        #region Load or reuse state

        /// <summary>
        /// Checks if this instance of <see cref="GraphAgnosticIncrementalSchedulingState"/> is reusable.
        /// </summary>
        private ReuseFromEngineStateKind CheckIfIncrementalSchedulingIsReusableFromEngineState(
            PipGraph pipGraph,
            GraphAgnosticIncrementalSchedulingStateId newId,
            string incrementalSchedulingStatePath)
        {
            Contract.Requires(pipGraph != null);
            Contract.Requires(newId != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(incrementalSchedulingStatePath));

            var reuseKind = pipGraph.GraphId != PipGraph.GraphId
                ? ReuseFromEngineStateKind.ChangedGraph
                : (!m_incrementalSchedulingStateId.IsAsSafeOrSaferThan(newId)
                    ? ReuseFromEngineStateKind.MismatchedId
                    : ReuseFromEngineStateKind.Reusable);

            if (reuseKind == ReuseFromEngineStateKind.Reusable)
            {
                Guid engineStateId = default;
                if (!TryLoad(incrementalSchedulingStatePath, reader => engineStateId = reader.ReadGuid()).Succeeded)
                {
                    return ReuseFromEngineStateKind.UnverifiedEngineStateId;
                }

                if (engineStateId != m_engineStateId)
                {
                    return ReuseFromEngineStateKind.MismatchedEngineStateId;
                }
            }

            return reuseKind;
        }

        /// <inheritdoc />
        public IIncrementalSchedulingState ReuseFromEngineState(
            LoggingContext loggingContext,
            PipGraph pipGraph,
            IConfiguration configuration,
            PreserveOutputsInfo preserveOutputSalt,
            string incrementalSchedulingPath,
            ITempCleaner tempDirectoryCleaner = null)
        {
            Contract.Requires(loggingContext != null);

            GraphAgnosticIncrementalSchedulingStateId newIncrementalSchedulingStateId = GraphAgnosticIncrementalSchedulingStateId.Create(pipGraph.Context.PathTable, configuration, preserveOutputSalt);
            ReuseFromEngineStateKind reuseKind = CheckIfIncrementalSchedulingIsReusableFromEngineState(pipGraph, newIncrementalSchedulingStateId, incrementalSchedulingPath);

            Tracing.Logger.Log.IncrementalSchedulingReuseState(
                loggingContext, 
                reuseKind.ToString(),
                reuseKind == ReuseFromEngineStateKind.Reusable ? m_engineStateId.ToString() : string.Empty);

            if (reuseKind != ReuseFromEngineStateKind.Reusable)
            {
                return null;
            }

            return new GraphAgnosticIncrementalSchedulingState(
                loggingContext,
                m_atomicSaveToken,
                PipGraph,
                m_internalPathTable,
                newIncrementalSchedulingStateId,
                new DirtyNodeTracker(pipGraph.DirectedGraph, DirtyNodeTracker.CreateSerializedState(), CreateDirtyNodeTrackerTraversal(pipGraph, configuration.Schedule.StopDirtyOnSucceedFastPips)),
                m_pipProducers,
                m_cleanPips,
                m_materializedPips,
                m_cleanSourceFiles,
                m_dynamicallyObservedFiles,
                m_dynamicallyProbedFiles,
                m_dynamicallyObservedEnumerations,
                m_pipOrigins,
                m_graphLogs,
                new DirtiedPips(),
                false,
                m_pipGraphSequenceNumber,
                m_indexToGraphLogs,
                Guid.NewGuid(), // Renew engine state id to avoid cross talk with different build server.
                tempDirectoryCleaner,
                configuration.Schedule.StopDirtyOnSucceedFastPips);
        }

        /// <summary>
        /// Loads an instance of <see cref="GraphAgnosticIncrementalSchedulingState"/> from disk.
        /// </summary>
        /// <returns>An instance of <see cref="GraphAgnosticIncrementalSchedulingState"/>; or null if failed.</returns>
        /// <remarks>
        /// When loading failed, an error must have been logged.
        /// </remarks>
        public static GraphAgnosticIncrementalSchedulingState Load(
            LoggingContext loggingContext,
            FileEnvelopeId atomicSaveToken,
            PipGraph pipGraph,
            IConfiguration configuration,
            PreserveOutputsInfo preserveOutputSalt,
            string incrementalSchedulingStatePath,
            bool analysisModeOnly = false,
            ITempCleaner tempDirectoryCleaner = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(analysisModeOnly || atomicSaveToken.IsValid);
            Contract.Requires(pipGraph != null);
            Contract.Requires(analysisModeOnly || configuration != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(incrementalSchedulingStatePath));

            FileEnvelopeId loadedAtomicSaveToken = default;
            Guid loadedGraphId = default;
            PathTable internalPathTable = default;
            GraphAgnosticIncrementalSchedulingStateId loadedIncrementalSchedulingStateId = default;
            PipProducers pipProducers = default;
            ConcurrentBigMap<PipStableId, PipGraphSequenceNumber> cleanPips = default;
            ConcurrentBigSet<PipStableId> materializedPips = default;
            ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber> cleanSourceFiles = default;
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedFiles = default;
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyProbedFiles = default;
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedEnumerations = default;
            PipOrigins pipOrigins = default;
            List<(Guid, DateTime)> graphLogs = default;
            DirtiedPips dirtiedPips = default;
            DirtyNodeTracker.DirtyNodeTrackerSerializedState dirtyNodeTrackerSerializedState = default;
            PipGraphSequenceNumber pipGraphSequenceNumber = PipGraphSequenceNumber.Zero;
            int indexToGraphLogs = default;
            Guid engineStateId = default;

            // Step 1: Load exisiting incremental scheduling state.
            var loadStateStopwatch = new StopwatchVar();
            Possible<Unit> tryLoadState;

            using (loadStateStopwatch.Start())
            {
                tryLoadState = TryLoad(
                    incrementalSchedulingStatePath,
                    reader =>
                    {
                        // TODO: Loading can be parallelized if we split the state into multiple files.
                        engineStateId = reader.ReadGuid();
                        loadedAtomicSaveToken = FileEnvelopeId.Deserialize(reader);
                        loadedGraphId = reader.ReadGuid();
                        var stringTableTask = StringTable.DeserializeAsync(reader);
                        internalPathTable = PathTable.DeserializeAsync(reader, stringTableTask).GetAwaiter().GetResult();
                        loadedIncrementalSchedulingStateId = GraphAgnosticIncrementalSchedulingStateId.Deserialize(reader);
                        pipProducers = PipProducers.Deserialize(reader);
                        cleanPips = ConcurrentBigMap<PipStableId, PipGraphSequenceNumber>.Deserialize(
                            reader,
                            () => new KeyValuePair<PipStableId, PipGraphSequenceNumber>(reader.ReadPipStableId(), PipGraphSequenceNumber.Deserialize(reader)));
                        materializedPips = ConcurrentBigSet<PipStableId>.Deserialize(
                            reader,
                            () => reader.ReadPipStableId());
                        cleanSourceFiles = ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber>.Deserialize(
                            reader,
                            () => new KeyValuePair<AbsolutePath, PipGraphSequenceNumber>(reader.ReadAbsolutePath(), PipGraphSequenceNumber.Deserialize(reader)));
                        dynamicallyObservedFiles = IncrementalSchedulingPathMapping<PipStableId>.Deserialize(reader, r => r.ReadPipStableId());
                        dynamicallyProbedFiles = IncrementalSchedulingPathMapping<PipStableId>.Deserialize(reader, r => r.ReadPipStableId());
                        dynamicallyObservedEnumerations = IncrementalSchedulingPathMapping<PipStableId>.Deserialize(reader, r => r.ReadPipStableId());
                        pipOrigins = PipOrigins.Deserialize(reader);
                        graphLogs = new List<(Guid, DateTime)>(reader.ReadReadOnlyList(r => (r.ReadGuid(), r.ReadDateTime())));
                        dirtiedPips = DirtiedPips.Deserialize(reader);
                        pipGraphSequenceNumber = PipGraphSequenceNumber.Deserialize(reader);
                        indexToGraphLogs = reader.ReadInt32();

                        if (loadedGraphId == pipGraph.GraphId)
                        {
                            // Deserializes dirty node tracker only if the persisted one can be reused, i.e., graph has not changed.
                            dirtyNodeTrackerSerializedState = DirtyNodeTracker.DirtyNodeTrackerSerializedState.Deserialize(reader);
                        }
                    });
            }

            Tracing.Logger.Log.IncrementalSchedulingLoadState(
                loggingContext, 
                incrementalSchedulingStatePath,
                tryLoadState.Succeeded ? "Success" : "Fail",
                tryLoadState.Succeeded ? string.Empty : tryLoadState.Failure.DescribeIncludingInnerFailures(), 
                (long)loadStateStopwatch.TotalElapsed.TotalMilliseconds);

            if (!tryLoadState.Succeeded)
            {
                SchedulerUtilities.TryLogAndMaybeRemoveCorruptFile(incrementalSchedulingStatePath, configuration, pipGraph.Context.PathTable, loggingContext, removeFile: true);
                return null;
            }

            GraphAgnosticIncrementalSchedulingStateId incrementalSchedulingStateId = loadedIncrementalSchedulingStateId;

            // Step 2: Check integrity of the loaded state if not in analysis mode.
            if (!analysisModeOnly)
            {
                if (loadedAtomicSaveToken != atomicSaveToken)
                {
                    Tracing.Logger.Log.IncrementalSchedulingTokensMismatch(
                        loggingContext,
                        loadedAtomicSaveToken.ToString(),
                        atomicSaveToken.ToString());

                    return null;
                }

                incrementalSchedulingStateId = GraphAgnosticIncrementalSchedulingStateId.Create(pipGraph.Context.PathTable, configuration, preserveOutputSalt);

                if (!loadedIncrementalSchedulingStateId.IsAsSafeOrSaferThan(incrementalSchedulingStateId))
                {
                    Tracing.Logger.Log.IncrementalSchedulingIdsMismatch(
                        loggingContext,
                        incrementalSchedulingStateId.ToString(),
                        loadedIncrementalSchedulingStateId.ToString());

                    return null;
                }

                // If not in analysis mode, then the latest nodes that get dirtied due to graph change or journal scan become irrelevant.
                dirtiedPips = new DirtiedPips();
            }

            DirtyNodeTracker dirtyNodeTracker = default;

            // Step 3: Adapt the incremental scheduling state to the given pip graph.

            pipGraphSequenceNumber = loadedGraphId != pipGraph.GraphId ? pipGraphSequenceNumber.Increment() : pipGraphSequenceNumber;

            // configuration.Schedule is null in analysisMode and this boolean doesn't do anything in that case.
            bool stopDirtyOnSuccceedFastPips = analysisModeOnly ? false : configuration.Schedule.StopDirtyOnSucceedFastPips;
            if (loadedGraphId == pipGraph.GraphId)
            {
                // Loaded graph id matches the graph id of the current graph, which means that
                // the dirty node tracker can be reused. Other states do not need to be changed.
                Contract.Assert(dirtyNodeTrackerSerializedState != null);
                dirtyNodeTracker = new DirtyNodeTracker(pipGraph.DirectedGraph, dirtyNodeTrackerSerializedState, CreateDirtyNodeTrackerTraversal(pipGraph, stopDirtyOnSuccceedFastPips));
            }
            else
            {
                // Re-initialize dirty node tracker.
                dirtyNodeTracker = CreateInitialDirtyNodeTracker(pipGraph, false, stopDirtyOnSuccceedFastPips);

                var processGraphChangeStopwatch = new StopwatchVar();

                using (processGraphChangeStopwatch.Start())
                {
                    // Adapt to graph change.
                    ProcessGraphChange(
                        loggingContext,
                        pipGraph,
                        internalPathTable,
                        dirtyNodeTracker,
                        pipProducers,
                        cleanPips,
                        materializedPips,
                        cleanSourceFiles,
                        pipOrigins,
                        dynamicallyObservedFiles,
                        dynamicallyProbedFiles,
                        dynamicallyObservedEnumerations,
                        dirtiedPips,
                        graphLogs,
                        pipGraphSequenceNumber,
                        out indexToGraphLogs);
                }

                Tracing.Logger.Log.IncrementalSchedulingProcessGraphChange(
                    loggingContext,
                    loadedGraphId.ToString(),
                    pipGraph.GraphId.ToString(),
                    (long)processGraphChangeStopwatch.TotalElapsed.TotalMilliseconds);
            }

            var state = new GraphAgnosticIncrementalSchedulingState(
                loggingContext,
                loadedAtomicSaveToken,
                pipGraph,
                internalPathTable,
                incrementalSchedulingStateId,
                dirtyNodeTracker,
                pipProducers,
                cleanPips,
                materializedPips,
                cleanSourceFiles,
                dynamicallyObservedFiles,
                dynamicallyProbedFiles,
                dynamicallyObservedEnumerations,
                pipOrigins,
                graphLogs,
                dirtiedPips,
                loadedGraphId != pipGraph.GraphId,
                pipGraphSequenceNumber,
                indexToGraphLogs,
                analysisModeOnly ? engineStateId : Guid.NewGuid(), // Renew if not analysis mode.
                tempDirectoryCleaner,
                stopDirtyOnSuccceedFastPips);

            StateStats.LogStats(state, Tracing.Logger.Log.IncrementalSchedulingStateStatsAfterLoad);

            return state;
        }

        private static Possible<Unit> TryLoad(string incrementalSchedulingStatePath, Action<BuildXLReader> load)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(incrementalSchedulingStatePath));
            Contract.Requires(load != null);

            if (!File.Exists(incrementalSchedulingStatePath))
            {
                return new Failure<string>(I($"File '{incrementalSchedulingStatePath}' not found"));
            }

            using (FileStream stream = FileUtilities.CreateFileStream(
                incrementalSchedulingStatePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                // Ok to evict the file from standby since the file will be overwritten and never reread from disk after this point.
                FileOptions.SequentialScan))
            {
                try
                {
                    Analysis.IgnoreResult(s_fileEnvelope.ReadHeader(stream));
                }
                catch (BuildXLException ex)
                {
                    return new Failure<string>(ex.Message);
                }

                try
                {
                    using (var reader = new BuildXLReader(debug: false, stream: stream, leaveOpen: true))
                    {
                        load(reader);
                    }
                }
                catch (Exception ex)
                {
                    return new Failure<string>(ex.GetLogEventMessage());
                }
            }

            return Unit.Void;
        }

        private static void UpdateOutputPathProducer(
            LoggingContext loggingContext,
            PathTable internalPathTable,
            AbsolutePath path,
            PipId currentProducer,
            PipGraph pipGraph,
            PipProducers pipProducers,
            ConcurrentBigMap<PipStableId, PipGraphSequenceNumber> cleanPips,
            ConcurrentBigSet<PipStableId> materializedPips,
            ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber> cleanSourceFiles,
            PipOrigins pipOrigins,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyProbedFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedEnumerations,
            int indexToGraphLogs,
            ConcurrentDictionary<PipStableId, PipId> dirtiedPipProducers)
        {
            Contract.Requires(path.IsValid);

            if (pipGraph.TryGetPipFingerprint(currentProducer, out ContentFingerprint currentProducerFingerprint))
            {
                if (pipProducers.TryGetProducer(path, out PipStableId existingProducerStableId) // (1)
                    && pipOrigins.TryGetFingerprint(existingProducerStableId, out ContentFingerprint existingProducerFingerprint) // (2)
                    && existingProducerFingerprint != currentProducerFingerprint)  // (3)
                {
                    // (1) The recorded producer of the path is clean.
                    // (2) Get the fingerprint of the recorded producer.
                    // (3) Unfortunately, the recorded producer is different from the current producer by their fingerprints.

                    // Pip producer has changed, dirty the graph-agnostic state for the recorded producer.
                    DirectDirtyGraphAgnosticPip(
                        existingProducerStableId,
                        cleanPips,
                        materializedPips,
                        pipOrigins,
                        pipProducers,
                        dynamicallyObservedFiles,
                        dynamicallyProbedFiles,
                        dynamicallyObservedEnumerations);

                    dirtiedPipProducers.TryAdd(existingProducerStableId, currentProducer);

                    LogChangeProducer(
                        loggingContext,
                        context =>
                        {
                            Tracing.Logger.Log.IncrementalSchedulingProcessGraphChangeProducerChange(
                                    context,
                                    path.ToString(internalPathTable),
                                    pipGraph.PipTable.GetPipSemiStableHash(currentProducer),
                                    existingProducerFingerprint.ToString());
                        });
                }
            }

            // If path is a source file, then mark it dirty, when necessary, by removing it from the clean state.
            if (cleanSourceFiles.TryRemove(path, out PipGraphSequenceNumber dummySourceSequenceNumber))
            {
                LogChangeProducer(
                    loggingContext,
                    context =>
                    {
                        Tracing.Logger.Log.IncrementalSchedulingProcessGraphChangePathNoLongerSourceFile(
                            context,
                            path.ToString(internalPathTable));
                    });
            }
        }

        private static void ProcessGraphChange(
            LoggingContext loggingContext,
            PipGraph pipGraph,
            PathTable internalPathTable,
            DirtyNodeTracker dirtyNodeTracker,
            PipProducers pipProducers,
            ConcurrentBigMap<PipStableId, PipGraphSequenceNumber> cleanPips,
            ConcurrentBigSet<PipStableId> materializedPips,
            ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber> cleanSourceFiles,
            PipOrigins pipOrigins,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyProbedFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedEnumerations,
            DirtiedPips dirtiedPips,
            List<(Guid, DateTime)> graphLogs,
            PipGraphSequenceNumber pipGraphSequenceNumber,
            out int indexToGraphLogs)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pipGraph != null);
            Contract.Requires(internalPathTable != null);
            Contract.Requires(dirtyNodeTracker != null);
            Contract.Requires(pipProducers != null);
            Contract.Requires(cleanPips != null);
            Contract.Requires(materializedPips != null);
            Contract.Requires(cleanSourceFiles != null);
            Contract.Requires(pipOrigins != null);
            Contract.Requires(dirtiedPips != null);
            Contract.Requires(graphLogs != null);

            var counters = new CounterCollection<GraphChangeCounter>();
            var sw = Stopwatch.StartNew();

            // Step 1. Find out if the pip graph has ever been encountered before.
            indexToGraphLogs = graphLogs.Count;
            bool hasSeenGraph = false;

            for (int i = 0; i < graphLogs.Count; ++i)
            {
                if (graphLogs[i].Item1 == pipGraph.GraphId)
                {
                    // Graph was encountered before.
                    indexToGraphLogs = i;
                    hasSeenGraph = true;
                    break;
                }
            }

            if (indexToGraphLogs == graphLogs.Count)
            {
                // Graph has never been seen before.
                graphLogs.Add((pipGraph.GraphId, DateTime.UtcNow));
            }

            Tracing.Logger.Log.IncrementalSchedulingProcessGraphChangeGraphId(
                loggingContext,
                hasSeenGraph.ToString(),
                graphLogs[indexToGraphLogs].Item1.ToString(),
                graphLogs[indexToGraphLogs].Item2.ToString("G"));

            int savedIndexToGraphLogs = indexToGraphLogs;
            var dirtiedPipProducers = new ConcurrentDictionary<PipStableId, PipId>();

            // Step 2. Update pip producers.
            var stepStopwatch = Stopwatch.StartNew();

            Parallel.ForEach(
               pipGraph.AllFilesAndProducers.ToList(),
               fileAndProducingPip =>
               {
                   FileArtifact file = fileAndProducingPip.Key;
                   AbsolutePath filePath = MapPath(pipGraph, file.Path, internalPathTable);

                   if (file.RewriteCount == 1)
                   {
                       // Exclude source files and rewritten files.
                       UpdateOutputPathProducer(
                           loggingContext,
                           internalPathTable,
                           filePath,
                           fileAndProducingPip.Value,
                           pipGraph,
                           pipProducers,
                           cleanPips,
                           materializedPips,
                           cleanSourceFiles,
                           pipOrigins,
                           dynamicallyObservedFiles,
                           dynamicallyProbedFiles,
                           dynamicallyObservedEnumerations,
                           savedIndexToGraphLogs,
                           dirtiedPipProducers);
                   }

                   if (file.IsSourceFile)
                   {
                       // Hash source file pips.
                       // Note that we *try* to add the source file into the cleanSourceFiles set. The source file
                       // may have been in the set already, which means that it is clean at the previous version of the pip graph.
                       // This also means that the source file has not changed since the previous version of the pip graph,
                       // and thus is also clean for any pip consuming it in the current version of the pip graph, see IsPipCleanAcrossGraphs.
                       //
                       // Example:
                       //       Graph 1: P -> f, Graph 2: Q -> f (i.e., P consumes f in Graph 1, and Q consumes f in Graph 2).
                       //   When building Graph 1, f is marked clean (and materialized) with version 1.
                       //   On switching to Graph 2, without any change on f, f is still clean with version 1, but Q is dirty.
                       //   At the end of building Graph 2, f is still clean with version 1, see that UpdateGraphAgnosticStateBasedOnDirtyNodeTracker
                       //   also updates the clean version only if f is not in the clean set. Thus, if we switch back to Graph 1, P, based
                       //   on IsPipCleanAcrossGraphs, is still clean.
                       cleanSourceFiles.TryAdd(filePath, pipGraphSequenceNumber);

                       // File is no longer produced by a non hash-source-file pip.
                       if (pipProducers.TryGetProducer(filePath, out PipStableId existingPipStableId)
                           && pipOrigins.TryGetFingerprint(existingPipStableId, out ContentFingerprint existingPipFingerprint))
                       {
                           // Dirty the graph-agnostic state of the recorded producer.
                           DirectDirtyGraphAgnosticPip(
                               existingPipStableId,
                               cleanPips,
                               materializedPips,
                               pipOrigins,
                               pipProducers,
                               dynamicallyObservedFiles,
                               dynamicallyProbedFiles,
                               dynamicallyObservedEnumerations);

                           dirtiedPipProducers.TryAdd(existingPipStableId, fileAndProducingPip.Value);

                           LogChangeProducer(
                               loggingContext,
                               context =>
                               {
                                   Tracing.Logger.Log.IncrementalSchedulingProcessGraphChangeProducerChange(
                                           context,
                                           filePath.ToString(internalPathTable),
                                           pipGraph.PipTable.GetPipSemiStableHash(fileAndProducingPip.Value),
                                           existingPipFingerprint.ToString());
                               });
                       }
                   }
               });

            counters.AddToCounter(GraphChangeCounter.UpdateFileProducersDuration, stepStopwatch.ElapsedMilliseconds);

            stepStopwatch.Restart();

            Parallel.ForEach(
                pipGraph.AllOutputDirectoriesAndProducers.ToList(),
                directoryAndProducingPip =>
                {
                    AbsolutePath directoryPath = MapPath(pipGraph, directoryAndProducingPip.Key.Path, internalPathTable);
                    UpdateOutputPathProducer(
                        loggingContext,
                        internalPathTable,
                        directoryPath,
                        directoryAndProducingPip.Value,
                        pipGraph,
                        pipProducers,
                        cleanPips,
                        materializedPips,
                        cleanSourceFiles,
                        pipOrigins,
                        dynamicallyObservedFiles,
                        dynamicallyProbedFiles,
                        dynamicallyObservedEnumerations,
                        savedIndexToGraphLogs,
                        dirtiedPipProducers);
                });

            counters.AddToCounter(GraphChangeCounter.UpdateDirectoryProducersDuration, stepStopwatch.ElapsedMilliseconds);

            dirtiedPips.PipsOfOtherPipGraphsGetDirtiedDueToGraphChange.AddRange(dirtiedPipProducers.Keys);
            counters.AddToCounter(GraphChangeCounter.UpdateDirectoryProducersDuration, dirtiedPips.PipsOfOtherPipGraphsGetDirtiedDueToGraphChange.Count);

            // Step 3. Verify that all clean pips in the current graph are still clean.
            var nodesToBeDirtied = new ConcurrentDictionary<NodeId, bool>();
            var nodesTentativelyClean = new ConcurrentDictionary<NodeId, bool>();
            var nodesTentativelyMaterialized = new ConcurrentDictionary<NodeId, bool>();

            // Example:
            //    P -> Q -> f, where P, Q are pips, and f is a source file.
            //    Suppose P, Q are clean at version 7, but because f was used in another graph, it can have version 42.
            //    Q is dirty because its version is less than the version of its dependency f.
            //    P is clean because its version equals the version of its dependency Q.
            //    We will mark P clean, but only tentatively, because later we will run a graph traversal that makes P dirty because Q is dirty.

            stepStopwatch.Restart();

            Parallel.ForEach(
                cleanPips.ToList(),
                pipStableIdAndPipGraphSequenceNumber =>
                {
                    if (TryGetGraphPipId(pipGraph, pipOrigins, pipStableIdAndPipGraphSequenceNumber.Key, out PipId pipId))
                    {
                        if (IsPipCleanAcrossGraphs(
                                loggingContext,
                                pipGraph,
                                pipGraph.GetPipFromPipId(pipId),
                                pipStableIdAndPipGraphSequenceNumber.Value,
                                internalPathTable,
                                cleanPips,
                                cleanSourceFiles,
                                pipOrigins))
                        {
                            // If pip is still clean, it may only be tentatively clean because it can be made dirty later.
                            nodesTentativelyClean.TryAdd(pipId.ToNodeId(), true);

                            if (materializedPips.Contains(pipStableIdAndPipGraphSequenceNumber.Key))
                            {
                                nodesTentativelyMaterialized.TryAdd(pipId.ToNodeId(), true);
                            }
                        }
                        else
                        {
                            // If pip is no longer clean, add it to the list so that we remember to dirty them later.
                            nodesToBeDirtied.TryAdd(pipId.ToNodeId(), true);
                        }
                    }
                });

            counters.AddToCounter(GraphChangeCounter.VerifyCleanPipsAcrossDuration, stepStopwatch.ElapsedMilliseconds);

            dirtiedPips.PipsOfCurrentGraphGetDirtiedDueToGraphChange.AddRange(nodesToBeDirtied.Keys);

            // Add to the set of dirty nodes the pips identified as dirty in step 2. This might end up flagging as dirty
            // pips in the transitive closure that otherwise would be clean
            foreach (var dirtyPipId in dirtiedPipProducers.Values)
            {
                nodesToBeDirtied.TryAdd(dirtyPipId.ToNodeId(), true);
            }

            // Mark both tentatively clean nodes and nodes to be dirtied clean. For the latter, we deliberately mark them clean, 
            // so that we can dirty them later (i.e., during graph traversal, visitation is stopped once a node is found to be dirty).
            foreach (var node in nodesTentativelyClean.Keys)
            {
                dirtyNodeTracker.MarkNodeClean(node);

                if (nodesTentativelyMaterialized.ContainsKey(node))
                {
                    dirtyNodeTracker.MarkNodeMaterialized(node);
                }
            }

            foreach (var node in nodesToBeDirtied.Keys)
            {
                dirtyNodeTracker.MarkNodeClean(node);
            }

            // Now, travese the graph to mark dirty the nodes to be dirtied.
            long pipsOfCurrentGraphGetDirtiedDueToGraphChangeCount = 0;
            var transitiveDirties = new HashSet<PipStableId>();

            stepStopwatch.Restart();

            dirtyNodeTracker.MarkNodesDirectDirty(
                nodesToBeDirtied.Keys,
                node =>
                {
                    ++pipsOfCurrentGraphGetDirtiedDueToGraphChangeCount;
                    if (TryGetPipStableId(pipGraph, pipOrigins, node.ToPipId(), out PipStableId pipStableId))
                    {
                        transitiveDirties.Add(pipStableId);
                    }
                });

            counters.AddToCounter(GraphChangeCounter.MarkDirtyNodesTransitivelyDuration, stepStopwatch.ElapsedMilliseconds);
            counters.AddToCounter(GraphChangeCounter.PipsOfCurrentGraphGetDirtiedDueToGraphChangeCount, pipsOfCurrentGraphGetDirtiedDueToGraphChangeCount);

            stepStopwatch.Restart();

            Parallel.ForEach(
                nodesToBeDirtied.Keys,
                node =>
                {
                    if (TryGetPipStableId(pipGraph, pipOrigins, node.ToPipId(), out PipStableId pipStableId))
                    {
                        DirectDirtyGraphAgnosticPip(
                            pipStableId,
                            cleanPips,
                            materializedPips,
                            pipOrigins,
                            pipProducers,
                            dynamicallyObservedFiles,
                            dynamicallyProbedFiles,
                            dynamicallyObservedEnumerations);
                    }
                });

            Parallel.ForEach(
                transitiveDirties,
                p => DirtyGraphAgnosticPip(
                        p,
                        cleanPips));

            counters.AddToCounter(GraphChangeCounter.DirtyGraphAgnosticStateDuration, stepStopwatch.ElapsedMilliseconds);
            counters.AddToCounter(GraphChangeCounter.TotalGraphChangeProcessingDuration, sw.ElapsedMilliseconds);

            counters.LogAsStatistics("GAISS" + nameof(ProcessGraphChange), loggingContext);
        }

        private static bool IsPipCleanAcrossGraphs(
            LoggingContext loggingContext,
            PipGraph pipGraph,
            Pip pip,
            PipGraphSequenceNumber pipGraphSequenceNumberWhenPipIsClean,
            PathTable internalPathTable,
            ConcurrentBigMap<PipStableId, PipGraphSequenceNumber> cleanPips,
            ConcurrentBigMap<AbsolutePath, PipGraphSequenceNumber> cleanSourceFiles, 
            PipOrigins pipOrigins)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pipGraph != null);
            Contract.Requires(pip != null);
            Contract.Requires(internalPathTable != null);
            Contract.Requires(cleanPips != null);
            Contract.Requires(cleanSourceFiles != null);
            Contract.Requires(pipOrigins != null);

            bool clean = PipArtifacts.ForEachInput(
                pip,
                fileOrDirectoryArtifact =>
                {
                    bool hasPipStableId = false;

                    if (fileOrDirectoryArtifact.IsFile)
                    {
                        FileArtifact fileArtifact = fileOrDirectoryArtifact.FileArtifact;
                        PipId pipId = PipId.Invalid;

                        if (fileArtifact.IsSourceFile)
                        {
                            // Input is a source file.
                            if (!cleanSourceFiles.TryGetValue(MapPath(pipGraph, fileArtifact.Path, internalPathTable), out PipGraphSequenceNumber sourceFileSequenceNumber)
                                || sourceFileSequenceNumber > pipGraphSequenceNumberWhenPipIsClean)
                            {
                                // Source file is dirty, or it is clean but at later version. Then, the pip is dirty.
                                LogPipDirtyAcrossGraph(
                                    loggingContext,
                                    context =>
                                    {
                                        Tracing.Logger.Log.IncrementalSchedulingPipDirtyAcrossGraphBecauseSourceIsDirty(
                                            context,
                                            pip.SemiStableHash,
                                            fileArtifact.Path.ToString(pipGraph.Context.PathTable));
                                    });
                                return false;
                            }
                        }
                        else
                        {
                            // Input is an output file.
                            if ((pipId = pipGraph.TryGetProducer(fileArtifact)).IsValid
                                && (!(hasPipStableId = TryGetPipStableId(pipGraph, pipOrigins, pipId, out PipStableId pipStableId))
                                    || !cleanPips.TryGetValue(pipStableId, out PipGraphSequenceNumber producerSequenceNumber)
                                    || producerSequenceNumber > pipGraphSequenceNumberWhenPipIsClean))
                            {
                                // Either
                                // 1. dependence pip is not tracked, i.e., it doesn't have a stable id, or
                                // 2. dependence pip is not clean, or
                                // 3. dependence pip is clean but at later version.
                                // Then, the pip is dirty.
                                LogPipDirtyAcrossGraph(
                                    loggingContext,
                                    context =>
                                    {
                                        string fingerprintText = string.Empty;

                                        if (hasPipStableId && pipOrigins.TryGetFingerprint(pipStableId, out ContentFingerprint pipFingerprint))
                                        {
                                            fingerprintText = pipFingerprint.ToString();
                                        }

                                        Tracing.Logger.Log.IncrementalSchedulingPipDirtyAcrossGraphBecauseDependencyIsDirty(
                                            context,
                                            pip.SemiStableHash,
                                            pipGraph.PipTable.GetPipSemiStableHash(pipId),
                                            fingerprintText);
                                    });
                                return false;
                            }
                        }
                    }
                    else
                    {
                        Contract.Assert(fileOrDirectoryArtifact.IsDirectory);

                        NodeId sealedDirectoryNodeId = pipGraph.GetSealedDirectoryNode(fileOrDirectoryArtifact.DirectoryArtifact);

                        if (!(hasPipStableId = TryGetPipStableId(pipGraph, pipOrigins, sealedDirectoryNodeId.ToPipId(), out PipStableId pipStableId))
                            || !cleanPips.TryGetValue(pipStableId, out PipGraphSequenceNumber sealedDirectorySequenceNumber)
                            || sealedDirectorySequenceNumber > pipGraphSequenceNumberWhenPipIsClean)
                        {
                            // Either
                            // 1. the input seal directory is not tracked, or
                            // 2. the input seal directory is not clean, or
                            // 3. the input seal directory is clean, but at later version.
                            // Then, the pip is dirty.
                            LogPipDirtyAcrossGraph(
                                loggingContext,
                                context =>
                                {
                                    string fingerprintText = string.Empty;

                                    if (hasPipStableId && pipOrigins.TryGetFingerprint(pipStableId, out ContentFingerprint pipFingerprint))
                                    {
                                        fingerprintText = pipFingerprint.ToString();
                                    }
                                    
                                    Tracing.Logger.Log.IncrementalSchedulingPipDirtyAcrossGraphBecauseDependencyIsDirty(
                                        context,
                                        pip.SemiStableHash,
                                        pipGraph.PipTable.GetPipSemiStableHash(sealedDirectoryNodeId.ToPipId()),
                                        fingerprintText);
                                });
                            return false;
                        }

                    }

                    return true;
                },
                includeLazyInputs: false);

            if (pip is SealDirectory sealDirectory && sealDirectory.Kind == SealDirectoryKind.Opaque)
            {
                // If the pip is an output directory, then PipArtifacts.ForEachInput will not let us
                // find the producer of that directory. Thus, we need to manually get the producer to check
                // if the output directory is still up-to-date.

                PipId pipId = PipId.Invalid;
                bool hasPipStableId = false;

                if ((pipId = pipGraph.TryGetProducer(sealDirectory.Directory)).IsValid
                    && (!(hasPipStableId = TryGetPipStableId(pipGraph, pipOrigins, pipId, out PipStableId pipStableId))
                        || !cleanPips.TryGetValue(pipStableId, out PipGraphSequenceNumber producerSequenceNumber)
                        || producerSequenceNumber > pipGraphSequenceNumberWhenPipIsClean))
                {
                    // Either
                    // 1. dependence pip is not tracked, i.e., it doesn't have a stable id, or
                    // 2. dependence pip is not clean, or
                    // 3. dependence pip is clean but at later version.
                    // Then, the pip is dirty.
                    LogPipDirtyAcrossGraph(
                        loggingContext,
                        context =>
                        {
                            string fingerprintText = string.Empty;

                            if (hasPipStableId && pipOrigins.TryGetFingerprint(pipStableId, out ContentFingerprint pipFingerprint))
                            {
                                fingerprintText = pipFingerprint.ToString();
                            }

                            Tracing.Logger.Log.IncrementalSchedulingPipDirtyAcrossGraphBecauseDependencyIsDirty(
                                context,
                                pip.SemiStableHash,
                                pipGraph.PipTable.GetPipSemiStableHash(pipId),
                                fingerprintText);
                        });
                    clean = false;
                }
            }

            return clean;
        }

        #endregion Load or reuse state

        #region Write text

        /// <inheritdoc />
        public void WriteText(TextWriter writer)
        {
            Contract.Requires(writer != null);

            WriteTextEntryWithBanner(writer, "Incremental Scheduling State Id", w => w.WriteLine(m_incrementalSchedulingStateId.ToString()));

            // Write graph-specific part.
            WriteTextCurrentGraph(writer);

            // Write dirtied pips.
            WriteTextDirtiedPips(writer);

            // Write dynamic state (contains both graph-specific and graph-agnostic parts).
            WriteTextDynamicState(writer);

            // Write graph-agnostic state.
            WriteTextGraphAgnosticState(writer);

            // Write graph logs.
            WriteTextGraphLogs(writer);

            // Write pip origins.
            m_pipOrigins.WriteText(writer);
        }

        private void WriteTextCurrentGraph(TextWriter writer)
        {
            Contract.Requires(writer != null);

            WriteTextEntryWithBanner(
                writer,
                "Current Pip Graph",
                w =>
                {
                    w.WriteLine(I($"Pip graph id              : {PipGraph.GraphId}"));
                    w.WriteLine(I($"Pip graph sequence number : {m_pipGraphSequenceNumber}"));
                    w.WriteLine(I($"Engine state id           : {m_engineStateId}"));
                    w.WriteLine(string.Empty);
                    WriteTextEntryWithHeader(w, "Perpetually dirty pips", w1 => WriteTextNodes(w1, PipGraph, DirtyNodeTracker.AllPerpertuallyDirtyNodes));
                    WriteTextEntryWithHeader(w, "Dirty pips", w1 => WriteTextNodes(w1, PipGraph, DirtyNodeTracker.AllDirtyNodes));
                    WriteTextEntryWithHeader(w, "Materialized pips", w1 => WriteTextNodes(w1, PipGraph, DirtyNodeTracker.AllMaterializedNodes, printHashSourceFile: false));
                });
        }

        private void WriteTextDirtiedPips(TextWriter writer)
        {
            Contract.Requires(writer != null);
            WriteTextEntryWithBanner(writer, "Dirtied Pips", w => m_dirtiedPips.WriteText(w, PipGraph, m_internalPathTable, m_pipOrigins));
        }

        private void WriteTextDynamicState(TextWriter writer)
        {
            Contract.Requires(writer != null);

            WriteTextEntryWithBanner(
                writer,
                "Dynamic Observations",
                w =>
                {
                    WriteTextEntryWithHeader(
                        w,
                        "Dynamically observed read files",
                        w1 => m_dynamicallyObservedFiles.WriteText(
                            w1, 
                            m_internalPathTable, 
                            pipStableId => 
                            {
                                string pipDescription = string.Empty;

                                if (TryGetGraphPipId(pipStableId, out PipId pipId))
                                {
                                    Pip pip = PipGraph.GetPipFromPipId(pipId);
                                    pipDescription = pip.GetDescription(PipGraph.Context);
                                }

                                return pipDescription + "(" + GetPipIdText(m_pipOrigins, pipStableId) + ")";
                            }, 
                            "Paths to pip fingerprints", 
                            "Pip fingerprints to paths"));

                    WriteTextEntryWithHeader(
                        w,
                        "Dynamically observed probed files",
                        w1 => m_dynamicallyProbedFiles.WriteText(
                            w1,
                            m_internalPathTable,
                            pipStableId =>
                            {
                                string pipDescription = string.Empty;

                                if (TryGetGraphPipId(pipStableId, out PipId pipId))
                                {
                                    Pip pip = PipGraph.GetPipFromPipId(pipId);
                                    pipDescription = pip.GetDescription(PipGraph.Context);
                                }

                                return pipDescription + "(" + GetPipIdText(m_pipOrigins, pipStableId) + ")";
                            },
                            "Paths to pip fingerprints",
                            "Pip fingerprints to paths"));

                    WriteTextEntryWithHeader(
                        w,
                        "Dynamically observed enumerations",
                        w1 => m_dynamicallyObservedEnumerations.WriteText(
                            w1,
                            m_internalPathTable,
                            pipStableId =>
                            {
                                string pipDescription = string.Empty;

                                if (TryGetGraphPipId(pipStableId, out PipId pipId))
                                {
                                    Pip pip = PipGraph.GetPipFromPipId(pipId);
                                    pipDescription = pip.GetDescription(PipGraph.Context);
                                }

                                return pipDescription + "(" + GetPipIdText(m_pipOrigins, pipStableId) + ")";
                            },
                            "Paths to pip fingerprints",
                            "Pip fingerprints to paths"));
                });
        }

        private void WriteTextGraphAgnosticState(TextWriter writer)
        {
            Contract.Requires(writer != null);

            WriteTextEntryWithBanner(
                writer,
                "Pip Graph Agnostic State",
                w =>
                {
                    WriteTextEntryWithHeader(w, "Clean pips", w1 => WriteTextMap(w1, m_cleanPips, f => GetPipIdText(m_pipOrigins, f), v => v.ToString()));
                    WriteTextEntryWithHeader(w, "Materialized pips", w1 => WriteTextSet(w1, m_materializedPips, f => GetPipIdText(m_pipOrigins, f)));
                    WriteTextEntryWithHeader(w, "Clean source files", w1 => WriteTextMap(w1, m_cleanSourceFiles, p => p.ToString(m_internalPathTable), v => v.ToString()));
                    WriteTextEntryWithHeader(w, "Pip producers", w1 => m_pipProducers.WriteText(w1, m_pipOrigins, m_internalPathTable));
                });
        }

        private void WriteTextGraphLogs(TextWriter writer)
        {
            WriteTextEntryWithBanner(
                writer,
                "Graph Logs",
                w =>
                {
                    WriteTextEntryWithHeader(
                        w,
                        "Graph logs",
                        w1 =>
                        {
                            for (int i = 0; i < m_graphLogs.Count; ++i)
                            {
                                w1.WriteLine(I($"{i}: {m_graphLogs[i].Item1}: {m_graphLogs[i].Item2.ToString("G")}"));
                            }
                        });
                });
        }

        #endregion Write text

        #region Logging

        private static int s_logChangeProducerCount = 0;

        private static void LogChangeProducer(LoggingContext loggingContext, Action<LoggingContext> logAction)
        {
            LogWithCounter(loggingContext, logAction, ref s_logChangeProducerCount);
        }

        private static int s_logPipDirtyAcrossGraph = 0;

        private static void LogPipDirtyAcrossGraph(LoggingContext loggingContext, Action<LoggingContext> logAction)
        {
            LogWithCounter(loggingContext, logAction, ref s_logPipDirtyAcrossGraph);
        }

        private static int s_logChangeOtherGraphDuringScan = 0;

        private static void LogChangeOtherGraphDuringScan(LoggingContext loggingContext, Action<LoggingContext> logAction)
        {
            LogWithCounter(loggingContext, logAction, ref s_logChangeOtherGraphDuringScan);
        }

        private static void LogWithCounter(LoggingContext loggingContext, Action<LoggingContext> logAction, ref int counter)
        {
            if (Interlocked.Increment(ref counter) <= MaxMessagesPerChangeType)
            {
                logAction(loggingContext);
            }
        }

        #endregion Logging

        #region State stats

        private static class StateStats
        {
            public static void LogStats(GraphAgnosticIncrementalSchedulingState state, Action<LoggingContext, IDictionary<string, long>> logAction)
            {
                logAction(state.m_loggingContext, CreateStats(state));
            }

            private static IDictionary<string, long> CreateStats(GraphAgnosticIncrementalSchedulingState state) =>
                new Dictionary<string, long>()
                {
                    ["CleanPips"] = state.m_cleanPips.Count,
                    ["MaterializedPips"] = state.m_materializedPips.Count,
                    ["PipOrigins"] = state.m_pipOrigins.Count,
                    ["PipProducers"] = state.m_pipProducers.ProducerCount,
                    ["ProducedPaths"] = state.m_pipProducers.ProducedPathCount,
                    ["DynamicallyObservedFiles"] = state.m_dynamicallyObservedFiles.Count,
                    ["DynamicallyProbedFiles"] = state.m_dynamicallyProbedFiles.Count,
                    ["DynamicallyObservedEnumerations"] = state.m_dynamicallyObservedEnumerations.Count
                };
        }

        #endregion

        #region Helpers

        private static DirtyNodeTracker.TraversalController CreateDirtyNodeTrackerTraversal(PipGraph pipGraph, bool stopOnSucceedFast) => !stopOnSucceedFast ? null : new (node => pipGraph.IsSucceedFast(node.ToPipId()));

        private static AbsolutePath MapPath(PathTable originPathTable, AbsolutePath path, PathTable targetPathTable)
        {
            Contract.Requires(originPathTable != null);
            Contract.Requires(originPathTable.IsValid);
            Contract.Requires(path.IsValid);
            Contract.Requires(targetPathTable != null);
            Contract.Requires(targetPathTable.IsValid);

            return AbsolutePath.Create(targetPathTable, path.ToString(originPathTable));
        }

        private static AbsolutePath MapPath(PipGraph pipGraph, AbsolutePath path, PathTable targetPathTable)
        {
            Contract.Requires(pipGraph != null);
            Contract.Requires(path.IsValid);
            Contract.Requires(targetPathTable != null);
            Contract.Requires(targetPathTable.IsValid);

            return MapPath(pipGraph.Context.PathTable, path, targetPathTable);
        }

        private AbsolutePath InternGraphPath(AbsolutePath path) => MapPath(PipGraph, path, m_internalPathTable);

        private bool TryGetPipStableId(in PipId pipId, out PipStableId pipStableId)
        {
            return TryGetPipStableId(PipGraph, m_pipOrigins, in pipId, out pipStableId);
        }

        private static bool TryGetPipStableId(PipGraph pipGraph, PipOrigins pipOrigins, in PipId pipId, out PipStableId pipStableId)
        {
            pipStableId = PipStableId.Invalid;
            return pipGraph.TryGetPipFingerprint(in pipId, out ContentFingerprint fingerprint) && pipOrigins.TryGetPipId(in fingerprint, out pipStableId);
        }

        private bool TryGetGraphPipId(PipStableId pipStableId, out PipId pipId)
        {
            pipId = PipId.Invalid;
            return m_pipOrigins.TryGetFingerprint(pipStableId, out ContentFingerprint fingerprint) && PipGraph.TryGetPipFromFingerprint(fingerprint, out pipId);
        }

        private static bool TryGetGraphPipId(PipGraph pipGraph, PipOrigins pipOrigins, PipStableId pipStableId, out PipId pipId)
        {
            pipId = PipId.Invalid;
            return pipOrigins.TryGetFingerprint(pipStableId, out ContentFingerprint fingerprint) && pipGraph.TryGetPipFromFingerprint(fingerprint, out pipId);
        }

        private void DirtyGraphAgnosticPip(PipStableId pipStableId)
        {
            DirtyGraphAgnosticPip(
                pipStableId,
                m_cleanPips);
        }

        private void DirectDirtyGraphAgnosticPip(PipStableId pipStableId)
        {
            DirectDirtyGraphAgnosticPip(
                pipStableId,
                m_cleanPips,
                m_materializedPips,
                m_pipOrigins,
                m_pipProducers,
                m_dynamicallyObservedFiles,
                m_dynamicallyProbedFiles,
                m_dynamicallyObservedEnumerations);
        }

        /// <summary>
        /// Clear state from a pip that has been directly invalidated by incremental scheduling and thus will not be skipped by the scheduler.
        /// The mappings cleared here such as dynamicallyObservedFiles, dynamicallyProbedFiles, and dynamicallyObservedEnumerations are populated
        /// when a cache check is done for a pip.  Therefore, we should only clear them when we are sure that cache check is going to happen.
        /// Pips are dirtied recursively, but downstream dirtied pips can still be skipped if the upstream pips were cached.
        /// </summary>
        private static void DirectDirtyGraphAgnosticPip(
            PipStableId pipStableId,
            ConcurrentBigMap<PipStableId, PipGraphSequenceNumber> cleanPips,
            ConcurrentBigSet<PipStableId> materializedPips,
            PipOrigins pipOrigins,
            PipProducers pipProducers,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyProbedFiles,
            IncrementalSchedulingPathMapping<PipStableId> dynamicallyObservedEnumerations)
        {
            cleanPips.TryRemove(pipStableId, out var _);
            materializedPips.Remove(pipStableId);
            pipOrigins.TryRemove(pipStableId, out var _);
            pipProducers.TryRemoveProducer(pipStableId);
            dynamicallyObservedFiles.ClearValue(pipStableId);
            dynamicallyProbedFiles.ClearValue(pipStableId);
            dynamicallyObservedEnumerations.ClearValue(pipStableId);
        }

        private static void DirtyGraphAgnosticPip(
            PipStableId pipStableId,
            ConcurrentBigMap<PipStableId, PipGraphSequenceNumber> cleanPips)
        {
            // pipOrigins and pipProducers could be safely removed here, as they are populated from the pip graph for every dirty node.
            // They are not included just for perf and the fact that they are not needed to be removed.
            // The values will only actually change on graph change (which calls DirectDirtyGraphAgnosticPip)

            // dynamicallyObserved* are not safe to remove here because those values are populated when a pip is not skipped
            // and only DirectDirtyGraphAgnosticPip pips are garenteeded to not be skipped.  If a new value for dynamicallyObserved*
            // Is created for a downstream pip, it will simply overwrite what is already there.
            cleanPips.TryRemove(pipStableId, out var _);
        }

        private enum ChangedPathKind
        {
            StaticArtifact,
            DynamicallyObservedPath,
            DynamicallyProbedPath,
            DynamicallyObservedPathEnumeration
        }

        #endregion Helpers
    }
}
