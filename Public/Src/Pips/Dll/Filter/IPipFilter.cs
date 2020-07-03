// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Pips.Filter
{

    /// <summary>
    /// The context in which a filter is applied
    /// </summary>
    public interface IPipFilterContext
    {
        /// <summary>
        /// The path table
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        PathTable PathTable { get; }

        /// <summary>
        /// All known pips
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        IList<PipId> AllPips { get; }

        /// <summary>
        /// Materializes pip (expensive)
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        Pip HydratePip(PipId pipId);

        /// <summary>
        /// Obtains the type of a pip
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        PipType GetPipType(PipId pipId);

        /// <summary>
        /// Get the semi stable hash of a pip
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        long GetSemiStableHash(PipId pipId);

        /// <summary>
        /// Gets all dependencies of a pip
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        IEnumerable<PipId> GetDependencies(PipId pipId);

        /// <summary>
        /// Gets all dependents of a pip
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        IEnumerable<PipId> GetDependents(PipId pipId);

        /// <summary>
        /// Gets the producer for the file or directory
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        PipId GetProducer(in FileOrDirectoryArtifact fileOrDirectory);

        /// <summary>
        /// Gets cached filtered outputs.
        /// </summary>
        bool TryGetCachedOutputs(PipFilter pipFilter, out IReadOnlySet<FileOrDirectoryArtifact> outputs);

        /// <summary>
        /// Caches filtered outputs.
        /// </summary>
        void CacheOutputs(PipFilter pipFilter, IReadOnlySet<FileOrDirectoryArtifact> outputs);
    }
}
