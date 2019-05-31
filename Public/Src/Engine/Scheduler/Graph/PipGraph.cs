// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Filter;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Defines graph of pips and allows adding Pips with validation.
    /// </summary>
    public sealed partial class PipGraph : PipGraphBase, IQueryablePipDependencyGraph
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
        /// Relation representing Service PipId -> Service Client PipId.
        /// </summary>
        private readonly ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>> m_servicePipClients;

        /// <summary>
        /// Unique identifier for a graph, established at creation time. This ID is durable under serialization and deserialization.
        /// </summary>
        [Pure]
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
        [Pure]
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
            PipTable pipTable,
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
                outputDirectoryRoots: serializedState.OutputDirectoryRoots,
                compositeOutputDirectoryProducers: serializedState.CompositeOutputDirectoryProducers,
                sourceSealedDirectoryRoots: serializedState.SourceSealedDirectoryRoots,
                temporaryPaths: serializedState.TemporaryPaths,
                rewritingPips: serializedState.RewritingPips,
                rewrittenPips: serializedState.RewrittenPips,
                latestWriteCountsByPath: serializedState.LatestWriteCountsByPath,
                apiServerMoniker: serializedState.ApiServerMoniker,
                pipStaticFingerprints: serializedState.PipStaticFingerprints)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(context != null);
            Contract.Requires(semanticPathExpander != null);

            Debugging.NodeIdDebugView.DebugPipGraph = this;
            Debugging.NodeIdDebugView.DebugContext = context;

            // Serialized State
            GraphId = serializedState.GraphId;
            Contract.Assume(GraphId != default(Guid), "Not convincingly unique.");

            m_sealedDirectoryNodes = serializedState.SealDirectoryNodes;
            m_servicePipClients = serializedState.ServicePipClients;
            MaxAbsolutePathIndex = serializedState.MaxAbsolutePath;
            SemistableFingerprint = serializedState.SemistableProcessFingerprint;
        }

        #endregion Constructor

        #region Dependency-based queries (IQueryablePipDependencyGraph)

        /// <summary>
        /// Performs a reachability check between <paramref name="from" /> and <paramref name="to" /> on <see cref="PipGraphBase.DataflowGraph" />.
        /// </summary>
        /// <remarks>
        /// TODO: This will not return correct results w.r.t. meta-pips, e.g. spec file pips. Need to get meta-pips ordered correctly (i.e., add them topologically).
        /// Mis-ordering is ignored for now to unblock dependency violation analysis for real pips.
        /// </remarks>
        internal bool IsReachableFrom(NodeId from, NodeId to)
        {
            if (from == PipId.DummyHashSourceFilePipId.ToNodeId() || to == PipId.DummyHashSourceFilePipId.ToNodeId())
            {
                return false;
            }

            // TODO: skipOutOfOrderNodes has to be used until meta-pips are ordered correctly.
            return DataflowGraph.IsReachableFrom(from, to, skipOutOfOrderNodes: true);
        }

        /// <inheritdoc />
        Pip IQueryablePipDependencyGraph.HydratePip(PipId pipId, PipQueryContext queryContext)
        {
            return PipTable.HydratePip(pipId, queryContext);
        }

        private NodeId TryFindContainingExclusiveOpaqueOutputDirectory(AbsolutePath filePath)
        {
            AbsolutePath path = filePath.GetParent(Context.PathTable);

            while (path.IsValid)
            {
                NodeId nodeId;
                if (OutputDirectoryProducers.TryGetValue(DirectoryArtifact.CreateWithZeroPartialSealId(path), out nodeId))
                {
                    return nodeId;
                }

                path = path.GetParent(Context.PathTable);
            }

            return NodeId.Invalid;
        }

        /// <inheritdoc/>
        public DirectoryArtifact TryGetSealSourceAncestor(AbsolutePath path)
        {
            // Walk the parent directories of the path to find if it is under a sealedSourceDirectory.
            foreach (var current in Context.PathTable.EnumerateHierarchyBottomUp(path.Value, HierarchicalNameTable.NameFlags.Sealed))
            {
                var currentDirectory = new AbsolutePath(current);
                if (SourceSealedDirectoryRoots.TryGetValue(currentDirectory, out var directoryArtifact))
                {
                    return directoryArtifact;
                }
            }
            return DirectoryArtifact.Invalid;
        }

        /// <inheritdoc/>
        public bool TryGetTempDirectoryAncestor(AbsolutePath path, out Pip pip, out AbsolutePath temPath)
        {
            // Walk the parent directories of the path to find if it is under a temp directory.
            foreach (var current in Context.PathTable.EnumerateHierarchyBottomUp(path.Value))
            {
                var currentDirectory = new AbsolutePath(current);
                if (TemporaryPaths.TryGetValue(currentDirectory, out var pipId))
                {
                    pip = PipTable.HydratePip(pipId, PipQueryContext.PipGraphRetrieveAllPips);
                    temPath = currentDirectory;
                    return true;
                }
            }

            pip = null;
            temPath = AbsolutePath.Invalid;
            return false;
        }

        /// <inheritdoc />
        public Pip GetSealedDirectoryPip(DirectoryArtifact directoryArtifact, PipQueryContext queryContext)
        {
            var nodeId = GetSealedDirectoryNode(directoryArtifact);
            var pip = PipTable.HydratePip(nodeId.ToPipId(), queryContext);
            return pip;
        }

        /// <inheritdoc />
        public PipId? TryFindProducerPipId(AbsolutePath producedPath, VersionDisposition versionDisposition, DependencyOrderingFilter? maybeOrderingFilter)
        {
            Contract.Assume(producedPath.IsValid);

            // First check if the file is witin any opaque output directory. If it is, attribute the production to that pip.
            NodeId opaqueDirectoryProducer = TryFindContainingExclusiveOpaqueOutputDirectory(producedPath);

            PipId matchedPipId;
            if (!maybeOrderingFilter.HasValue)
            {
                // No filter: We are looking for the earliest or latest producer of the path.
                if (opaqueDirectoryProducer.IsValid)
                {
                    matchedPipId = opaqueDirectoryProducer.ToPipId();
                }
                else if (versionDisposition == VersionDisposition.Latest)
                {
                    FileArtifact artifact = TryGetLatestFileArtifactForPath(producedPath);
                    if (!artifact.IsValid)
                    {
                        return null;
                    }

                    matchedPipId = PipProducers[artifact].ToPipId();
                }
                else
                {
                    Contract.Assert(versionDisposition == VersionDisposition.Earliest);
                    NodeId producerNode = TryGetOriginalProducerForPath(producedPath);
                    if (!producerNode.IsValid)
                    {
                        return null;
                    }

                    matchedPipId = producerNode.ToPipId();
                }
            }
            else
            {
                // Filter: We need to find an artifact relative to other pips.
                DependencyOrderingFilter orderingFilter = maybeOrderingFilter.Value;
                Contract.Assert(orderingFilter.Reference != null);

                NodeId originalProducerNode = opaqueDirectoryProducer;
                switch (orderingFilter.Filter)
                {
                    case DependencyOrderingFilterType.PossiblyPrecedingInWallTime:
                        {
                            // Here we need to find a producer for this path 'possibly preceding' the reference in some actual execution order.
                            // The found artifact's producer is either definitely preceding (reference reachable from producer) or concurrent (neither reachable from the other).
                            // Equivalently, the disallowed condition is that the producer is reachable from the reference - i.e., the reference occurs earlier in all execution orders.
                            //
                            // Before the reachability check, we need to pick the right artifact (producer) for the path (there may be multiple in the event of rewrites):
                            // - If the artifact is written once (or source), this is trivial.
                            // - If the artifact is rewritten multiple times, we have the property that any producer P_i (producing version i) is reachable from P_(i-1), down to the first version -
                            //   rewrites are serialized in order of version. So, we check reachability from the reference to the *lowest* version, which determines reachability to *all* versions.
                            //
                            // Examples:
                            //    R -> P_1 -> P_2 (find none; lowest reachable)
                            //    P_1 -> R -> P_2 (find P_1 since not reachable from R)
                            //    P_1    R -> P_2 (same as prior, but this time P_1 is concurrent with R).
                            //      \______/
                            // TODO: This approach is not specific to the 'latest or earliest possible' criterion - 'version dispositon'; consider
                            //       P_1 -> P_2 -> R should report P_2, not P_1
                            //       Consider a fancier IsReachableFrom(from, {set of to}) which returns the 'to' node found first (fewest hops).
                            if (!originalProducerNode.IsValid)
                            {
                                originalProducerNode = TryGetOriginalProducerForPath(producedPath);
                            }

                            if (!originalProducerNode.IsValid)
                            {
                                return null;
                            }

                            var referenceNode = orderingFilter.Reference.PipId.ToNodeId();

                            if (IsReachableFrom(referenceNode, originalProducerNode))
                            {
                                // Reference must execute before any version produced, so no match.
                                return null;
                            }

                            matchedPipId = originalProducerNode.ToPipId();
                        }

                        break;
                    case DependencyOrderingFilterType.Concurrent:
                        {
                            // We want to find a pip that is neither ordered before nor after the reference. This means that there is not a path between
                            // them when traversing edges either direction.
                            // Before each reachability check, we need to pick a suitable producer for the path (there may be multiple in the event of rewrites).
                            // Note that in general, we have to check concurrency with each version.
                            // As a tricky case, consider the following with and without P_2:
                            // P_1 ->   R -> P_3
                            //      \__>P_2>__/
                            // R is concurrent with P_2 if it is present. But without P_2, it is well-ordered between P_1 and P_3 (so concurrent with no P_*)
                            FileArtifact latestArtifact = TryGetLatestFileArtifactForPath(producedPath);

                            var referenceNode = orderingFilter.Reference.PipId.ToNodeId();

                            if (!latestArtifact.IsValid)
                            {
                                // Check for an opaque directory producer
                                if (opaqueDirectoryProducer.IsValid &&
                                    !IsReachableFrom(referenceNode, opaqueDirectoryProducer) &&
                                    !IsReachableFrom(opaqueDirectoryProducer, referenceNode))
                                {
                                    matchedPipId = opaqueDirectoryProducer.ToPipId();
                                }
                                else
                                {
                                    return null;
                                }
                            }
                            else
                            {
                                matchedPipId = PipId.Invalid;
                                for (int rewriteCount = latestArtifact.RewriteCount; rewriteCount >= 0; rewriteCount--)
                                {
                                    var thisArtifact = new FileArtifact(producedPath, rewriteCount);
                                    NodeId producerNodeId;
                                    if (!PipProducers.TryGetValue(thisArtifact, out producerNodeId))
                                    {
                                        Contract.Assume(
                                            rewriteCount == 0,
                                            "Rewrite counts are dense down to zero, unless source rewrites are disallowed (then zero might be missing).");
                                        break;
                                    }

                                    if (!IsReachableFrom(referenceNode, producerNodeId) &&
                                        !IsReachableFrom(producerNodeId, referenceNode))
                                    {
                                        // TODO: Should respect version disposition rather than disingenuously returning the latest.
                                        matchedPipId = producerNodeId.ToPipId();
                                        break;
                                    }
                                }
                            }
                        }

                        break;
                    case DependencyOrderingFilterType.OrderedBefore:
                        {
                            // We want to find a pip that is ordered before the reference. This means that there is a path from a producer of thepath to the reference.
                            // Since the earliest producer of a path is ordered before any later producers (higher write counts), we can try to find a path from there.
                            if (!originalProducerNode.IsValid)
                            {
                                originalProducerNode = TryGetOriginalProducerForPath(producedPath);
                            }

                            if (!originalProducerNode.IsValid)
                            {
                                return null;
                            }

                            var referenceNode = orderingFilter.Reference.PipId.ToNodeId();

                            if (!IsReachableFrom(originalProducerNode, referenceNode))
                            {
                                // No path from the original producer to the reference, so original producer is not ordered before the reference.
                                return null;
                            }

                            matchedPipId = originalProducerNode.ToPipId();
                        }

                        break;
                    default:
                        throw Contract.AssertFailure("Unhandled DependencyOrderingFilterType (not yet supported by Scheduler).");
                }
            }

            if (!matchedPipId.IsValid)
            {
                return null;
            }

            return matchedPipId;
        }

        /// <inheritdoc />
        public Pip TryFindProducer(AbsolutePath producedPath, VersionDisposition versionDisposition, DependencyOrderingFilter? maybeOrderingFilter)
        {
            PipId? matchedPipId = TryFindProducerPipId(producedPath, versionDisposition, maybeOrderingFilter);
            if (!matchedPipId.HasValue)
            {
                return null;
            }

            if (matchedPipId.Value == PipId.DummyHashSourceFilePipId)
            {
                return new HashSourceFile(FileArtifact.CreateSourceFile(producedPath));
            }

            return PipTable.HydratePip(matchedPipId.Value, PipQueryContext.PipGraphTryFindProducer);
        }


        /// <summary>
        /// For a given service PipId (<paramref name="servicePipId"/>), looks up all its clients, hydrates and returns them.
        /// </summary>
        public IEnumerable<Pip> GetServicePipClients(PipId servicePipId)
        {
            ConcurrentBigSet<PipId> clients;
            if (!m_servicePipClients.TryGetValue(servicePipId, out clients))
            {
                return CollectionUtilities.EmptyArray<Pip>();
            }

            var result = new Pip[clients.Count];
            for (int i = 0; i < clients.Count; i++)
            {
                result[i] = PipTable.HydratePip(clients[i], PipQueryContext.PipGraphAddServicePipDependency);
            }

            return result;
        }

        #endregion

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
        /// Checks if a number is a valid numeric representation of a pip
        /// </summary>
        [Pure]
        public bool CanGetPipFromUInt32(uint value)
        {
            var nodeId = new NodeId(value);
            return DataflowGraph.ContainsNode(nodeId);
        }

        /// <summary>
        /// Turns a previously obtained numeric representation of a pip back into the pip
        /// </summary>
        public Pip GetPipFromUInt32(uint value)
        {
            Contract.Requires(CanGetPipFromUInt32(value));
            return PipTable.HydratePip(new PipId(value), PipQueryContext.PipGraphGetPipFromUInt32);
        }

        /// <summary>
        /// Hydrites a pip from a <see cref="PipId"/>
        /// </summary>
        public Pip GetPipFromPipId(PipId pipId)
        {
            return PipTable.HydratePip(pipId, PipQueryContext.PipGraphGetPipFromUInt32);
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
        /// Gets the producing pips for the given file artifact
        /// </summary>
        /// <param name="filePath">The produced file paht</param>
        /// <returns>List of pips that produce/rewrite the given file, or an empty list if file is not produced</returns>
        public IEnumerable<Pip> GetProducingPips(AbsolutePath filePath)
        {
            List<FileArtifact> files = (from k in PipProducers.Keys where k.Path == filePath select k).ToList();
            List<NodeId> nodeIds = PipProducers.Where(kvp => files.Contains(kvp.Key)).Select(kvp => kvp.Value).ToList();

            return HydratePips(nodeIds, PipQueryContext.PipGraphGetProducingPips);
        }

        private static bool IsInput(AbsolutePath path, FileArtifact artifact, bool isInput)
        {
            return
                path == artifact.Path &&
                (isInput || artifact.RewriteCount > 1);
        }

        private static bool IsInput(AbsolutePath path, IEnumerable<FileArtifact> artifacts, bool isInput)
        {
            foreach (FileArtifact artifact in artifacts)
            {
                if (IsInput(path, artifact, isInput))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInput(AbsolutePath path, Pip pip)
        {
            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    var copyFile = (CopyFile)pip;
                    return
                        IsInput(path, copyFile.Source, isInput: true) ||
                        IsInput(path, copyFile.Destination, isInput: false);
                case PipType.WriteFile:
                    var writeFile = (WriteFile)pip;
                    return IsInput(path, writeFile.Destination, isInput: false);
                case PipType.Process:
                    var process = (Process)pip;
                    return
                        IsInput(path, process.Dependencies, isInput: true) ||
                        IsInput(path, process.GetOutputs(), isInput: false);
                case PipType.SealDirectory:
                    var sealDirectory = (SealDirectory)pip;
                    return IsInput(path, sealDirectory.Contents, isInput: true);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Get the list of pips that consume a given file artifact
        /// </summary>
        /// <param name="filePath">The consumed file path</param>
        /// <returns>List of pips that consume the given file, or an empty list if file is not consumed</returns>
        public IEnumerable<Pip> GetConsumingPips(AbsolutePath filePath)
        {
            var potentialConsumers = new HashSet<NodeId>();
            var artifact = FileArtifact.CreateSourceFile(filePath);

            while (true)
            {
                NodeId producer;
                if (PipProducers.TryGetValue(artifact, out producer))
                {
                    foreach (Edge edge in DataflowGraph.GetOutgoingEdges(producer))
                    {
                        potentialConsumers.Add(edge.OtherNode);
                    }
                }
                else if (!artifact.IsSourceFile)
                {
                    // No producer was found. Stop looking for more producers if this wasn't a source file, since there
                    // will always be a continuous chain of producers of later rewritten versions of the file
                    break;
                }

                // Look for a producer of the next rewritten version if a producer was found for this version or
                // the file was a source file (rewrite version 0)
                artifact = artifact.CreateNextWrittenVersion();
            }

            return HydratePips(potentialConsumers, PipQueryContext.PipGraphGetConsumingPips)
                .Where(pip => IsInput(filePath, pip));
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
        /// Checks to see if a path is part of the build or not
        /// </summary>
        /// <returns>true if the path is a known input or output file</returns>
        public bool IsPathInBuild(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            if (TryGetOriginalProducerForPath(path).IsValid)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets all directories containing outputs.
        /// </summary>
        /// <returns>Set of all directories that contain outputs.</returns>
        public HashSet<AbsolutePath> AllDirectoriesContainingOutputs()
        {
            var outputFileDirectories = PipProducers.Keys.Select(f => f.Path.GetParent(Context.PathTable)).ToReadOnlySet();
            var outputDirectories = new HashSet<AbsolutePath>(OutputDirectoryProducers.Keys.Select(d => d.Path));
            outputDirectories.UnionWith(outputFileDirectories);

            return outputDirectories;
        }

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
            Contract.Ensures(Contract.Result<Pip>() != null);

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
            if (!success)
            {
                Contract.Assert(false, $"Directory artifact (path: '{directoryArtifact.Path.ToString(Context.PathTable)}', PartialSealId: '{directoryArtifact.PartialSealId}', IsSharedOpaque: '{directoryArtifact.IsSharedOpaque}') should be present.");
            }
            return nodeId;
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
        /// Checks if a pip rewrites its input.
        /// </summary>
        public bool IsRewritingPip(PipId pipId)
        {
            return RewritingPips.Contains(pipId);
        }

        /// <summary>
        /// Checks if a pip has one of its outputs rewritten.
        /// </summary>
        public bool IsRewrittenPip(PipId pipId)
        {
            return RewrittenPips.Contains(pipId);
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
        /// Gets all output directories and their corresponding producers.
        /// </summary>
        public IEnumerable<KeyValuePair<DirectoryArtifact, PipId>> AllOutputDirectoriesAndProducers
            => OutputDirectoryProducers.Select(kvp => new KeyValuePair<DirectoryArtifact, PipId>(kvp.Key, kvp.Value.ToPipId()));

        /// <summary>
        /// Gets the number of known files for the build
        /// </summary>
        public int FileCount => PipProducers.Count;

        /// <summary>
        /// Gets the number of declared content (file or sealed directories or service pips) for the build
        /// </summary>
        public int ContentCount => FileCount + m_sealedDirectoryNodes.Count + m_servicePipClients.Count;

        /// <summary>
        /// Gets the number of declared content (file or sealed directories) for the build
        /// </summary>
        internal int ArtifactContentCount => FileCount + m_sealedDirectoryNodes.Count;

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
            var result = m_servicePipClients.TryGet(servicePipId);
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
        /// Gets the producer for the statically defined file or directory
        /// </summary>
        public PipId TryGetProducer(in FileOrDirectoryArtifact fileOrDirectory)
        {
            NodeId producer;
            if (fileOrDirectory.IsFile)
            {
                PipProducers.TryGetValue(fileOrDirectory.FileArtifact, out producer);
            }
            else if (!OutputDirectoryProducers.TryGetValue(fileOrDirectory.DirectoryArtifact, out producer))
            {
                m_sealedDirectoryNodes.TryGetValue(fileOrDirectory.DirectoryArtifact, out producer);
            }

            return producer.IsValid ? producer.ToPipId() : PipId.Invalid;
        }

        /// <summary>
        /// Gets the producer for the statically defined file or directory
        /// </summary>
        public PipId GetProducer(in FileOrDirectoryArtifact fileOrDirectory)
        {
            var producer = TryGetProducer(fileOrDirectory);
            Contract.Assert(producer.IsValid);
            return producer;
        }

        /// <summary>
        /// Tries to get pip fingerprints.
        /// </summary>
        public bool TryGetPipFingerprint(in PipId pipId, out ContentFingerprint fingerprint) => PipStaticFingerprints.TryGetFingerprint(pipId, out fingerprint);

        /// <summary>
        /// Tries to get pip from fingerprints.
        /// </summary>
        public bool TryGetPipFromFingerprint(in ContentFingerprint fingerprint, out PipId pipId) => PipStaticFingerprints.TryGetPip(fingerprint, out pipId);

        /// <summary>
        /// Gets all pip static fingerprints.
        /// </summary>
        public IEnumerable<KeyValuePair<PipId, ContentFingerprint>> AllPipStaticFingerprints => PipStaticFingerprints.PipStaticFingerprints;

        /// <summary>
        /// Checks if artifact must remain writable.
        /// </summary>
        public bool MustArtifactRemainWritable(in FileOrDirectoryArtifact artifact)
        {
            Contract.Requires(artifact.IsValid);

            PipId pipId = TryGetProducer(artifact);
            Contract.Assert(pipId.IsValid);

            return PipTable.GetMutable(pipId).MustOutputsRemainWritable();
        }

        /// <summary>
        /// Checks if artifact is an output that should be preserved.
        /// </summary>
        public bool IsPreservedOutputArtifact(in FileOrDirectoryArtifact artifact)
        {
            Contract.Requires(artifact.IsValid);

            if (artifact.IsFile && artifact.FileArtifact.IsSourceFile)
            {
                // Shortcut, source file is not preserved.
                return false;
            }

            PipId pipId = TryGetProducer(artifact);
            Contract.Assert(pipId.IsValid);

            if (!PipTable.GetMutable(pipId).IsPreservedOutputsPip())
            {
                // If AllowPreserveOutputs is disabled for the pip, return false before hydrating pip. 
                return false;
            }

            if (!PipTable.GetMutable(pipId).HasPreserveOutputWhitelist())
            {
                // If whitelist is not given, we preserve all outputs of the given pip.
                // This is shortcut to avoid hydrating pip in order to get the whitelist.
                return true;
            }

            Process process = PipTable.HydratePip(pipId, PipQueryContext.PreserveOutput) as Process;
            return PipArtifacts.IsPreservedOutputByPip(process, artifact.Path, Context.PathTable);
        }

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
        internal bool FilterNodesToBuild(LoggingContext loggingContext, RootFilter filter, out RangedNodeSet filteredIn, bool canonicalizeFilter)
        {
            Contract.Ensures(Contract.ValueAtReturn(out filteredIn) != null);

            var matchingNodes = new RangedNodeSet();

            // We would use NodeRange here but that (due to other usages) acquires the global exclusive lock, which is not recursive.
            // The caller to this method should already be holding it.
            matchingNodes.ClearAndSetRange(DataflowGraph.NodeRange);

            using (PerformanceMeasurement.Start(
                loggingContext,
                Statistics.ApplyingFilterToPips,
                BuildXL.Scheduler.Tracing.Logger.Log.StartFilterApplyTraversal,
                BuildXL.Scheduler.Tracing.Logger.Log.EndFilterApplyTraversal))
            {
                Contract.Assert(
                    !filter.IsEmpty,
                    "Builds with an empty filter should not actually perform filtering. Instead their pips should be added to the schedule with an initial state of Waiting. "
                    + "Or in the case of a cached graph, all pips should be scheduled without going through the overhead of filtering.");

                var outputs = FilterOutputs(filter, canonicalizeFilter);

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
                    BuildXL.Scheduler.Tracing.Logger.Log.NoPipsMatchedFilter(loggingContext, filter.FilterExpression);
                    return false;
                }
            }

            return true;
        }

        private sealed class PipFilterContext : IPipFilterContext
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

            public PathTable PathTable => m_graph.Context.PathTable;

            public IList<PipId> AllPips => m_graph.PipTable.StableKeys;

            public Pip HydratePip(PipId pipId)
            {
                Contract.Requires(pipId.IsValid);
                return m_graph.PipTable.HydratePip(pipId, PipQueryContext.PipGraphFilterNodes);
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
        internal IReadOnlyList<FileOrDirectoryArtifact> FilterOutputsForClean(RootFilter filter, bool canonicalizeFilter = true)
        {
            var outputs = FilterOutputs(filter, canonicalizeFilter);

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

            var context = new PipFilterContext(this);
            var pipFilter = canonicalizeFilter ? filter.PipFilter.Canonicalize(new FilterCanonicalizer()) : filter.PipFilter;
            return pipFilter.FilterOutputs(context);
        }

        #endregion Filtering
    }
}
