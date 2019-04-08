// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text.RegularExpressions;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Default implementation of IFileFilter
    /// </summary>
    public sealed class FileFilter : IFileFilter
    {
        private Func<string, bool> m_fileFilterFunction;

        private IEnumerable<string> m_fileFilters;
        private bool m_fileExcludeFilter;

        private Regex m_fileNameFilter;

        private bool FileStartsWith(string filename)
        {
            bool filterPrefixMatch = false;

            // Loop trough the filter strings. All these filters are file name prefixes.
            foreach (var filter in m_fileFilters)
            {
                // Check if the file name matches the prefix
                if (filename.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                {
                    // We have a match
                    filterPrefixMatch = true;
                    break;
                }
            }

            // We should load the file when we have a match and the filter IS NOT an exclude filter, or
            // we do not have a match and the filter IS an exclude filter.
            return filterPrefixMatch != m_fileExcludeFilter;
        }

        private bool FileNameFilterMatches(string filename)
        {
            return m_fileNameFilter.IsMatch(filename);
        }

        /// <summary>
        /// Instances a FileFilter that uses a supplied delegate to perform the filtering
        /// </summary>
        /// <param name="fileFilterFunction">The delegate to use when filtering files</param>
        public FileFilter(Func<string, bool> fileFilterFunction)
        {
            Contract.Requires(fileFilterFunction != null);

            m_fileFilterFunction = fileFilterFunction;
        }

        /// <summary>
        /// Instances a FileFilter that uses a list of file name prefixes to perform the filtering
        /// </summary>
        /// <param name="fileFilters">List of file name prefixes used to specify which files to load.</param>
        /// <param name="fileExcludeFilter">When true, the file filters are exclude filters and files with names that start with any of the prefixes that
        /// are listed in fileFilters will not be loaded. When false, the file filters are include filters and only files that start with at least
        /// one prefix listed will be loaded</param>
        public FileFilter(IEnumerable<string> fileFilters, bool fileExcludeFilter = false)
        {
            Contract.Requires(fileFilters != null);

            m_fileFilters = fileFilters;
            m_fileExcludeFilter = fileExcludeFilter;
            m_fileFilterFunction = FileStartsWith;
        }

        /// <summary>
        /// Instances a FileFilter that uses the specified regex to match on file names
        /// </summary>
        /// <param name="fileNameFilter">The file name regex to filter which files are loaded</param>
        public FileFilter(Regex fileNameFilter)
        {
            Contract.Requires(fileNameFilter != null);

            m_fileNameFilter = fileNameFilter;
            m_fileFilterFunction = FileNameFilterMatches;
        }

        public bool ShouldLoadFile(string filename)
        {
            return m_fileFilterFunction.Invoke(filename);
        }
    }
}
