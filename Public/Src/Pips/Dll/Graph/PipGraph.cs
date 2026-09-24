// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Operations;
using BuildXL.Storage.Fingerprints;
using BuildXL.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Pips.Graph
{
    using BuildXL.Pips.DirectedGraph;
    using BuildXL.Utilities.Configuration;

    /// <summary>
    /// Defines graph of pips and allows adding Pips with validation.
    /// </summary>
    public sealed partial class PipGraph : PipGraphBase, IPipScheduleTraversal
    {
        /// <summary>
        /// Envelope for graph serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelopeGraph = new FileEnvelope(name: "PipGraph", version: 0);

        /// <summary>
        /// Envelope for graph id serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelopeGraphId = new FileEnvelope(name: "PipGraphId", version: 0);

        #region State

        /// <summary>
        /// Mapping from include directory artifacts to the <see cref="SealDirectory" /> pips nodes that indicate their completion.
        /// </summary>
        /// <remarks>
        /// Pips which depend on an include directory artifact in its final, immutable state should have a dependency edge
        /// to the corresponding <see cref="SealDirectory" /> node.
        /// </remarks>
        private readonly ConcurrentBigMap<DirectoryArtifact, NodeId> m_sealedDirectoryNodes;

        /// <summary>
        /// Unique identifier for a graph, established at creation time. This ID is durable under serialization and deserialization.
        /// </summary>
        public Guid GraphId { get; }

        /// <summary>
        /// Gets the fingerprint used for looking up performance data.
        /// This is calculated by taking the first N process semistable hashes after sorting.
        /// This provides a stable fingerprint because it is unlikely that modifications to this pip graph
        /// will change those semistable hashes. Further, it is unlikely that pip graphs of different codebases
        /// will share these values.
        /// </summary>
        public ContentFingerprint SemistableFingerprint { get; }

        /// <summary>
        /// Gets the range of node IDs valid in the current graph.
        /// </summary>
        public NodeRange NodeRange => DataflowGraph.NodeRange;

        /// <summary>
        /// The maximum index of serialized absolute paths.
        /// </summary>
        public readonly int MaxAbsolutePathIndex;

        #endregion State

        #region Constructor

        private PipGraph(
            SerializedState serializedState,
            DirectedGraph directedGraph,
            IPipTable pipTable,
            PipExecutionContext context,
            SemanticPathExpander semanticPathExpander)
            : base(
                pipTable: pipTable,
                context: context,
                semanticPathExpander: semanticPathExpander,
                dataflowGraph: directedGraph,
                values: serializedState.Values,
                specFiles: serializedState.SpecFiles,
                modules: serializedState.Modules,
                pipProducers: serializedState.PipProducers,
                outputDirectoryProducers: serializedState.OpaqueDirectoryProducers,
                outputsUnderOpaqueExistenceAssertions: serializedState.OutputsUnderOpaqueExistenceAssertions,
                outputDirectoryExclusions: serializedState.OutputDirectoryExclusions,
                outputDirectoryRoots: serializedState.OutputDirectoryRoots,
                compositeOutputDirectoryProducers: serializedState.CompositeOutputDirectoryProducers,
                sourceSealedDirectoryRoots: serializedState.SourceSealedDirectoryRoots,
                temporaryPaths: serializedState.TemporaryPaths,
                rewritingPips: serializedState.RewritingPips,
                rewrittenPips: serializedState.RewrittenPips,
                latestWriteCountsByPath: serializedState.LatestWriteCountsByPath,
                servicePipClients: serializedState.ServicePipClients,
                apiServerMoniker: serializedState.ApiServerMoniker,
                pipStaticFingerprints: serializedState.PipStaticFingerprints)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(context != null);
            Contract.Requires(semanticPathExpander != null);

            NodeIdDebugView.DebugPipGraph = this;
            NodeIdDebugView.DebugContext = context;

            // Serialized State
            GraphId = serializedState.GraphId;
            Contract.Assume(GraphId != default(Guid), "Not convincingly unique.");

            m_sealedDirectoryNodes = serializedState.SealDirectoryNodes;
            MaxAbsolutePathIndex = serializedState.MaxAbsolutePath;
            SemistableFingerprint = serializedState.SemistableProcessFingerprint;
        }

        #endregion Constructor

        #region Queries

        /// <summary>
        /// Retrieves all pips
        /// </summary>
        public IEnumerable<Pip> RetrieveAllPips()
        {
            PipId[] pipIds = PipTable.Keys.ToArray();
            return HydratePips(pipIds, PipQueryContext.PipGraphRetrieveAllPips);
        }

        /// <summary>
        /// Retrieves all pips of a particular pip type
        /// </summary>
        public IEnumerable<Pip> RetrievePipsOfType(PipType pipType)
        {
            var pipIds = new List<PipId>();

            foreach (PipId pipId in PipTable.Keys)
            {
                if (PipTable.GetPipType(pipId) == pipType)
                {
                    pipIds.Add(pipId);
                }
            }

            return HydratePips(pipIds, PipQueryContext.PipGraphRetrievePipsOfType);
        }

        /// <summary>
        /// Retrieves all pips of a particular pip type
        /// </summary>
        public IEnumerable<PipReference> RetrievePipReferencesOfType(PipType pipType)
        {
            var pipIds = new List<PipId>();

            foreach (PipId pipId in PipTable.Keys)
            {
                if (PipTable.GetPipType(pipId) == pipType)
                {
                    pipIds.Add(pipId);
                }
            }

            return AsPipReferences(pipIds, PipQueryContext.PipGraphRetrievePipsOfType);
        }

        /// <summary>
        /// Returns the PipReferences for the given PipIds
        /// </summary>
        public IEnumerable<PipReference> AsPipReferences(IEnumerable<PipId> pipIds, PipQueryContext context)
        {
            // no locking needed here
            foreach (PipId pipId in pipIds)
            {
                yield return new PipReference(PipTable, pipId, context);
            }
        }

        /// <summary>
        /// Gets a numeric representation of a pip id
        /// </summary>
        public static uint GetUInt32FromPip(Pip pip)
        {
            Contract.Requires(pip != null);
            return pip.PipId.Value;
        }

        /// <summary>
        /// Gets the list of pips generated by a spec file
        /// </summary>
        /// <param name="specPath">The spec file path</param>
        /// <returns>List of pips generated by the specified spec file</returns>
        public IEnumerable<Pip> GetPipsPerSpecFile(AbsolutePath specPath)
        {
            return (from p in PipTable.Keys.Select(pipId => PipTable.HydratePip(pipId, PipQueryContext.PipGraphGetPipsPerSpecFile))
                    where p.Provenance != null && p.Provenance.Token.Path == specPath
                    select p).ToList();
        }

        /// <summary>
        /// Retrieves the producing node for the original file artifact for the given path (the one with the lowest version)
        /// If there is no such artifact (the path has not been used as an input or output), <see cref="NodeId.Invalid" /> is
        /// returned.
        /// </summary>
        /// <remarks>
        /// The graph lock need not be held when calling this method.
        /// Internal for use in change-based scheduling, in which we need to map changed file paths back to ndoes.
        /// </remarks>
        internal NodeId TryGetOriginalProducerForPath(string pathStr)
        {
            Contract.Requires(pathStr != null);

            AbsolutePath path;
            return !AbsolutePath.TryGet(Context.PathTable, pathStr, out path) ? NodeId.Invalid : TryGetOriginalProducerForPath(path);
        }

        /// <summary>
        /// Gets all directories containing outputs.
        /// </summary>
        /// <returns>Set of all directories that contain outputs.</returns>
        public HashSet<AbsolutePath> AllDirectoriesContainingOutputs() =>
            PipProducers.Keys.Select(f => f.Path.GetParent(Context.PathTable))
            .Concat(OutputDirectoryProducers.Keys.Select(d => d.Path))
            .Concat(CompositeOutputDirectoryProducers.Keys.Select(d => d.Path))
            .ToHashSet();

        /// <summary>
        /// Gets all parents containing temporary paths.
        /// </summary>
        public HashSet<AbsolutePath> AllParentsOfTemporaryPaths() => TemporaryPaths.Select(kvp => kvp.Key.GetParent(Context.PathTable)).ToHashSet();

        /// <summary>
        /// Attempts to find the highest-versioned file artifact for a path that precedes the specified pip in graph order.
        /// The returned file artifact (if not invalid) is possibly generated causally before the specified pip,
        /// but to be sure requires a graph reachability check between the two.
        /// </summary>
        /// <remarks>
        /// The graph lock must not be held.
        /// </remarks>
        internal FileArtifact TryGetFileArtifactPrecedingPip(PipId readingPipId, AbsolutePath path)
        {
            FileArtifact latestArtifact = TryGetLatestFileArtifactForPath(path);
            if (!latestArtifact.IsValid)
            {
                return FileArtifact.Invalid;
            }

            var readingNodeId = readingPipId.ToNodeId();

            // m_pipProducers[latestArtifact] must exist. If latestArtifact has a write count > 0, then
            // the path at versions [1, latestArtifact.RewriteCount] must exist, and *possibly* also at version 0
            // (source) as well (depends if rewriting sources is allowed).

            // We want to find the producer with the highest node ID such that it is strictly less than readingNodeId.
            for (int rewriteCount = latestArtifact.RewriteCount; rewriteCount >= 1; rewriteCount--)
            {
                var thisArtifact = new FileArtifact(path, rewriteCount);
                NodeId producerNodeId = PipProducers[thisArtifact];
                if (producerNodeId.Value < readingNodeId.Value)
                {
                    return thisArtifact;
                }
            }

            {
                NodeId producerNodeId;
                var sourceArtifact = new FileArtifact(path, rewriteCount: 0);
                if (PipProducers.TryGetValue(sourceArtifact, out producerNodeId) && producerNodeId.Value < readingNodeId.Value)
                {
                    return sourceArtifact;
                }
            }

            return FileArtifact.Invalid;
        }

        internal Pip GetProducingPip(FileArtifact fileArtifact)
        {
            Contract.Requires(fileArtifact.IsValid);

            NodeId producerNodeId;

            bool getProducerNodeId = PipProducers.TryGetValue(fileArtifact, out producerNodeId);
            Contract.Assume(getProducerNodeId, "Every file artifact added into the scheduler has a producer.");

            Pip pip = PipTable.HydratePip(producerNodeId.ToPipId(), PipQueryContext.PipGraphGetProducingPip);
            Contract.Assert(pip != null, "There should be a one-to-one correspondence between node id and pip");

            return pip;
        }

        /// <inheritdoc />
        public override NodeId GetSealedDirectoryNode(DirectoryArtifact directoryArtifact)
        {
            bool success = m_sealedDirectoryNodes.TryGetValue(directoryArtifact, out var nodeId);
            Contract.Assert(success, $"Directory artifact (path: '{directoryArtifact.Path.ToString(Context.PathTable)}', PartialSealId: '{directoryArtifact.PartialSealId}', IsSharedOpaque: '{directoryArtifact.IsSharedOpaque}') should be present.");
            
            return nodeId;
        }

        /// <inheritdoc />
        protected override bool TryGetSealedDirectoryNode(DirectoryArtifact directoryArtifact, out NodeId nodeId) =>
            m_sealedDirectoryNodes.TryGetValue(directoryArtifact, out nodeId);

        /// <inheritdoc />
        protected override bool TryGetDirectoryProducer(DirectoryArtifact directoryArtifact, out NodeId nodeId)
        {
            if (OutputDirectoryProducers.TryGetValue(directoryArtifact, out nodeId))
            {
                return true;
            }

            return m_sealedDirectoryNodes.TryGetValue(directoryArtifact, out nodeId);
        }

        internal bool TryGetValuePip(FullSymbol fullSymbol, QualifierId qualifierId, AbsolutePath specFile, out PipId pipId)
        {
            NodeId nodeId;
            if (!Values.TryGetValue((fullSymbol, qualifierId, specFile), out nodeId))
            {
                pipId = PipId.Invalid;
                return false;
            }

            pipId = nodeId.ToPipId();
            return true;
        }

        /// <summary>
        /// Gets all known files for the build
        /// </summary>
        public IEnumerable<FileArtifact> AllFiles => PipProducers.Keys;

        /// <summary>
        /// Gets all known seal directories for the build
        /// </summary>
        public IEnumerable<DirectoryArtifact> AllSealDirectories => m_sealedDirectoryNodes.Keys;

        /// <summary>
        /// Gets all files and their corresponding producers.
        /// </summary>
        public IEnumerable<KeyValuePair<FileArtifact, PipId>> AllFilesAndProducers
            => PipProducers.Select(kvp => new KeyValuePair<FileArtifact, PipId>(kvp.Key, kvp.Value.ToPipId()));

        /// <summary>
        /// Gets all seal directories and their producers
        /// </summary>
        public IEnumerable<KeyValuePair<DirectoryArtifact, PipId>> AllSealDirectoriesAndProducers
            => m_sealedDirectoryNodes.Select(kvp => new KeyValuePair<DirectoryArtifact, PipId>(kvp.Key, kvp.Value.ToPipId()));

        /// <summary>
        /// Gets all output directories and their corresponding producers.
        /// </summary>
        public IEnumerable<KeyValuePair<DirectoryArtifact, PipId>> AllOutputDirectoriesAndProducers
            => OutputDirectoryProducers.Select(kvp => new KeyValuePair<DirectoryArtifact, PipId>(kvp.Key, kvp.Value.ToPipId()));

        /// <summary>
        /// Gets all composite shared output directories and their corresponding producers.
        /// </summary>
        public IEnumerable<KeyValuePair<DirectoryArtifact, PipId>> AllCompositeSharedOpaqueDirectoriesAndProducers
            => CompositeOutputDirectoryProducers.Select(kvp => new KeyValuePair<DirectoryArtifact, PipId>(kvp.Key, kvp.Value.ToPipId()));

        /// <summary>
        /// Gets the number of known files for the build
        /// </summary>
        public int FileCount => PipProducers.Count;

        /// <summary>
        /// Gets the number of declared content (file or sealed directories or service pips) for the build
        /// </summary>
        public int ContentCount => FileCount + m_sealedDirectoryNodes.Count + ServicePipClients.Count;

        /// <summary>
        /// Gets the number of declared content (file, sealed directories, or temp directories) for the build
        /// </summary>
        internal int ArtifactContentCount => FileCount + m_sealedDirectoryNodes.Count + TemporaryPaths.Count;

        /// <summary>
        /// Gets the associated file or directory for the given content index. <paramref name="contentIndex"/> should be in the range [0, <see cref="ArtifactContentCount"/>)
        /// </summary>
        internal FileOrDirectoryArtifact GetArtifactContent(int contentIndex)
        {
            if (contentIndex < FileCount)
            {
                return PipProducers.BackingSet[contentIndex].Key;
            }

            contentIndex -= FileCount;

            if (contentIndex < m_sealedDirectoryNodes.Count)
            {
                return m_sealedDirectoryNodes.BackingSet[contentIndex].Key;
            }

            contentIndex -= m_sealedDirectoryNodes.Count;

            if (contentIndex < TemporaryPaths.Count)
            {
                return DirectoryArtifact.CreateWithZeroPartialSealId(TemporaryPaths.BackingSet[contentIndex].Key);
            }

            throw Contract.AssertFailure("Out of range: contentIndex >= ArtifactContentCount");
        }

        /// <summary>
        /// Gets an unique index less than <see cref="ContentCount"/> representing the content (or null if the content is not declared)
        /// </summary>
        internal int? GetContentIndex(in FileOrDirectoryArtifact artifact)
        {
            if (artifact.IsFile)
            {
                var result = PipProducers.TryGet(artifact.FileArtifact);
                return result.IsFound ? (int?)result.Index : null;
            }
            else
            {
                var result = m_sealedDirectoryNodes.TryGet(artifact.DirectoryArtifact);
                return result.IsFound ? (int?)(result.Index + FileCount) : null;
            }
        }

        /// <summary>
        /// Gets a unique index less than <see cref="ContentCount"/> representing the input content of the service (or null if the service is not declared)
        /// </summary>
        internal int? GetServiceContentIndex(PipId servicePipId)
        {
            var result = ServicePipClients.TryGet(servicePipId);
            return result.IsFound ? (int?)(result.Index + FileCount + m_sealedDirectoryNodes.Count) : null;
        }

        /// <summary>
        /// Gets seal directories by kind.
        /// </summary>
        internal IEnumerable<SealDirectory> GetSealDirectoriesByKind(PipQueryContext queryContext, Func<SealDirectoryKind, bool> kindPredicate)
        {
            return
                m_sealedDirectoryNodes.Values.Select(
                    sealDirectoryNode => (SealDirectory)PipTable.HydratePip(sealDirectoryNode.ToPipId(), queryContext))
                    .Where(sealDirectory => kindPredicate(sealDirectory.Kind));
        }

        /// <summary>
        /// Gets seal directories by kind.
        /// </summary>
        /// <remarks>This method is used for testing.</remarks>
        public IEnumerable<SealDirectory> GetSealDirectoriesByKind(Func<SealDirectoryKind, bool> kindPredicate) => GetSealDirectoriesByKind(PipQueryContext.PipGraphGetSealDirectoryByKind, kindPredicate);

        /// <summary>
        /// Gets all pip static fingerprints.
        /// </summary>
        public IEnumerable<KeyValuePair<PipId, ContentFingerprint>> AllPipStaticFingerprints => PipStaticFingerprints.PipStaticFingerprints;

        #endregion Queries

        #region Helpers

        /// <summary>
        /// Checks if a given pip has existed in the schedule.
        /// </summary>
        internal static bool PipExists(Pip pip)
        {
            Contract.Requires(pip != null, "Argument pip cannot be null");
            return pip.PipId.IsValid;
        }

        #endregion Helpers

        #region Filtering


        /// <summary>
        /// Applies the filter to each node in the build graph.
        /// </summary>
        public bool FilterNodesToBuild(LoggingContext loggingContext, RootFilter filter, out RangedNodeSet filteredIn)
        {
            var matchingNodes = new RangedNodeSet();

            // We would use NodeRange here but that (due to other usages) acquires the global exclusive lock, which is not recursive.
            // The caller to this method should already be holding it.
            matchingNodes.ClearAndSetRange(DataflowGraph.NodeRange);

            using (PerformanceMeasurement.Start(
                loggingContext,
                Statistics.ApplyingFilterToPips,
                Tracing.Logger.Log.StartFilterApplyTraversal,
                Tracing.Logger.Log.EndFilterApplyTraversal))
            {
                Contract.Assert(
                    !filter.IsEmpty,
                    "Builds with an empty filter should not actually perform filtering. Instead their pips should be added to the schedule with an initial state of Waiting. "
                    + "Or in the case of a cached graph, all pips should be scheduled without going through the overhead of filtering.");

                var outputs = FilterOutputs(filter);

                int addAttempts = 0;

                // The outputs set contains all file artifacts that passed the filter. This means that we may
                // have multiple file artifacts per path (due to rewrites), which is not desirable - we want to approximate the
                // 'set of artifacts which must be materialized' and for each path we would only like to materialize
                // the latest filter-passing version. So, we determine the max write count for any filter-passing path.
                // TODO: This could be cheaper by having filter implementations operate on a (path -> max write count) mapping,
                //       which would correspond nicely to the structure by which filters are applied and aggregated.
                var maxWriteCounts = new Dictionary<AbsolutePath, int>();

                // There might be multiple file artifacts per path but if we get the one that has the highest max write count, we can include the others as well due to its dependencies.
                var fileArtifacts = new Dictionary<AbsolutePath, FileArtifact>();

                // There might be many seal directories per path so we should include all.
                var directoryArtifacts = new MultiValueDictionary<AbsolutePath, DirectoryArtifact>();
                foreach (FileOrDirectoryArtifact outputFileOrDirectory in outputs)
                {
                    if (outputFileOrDirectory.IsFile)
                    {
                        var output = outputFileOrDirectory.FileArtifact;
                        int writeCount = output.RewriteCount;
                        int existingMaxWriteCount;
                        if (maxWriteCounts.TryGetValue(output.Path, out existingMaxWriteCount))
                        {
                            if (writeCount <= existingMaxWriteCount)
                            {
                                continue;
                            }
                        }

                        maxWriteCounts[output.Path] = writeCount;
                        fileArtifacts[output.Path] = outputFileOrDirectory.FileArtifact;
                    }
                    else
                    {
                        Contract.Assert(outputFileOrDirectory.IsDirectory);
                        directoryArtifacts.Add(outputFileOrDirectory.DirectoryArtifact.Path, outputFileOrDirectory.DirectoryArtifact);
                    }
                }

                // Map the output files to which pips actually need to run. This is a temporary step until the
                // scheduler operates on outputs directly.
                // TODO: This means we can't represent the fact that some but not all outputs of a node are filter-passing.
                //       In the case of a process with some rewritten outputs and some final filter-passing outputs, we are
                //       not be able to guarantee a path is materialized only once (when replaying fully from cache), which is
                //       problematic for reaching an incremental scheduling fixpoint. This can be fixed by materializing on a per-file
                //       rather than per-pip basis.
                var outputPaths = outputs.Select(a => a.Path).Distinct().ToArray();
                Parallel.ForEach(
                    outputPaths,
                    path =>
                    {
                        NodeId node;
                        FileArtifact fileArtifact;
                        if (fileArtifacts.TryGetValue(path, out fileArtifact))
                        {
                            if (PipProducers.TryGetValue(fileArtifact, out node))
                            {
                                matchingNodes.AddAtomic(node);
                                Interlocked.Increment(ref addAttempts);
                            }
                            else
                            {
                                Contract.Assert(false, "Filtering matched file output that is not registered as being produced by any pip.");
                            }

                            return;
                        }

                        IReadOnlyList<DirectoryArtifact> directoryList;
                        if (directoryArtifacts.TryGetValue(path, out directoryList))
                        {
                            foreach (var directoryArtifact in directoryList)
                            {
                                if (OutputDirectoryProducers.TryGetValue(directoryArtifact, out node) ||
                                    m_sealedDirectoryNodes.TryGetValue(directoryArtifact, out node))
                                {
                                    matchingNodes.AddAtomic(node);
                                    Interlocked.Increment(ref addAttempts);
                                }
                                else
                                {
                                    Contract.Assert(false, "Filtering matched directory output that is not registered as being produced by any pip.");
                                }
                            }
                        }
                        else
                        {
                            Contract.Assert(false, "The path must exist in either fileArtifacts or directoryArtifacts");
                        }
                    });

                filteredIn = matchingNodes;

                if (addAttempts == 0)
                {
                    Contract.Assume(outputs.Count == 0);
                    Tracing.Logger.Log.NoPipsMatchedFilter(loggingContext, filter.FilterExpression);
                    return false;
                }
            }

            return true;
        }

        private sealed class PipFilterContext : IPipFilterContext, IDisposable
        {
            private readonly PipGraph m_graph;

            /// <summary>
            /// Cache for <see cref="PipFilter"/> <code>FilterOutputs</code> method.
            /// </summary>
            /// <remarks>
            /// Instead of caching the resulting outputs inside each pip filter instance itself, the cache is placed here
            /// so that pip filters are reusable for different <see cref="IPipFilterContext"/>s.
            /// </remarks>
            private readonly Dictionary<PipFilter, IReadOnlySet<FileOrDirectoryArtifact>> m_cachedOutputs =
                new Dictionary<PipFilter, IReadOnlySet<FileOrDirectoryArtifact>>(new CachedOutputKeyComparer());

            public PipFilterContext(PipGraph graph)
            {
                m_graph = graph;
            }

            /// <summary>
            /// Releases the strong pip references held during filtering,
            /// allowing the GC to reclaim hydrated pips.
            /// </summary>
            public void Dispose()
            {
#if NET6_0_OR_GREATER
                m_strongPipRefs.Clear();
#endif
            }

            public PathTable PathTable => m_graph.Context.PathTable;

            public IList<PipId> AllPips => m_graph.PipTable.StableKeys;

            /// <summary>
            /// Strong references to hydrated pips, preventing GC from collecting them
            /// while filtering is in progress. This ensures the PipTable's weak reference
            /// cache remains effective across multiple filter evaluations.
            /// </summary>
#if NET6_0_OR_GREATER
            private readonly System.Collections.Concurrent.ConcurrentBag<Pip> m_strongPipRefs =
                new System.Collections.Concurrent.ConcurrentBag<Pip>();
#endif

            public Pip HydratePip(PipId pipId)
            {
                Contract.Requires(pipId.IsValid);
                var pip = m_graph.PipTable.HydratePip(pipId, PipQueryContext.PipGraphFilterNodes);
#if NET6_0_OR_GREATER
                m_strongPipRefs.Add(pip);
#endif
                return pip;
            }

            public PipType GetPipType(PipId pipId)
            {
                Contract.Requires(pipId.IsValid);
                return m_graph.PipTable.GetPipType(pipId);
            }

            public long GetSemiStableHash(PipId pipId)
            {
                Contract.Requires(pipId.IsValid);
                return m_graph.PipTable.GetPipSemiStableHash(pipId);
            }

            public IEnumerable<PipId> GetDependencies(PipId pipId)
            {
                Contract.Requires(pipId.IsValid);
                return m_graph.DataflowGraph.GetIncomingEdges(pipId.ToNodeId()).Select(edge => edge.OtherNode.ToPipId());
            }

            public IEnumerable<PipId> GetDependents(PipId pipId)
            {
                Contract.Requires(pipId.IsValid);
                return m_graph.DataflowGraph.GetOutgoingEdges(pipId.ToNodeId()).Select(edge => edge.OtherNode.ToPipId());
            }

            public PipId GetProducer(in FileOrDirectoryArtifact fileOrDirectory)
            {
                Contract.Requires(fileOrDirectory.IsValid);
                return m_graph.GetProducer(fileOrDirectory);
            }

            public bool TryGetCachedOutputs(PipFilter pipFilter, out IReadOnlySet<FileOrDirectoryArtifact> outputs)
            {
                return m_cachedOutputs.TryGetValue(pipFilter, out outputs);
            }
        
            public void CacheOutputs(PipFilter pipFilter, IReadOnlySet<FileOrDirectoryArtifact> outputs)
            {
                m_cachedOutputs[pipFilter] = outputs;
            }

            private class CachedOutputKeyComparer : IEqualityComparer<PipFilter>
            {
                public bool Equals(PipFilter x, PipFilter y)
                {
                    return ReferenceEquals(x, y);
                }
        
                public int GetHashCode(PipFilter obj)
                {
                    // For filter-output cache key, we use the pointer value of the object
                    // for the following reason:
                    // (1) Caching is beneficial if the pip filter is canonicalized, i.e., reduced to a unique instance.
                    // (2) Computing such a pointer value is cheap.
                    // (3) Computing the hash code of pip filter using GetHashCode is more expensive than just getting
                    //     the pointer value, because the calculation of GetHashCode "traverses" the tree/graph structure
                    //     of the pip filter.
                    return RuntimeHelpers.GetHashCode(obj);
                }
            }
        }

        /// <summary>
        /// Gets filtered outputs appropriate for a clean operation
        /// </summary>
        public IReadOnlyList<FileOrDirectoryArtifact> FilterOutputsForClean(RootFilter filter)
        {
            var outputs = FilterOutputs(filter);

            List<FileOrDirectoryArtifact> outputsForDeletion = new List<FileOrDirectoryArtifact>(outputs.Count);
            foreach (var output in outputs)
            {
                if (output.IsDirectory)
                {
                    // Only for output directories can the full directory be deleted
                    if (OutputDirectoryProducers.ContainsKey(output.DirectoryArtifact))
                    {
                        outputsForDeletion.Add(output);
                    }
                    else
                    {
                        // Otherwise, delete the individual output files
                        foreach (var file in ListSealedDirectoryContents(output.DirectoryArtifact))
                        {
                            if (file.IsOutputFile)
                            {
                                outputsForDeletion.Add(file);
                            }
                        }
                    }
                }
                else if (output.FileArtifact.IsOutputFile)
                {
                    // Only output files can be cleaned
                    outputsForDeletion.Add(output.FileArtifact);
                }
            }

            return outputsForDeletion;
        }

        internal IReadOnlySet<FileOrDirectoryArtifact> FilterOutputs(RootFilter filter, bool canonicalizeFilter = true)
        {
            Contract.Requires(filter != null);

            if (filter.IsEmpty)
            {
                var outputs = new ReadOnlyHashSet<FileOrDirectoryArtifact>(PipProducers.Keys.Select(FileOrDirectoryArtifact.Create));
                outputs.UnionWith(OutputDirectoryProducers.Keys.Select(FileOrDirectoryArtifact.Create));
                return outputs;
            }

            using (var context = new PipFilterContext(this))
            {
                var pipFilter = filter.PipFilter;
                return pipFilter.FilterOutputs(context);
            }
        }

        #endregion Filtering
    }
}
