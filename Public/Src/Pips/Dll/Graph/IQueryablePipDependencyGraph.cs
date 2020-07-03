// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Filtered query operations on a dependency graph of pips.
    /// Note that these operations may be moderately expensive due to requiring graph reachability checks.
    /// </summary>
    public interface IQueryablePipDependencyGraph
    {
        /// <summary>
        /// Retrieves the underlying directed graph.
        /// </summary>
        [NotNull]
        IReadonlyDirectedGraph DirectedGraph { get; }

        /// <summary>
        /// Tries to find a producer of the given path (i.e., artifact with the highest rewrite count) that
        /// satisfies an optional ordering filter. Returns null if a corresponding pip is not found.
        /// </summary>
        Pip TryFindProducer(AbsolutePath producedPath, VersionDisposition versionDisposition, DependencyOrderingFilter? orderingFilter = null);

        /// <summary>
        /// Hydrates the pip from the pip id.
        /// </summary>
        Pip HydratePip(PipId pipId, PipQueryContext queryContext);

        /// <summary>
        /// Returns the first (by walking the path upwards) source seal directory containing <paramref name="path"/> 
        /// or <see cref="DirectoryArtifact.Invalid"/> if there is no such container
        /// </summary>
        /// <remarks>
        /// If there are multiple source sealed directories sharing the same root, an arbitrary one is returned.
        /// </remarks>
        DirectoryArtifact TryGetSealSourceAncestor(AbsolutePath path);

        /// <summary>
        /// Tries to find if the given path is under a temporary directory
        /// </summary>
        bool TryGetTempDirectoryAncestor(AbsolutePath path, out Pip pip, out AbsolutePath temPath);

        /// <summary>
        /// Returns the seal directory pip that corresponds to the given directory artifact
        /// </summary>
        Pip GetSealedDirectoryPip(DirectoryArtifact directoryArtifact, PipQueryContext queryContext);

        /// <summary>
        /// Returns whether pip <paramref name="to"/> is reachable from pip <paramref name="from"/> 
        /// following the data dependency graph.  In other words, if 'PipTo' is reachable from 'PipFrom',
        /// that ensures that 'PipTo' is always executed after 'PipFrom'.
        /// </summary>
        bool IsReachableFrom(PipId from, PipId to);
    }
}
