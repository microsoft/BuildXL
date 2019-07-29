// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Relative dependency ordering required for a <see cref="DependencyOrderingFilter" />.
    /// </summary>
    public enum DependencyOrderingFilterType
    {
        /// <summary>
        /// The found pip must possibly precede the reference pip in wall-clock time.
        /// This excludes only the case that the found pip is ordered after the reference pip.
        /// </summary>
        PossiblyPrecedingInWallTime,

        /// <summary>
        /// The found pip must be concurrent with the reference pip.
        /// (it may not be ordered after or ordered before the reference pip).
        /// </summary>
        Concurrent,

        /// <summary>
        /// The found pip must be ordered befor the reference pip.
        /// </summary>
        OrderedBefore,
    }

    /// <summary>
    /// Given multiple producers of a path (different versions), indicates if the earliest or latest matching version should be found.
    /// </summary>
    public enum VersionDisposition
    {
        /// <summary>
        /// Prefer earlier versions of the path.
        /// </summary>
        Earliest,

        /// <summary>
        /// Prefer later versions of the path.
        /// </summary>
        Latest,
    }

    /// <summary>
    /// Dependency-based filter for pip queries.
    /// The dependency filter is applied to a potential found pip and the <see cref="Reference" /> pip.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
    public readonly struct DependencyOrderingFilter
    {
        /// <summary>
        /// The pip with respect to which the filter evaluates a query candidate.
        /// </summary>
        public readonly Pip Reference;

        /// <summary>
        /// Dependency constraint between the <see cref="Reference" /> and queried pip.
        /// </summary>
        public readonly DependencyOrderingFilterType Filter;

        /// <nodoc />
        public DependencyOrderingFilter(DependencyOrderingFilterType filter, Pip reference)
        {
            Contract.Requires(reference != null);
            Reference = reference;
            Filter = filter;
        }
    }

    /// <summary>
    /// Filtered query operations on a dependency graph of pips.
    /// Note that these operations may be moderately expensive due to requiring graph reachability checks.
    /// </summary>
    public interface IQueryablePipDependencyGraph
    {
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
