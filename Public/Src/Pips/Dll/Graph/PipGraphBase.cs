// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Operations;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Pips.Graph
{
    using BuildXL.Pips.DirectedGraph;

    /// <summary>
    /// Base class for PipGraph and PipGraph.Builder containing common state which can safely be accessed during graph build (or providing
    /// override functionality to allow locking) and implementing <see cref="IPipScheduleTraversal"/>
    /// </summary>
    public abstract class PipGraphBase : IPipScheduleTraversal, IPipGraphFileSystemView, IQueryablePipDependencyGraph
    {
        #region Context State

        /// <summary>
        /// The container for context objects
        /// </summary>
        public readonly PipExecutionContext Context;

        /// <summary>
        /// Pip table holding all known pips.
        /// </summary>
        public readonly IPipTable PipTable;

        /// <summary>
        /// Expander used when a path string should be machine / configuration independent.
        /// </summary>
        public readonly SemanticPathExpander SemanticPathExpander;

        #endregion Context State

        #region Serialized State

        /// <summary>
        /// Supporting data-flow graph.
        /// </summary>
        public DirectedGraph DataflowGraph;

        /// <inheritdoc />
        public IReadonlyDirectedGraph DirectedGraph => DataflowGraph;

        /// <summary>
        /// Mapping from full symbol and qualifier to value nodes.
        /// </summary>
        /// <remarks>
        /// Maintained by <see cref="PipGraph.Builder.AddOutputValue" />, <see cref="PipGraph.Builder.AddValueDependency" />, and <see cref="PipGraph.Builder.AddValueValueDependency" />
        /// </remarks>
        public readonly ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId> Values;

        /// <summary>
        /// Mapping from spec fileartifact to specfile nodes.
        /// </summary>
        /// <remarks>
        /// Maintained by <see cref="PipGraph.Builder.AddSpecFile" />
        /// </remarks>
        protected readonly ConcurrentBigMap<FileArtifact, NodeId> SpecFiles;

        /// <summary>
        /// Mapping from module id to module nodes.
        /// </summary>
        /// <remarks>
        /// Maintained by <see cref="PipGraph.Builder.AddSpecFile" />
        /// </remarks>
        public readonly ConcurrentBigMap<ModuleId, NodeId> Modules;

        /// <summary>
        /// Mapping from output file artifacts to the node ids of pips that produce them.
        /// </summary>
        /// <remarks>
        /// Maintained by <see cref="PipGraph.Builder.AddOutput" />
        /// </remarks>
        protected readonly ConcurrentBigMap<FileArtifact, NodeId> PipProducers;

        /// <summary>
        /// Mapping from output directories to the node ids of pips that produce them.
        /// </summary>
        protected readonly ConcurrentBigMap<DirectoryArtifact, NodeId> OutputDirectoryProducers;

        /// <summary>
        /// Mapping from directory to the set of output existence assertions
        /// </summary>
        /// <remarks>
        /// The set of existence assertions under opaque directories are made a property of the graph rather than
        /// a property of consuming pips for implementation simplicity. Otherwise, the file artifacts produced by
        /// asserting existence would have to be tracked until they become inputs of consuming pips, and only checked there.
        /// The net effect of the current implementation is that existence assertions are verified as soon as the producer
        /// executes (or gets replayed from the cache) instead of waiting for an actual consumer of it. Therefore, 
        /// the assertion validation happens eagerly. This is compatible with the spirit of these assertions and also
        /// enable some extra scenarios (e.g. specs can assert a particular file is produced under an opaque even if
        /// that file is not actually consumed by anybody)
        /// </remarks>
        protected readonly ConcurrentBigMap<DirectoryArtifact, HashSet<FileArtifact>> OutputsUnderOpaqueExistenceAssertions;

        /// <summary>
        /// The set of all output directory exclusions
        /// </summary>
        public readonly ConcurrentBigSet<AbsolutePath> OutputDirectoryExclusions;

        /// <summary>
        /// Mapping from composite output directories to the node ids of seal directory pips that produce them.
        /// </summary>
        protected readonly ConcurrentBigMap<DirectoryArtifact, NodeId> CompositeOutputDirectoryProducers;

        /// <summary>
        /// Mapping from directory root to the directory artifact representing a source sealed directory 
        /// with that root. If multiple source sealed directories share the same root, only one of them
        /// will be the artifact actually associated with a given root (this is enough for reporting purposes)
        /// </summary>
        protected readonly ConcurrentBigMap<AbsolutePath, DirectoryArtifact> SourceSealedDirectoryRoots;

        /// <summary>
        /// Mapping from temp directories to the pip that declare them.
        /// </summary>
        protected readonly ConcurrentBigMap<AbsolutePath, PipId> TemporaryPaths;

        /// <summary>
        /// All roots in <see cref="OutputDirectoryProducers"/> keys.
        /// The value indicates if any of the corresponding output directories is shared opaque, plus all the output directory artifacts for that root.
        /// </summary>
        protected readonly ConcurrentBigMap<AbsolutePath, (bool anyIsSharedOpaque, HashSet<DirectoryArtifact> directoryArtifacts)> OutputDirectoryRoots;

        /// <summary>
        /// Set of pips that rewrite their inputs.
        /// </summary>
        protected readonly ConcurrentBigSet<PipId> RewritingPips;

        /// <summary>
        /// Set of pips whose outputs are rewritten.
        /// </summary>
        protected readonly ConcurrentBigSet<PipId> RewrittenPips;

        /// <summary>
        /// For a given path, gives the highest write count of a related file artifact so far.
        /// </summary>
        /// <remarks>
        /// We use this state to validate double-write / re-write ordering:
        /// A pip can produce an output artifact with write count 1.
        /// A pip can produce an output artifact with write count X (X > 1) if:
        /// It has an input artifact of the same path, with write count X - 1, or
        /// It does not have an input artifact with the same path.
        /// Additionally, any new output artifact must have a write count one greater than its path's existing write count.
        /// If that is held true, we can assert that all double-writes / re-writes are 'ordered' with respect to each other.
        /// This mapping is also used to find antecedent producers for rewritten outputs, so that ordering edges can be added.
        /// Maintained by <see cref="PipGraph.Builder.AddOutput" />
        /// </remarks>
        protected readonly ConcurrentBigMap<AbsolutePath, int> LatestWriteCountsByPath;

        /// <summary>
        /// String id corresponding to the <see cref="BuildXL.Ipc.Common.IpcMoniker.Id"/> property of the moniker used by the
        /// BuildXL API server; <see cref="StringId.Invalid"/> indicates that no BuildXL API operation has been requested.
        /// </summary>
        public readonly StringId ApiServerMoniker;

        /// <summary>
        /// Pip static fingerprints.
        /// </summary>
        protected readonly PipGraphStaticFingerprints PipStaticFingerprints;

        /// <summary>
        /// Relation from service pip IDs to their client pip IDs.
        /// </summary>
        protected readonly ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>> ServicePipClients;

        #endregion Serialized State

        #region Constructors

        protected PipGraphBase(
                IPipTable pipTable,
                PipExecutionContext context,
                SemanticPathExpander semanticPathExpander,
                DirectedGraph dataflowGraph)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(context != null);
            Contract.Requires(semanticPathExpander != null);
            Contract.Requires(dataflowGraph != null);

            PipTable = pipTable;
            Context = context;
            SemanticPathExpander = semanticPathExpander;
            DataflowGraph = dataflowGraph;

            Values = new ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId>();
            SpecFiles = new ConcurrentBigMap<FileArtifact, NodeId>();
            Modules = new ConcurrentBigMap<ModuleId, NodeId>();
            PipProducers = new ConcurrentBigMap<FileArtifact, NodeId>();
            OutputDirectoryProducers = new ConcurrentBigMap<DirectoryArtifact, NodeId>();
            OutputsUnderOpaqueExistenceAssertions = new ConcurrentBigMap<DirectoryArtifact, HashSet<FileArtifact>>();
            OutputDirectoryExclusions = new ConcurrentBigSet<AbsolutePath>();
            OutputDirectoryRoots = new ConcurrentBigMap<AbsolutePath, (bool anyIsSharedOpaque, HashSet<DirectoryArtifact> directoryArtifacts)>();
            CompositeOutputDirectoryProducers = new ConcurrentBigMap<DirectoryArtifact, NodeId>();
            SourceSealedDirectoryRoots = new ConcurrentBigMap<AbsolutePath, DirectoryArtifact>();
            TemporaryPaths = new ConcurrentBigMap<AbsolutePath, PipId>();
            RewritingPips = new ConcurrentBigSet<PipId>();
            RewrittenPips = new ConcurrentBigSet<PipId>();
            LatestWriteCountsByPath = new ConcurrentBigMap<AbsolutePath, int>();
            ApiServerMoniker = StringId.Invalid;
            PipStaticFingerprints = new PipGraphStaticFingerprints();
            ServicePipClients = new ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>>();
        }

        /// <summary>
        /// Initialize state from deserializing
        /// </summary>
        protected PipGraphBase(
                IPipTable pipTable,
                PipExecutionContext context,
                SemanticPathExpander semanticPathExpander,
                DirectedGraph dataflowGraph,
                ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId> values,
                ConcurrentBigMap<FileArtifact, NodeId> specFiles,
                ConcurrentBigMap<ModuleId, NodeId> modules,
                ConcurrentBigMap<FileArtifact, NodeId> pipProducers,
                ConcurrentBigMap<DirectoryArtifact, NodeId> outputDirectoryProducers,
                ConcurrentBigMap<DirectoryArtifact, HashSet<FileArtifact>> outputsUnderOpaqueExistenceAssertions,
                ConcurrentBigSet<AbsolutePath> outputDirectoryExclusions,
                ConcurrentBigMap<AbsolutePath, (bool isSharedOpaque, HashSet<DirectoryArtifact> artifacts)> outputDirectoryRoots,
                ConcurrentBigMap<DirectoryArtifact, NodeId> compositeOutputDirectoryProducers,
                ConcurrentBigMap<AbsolutePath, DirectoryArtifact> sourceSealedDirectoryRoots,
                ConcurrentBigMap<AbsolutePath, PipId> temporaryPaths,
                ConcurrentBigSet<PipId> rewritingPips,
                ConcurrentBigSet<PipId> rewrittenPips,
                ConcurrentBigMap<AbsolutePath, int> latestWriteCountsByPath,
                ConcurrentBigMap<PipId, ConcurrentBigSet<PipId>> servicePipClients,
                StringId apiServerMoniker,
                PipGraphStaticFingerprints pipStaticFingerprints)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(context != null);
            Contract.Requires(semanticPathExpander != null);
            Contract.Requires(values != null);
            Contract.Requires(specFiles != null);
            Contract.Requires(modules != null);
            Contract.Requires(pipProducers != null);
            Contract.Requires(outputDirectoryProducers != null);
            Contract.Requires(outputsUnderOpaqueExistenceAssertions != null);
            Contract.Requires(outputDirectoryExclusions != null);
            Contract.Requires(outputDirectoryRoots != null);
            Contract.Requires(compositeOutputDirectoryProducers != null);
            Contract.Requires(sourceSealedDirectoryRoots != null);
            Contract.Requires(temporaryPaths != null);
            Contract.Requires(rewritingPips != null);
            Contract.Requires(rewrittenPips != null);
            Contract.Requires(latestWriteCountsByPath != null);
            Contract.Requires(servicePipClients != null);
            Contract.Requires(pipStaticFingerprints != null);

            PipTable = pipTable;
            Context = context;
            SemanticPathExpander = semanticPathExpander;
            DataflowGraph = dataflowGraph;

            // Serialized State
            Values = values;
            SpecFiles = specFiles;
            Modules = modules;
            PipProducers = pipProducers;
            OutputDirectoryProducers = outputDirectoryProducers;
            OutputsUnderOpaqueExistenceAssertions = outputsUnderOpaqueExistenceAssertions;
            OutputDirectoryExclusions = outputDirectoryExclusions;
            OutputDirectoryRoots = outputDirectoryRoots;
            CompositeOutputDirectoryProducers = compositeOutputDirectoryProducers;
            SourceSealedDirectoryRoots = sourceSealedDirectoryRoots;
            TemporaryPaths = temporaryPaths;
            RewritingPips = rewritingPips;
            RewrittenPips = rewrittenPips;
            LatestWriteCountsByPath = latestWriteCountsByPath;
            ServicePipClients = servicePipClients;
            ApiServerMoniker = apiServerMoniker;
            PipStaticFingerprints = pipStaticFingerprints;
        }

        #endregion Constructors

        #region Queries

        /// <summary>
        /// Retrieves the latest file artifact for the given path.
        /// If there is no such artifact (the path has not been used as an input or output), <see cref="FileArtifact.Invalid" /> is
        /// returned.
        /// </summary>
        /// <remarks>
        /// The graph lock need not be held when calling this method.
        /// </remarks>
        public FileArtifact TryGetLatestFileArtifactForPath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            int existingLatestVersion;
            bool hasExistingVersion = LatestWriteCountsByPath.TryGetValue(path, out existingLatestVersion);
            return hasExistingVersion ? new FileArtifact(path, existingLatestVersion) : FileArtifact.Invalid;
        }

        /// <summary>
        /// If exists, returns a path to a declared output directory containing <paramref name="path"/> and
        /// an indicator of whether that output directory is shared or exclusive.
        /// </summary>
        public Optional<(AbsolutePath path, bool isShared)> TryGetParentOutputDirectory(AbsolutePath path)
        {
            // If there are no output directories, shortcut the search
            if (OutputDirectoryRoots.Count == 0)
            {
                return default;
            }

            // Walk the parent directories of the path to find if it is under a shared opaque directory.
            foreach (var current in Context.PathTable.EnumerateHierarchyBottomUp(path.Value))
            {
                var currentPath = new AbsolutePath(current);
                if (OutputDirectoryRoots.TryGetValue(currentPath, out (bool isSharedOpaque, HashSet<DirectoryArtifact> artifacts) tuple))
                {
                    return new Optional<(AbsolutePath path, bool isShared)>((currentPath, tuple.isSharedOpaque));
                }
            }

            return default;
        }

        /// <summary>
        /// Returns whether there is (by walking the path upwards) an output directory -shared or exclusive- containing <paramref name="path"/>.
        /// If there is, the directory kind is returned via the <paramref name="isItUnderSharedOpaque"/> out parameter.
        /// </summary>
        public bool IsPathUnderOutputDirectory(AbsolutePath path, out bool isItUnderSharedOpaque)
        {
            var result = TryGetParentOutputDirectory(path);
            isItUnderSharedOpaque = result.HasValue && result.Value.isShared;
            return result.HasValue;
        }

        protected IEnumerable<Pip> HydratePips(IEnumerable<PipId> pipIds, PipQueryContext context)
        {
            // no locking needed here
            foreach (PipId pipId in pipIds)
            {
                Pip pip = PipTable.HydratePip(pipId, context);
                yield return pip;
            }
        }

        protected IEnumerable<Pip> HydratePips(IEnumerable<NodeId> nodeIds, PipQueryContext context)
        {
            // no locking needed here
            foreach (NodeId nodeId in nodeIds)
            {
                Pip pip = PipTable.HydratePip(nodeId.ToPipId(), context);
                yield return pip;
            }
        }

        /// <inheritdoc />
        Pip IQueryablePipDependencyGraph.HydratePip(PipId pipId, PipQueryContext queryContext) =>
            PipTable.HydratePip(pipId, queryContext);

        /// <inheritdoc />
        bool IQueryablePipDependencyGraph.IsReachableFrom(PipId from, PipId to) =>
            IsReachableFrom(from.ToNodeId(), to.ToNodeId());

        /// <summary>
        /// Performs a reachability check between two nodes.
        /// </summary>
        public bool IsReachableFrom(NodeId from, NodeId to)
        {
            if (from == PipId.DummyHashSourceFilePipId.ToNodeId() || to == PipId.DummyHashSourceFilePipId.ToNodeId())
            {
                return false;
            }

            return DataflowGraph.IsReachableFrom(from, to, skipOutOfOrderNodes: true);
        }

        /// <inheritdoc />
        public RewritePolicy GetRewritePolicy(PipId pipId) => PipTable.GetRewritePolicy(pipId);

        /// <inheritdoc />
        public string GetFormattedSemiStableHash(PipId pipId) => PipTable.GetFormattedSemiStableHash(pipId);

        /// <inheritdoc />
        public AbsolutePath GetProcessExecutablePath(PipId pipId) => PipTable.GetProcessExecutablePath(pipId);

        /// <inheritdoc />
        public DirectoryArtifact TryGetSealSourceAncestor(AbsolutePath path)
        {
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

        private NodeId TryFindContainingExclusiveOpaqueOutputDirectory(AbsolutePath filePath)
        {
            for (var path = filePath.GetParent(Context.PathTable); path.IsValid; path = path.GetParent(Context.PathTable))
            {
                if (OutputDirectoryProducers.TryGetValue(DirectoryArtifact.CreateWithZeroPartialSealId(path), out var producer))
                {
                    return producer;
                }
            }

            return NodeId.Invalid;
        }

        /// <inheritdoc />
        public PipId TryFindContainingExclusiveOpaqueOutputDirectoryProducer(AbsolutePath filePath)
        {
            var producer = TryFindContainingExclusiveOpaqueOutputDirectory(filePath);
            return producer.IsValid ? producer.ToPipId() : PipId.Invalid;
        }

        /// <inheritdoc />
        public bool TryGetTempDirectoryAncestor(AbsolutePath path, out Pip pip, out AbsolutePath tempPath)
        {
            foreach (var current in Context.PathTable.EnumerateHierarchyBottomUp(path.Value))
            {
                var currentDirectory = new AbsolutePath(current);
                if (TemporaryPaths.TryGetValue(currentDirectory, out var pipId))
                {
                    pip = PipTable.HydratePip(pipId, PipQueryContext.PipGraphRetrieveAllPips);
                    tempPath = currentDirectory;
                    return true;
                }
            }

            pip = null;
            tempPath = AbsolutePath.Invalid;
            return false;
        }

        /// <inheritdoc />
        public Pip GetSealedDirectoryPip(DirectoryArtifact directoryArtifact, PipQueryContext queryContext) =>
            PipTable.HydratePip(GetSealedDirectoryNode(directoryArtifact).ToPipId(), queryContext);

        /// <summary>
        /// Attempts to get the sealed-directory node from representation-specific storage.
        /// </summary>
        protected abstract bool TryGetSealedDirectoryNode(DirectoryArtifact directoryArtifact, out NodeId nodeId);

        /// <summary>
        /// Attempts to get a directory producer from representation-specific storage.
        /// </summary>
        protected abstract bool TryGetDirectoryProducer(DirectoryArtifact directoryArtifact, out NodeId nodeId);

        /// <summary>
        /// Attempts to find a producer node for a path relative to an optional dependency ordering.
        /// </summary>
        public PipId? TryFindProducerPipId(
            AbsolutePath path,
            VersionDisposition versionDisposition,
            DependencyOrderingFilter? maybeOrderingFilter,
            bool includeFilesUnderExclusiveOpaques = false)
        {
            Contract.Assume(path.IsValid);

            NodeId opaqueDirectoryProducer = includeFilesUnderExclusiveOpaques ? TryFindContainingExclusiveOpaqueOutputDirectory(path) : NodeId.Invalid;

            PipId matchedPipId;
            if (!maybeOrderingFilter.HasValue)
            {
                if (opaqueDirectoryProducer.IsValid)
                {
                    matchedPipId = opaqueDirectoryProducer.ToPipId();
                }
                else if (versionDisposition == VersionDisposition.Latest)
                {
                    FileArtifact artifact = TryGetLatestFileArtifactForPath(path);
                    if (!artifact.IsValid)
                    {
                        return null;
                    }

                    matchedPipId = PipProducers[artifact].ToPipId();
                }
                else
                {
                    Contract.Assert(versionDisposition == VersionDisposition.Earliest);
                    NodeId producerNode = TryGetOriginalProducerForPath(path);
                    if (!producerNode.IsValid)
                    {
                        return null;
                    }

                    matchedPipId = producerNode.ToPipId();
                }
            }
            else
            {
                DependencyOrderingFilter orderingFilter = maybeOrderingFilter.Value;
                Contract.Assert(orderingFilter.Reference != null);

                NodeId originalProducerNode = opaqueDirectoryProducer;
                switch (orderingFilter.Filter)
                {
                    case DependencyOrderingFilterType.PossiblyPrecedingInWallTime:
                        {
                            if (!originalProducerNode.IsValid)
                            {
                                originalProducerNode = TryGetOriginalProducerForPath(path);
                            }

                            if (!originalProducerNode.IsValid)
                            {
                                return null;
                            }

                            var referenceNode = orderingFilter.Reference.PipId.ToNodeId();

                            if (IsReachableFrom(referenceNode, originalProducerNode))
                            {
                                return null;
                            }

                            matchedPipId = originalProducerNode.ToPipId();
                        }

                        break;
                    case DependencyOrderingFilterType.Concurrent:
                        {
                            FileArtifact latestArtifact = TryGetLatestFileArtifactForPath(path);
                            var referenceNode = orderingFilter.Reference.PipId.ToNodeId();

                            if (!latestArtifact.IsValid)
                            {
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
                                    var thisArtifact = new FileArtifact(path, rewriteCount);
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
                                        matchedPipId = producerNodeId.ToPipId();
                                        break;
                                    }
                                }
                            }
                        }

                        break;
                    case DependencyOrderingFilterType.OrderedBefore:
                        {
                            if (!originalProducerNode.IsValid)
                            {
                                originalProducerNode = TryGetOriginalProducerForPath(path);
                            }

                            if (!originalProducerNode.IsValid)
                            {
                                return null;
                            }

                            var referenceNode = orderingFilter.Reference.PipId.ToNodeId();

                            if (!IsReachableFrom(originalProducerNode, referenceNode))
                            {
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
        public Pip TryFindProducer(
            AbsolutePath path,
            VersionDisposition versionDisposition,
            DependencyOrderingFilter? orderingFilter = null,
            bool includeFilesUnderExclusiveOpaques = false)
        {
            var pipId = TryFindProducerPipId(path, versionDisposition, orderingFilter, includeFilesUnderExclusiveOpaques);
            if (!pipId.HasValue)
            {
                return null;
            }

            return pipId.Value == PipId.DummyHashSourceFilePipId
                ? new HashSourceFile(FileArtifact.CreateSourceFile(path))
                : PipTable.HydratePip(pipId.Value, PipQueryContext.PipGraphTryFindProducer);
        }

        /// <summary>
        /// Attempts to get the producer of an artifact.
        /// </summary>
        public PipId TryGetProducer(in FileOrDirectoryArtifact artifact)
        {
            if (artifact.IsFile)
            {
                return PipProducers.TryGetValue(artifact.FileArtifact, out var fileProducer)
                    ? fileProducer.ToPipId()
                    : PipId.Invalid;
            }

            return TryGetDirectoryProducer(artifact.DirectoryArtifact, out var directoryProducer)
                ? directoryProducer.ToPipId()
                : PipId.Invalid;
        }

        /// <summary>
        /// Gets the producer of an artifact.
        /// </summary>
        public PipId GetProducer(in FileOrDirectoryArtifact artifact)
        {
            var producer = TryGetProducer(artifact);
            Contract.Assert(producer.IsValid);
            return producer;
        }

        /// <summary>
        /// Gets the clients of a service pip.
        /// </summary>
        public IEnumerable<Pip> GetServicePipClients(PipId servicePipId)
        {
            if (!ServicePipClients.TryGetValue(servicePipId, out var clients))
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

        /// <summary>
        /// Gets all service pip IDs.
        /// </summary>
        public IEnumerable<PipId> GetServicePipIds() => ServicePipClients.Keys;

        /// <summary>
        /// Gets pips producing a path.
        /// </summary>
        public IEnumerable<Pip> GetProducingPips(AbsolutePath filePath) =>
            HydratePips(
                PipProducers.Where(kvp => kvp.Key.Path == filePath).Select(kvp => kvp.Value),
                PipQueryContext.PipGraphGetProducingPips);

        /// <summary>
        /// Gets pips consuming a directory artifact.
        /// </summary>
        public IEnumerable<Pip> GetConsumingPips(DirectoryArtifact directory)
        {
            var producer = GetProducer(directory);
            return HydratePips(
                    DataflowGraph.GetOutgoingEdges(producer.ToNodeId()).Select(edge => edge.OtherNode),
                    PipQueryContext.PipGraphGetConsumingPips)
                .Where(
                    pip => (pip is Process process && process.DirectoryDependencies.Contains(directory)) ||
                           (pip is SealDirectory sealDirectory && sealDirectory.Directory == directory));
        }

        /// <summary>
        /// Gets pips consuming a path.
        /// </summary>
        public IEnumerable<Pip> GetConsumingPips(AbsolutePath filePath)
        {
            var consumers = new HashSet<NodeId>();
            var artifact = FileArtifact.CreateSourceFile(filePath);

            while (true)
            {
                if (PipProducers.TryGetValue(artifact, out var producer))
                {
                    foreach (var edge in DataflowGraph.GetOutgoingEdges(producer))
                    {
                        consumers.Add(edge.OtherNode);
                    }
                }
                else if (!artifact.IsSourceFile)
                {
                    break;
                }

                artifact = artifact.CreateNextWrittenVersion();
            }

            return HydratePips(consumers, PipQueryContext.PipGraphGetConsumingPips)
                .Where(pip => IsInput(filePath, pip));
        }

        private static bool IsInput(AbsolutePath path, FileArtifact artifact, bool isInput) =>
            path == artifact.Path && (isInput || artifact.RewriteCount > 1);

        private static bool IsInput(AbsolutePath path, IEnumerable<FileArtifact> artifacts, bool isInput) =>
            artifacts.Any(artifact => IsInput(path, artifact, isInput));

        private static bool IsInput(AbsolutePath path, Pip pip)
        {
            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    var copyFile = (CopyFile)pip;
                    return IsInput(path, copyFile.Source, isInput: true) ||
                           IsInput(path, copyFile.Destination, isInput: false);
                case PipType.WriteFile:
                    return IsInput(path, ((WriteFile)pip).Destination, isInput: false);
                case PipType.Process:
                    var process = (Process)pip;
                    return IsInput(path, process.Dependencies, isInput: true) ||
                           IsInput(path, process.GetOutputs(), isInput: false);
                case PipType.SealDirectory:
                    return IsInput(path, ((SealDirectory)pip).Contents, isInput: true);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns whether a numeric pip identifier is present.
        /// </summary>
        public bool CanGetPipFromUInt32(uint value) => DataflowGraph.ContainsNode(new NodeId(value));

        /// <summary>
        /// Gets a pip from its numeric identifier.
        /// </summary>
        public Pip GetPipFromUInt32(uint value)
        {
            Contract.Requires(CanGetPipFromUInt32(value));
            return PipTable.HydratePip(new PipId(value), PipQueryContext.PipGraphGetPipFromUInt32);
        }

        /// <summary>
        /// Gets a pip from its identifier.
        /// </summary>
        public Pip GetPipFromPipId(PipId pipId) =>
            PipTable.HydratePip(pipId, PipQueryContext.PipGraphGetPipFromUInt32);

        /// <summary>
        /// Attempts to get the pip representing a module.
        /// </summary>
        public bool TryGetModulePip(ModuleId moduleId, out PipId pipId)
        {
            if (Modules.TryGetValue(moduleId, out var nodeId))
            {
                pipId = nodeId.ToPipId();
                return true;
            }

            pipId = PipId.Invalid;
            return false;
        }

        /// <summary>
        /// Attempts to get the fingerprint of a pip.
        /// </summary>
        public bool TryGetPipFingerprint(in PipId pipId, out ContentFingerprint fingerprint) =>
            PipStaticFingerprints.TryGetFingerprint(pipId, out fingerprint);

        /// <summary>
        /// Attempts to get a pip from its fingerprint.
        /// </summary>
        public bool TryGetPipFromFingerprint(in ContentFingerprint fingerprint, out PipId pipId) =>
            PipStaticFingerprints.TryGetPip(fingerprint, out pipId);

        /// <summary>
        /// Returns whether an artifact must remain writable.
        /// </summary>
        public bool MustArtifactRemainWritable(in FileOrDirectoryArtifact artifact)
        {
            Contract.Requires(artifact.IsValid);
            return PipTable.MustOutputsRemainWritable(GetProducer(artifact));
        }

        /// <summary>
        /// Returns whether an artifact is a preserved output.
        /// </summary>
        public bool IsPreservedOutputArtifact(in FileOrDirectoryArtifact artifact, int preserveOutputTrustLevel)
        {
            Contract.Requires(artifact.IsValid);

            if (artifact.IsFile && artifact.FileArtifact.IsSourceFile)
            {
                return false;
            }

            var pipId = GetProducer(artifact);
            if (!PipTable.IsPreservedOutputsPip(pipId) ||
                PipTable.GetProcessPreserveOutputsTrustLevel(pipId) < preserveOutputTrustLevel)
            {
                return false;
            }

            if (!PipTable.HasPreserveOutputAllowlist(pipId))
            {
                return true;
            }

            var process = PipTable.HydratePip(pipId, PipQueryContext.PreserveOutput) as Process;
            return PipArtifacts.IsPreservedOutputByPip(process, artifact.Path, Context.PathTable, preserveOutputTrustLevel);
        }

        /// <summary>
        /// Returns whether a path is part of the graph.
        /// </summary>
        public bool IsPathInBuild(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return TryGetOriginalProducerForPath(path).IsValid;
        }

        /// <summary>
        /// Returns whether a path is part of the graph or protected from scrubbing.
        /// </summary>
        public bool IsPathInBuildOrShouldNotBeScrubbed(AbsolutePath path) =>
            IsPathInBuild(path) || SourceSealedDirectoryRoots.ContainsKey(path);

        /// <summary>
        /// Returns whether a pip rewrites an input.
        /// </summary>
        public bool IsRewritingPip(PipId pipId) => RewritingPips.Contains(pipId);

        /// <summary>
        /// Returns whether a pip has an output rewritten.
        /// </summary>
        public bool IsRewrittenPip(PipId pipId) => RewrittenPips.Contains(pipId);

        /// <summary>
        /// Gets the unique dependency node IDs (incoming edges) for a given node.
        /// </summary>
        private HashSet<NodeId> GetDependencyNodeIds(NodeId node)
        {
            // We use a hash set since there may be multiple edges (light and heavy)
            // between two pips A and B.
            var nodeIds = new HashSet<NodeId>();

            foreach (Edge edge in DataflowGraph.GetIncomingEdges(node))
            {
                nodeIds.Add(edge.OtherNode);
            }

            return nodeIds;
        }

        /// <summary>
        /// Returns the direct dependencies of the given pip as a collection of pip ids.
        /// The result is sorted by pip id to guarantee deterministic ordering regardless of
        /// graph construction order.
        /// </summary>
        /// <remarks>
        /// The result is deterministic for a given build graph, as pips are sorted by their pip id. This is meant to be consumed
        /// by DScript ambients that must always be deterministic.
        /// </remarks>
        public IEnumerable<PipId> GetPipDependenciesSorted(PipId pipId)
        {
            var nodeIds = GetDependencyNodeIds(pipId.ToNodeId());
            var result = new PipId[nodeIds.Count];
            int i = 0;
            foreach (var nodeId in nodeIds)
            {
                result[i++] = nodeId.ToPipId();
            }

            // The dataflow graph (used by GetDependencyNodeIds) does not gurantee any determinism in the order
            // in which pips are returned.
            Array.Sort(result, (a, b) => a.Value.CompareTo(b.Value));
            return result;
        }

        /// <summary>
        /// Gets pips that are dependencies of this node (incoming edges).
        /// </summary>
        /// <param name="node">Node's Id</param>
        /// <returns>List of predecessor pips</returns>
        private IEnumerable<Pip> GetPipDependenciesOfNode(NodeId node)
        {
            return HydratePips(GetDependencyNodeIds(node), PipQueryContext.PipGraphGetPipDependenciesOfNode);
        }

        /// <summary>
        /// Gets pips that are dependent upon this node (outgoing edges).
        /// </summary>
        /// <param name="node">Node's Id</param>
        /// <returns>List of successor nodes</returns>
        private IEnumerable<Pip> GetPipsDependentUponNode(NodeId node)
        {
            // We use a hash set here since there may be multiple edges (light and heavy)
            // between two pips A and B.
            var nodeIds = new HashSet<NodeId>();

            foreach (Edge edge in DataflowGraph.GetOutgoingEdges(node))
            {
                nodeIds.Add(edge.OtherNode);
            }

            return HydratePips(nodeIds, PipQueryContext.PipGraphGetPipsDependentUponNode);
        }

        /// <summary>
        /// Retrieves the producing node for the original file artifact for the given path (the one with the lowest version)
        /// If there is no such artifact (the path has not been used as an input or output), <see cref="NodeId.Invalid" /> is
        /// returned.
        /// </summary>
        /// <remarks>
        /// The graph lock need not be held when calling this method.
        /// </remarks>
        protected NodeId TryGetOriginalProducerForPath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            NodeId node;

            DirectoryArtifact directoryAsSource = DirectoryArtifact.CreateWithZeroPartialSealId(path);
            if (OutputDirectoryProducers.TryGetValue(directoryAsSource, out node))
            {
                return node;
            }

            FileArtifact pathAsSource = FileArtifact.CreateSourceFile(path);
            if (PipProducers.TryGetValue(pathAsSource.CreateNextWrittenVersion(), out node))
            {
                return node;
            }
            else if (PipProducers.TryGetValue(pathAsSource, out node))
            {
                return node;
            }

            return NodeId.Invalid;
        }

        /// <summary>
        /// Returns the pip data associated with a file (if applicable).
        /// </summary>
        public PipData QueryFileArtifactPipData(FileArtifact artifact)
        {
            NodeId nodeId;
            if (!PipProducers.TryGetValue(artifact, out nodeId))
            {
                Contract.Assume(false, "Unable to find pip producer for file");
            }

            PipId pipId = nodeId.ToPipId();
            PipType pipType = PipTable.GetPipType(pipId);
            Contract.Assume(pipId.IsValid, "Unable to find pip producer for file");

            switch (pipType)
            {
                case PipType.WriteFile:
                    {
                        Pip pip = PipTable.HydratePip(pipId, PipQueryContext.PipGraphQueryFileArtifactPipDataWriteFile);
                        return ((WriteFile)pip).Contents;
                    }

                case PipType.CopyFile:
                    {
                        Pip pip = PipTable.HydratePip(pipId, PipQueryContext.PipGraphQueryFileArtifactPipDataCopyFile);
                        return QueryFileArtifactPipData(((CopyFile)pip).Source);
                    }
            }

            return PipData.Invalid;
        }

        /// <summary>
        /// If there exists any artifact with a path that is a prefix of the given path (or equal to the given path),
        /// returns one such path. No particular precedence is guaranteed.
        /// </summary>
        /// <remarks>
        /// The graph lock need not be held.
        /// </remarks>
        public FileOrDirectoryArtifact TryGetAnyArtifactWithPathPrefix(AbsolutePath prefix)
        {
            Contract.Requires(prefix.IsValid);

            // First we establish the existence of a path with this prefix.
            // This is wise since we can avoid the GraphLock for path -> producer lookups,
            // but presently not for path -> latest file artifact lookups.
            AbsolutePath producedPath = prefix;
            NodeId producer = TryGetOriginalProducerForPath(prefix);
            if (!producer.IsValid)
            {
                foreach (HierarchicalNameId descendantOfPrefix in Context.PathTable.EnumerateHierarchyTopDown(prefix.Value))
                {
                    producedPath = new AbsolutePath(descendantOfPrefix);
                    producer = TryGetOriginalProducerForPath(producedPath);
                    if (producer.IsValid)
                    {
                        break;
                    }
                }
            }

            if (producer.IsValid)
            {
                DirectoryArtifact directoryArtifact = TryGetDirectoryArtifactForPath(producedPath);
                if (directoryArtifact.IsValid)
                {
                    return FileOrDirectoryArtifact.Create(directoryArtifact);
                }

                FileArtifact fileArtifact = TryGetLatestFileArtifactForPath(producedPath);

                // We want to have at least one valid artifact per producer
                Contract.Assume(fileArtifact.IsValid, "We found a producer, so we should find an artifact.");
                return FileOrDirectoryArtifact.Create(fileArtifact);
            }

            return FileOrDirectoryArtifact.Invalid;
        }

        /// <summary>
        /// Tries to get the producer's node of a given file.
        /// </summary>
        public bool TryGetProducerNode(FileArtifact fileArtifact, out NodeId nodeId) => PipProducers.TryGetValue(fileArtifact, out nodeId);

        /// <summary>
        /// Gets the producer's node of a given file.
        /// </summary>
        public NodeId GetProducerNode(FileArtifact sourceArtifact) => PipProducers[sourceArtifact];

        /// <summary>
        /// Gets the node id for the sealed directory corresponding to the directory artifact
        /// </summary>
        public abstract NodeId GetSealedDirectoryNode(DirectoryArtifact directoryArtifact);

        /// <summary>
        /// Lists the contents of a sealed directory. The artifact may refer to a partial or fully-sealed directory.
        /// </summary>
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealedDirectoryContents(
            DirectoryArtifact directoryArtifact)
        {
            NodeId nodeId = GetSealedDirectoryNode(directoryArtifact);

            var sealedDirectory = (SealDirectory)PipTable.HydratePip(
                nodeId.ToPipId(),
                PipQueryContext.PipGraphListSealedDirectoryContents);

            return sealedDirectory.Contents;
        }

        /// <summary>
        /// Enumerates the immediate children of a given path.
        /// This acts upon the filesystem view as defined by all statically-defined file artifacts.
        /// (any returned path corresponds to an artifact path, or a prefix of an artifact path).
        /// </summary>
        /// <remarks>
        /// The graph lock need not be held.
        /// </remarks>
        public ImmediateChildPathEnumerator EnumerateImmediateChildPaths(AbsolutePath prefix)
        {
            Contract.Requires(prefix.IsValid);
            return new ImmediateChildPathEnumerator(this, prefix);
        }

        /// <summary>
        /// Enumerator for visiting the immediate children of a given path.
        /// This enumerator acts upon the filesystem view as defined by all statically-defined file artifacts.
        /// (any returned path corresponds to an artifact path, or a prefix of an artifact path).
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
        public struct ImmediateChildPathEnumerator : IEnumerator<AbsolutePath>, IEnumerable<AbsolutePath>
        {
            private readonly PipGraphBase m_graph;
            private HierarchicalNameTable.ImmediateChildEnumerator m_innerNameEnumerator;

            internal ImmediateChildPathEnumerator(PipGraphBase graph, AbsolutePath prefix)
            {
                Contract.Requires(graph != null);
                Contract.Requires(prefix.IsValid);
                m_graph = graph;
                m_innerNameEnumerator = graph.Context.PathTable.EnumerateImmediateChildren(prefix.Value);
            }

            /// <inheritdoc />
            public AbsolutePath Current => new AbsolutePath(m_innerNameEnumerator.Current);

            /// <inheritdoc />
            public void Dispose()
            {
            }

            /// <inheritdoc />
            object IEnumerator.Current => Current;

            /// <inheritdoc />
            public bool MoveNext()
            {
                while (m_innerNameEnumerator.MoveNext())
                {
                    var currentPath = new AbsolutePath(m_innerNameEnumerator.Current);

                    if (m_graph.TryGetAnyArtifactWithPathPrefix(currentPath).IsValid)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                throw new NotSupportedException();
            }

            /// <nodoc />
            public ImmediateChildPathEnumerator GetEnumerator()
            {
                return this;
            }

            IEnumerator<AbsolutePath> IEnumerable<AbsolutePath>.GetEnumerator()
            {
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return this;
            }
        }

        /// <summary>
        /// Checks if a pip id belongs to a pip with succeed-fast property.
        /// </summary>
        public bool IsSucceedFast(PipId pipId) => PipTable.IsSucceedFast(pipId);

        #endregion Queries

        #region IPipScheduleTraversal Members

        /// <summary>
        /// Retrieves all pips that have been scheduled
        /// NOTE: Excludes meta pips.
        /// </summary>
        public virtual IEnumerable<Pip> RetrieveScheduledPips()
        {
            foreach (PipId pipId in PipTable.Keys)
            {
                if (!PipTable.GetPipType(pipId).IsMetaPip())
                {
                    yield return PipTable.HydratePip(pipId, PipQueryContext.PipGraphRetrieveScheduledPips);
                }
            }
        }

        /// <inheritdoc />
        public virtual IReadOnlyCollection<KeyValuePair<DirectoryArtifact, HashSet<FileArtifact>>> RetrieveOutputsUnderOpaqueExistenceAssertions()
        {
            return OutputsUnderOpaqueExistenceAssertions;
        }

        public virtual IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip)
        {
            var nodeId = pip.PipId.ToNodeId();
            return GetPipDependenciesOfNode(nodeId);
        }

        public virtual IEnumerable<PipReference> RetrievePipReferenceImmediateDependencies(PipId pipId, PipType? pipType)
        {
            foreach (Edge edge in DataflowGraph.GetIncomingEdges(pipId.ToNodeId()))
            {
                var otherPipId = edge.OtherNode.ToPipId();
                if (pipType.HasValue)
                {
                    // If we are filtering by pipType, do a check and skip other pips
                    if (PipTable.GetPipType(otherPipId) != pipType.Value)
                    {
                        continue;
                    }
                }

                yield return new PipReference(PipTable, otherPipId, PipQueryContext.PipGraphGetPipDependenciesOfNode);
            }
        }

        public virtual IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip)
        {
            var nodeId = pip.PipId.ToNodeId();
            return GetPipsDependentUponNode(nodeId);
        }

        public virtual IEnumerable<PipReference> RetrievePipReferenceImmediateDependents(PipId pipId, PipType? pipType = null)
        {
            foreach (Edge edge in DataflowGraph.GetOutgoingEdges(pipId.ToNodeId()))
            {
                var otherPipId = edge.OtherNode.ToPipId();
                if (pipType.HasValue)
                {
                    // If we are filtering by pipType, do a check and skip other pips
                    if (PipTable.GetPipType(otherPipId) != pipType.Value)
                    {
                        continue;
                    }
                }

                yield return new PipReference(PipTable, otherPipId, PipQueryContext.PipGraphGetPipsDependentUponNode);
            }
        }

        public DirectoryArtifact TryGetDirectoryArtifactForPath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            NodeId nodeId;
            DirectoryArtifact artifact = DirectoryArtifact.CreateWithZeroPartialSealId(path);
            if (OutputDirectoryProducers.TryGetValue(artifact, out nodeId))
            {
                return artifact;
            }

            return DirectoryArtifact.Invalid;
        }

        public IReadOnlySet<FileArtifact> GetExistenceAssertionsUnderOpaqueDirectory(DirectoryArtifact directoryArtifact)
        {
            var result = OutputsUnderOpaqueExistenceAssertions.TryGet(directoryArtifact);
            if (!result.IsFound)
            {
                return CollectionUtilities.EmptySet<FileArtifact>();
            }

            return result.Item.Value.ToReadOnlySet();
        }

        public int PipCount => PipTable.Count;

        #endregion
    }
}
