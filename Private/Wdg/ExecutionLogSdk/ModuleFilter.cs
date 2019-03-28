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
    /// Default implementation of IModuleFilter
    /// </summary>
    public sealed class ModuleFilter : IModuleFilter
    {
        private Func<ModulePip, CachedGraph, bool> m_moduleFilterFunction;

        private IEnumerable<string> m_moduleFilters;
        private bool m_moduleExcludeFilter;

        private Regex m_moduleNameFilter;

        private bool ModuleStartsWith(ModulePip module, CachedGraph cachedGraph)
        {
            bool filterPrefixMatch = false;
            string moduleName = module.Identity.ToString(cachedGraph.Context.StringTable);

            // Loop trough the filter strings. All these filters are module name prefixes.
            foreach (var filter in m_moduleFilters)
            {
                // Check if the module name matches the prefix
                if (moduleName.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                {
                    // We have a match
                    filterPrefixMatch = true;
                    break;
                }
            }

            // We should load the module when we have a match and the filter IS NOT an exclude filter, or
            // we do not have a match and the filter IS an exclude filter.
            return filterPrefixMatch != m_moduleExcludeFilter;
        }

        private bool ModuleNameFilterMatches(ModulePip module, CachedGraph cachedGraph)
        {
            string moduleName = module.Identity.ToString(cachedGraph.Context.StringTable);

            return m_moduleNameFilter.IsMatch(moduleName);
        }

        /// <summary>
        /// Instances a ModuleFilter that uses a supplied delegate to perform the filtering
        /// </summary>
        /// <param name="moduleFilterFunction">The delegate to use when filtering modules</param>
        public ModuleFilter(Func<ModulePip, CachedGraph, bool> moduleFilterFunction)
        {
            Contract.Requires(moduleFilterFunction != null);

            m_moduleFilterFunction = moduleFilterFunction;
        }

        /// <summary>
        /// Instances a ModuleFilter that uses a list of module name prefixes to perform the filtering
        /// </summary>
        /// <param name="moduleFilters">List of module name prefixes used to specify which modules to load.</param>
        /// <param name="moduleExcludeFilter">When true, the module filters are exclude filters and modules with names that start with any of the prefixes that
        /// are listed in moduleFilters will not be loaded. When false, the module filters are include filters and only module names that start with at least
        /// one prefix listed will be loaded</param>
        public ModuleFilter(IEnumerable<string> moduleFilters, bool moduleExcludeFilter = false)
        {
            Contract.Requires(moduleFilters != null);

            m_moduleFilters = moduleFilters;
            m_moduleExcludeFilter = moduleExcludeFilter;
            m_moduleFilterFunction = ModuleStartsWith;
        }

        /// <summary>
        /// Instances a ModuleFilter that uses the specified regex to match on module names
        /// </summary>
        /// <param name="moduleNameFilter">The module name regex to filter which modules are loaded</param>
        public ModuleFilter(Regex moduleNameFilter)
        {
            Contract.Requires(moduleNameFilter != null);

            m_moduleNameFilter = moduleNameFilter;
            m_moduleFilterFunction = ModuleNameFilterMatches;
        }

        public bool ShouldLoadModule(ModulePip module, CachedGraph cachedGraph)
        {
            return m_moduleFilterFunction.Invoke(module, cachedGraph);
        }
    }
}
