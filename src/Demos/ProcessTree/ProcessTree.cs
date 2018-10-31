// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Processes;

namespace BuildXL.Demo
{
    /// <summary>
    /// A tree of processes reported by the sandbox
    /// </summary>
    public sealed class ProcessTree
    {
        /// <nodoc/>
        public ProcessNode Root { get; }

        /// <nodoc/>
        public ProcessTree(ProcessNode root)
        {
            Contract.Requires(root != null);
            Root = root;
        }
    }

    /// <summary>
    /// A node in the process tree, containing a <see cref="ReportedProcess"/>, as reported by the sandbox and its children
    /// </summary>
    public sealed class ProcessNode
    {
        private readonly List<ProcessNode> m_children;

        /// <nodoc/>
        public ReportedProcess ReportedProcess { get; }

        /// <summary>
        /// In order to inspect the children of this node, the node has to be sealed, <see cref="Seal"/>.
        /// </summary>
        public IReadOnlyCollection<ProcessNode> Children {
            get
            {
                Contract.Assert(IsSealed);
                return m_children;
            }
        }

        /// <nodoc/>
        public bool IsSealed { get; private set; } = false;

        /// <nodoc/>
        public ProcessNode(ReportedProcess reportedProcess)
        {
            ReportedProcess = reportedProcess;
            m_children = new List<ProcessNode>();
        }

        /// <summary>
        /// In order to add a child to this node, it must not be sealed
        /// </summary>
        /// <param name="reportedProcess"></param>
        public void AddChildren(ProcessNode reportedProcess)
        {
            Contract.Assert(!IsSealed);
            Contract.Requires(reportedProcess != null);

            m_children.Add(reportedProcess);
        }

        /// <summary>
        /// Seals the node, disallowing the addition of extra children and makes <see cref="Children"/> available
        /// </summary>
        public void Seal()
        {
            Contract.Assert(!IsSealed);
            IsSealed = true;
        }
    }
}
