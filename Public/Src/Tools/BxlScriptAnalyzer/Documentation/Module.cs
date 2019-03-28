// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    /// <summary>
    /// The root of a module.
    /// </summary>
    public class Module
    {
        private int m_nextNodeId = 0;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="docWorkspace">Workspace the module belongs to.</param>
        /// <param name="name">Name of the module.</param>
        /// <param name="version">Version of the module.</param>
        public Module(DocWorkspace docWorkspace, string name, string version)
        {
            DocWorkspace = docWorkspace;
            Name = name;
            Version = version;
        }

        /// <summary>
        /// Name of the module.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Version of the module.
        /// </summary>
        public string Version { get; }

        /// <nodoc />
        public DocWorkspace DocWorkspace { get; set; }

        /// <summary>
        /// Whether the module is ignored
        /// </summary>
        public bool Ignored { get; set; }

        /// <summary>
        /// Gets the title string for the module.
        /// </summary>
        public string Title => !string.IsNullOrEmpty(Version) ? I($"{Name} (Version {Version})") : Name;

        private ConcurrentDictionary<string, DocNode> ChildNodes { get; } = new ConcurrentDictionary<string, DocNode>();

        internal int GetNextNodeId()
        {
            return Interlocked.Increment(ref m_nextNodeId);
        }

        internal DocNode GetOrAdd(DocNodeType type, DocNodeVisibility visibility, AbsolutePath specPath, string name, List<string> trivia, string appendix)
        {
            return ChildNodes.GetOrAdd(name, new DocNode(type, visibility, name, trivia, this, specPath, null, appendix));
        }

        internal bool HasChildren => ChildNodes.Count != 0;

        internal IEnumerable<DocNode> Children => ChildNodes.Values;
    }
}
