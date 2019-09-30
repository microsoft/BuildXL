// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Root level pip filter
    /// </summary>
    public sealed class RootFilter
    {
        /// <summary>
        /// The pip filter expression
        /// </summary>
        public readonly PipFilter PipFilter;

        /// <summary>
        /// The raw pip filter expression (i.e. not canonicalized and reduced)
        /// </summary>
        public readonly PipFilter RawPipFilter;

        /// <summary>
        /// Whether ValuesToResolve should allow value short circuiting
        /// </summary>
        private bool m_allowValueShortCircuiting = true;

        /// <summary>
        /// The filter expression that was parsed.
        /// </summary>
        /// <remarks>
        /// This is for tracing only and may not be set
        /// </remarks>
        public readonly string FilterExpression = "UNSPECIFIED";

        /// <summary>
        /// True if this is an empty pip filter
        /// </summary>
        public bool IsEmpty => PipFilter is EmptyFilter;

        private readonly Lazy<IReadOnlyList<FullSymbol>> m_valuesToResolve;
        private readonly Lazy<IReadOnlyList<AbsolutePath>> m_pathsRootsToResolve;
        private readonly Lazy<IReadOnlyList<StringId>> m_modulesToResolve;

        /// <summary>
        /// Creates a RootFilter
        /// </summary>
        public RootFilter(PipFilter filter, string filterExpression = null)
        {
            Contract.Requires(filter != null);

            var canonicalizer = new FilterCanonicalizer();
            PipFilter = filter.Canonicalize(canonicalizer);
            RawPipFilter = filter;

            if (filterExpression != null)
            {
                FilterExpression = filterExpression;
            }

            m_valuesToResolve = new Lazy<IReadOnlyList<FullSymbol>>(ComputeValuesToResolve);
            m_pathsRootsToResolve = new Lazy<IReadOnlyList<AbsolutePath>>(ComputePathsRootsToResolve);
            m_modulesToResolve = new Lazy<IReadOnlyList<StringId>>(ComputeModulesToResolve);
        }

        /// <summary>
        /// Disables the filter from providing value short circuiting information.
        /// </summary>
        public void DisableValueShortCircuiting()
        {
            m_allowValueShortCircuiting = false;
        }

        /// <summary>
        /// Data used to limit evaluation
        /// </summary>
        public EvaluationFilter GetEvaluationFilter(SymbolTable symbolTable, PathTable pathTable) => new EvaluationFilter(symbolTable, pathTable, ValuesToResolve, PathsRootsToResolve, ModulesToResolve);

        private IReadOnlyList<FullSymbol> ComputeValuesToResolve()
        {
            Contract.Ensures(Contract.Result<IReadOnlyList<FullSymbol>>() != null);

            // If dependents are selected, we must resolve all values to create the full graph
            if (!m_allowValueShortCircuiting)
            {
                return CollectionUtilities.EmptyArray<FullSymbol>();
            }

            // Check if a subset of values can be resolved
            IEnumerable<FullSymbol> valuesToResolve = PipFilter.GetValuesToResolve();
            if (valuesToResolve != null)
            {
                return new List<FullSymbol>(valuesToResolve);
            }

            // All values must be resolved
            return CollectionUtilities.EmptyArray<FullSymbol>();
        }

        /// <summary>
        /// The values that need to be resolved to satisfy the filter
        /// </summary>
        private IReadOnlyList<FullSymbol> ValuesToResolve => m_valuesToResolve.Value;

        private IReadOnlyList<AbsolutePath> ComputePathsRootsToResolve()
        {
            Contract.Ensures(Contract.Result<IReadOnlyList<AbsolutePath>>() != null);

            // If dependents are selected, we must resolve all values to create the full graph
            if (!m_allowValueShortCircuiting)
            {
                return CollectionUtilities.EmptyArray<AbsolutePath>();
            }

            // Check if a subset of values can be resolved
            IEnumerable<AbsolutePath> valuesToResolve = PipFilter.GetSpecRootsToResolve();
            if (valuesToResolve != null)
            {
                return new List<AbsolutePath>(valuesToResolve);
            }

            // All values must be resolved
            return CollectionUtilities.EmptyArray<AbsolutePath>();
        }

        /// <summary>
        /// The path roots that need to be resolved to satisfy the filter
        /// </summary>
        private IReadOnlyList<AbsolutePath> PathsRootsToResolve => m_pathsRootsToResolve.Value;

        private IReadOnlyList<StringId> ComputeModulesToResolve()
        {
            Contract.Ensures(Contract.Result<IReadOnlyList<StringId>>() != null);

            // If dependents are selected, we must resolve all values to create the full graph
            if (!m_allowValueShortCircuiting)
            {
                return CollectionUtilities.EmptyArray<StringId>();
            }

            // Check if a subset of values can be resolved
            IEnumerable<StringId> modulesToResolve = PipFilter.GetModulesToResolve();
            if (modulesToResolve != null)
            {
                return new List<StringId>(modulesToResolve);
            }

            // All values must be resolved
            return CollectionUtilities.EmptyArray<StringId>();
        }

        /// <summary>
        /// The modules that need to be resolved to satisfy the filter.
        /// </summary>
        private IReadOnlyList<StringId> ModulesToResolve => m_modulesToResolve.Value;

        /// <summary>
        /// Gets statistics about the filter
        /// </summary>
        public FilterStatistics GetStatistics()
        {
            FilterStatistics stats = default;
            stats.ValuesToSelectivelyEvaluate = ValuesToResolve.Count;
            stats.PathsToSelectivelyEvaluate = PathsRootsToResolve.Count;
            stats.ModulesToSelectivelyEvaluate = ModulesToResolve.Count;
            PipFilter.AddStatistics(ref stats);
            return stats;
        }

        /// <inheritdoc/>
        public override int GetHashCode() => PipFilter.GetHashCode();

        /// <summary>
        /// Check whether two root filters are the same
        /// </summary>
        public bool Matches(RootFilter rootFilter)
        {
            // TODO: It is incomplete
            return rootFilter.FilterExpression == FilterExpression;
        }
    }
}
