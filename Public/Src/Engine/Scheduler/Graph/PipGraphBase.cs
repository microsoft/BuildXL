// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Base class for PipGraph and PipGraph.Builder containing common state which can safely be accessed during graph build (or providing
    /// override functionality to allow locking) and implementing <see cref="IPipScheduleTraversal"/>
    /// </summary>
    public abstract class PipGraphBase : IPipScheduleTraversal, IPipGraphFileSystemView
    {
        #region Context State

        /// <summary>
        /// The container for context objects
        /// </summary>
        public readonly PipExecutionContext Context;

        /// <summary>
        /// Pip table holding all known pips.
        /// </summary>
        public readonly PipTable PipTable;

        /// <summary>
        /// Expander used when a path string should be machine / configuration independent.
        /// </summary>
        public readonly SemanticPathExpander SemanticPathExpander;

        #endregion Context State

        #region Serialized State

        /// <summary>
        /// Supporting data-flow graph.
        /// </summary>
        public readonly DirectedGraph DataflowGraph;

        /// <summary>
        /// Mapping from full symbol and qualifier to value nodes.
        /// </summary>
        /// <remarks>
        /// Maintained by <see cref="PipGraph.Builder.AddOutputValue" />, <see cref="PipGraph.Builder.AddValueDependency" />, and <see cref="PipGraph.Builder.AddValueValueDependency" />
        /// </remarks>
        protected readonly ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId> Values;

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
        /// The value indicates if any of the corresponding output directories is shared opaque.
        /// </summary>
        protected readonly ConcurrentBigMap<AbsolutePath, bool> OutputDirectoryRoots;

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
        /// String id corresponding to the <see cref="BuildXL.Ipc.Interfaces.IIpcMoniker.Id"/> property of the moniker used by the
        /// BuildXL API server; <see cref="StringId.Invalid"/> indicates that no BuildXL API operation has been requested.
        /// </summary>
        public readonly StringId ApiServerMoniker;

        /// <summary>
        /// Pip static fingerprints.
        /// </summary>
        protected readonly PipGraphStaticFingerprints PipStaticFingerprints;

        #endregion Serialized State

        #region Constructors

        protected PipGraphBase(
                PipTable pipTable,
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
            OutputDirectoryRoots = new ConcurrentBigMap<AbsolutePath, bool>();
            CompositeOutputDirectoryProducers = new ConcurrentBigMap<DirectoryArtifact, NodeId>();
            SourceSealedDirectoryRoots = new ConcurrentBigMap<AbsolutePath, DirectoryArtifact>();
            TemporaryPaths = new ConcurrentBigMap<AbsolutePath, PipId>();
            RewritingPips = new ConcurrentBigSet<PipId>();
            RewrittenPips = new ConcurrentBigSet<PipId>();
            LatestWriteCountsByPath = new ConcurrentBigMap<AbsolutePath, int>();
            ApiServerMoniker = StringId.Invalid;
            PipStaticFingerprints = new PipGraphStaticFingerprints();
        }

        /// <summary>
        /// Initialize state from deserializing
        /// </summary>
        protected PipGraphBase(
                PipTable pipTable,
                PipExecutionContext context,
                SemanticPathExpander semanticPathExpander,
                DirectedGraph dataflowGraph,
                ConcurrentBigMap<(FullSymbol, QualifierId, AbsolutePath), NodeId> values,
                ConcurrentBigMap<FileArtifact, NodeId> specFiles,
                ConcurrentBigMap<ModuleId, NodeId> modules,
                ConcurrentBigMap<FileArtifact, NodeId> pipProducers,
                ConcurrentBigMap<DirectoryArtifact, NodeId> outputDirectoryProducers,
                ConcurrentBigMap<AbsolutePath, bool> outputDirectoryRoots,
                ConcurrentBigMap<DirectoryArtifact, NodeId> compositeOutputDirectoryProducers,
                ConcurrentBigMap<AbsolutePath, DirectoryArtifact> sourceSealedDirectoryRoots,
                ConcurrentBigMap<AbsolutePath, PipId> temporaryPaths,
                ConcurrentBigSet<PipId> rewritingPips,
                ConcurrentBigSet<PipId> rewrittenPips,
                ConcurrentBigMap<AbsolutePath, int> latestWriteCountsByPath,
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
            Contract.Requires(outputDirectoryRoots != null);
            Contract.Requires(compositeOutputDirectoryProducers != null);
            Contract.Requires(sourceSealedDirectoryRoots != null);
            Contract.Requires(temporaryPaths != null);
            Contract.Requires(rewritingPips != null);
            Contract.Requires(rewrittenPips != null);
            Contract.Requires(latestWriteCountsByPath != null);
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
            OutputDirectoryRoots = outputDirectoryRoots;
            CompositeOutputDirectoryProducers = compositeOutputDirectoryProducers;
            SourceSealedDirectoryRoots = sourceSealedDirectoryRoots;
            TemporaryPaths = temporaryPaths;
            RewritingPips = rewritingPips;
            RewrittenPips = rewrittenPips;
            LatestWriteCountsByPath = latestWriteCountsByPath;
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
        private Optional<(AbsolutePath path, bool isShared)> TryGetParentOutputDirectory(AbsolutePath path)
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
                if (OutputDirectoryRoots.TryGetValue(currentPath, out bool isSharedOpaque))
                {
                    return new Optional<(AbsolutePath path, bool isShared)>((currentPath, isSharedOpaque));
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
            isItUnderSharedOpaque = result.IsValid && result.Value.isShared;
            return result.IsValid;
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

        /// <summary>
        /// Gets pips that are dependencies of this node (incoming edges).
        /// </summary>
        /// <param name="node">Node's Id</param>
        /// <returns>List of predecessor pips</returns>
        private IEnumerable<Pip> GetPipDependenciesOfNode(NodeId node)
        {
            // We use a hash set here since there may be multiple edges (light and heavy)
            // between two pips A and B.
            var nodeIds = new HashSet<NodeId>();

            foreach (Edge edge in DataflowGraph.GetIncomingEdges(node))
            {
                nodeIds.Add(edge.OtherNode);
            }

            return HydratePips(nodeIds, PipQueryContext.PipGraphGetPipDependenciesOfNode);
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

        public int PipCount => PipTable.Count;

        #endregion
    }

    /// <summary>
    /// View of a filesystem based off of a PipGraph
    /// </summary>
    public interface IPipGraphFileSystemView
    {
        /// <nodoc/>
        FileArtifact TryGetLatestFileArtifactForPath(AbsolutePath path);

        /// <nodoc/>
        bool IsPathUnderOutputDirectory(AbsolutePath path, out bool isItSharedOpaque);
    }
}
