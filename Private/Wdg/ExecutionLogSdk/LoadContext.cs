// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine;
using BuildXL.Pips.Operations;

namespace Tool.ExecutionLogSdk
{
    public sealed class LoadContext : IFileFilter, IPipFilter, IModuleFilter
    {
        private List<IFileFilter> m_fileFilters;
        private List<IPipFilter> m_pipFilters;
        private List<IModuleFilter> m_moduleFilters;

        /// <summary>
        /// The load options to use when loading an XLG
        /// </summary>
        public ExecutionLogLoadOptions LoadOptions { get; }

        public bool HasFileFilters => m_fileFilters.Count != 0;

        public bool HasPipFilters => m_pipFilters.Count != 0;

        public bool HasModuleFilters => m_moduleFilters.Count != 0;

        /// <summary>
        /// Instances a LoadContext object with the specified load options
        /// </summary>
        /// <param name="loadOptions">The load options to use when loading an XLG</param>
        public LoadContext(ExecutionLogLoadOptions loadOptions = ExecutionLogLoadOptions.LoadPipDataBuildGraphAndPipPerformanceData)
        {
            LoadOptions = loadOptions;
            m_fileFilters = new List<IFileFilter>();
            m_pipFilters = new List<IPipFilter>();
            m_moduleFilters = new List<IModuleFilter>();
        }

        /// <summary>
        /// Adds a file filter to the list of file filters to use when filtering files
        /// </summary>
        /// <param name="fileFilter">The file filter to add</param>
        public void AddFileFilter(IFileFilter fileFilter)
        {
            Contract.Requires(fileFilter != null);

            m_fileFilters.Add(fileFilter);
        }

        /// <summary>
        /// Adds a pip filter to the list of pip filters to use when filtering pips
        /// </summary>
        /// <param name="pipFilter">The pip filter to add</param>
        public void AddPipFilter(IPipFilter pipFilter)
        {
            Contract.Requires(pipFilter != null);

            m_pipFilters.Add(pipFilter);
        }

        /// <summary>
        /// Adds a module filter to the list of module filters to use when filtering modules
        /// </summary>
        /// <param name="moduleFilter">The module filter to add</param>
        public void AddModuleFilter(IModuleFilter moduleFilter)
        {
            Contract.Requires(moduleFilter != null);

            m_moduleFilters.Add(moduleFilter);
        }

        public bool ShouldLoadFile(string filename)
        {
            foreach (IFileFilter fileFilter in m_fileFilters)
            {
                if (!fileFilter.ShouldLoadFile(filename))
                {
                    return false;
                }
            }

            return true;
        }

        public bool ShouldLoadPip(Process process, CachedGraph cachedGraph)
        {
            foreach (IPipFilter pipFilter in m_pipFilters)
            {
                if (!pipFilter.ShouldLoadPip(process, cachedGraph))
                {
                    return false;
                }
            }

            return true;
        }

        public bool ShouldLoadModule(ModulePip module, CachedGraph cachedGraph)
        {
            foreach (IModuleFilter moduleFilter in m_moduleFilters)
            {
                if (!moduleFilter.ShouldLoadModule(module, cachedGraph))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
