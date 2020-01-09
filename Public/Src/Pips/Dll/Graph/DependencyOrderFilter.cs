// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;

namespace BuildXL.Pips.Graph
{
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
}
