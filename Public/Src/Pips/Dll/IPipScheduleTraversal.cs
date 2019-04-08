// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using JetBrains.Annotations;

namespace BuildXL.Pips
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
        /// The count of pips in the graph
        /// </summary>
        int PipCount { get; }
    }
}
