// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
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
        /// <remarks>
        /// Observe this method is based on the information on the pip graph exclusively, and therefore dynamic outputs (i.e. outputs under an opaque directory) are not considered.
        /// For back compat reasons (some analyzers may depend on this behavior), setting <paramref name="includeFilesUnderExclusiveOpaques"/> will perform a search on exclusive
        /// opaque producers checking whether the root of the opaque includes the given path. This query may result in false positives when the given path was not actually produced.
        /// </remarks>
        Pip TryFindProducer(AbsolutePath path, VersionDisposition versionDisposition, DependencyOrderingFilter? orderingFilter = null, bool includeFilesUnderExclusiveOpaques = false);

        /// <summary>
        /// Hydrates the pip from the pip id.
        /// </summary>
        Pip HydratePip(PipId pipId, PipQueryContext queryContext);

        /// <summary>
        /// Returns the double write policy for a process pip without hydrating the pip
        /// </summary>
        RewritePolicy GetRewritePolicy(PipId pipId);

        /// <summary>
        /// Get a formatted pip semi stable hash without the need to hydrate the pip
        /// </summary>
        string GetFormattedSemiStableHash(PipId pipId);

        /// <summary>
        /// Get the process executable path without the need to hydrate the pip
        /// </summary>
        AbsolutePath GetProcessExecutablePath(PipId pipId);

        /// <summary>
        /// Returns the first (by walking the path upwards) source seal directory containing <paramref name="path"/> 
        /// or <see cref="DirectoryArtifact.Invalid"/> if there is no such container
        /// </summary>
        /// <remarks>
        /// If there are multiple source sealed directories sharing the same root, an arbitrary one is returned.
        /// </remarks>
        DirectoryArtifact TryGetSealSourceAncestor(AbsolutePath path);

        /// <summary>
        /// Returns the first (by walking the path upwards) exclusive opaque directory containing <paramref name="filePath"/> 
        /// or <see cref="PipId.Invalid"/> if there is no such producer
        /// </summary>
        PipId TryFindContainingExclusiveOpaqueOutputDirectoryProducer(AbsolutePath filePath);

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
