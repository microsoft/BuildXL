// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Provides the ability to get semantic information from paths
    /// </summary>
    public class SemanticPathExpander : PathExpander
    {
        /// <summary>
        /// The default expander which does has semantic path information
        /// </summary>
        public static new readonly SemanticPathExpander Default = new SemanticPathExpander();

        /// <summary>
        /// Gets the semantic path information for the given path in the context of the given pip.
        /// </summary>
        /// <param name="path">the path</param>
        /// <returns>the semantic path info if it exists</returns>
        public virtual SemanticPathInfo GetSemanticPathInfo(AbsolutePath path)
        {
            return SemanticPathInfo.Invalid;
        }

        /// <summary>
        /// Gets the semantic path information for the given path in the context of the given pip.
        /// </summary>
        /// <param name="path">the path</param>
        /// <returns>the semantic path info if it exists</returns>
        public virtual SemanticPathInfo GetSemanticPathInfo(string path)
        {
            return SemanticPathInfo.Invalid;
        }

        /// <summary>
        /// Gets the semantic path expander for the given module.
        /// </summary>
        /// <param name="moduleId">the module id</param>
        /// <returns>the module-specific semantic path expander or the current expander if none</returns>
        public virtual SemanticPathExpander GetModuleExpander(ModuleId moduleId)
        {
            return this;
        }

        /// <summary>
        /// Retrieves all roots that are writable
        /// </summary>
        public virtual IEnumerable<AbsolutePath> GetWritableRoots()
        {
            return Enumerable.Empty<AbsolutePath>();
        }

        /// <summary>
        /// Returns all paths where directories are allowed to be created
        /// </summary>
        public virtual IEnumerable<AbsolutePath> GetPathsWithAllowedCreateDirectory()
        {
            return Enumerable.Empty<AbsolutePath>();
        }
    }
}
