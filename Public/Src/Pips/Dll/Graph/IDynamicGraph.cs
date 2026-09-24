// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Operations;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Append-only pip graph surface available while graph construction and scheduling overlap.
    /// </summary>
    /// <remarks>
    /// This intentionally omits finalized-graph identity, counts, node ranges, filtering, and whole-graph enumeration.
    /// Queries over outgoing edges return the dependents known at the time of the call; future pips may add more.
    /// Besides the general change in semantics where now all queries represent a snapshot of the graph at the time of the call, the dynamic-specific behavior
    /// is represented by <see cref="ReadPipAdmissionsAsync(CancellationToken)"/>
    /// </remarks>
    public interface IDynamicGraph : IPipGraphFileSystemView, IQueryablePipDependencyGraph
    {
        /// <summary>
        /// Pip table containing the pips admitted so far.
        /// </summary>
        IPipTable PipTable { get; }

        /// <summary>
        /// Expander used for configuration-independent path rendering.
        /// </summary>
        SemanticPathExpander SemanticPathExpander { get; }

        /// <summary>
        /// BuildXL API server moniker, if one has been requested so far.
        /// </summary>
        StringId ApiServerMoniker { get; }

        /// <summary>
        /// Produces a notification after each pip and all its incoming edges have been committed.
        /// Notifications committed before enumeration starts must be retained.
        /// </summary>
        IAsyncEnumerable<PipId> ReadPipAdmissionsAsync(CancellationToken cancellationToken);

        /// <summary>Queries pip data associated with a file artifact.</summary>
        PipData QueryFileArtifactPipData(FileArtifact artifact);

        /// <summary>Attempts to retrieve the fingerprint associated with a pip.</summary>
        bool TryGetPipFingerprint(in PipId pipId, out ContentFingerprint fingerprint);

        /// <summary>Returns whether a pip is configured to fail the build immediately.</summary>
        bool IsSucceedFast(PipId pipId);

        /// <summary>Attempts to find the producer of an artifact.</summary>
        PipId TryGetProducer(in FileOrDirectoryArtifact artifact);

        /// <summary>Gets the producer of an artifact.</summary>
        PipId GetProducer(in FileOrDirectoryArtifact artifact);

        /// <summary>Attempts to find the producer node of a file artifact.</summary>
        bool TryGetProducerNode(FileArtifact fileArtifact, out NodeId nodeId);

        /// <summary>Gets the producer node of a file artifact.</summary>
        NodeId GetProducerNode(FileArtifact fileArtifact);

        /// <summary>Gets the node that seals a directory artifact.</summary>
        NodeId GetSealedDirectoryNode(DirectoryArtifact directoryArtifact);

        /// <summary>Returns whether one admitted node is reachable from another.</summary>
        bool IsReachableFrom(NodeId from, NodeId to);

        /// <summary>Gets the admitted client pips of a service pip.</summary>
        IEnumerable<Pip> GetServicePipClients(PipId servicePipId);

        /// <summary>Attempts to find the producer pip for a path.</summary>
        PipId? TryFindProducerPipId(
            AbsolutePath path,
            VersionDisposition versionDisposition,
            DependencyOrderingFilter? orderingFilter,
            bool includeFilesUnderExclusiveOpaques = false);

        /// <summary>Gets the immediate dependencies of a pip.</summary>
        IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip);

        /// <summary>
        /// Returns dependents admitted at the time of the call. The result is not a final dependent closure.
        /// </summary>
        IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip);

        /// <summary>Gets pips producing artifacts at a path.</summary>
        IEnumerable<Pip> GetProducingPips(AbsolutePath filePath);

        /// <summary>
        /// Returns consumers admitted at the time of the call.
        /// </summary>
        IEnumerable<Pip> GetConsumingPips(DirectoryArtifact directory);

        /// <summary>
        /// Returns consumers admitted at the time of the call.
        /// </summary>
        IEnumerable<Pip> GetConsumingPips(AbsolutePath filePath);

        /// <summary>Gets a pip from its identifier.</summary>
        Pip GetPipFromPipId(PipId pipId);

        /// <summary>Returns whether the current table can resolve the given numeric pip identifier.</summary>
        bool CanGetPipFromUInt32(uint value);

        /// <summary>Gets a pip from its numeric identifier.</summary>
        Pip GetPipFromUInt32(uint value);

        /// <summary>Attempts to find the pip representing a module.</summary>
        bool TryGetModulePip(ModuleId moduleId, out PipId pipId);

        /// <summary>Lists the files sealed into a directory artifact.</summary>
        SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealedDirectoryContents(DirectoryArtifact directoryArtifact);

        /// <summary>Returns whether an artifact must remain writable.</summary>
        bool MustArtifactRemainWritable(in FileOrDirectoryArtifact artifact);

        /// <summary>Returns whether an artifact is a preserved output at the specified trust level.</summary>
        bool IsPreservedOutputArtifact(in FileOrDirectoryArtifact artifact, int preserveOutputTrustLevel);

        /// <summary>Returns whether a path is part of the admitted build.</summary>
        bool IsPathInBuild(AbsolutePath path);

        /// <summary>Returns whether a path is part of the build or protected from scrubbing.</summary>
        bool IsPathInBuildOrShouldNotBeScrubbed(AbsolutePath path);

        /// <summary>Attempts to get the directory artifact associated with a path.</summary>
        DirectoryArtifact TryGetDirectoryArtifactForPath(AbsolutePath path);

        /// <summary>Returns whether a pip rewrites an artifact.</summary>
        bool IsRewritingPip(PipId pipId);

        /// <summary>Returns whether a pip's output is rewritten by another pip.</summary>
        bool IsRewrittenPip(PipId pipId);

        /// <summary>Attempts to find a pip by fingerprint.</summary>
        bool TryGetPipFromFingerprint(in ContentFingerprint fingerprint, out PipId pipId);
    }
}
