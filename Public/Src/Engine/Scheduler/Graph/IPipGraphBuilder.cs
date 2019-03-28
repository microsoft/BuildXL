// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Extends <see cref="IPipGraph"/> to add a method (<see cref="Build"/>) for materializing added pips
    /// into a <see cref="PipGraph"/>.  After <see cref="Build"/> has been called, this builder becomes
    /// immutable; hence no subsequent Add*Pip method calls are allowed.
    /// </summary>
    public interface IPipGraphBuilder : IPipGraph
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
