// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace MaterializationDaemon
{
    /// <summary>
    /// Base class for all manifest parsers.
    /// </summary>
    public abstract class ManifestParser
    {
        /// <summary>
        /// File path of the manifest file.
        /// </summary>
        protected readonly string m_fileName;

        /// <nodoc/>
        public ManifestParser(string fileName)
        {
            m_fileName = fileName;
        }

        /// <summary>
        /// Processes the manifest file and returns paths to the files referenced by the manifest.
        /// </summary>
        public abstract List<string> ExtractFiles();
    }
}
