// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Extends <see cref="IMutablePipGraph"/> to add a method (<see cref="Build"/>) for materializing added pips
    /// into a <see cref="PipGraph"/>.  After <see cref="Build"/> has been called, this builder becomes
    /// immutable; hence no subsequent Add*Pip method calls are allowed.
    /// </summary>
    public interface IPipGraphBuilder : IMutablePipGraph
    {
        /// <summary>
        /// Indicates whether the pip graph has been finalized such that further modifications are not allowed.
        /// </summary>
        [Pure]
        bool IsImmutable { get; }

        /// <summary>
        /// Marks the pip graph as complete and subsequently immutable.
        /// Following this, <see cref="IsImmutable"/> is set.
        /// </summary>
        PipGraph Build();
    }
}
