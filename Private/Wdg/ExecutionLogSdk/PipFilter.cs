// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text.RegularExpressions;
using BuildXL.Engine;
using BuildXL.Pips.Operations;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Default implementation of IPipFilter
    /// </summary>
    public sealed class PipFilter : IPipFilter
    {
        private Func<Process, CachedGraph, bool> m_pipFilterFunction;

        private IEnumerable<string> m_pipFilters;
        private bool m_pipExcludeFilter;

        private Regex m_pipNameFilter;

        private bool PipStartsWith(Process process, CachedGraph cachedGraph)
        {
            bool filterPrefixMatch = false;
            string pipName = process.Provenance.OutputValueSymbol.ToString(cachedGraph.Context.SymbolTable);

            // Loop trough the filter strings. All these filters are pip name prefixes.
            foreach (var filter in m_pipFilters)
            {
                // Check if the pip name matches the prefix
                if (pipName.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                {
                    // We have a match
                    filterPrefixMatch = true;
                    break;
                }
            }

            // We should load the pip when we have a match and the filter IS NOT an exclude filter, or
            // we do not have a match and the filter IS an exclude filter.
            return filterPrefixMatch != m_pipExcludeFilter;
        }

        private bool PipNameFilterMatches(Process process, CachedGraph cachedGraph)
        {
            string pipName = process.Provenance.OutputValueSymbol.ToString(cachedGraph.Context.SymbolTable);

            return m_pipNameFilter.IsMatch(pipName);
        }

        /// <summary>
        /// Instances a PipFilter that uses a supplied delegate to perform the filtering
        /// </summary>
        /// <param name="pipFilterFunction">The delegate to use when filtering pips</param>
        public PipFilter(Func<Process, CachedGraph, bool> pipFilterFunction)
        {
            Contract.Requires(pipFilterFunction != null);

            m_pipFilterFunction = pipFilterFunction;
        }

        /// <summary>
        /// Instances a PipFilter that uses a list of pip name prefixes to perform the filtering
        /// </summary>
        /// <param name="pipFilters">List of pip name prefixes used to specify which pips to load.</param>
        /// <param name="pipExcludeFilter">When true, the pip filters are exclude filters and pips with names that start with any of the prefixes that
        /// are listed in pipFilters will not be loaded. When false, the pip filters are include filters and only pips that start with at least
        /// one prefix listed will be loaded</param>
        public PipFilter(IEnumerable<string> pipFilters, bool pipExcludeFilter = false)
        {
            Contract.Requires(pipFilters != null);

            m_pipFilters = pipFilters;
            m_pipExcludeFilter = pipExcludeFilter;
            m_pipFilterFunction = PipStartsWith;
        }

        /// <summary>
        /// Instances a PipFilter that uses the specified regex to match on pip names
        /// </summary>
        /// <param name="pipNameFilter">The pip name regex to filter which pips are loaded</param>
        public PipFilter(Regex pipNameFilter)
        {
            Contract.Requires(pipNameFilter != null);

            m_pipNameFilter = pipNameFilter;
            m_pipFilterFunction = PipNameFilterMatches;
        }

        public bool ShouldLoadPip(Process process, CachedGraph cachedGraph)
        {
            return m_pipFilterFunction.Invoke(process, cachedGraph);
        }
    }
}
