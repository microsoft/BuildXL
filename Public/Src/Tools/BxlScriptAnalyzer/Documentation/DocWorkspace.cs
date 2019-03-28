// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    /// <summary>
    /// Documentation workspace containing a set of modules.
    /// </summary>
    public class DocWorkspace
    {
        /// <nodoc />
        public DocWorkspace(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Name of the workspace.
        /// </summary>
        public string Name { get; }

        private readonly ConcurrentDictionary<(string, string), Module> m_modules = new ConcurrentDictionary<(string, string), Module>();

        /// <summary>
        /// Get the set of modules.
        /// </summary>
        public IEnumerable<Module> Modules => m_modules.Values;

        /// <summary>
        /// Add a new module to the workspace.
        /// </summary>
        /// <param name="name">Name of the new module.</param>
        /// <param name="version">Version of the new module.</param>
        /// <returns>The added Module.</returns>
        public Module GetOrAddModule(string name, string version)
        {
            return m_modules.GetOrAdd((name, version), _ => new Module(this, name, version));
        }
    }
}
