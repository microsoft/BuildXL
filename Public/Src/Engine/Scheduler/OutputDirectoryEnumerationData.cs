// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Data needed when enumerating process output directories.
    /// </summary>
    public sealed class OutputDirectoryEnumerationData
    {
        /// <summary>
        /// Underlying process.
        /// </summary>
        public Process Process 
        {
            get => m_process;
            set
            {
                Contract.Requires(value != null);
                Clear();
                m_process = value;
                UntrackedPaths.UnionWith(Process.UntrackedPaths);
                UntrackedScopes.UnionWith(Process.UntrackedScopes);
                OutputDirectoryExclusions.UnionWith(Process.OutputDirectoryExclusions);
                OutputFilePaths.UnionWith(Process.FileOutputs.Select(f => f.Path));
            }
        }

        /// <summary>
        /// Untracked paths.
        /// </summary>
        public readonly HashSet<AbsolutePath> UntrackedPaths = new HashSet<AbsolutePath>();

        /// <summary>
        /// Untracked scopes.
        /// </summary>
        public readonly HashSet<AbsolutePath> UntrackedScopes = new HashSet<AbsolutePath>();

        /// <summary>
        /// Excluded directories.
        /// </summary>
        public readonly HashSet<AbsolutePath> OutputDirectoryExclusions = new HashSet<AbsolutePath>();

        /// <summary>
        /// Statically declared output files.
        /// </summary>
        public readonly HashSet<AbsolutePath> OutputFilePaths = new HashSet<AbsolutePath>();

        private Process m_process;

        /// <summary>
        /// Clears data.
        /// </summary>
        public void Clear()
        {
            m_process = null;
            UntrackedPaths.Clear();
            UntrackedScopes.Clear();
            OutputDirectoryExclusions.Clear();
            OutputFilePaths.Clear();
        }
    }
}
