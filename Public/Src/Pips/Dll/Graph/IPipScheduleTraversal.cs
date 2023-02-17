// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Traversal for scheduled pips
    /// </summary>
    public interface IPipScheduleTraversal
    {
        /// <summary>
        /// Retrieves all pips that have been scheduled
        /// </summary>
        [NotNull]
        IEnumerable<Pip> RetrieveScheduledPips();

        /// <summary>
        /// Retrieves the immediate dependencies of a pip
        /// </summary>
        [NotNull]
        IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip);

        /// <summary>
        /// Retrieves the immediate dependents of a pip
        /// </summary>
        [NotNull]
        IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip);

        /// <summary>
        /// Gets the files that are asserted to exist under a directory artifact.
        /// </summary>
        /// <remarks>
        /// Currently this is only used in the context of pip graph fragments
        ///   so that a fragment can assert a file exists in a directory which originates in another fragment.
        /// Each of these file artifacts, upon deserialization from the fragment,
        ///   need to be added to the graph after the directory they come from is added and before the file is used.
        /// Since the assertions are loaded before any pips from the fragment, only directories in other fragments can be specified.
        /// Future work can load the assertions in topological order with the pips that reference them so they can reference a directory created in the same fragment.
        /// </remarks>
        IReadOnlyCollection<KeyValuePair<DirectoryArtifact, HashSet<FileArtifact>>> RetrieveOutputsUnderOpaqueExistenceAssertions();

        /// <summary>
        /// The count of pips in the graph
        /// </summary>
        int PipCount { get; }
    }
}
