// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using static BuildXL.Scheduler.Graph.PipGraph;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Class for building graph fragments.
    /// </summary>
    public class GraphFragmentBuilder : IPipGraph
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PipExecutionContext m_pipExecutionContext;
        private readonly SealedDirectoryTable m_sealDirectoryTable;
        private readonly ConcurrentQueue<Pip> m_pips = new ConcurrentQueue<Pip>();
        private readonly Lazy<IIpcMoniker> m_lazyApiServerMoniker;
        private WindowsOsDefaults m_windowsOsDefaults;
        private MacOsDefaults m_macOsDefaults;
        private readonly object m_osDefaultLock = new object();
        private int m_nextPipId = 0;

        /// <summary>
        /// Creates an instance of <see cref="GraphFragmentBuilder"/>.
        /// </summary>
        public GraphFragmentBuilder(LoggingContext loggingContext, PipExecutionContext pipExecutionContext, IConfiguration configuration)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pipExecutionContext != null);

            m_loggingContext = loggingContext;
            m_pipExecutionContext = pipExecutionContext;
            m_lazyApiServerMoniker = configuration.Schedule.UseFixedApiServerMoniker
                ? Lazy.Create(() => IpcFactory.GetFixedMoniker())
                : Lazy.Create(() => IpcFactory.GetProvider().CreateNewMoniker());
            m_sealDirectoryTable = new SealedDirectoryTable(m_pipExecutionContext.PathTable);
        }

        /// <inheritdoc />
        public int PipCount => m_pips.Count;

        private bool AddPip(Pip pip)
        {
            m_pips.Enqueue(pip);
            pip.PipId = new PipId((uint)Interlocked.Increment(ref m_nextPipId));
            return true;
        }

        /// <inheritdoc />
        public bool AddCopyFile([NotNull] CopyFile copyFile, PipId valuePip) => AddPip(copyFile);

        /// <inheritdoc />
        public bool AddIpcPip([NotNull] IpcPip ipcPip, PipId valuePip) => AddPip(ipcPip);

        /// <inheritdoc />
        public bool AddModule([NotNull] ModulePip module)
        {
            return AddPip(module);
        }

        /// <inheritdoc />
        public bool AddModuleModuleDependency(ModuleId moduleId, ModuleId dependency) => true;

        /// <inheritdoc />
        public bool AddOutputValue([NotNull] ValuePip value) => AddPip(value);

        /// <inheritdoc />
        public bool AddProcess([NotNull] Process process, PipId valuePip) => AddPip(process);

        /// <inheritdoc />
        public DirectoryArtifact AddSealDirectory([NotNull] SealDirectory sealDirectory, PipId valuePip)
        {
            AddPip(sealDirectory);
            DirectoryArtifact artifactForNewSeal;

            if (sealDirectory.Kind == SealDirectoryKind.SharedOpaque)
            {
                Contract.Assume(sealDirectory.Directory.IsSharedOpaque);
                artifactForNewSeal = sealDirectory.Directory;
            }
            else
            {
                // For the regular dynamic case, the directory artifact is always
                // created with sealId 0. For other cases, we reserve it
                artifactForNewSeal = sealDirectory.Kind == SealDirectoryKind.Opaque
                    ? DirectoryArtifact.CreateWithZeroPartialSealId(sealDirectory.DirectoryRoot)
                    : m_sealDirectoryTable.ReserveDirectoryArtifact(sealDirectory);
                sealDirectory.SetDirectoryArtifact(artifactForNewSeal);
            }

            m_sealDirectoryTable.AddSeal(sealDirectory);

            return artifactForNewSeal;
        }

        /// <inheritdoc />
        public bool AddSpecFile([NotNull] SpecFilePip specFile) => AddPip(specFile);

        /// <inheritdoc />
        public bool AddValueValueDependency(in ValuePip.ValueDependency valueDependency) => true;

        /// <inheritdoc />
        public bool AddWriteFile([NotNull] WriteFile writeFile, PipId valuePip) => AddPip(writeFile);

        /// <inheritdoc />
        public bool ApplyCurrentOsDefaults(ProcessBuilder processBuilder)
        {
            // TODO: This is a copy from PipGraph.Builder. Refactor it!
            if (OperatingSystemHelper.IsUnixOS)
            {
                if (m_macOsDefaults == null)
                {
                    lock (m_osDefaultLock)
                    {
                        if (m_macOsDefaults == null)
                        {
                            m_macOsDefaults = new MacOsDefaults(m_pipExecutionContext.PathTable, this);
                        }
                    }
                }

                return m_macOsDefaults.ProcessDefaults(processBuilder);
            }
            else
            {
                if (m_windowsOsDefaults == null)
                {
                    lock (m_osDefaultLock)
                    {
                        if (m_windowsOsDefaults == null)
                        {
                            m_windowsOsDefaults = new WindowsOsDefaults(m_pipExecutionContext.PathTable);
                        }
                    }
                }

                return m_windowsOsDefaults.ProcessDefaults(processBuilder);
            }
        }

        /// <inheritdoc />
        public IIpcMoniker GetApiServerMoniker() => m_lazyApiServerMoniker.Value;

        /// <inheritdoc />
        public GraphPatchingStatistics PartiallyReloadGraph([NotNull] HashSet<AbsolutePath> affectedSpecs) => default;

        /// <inheritdoc />
        public DirectoryArtifact ReserveSharedOpaqueDirectory(AbsolutePath directoryArtifactRoot) => m_sealDirectoryTable.CreateSharedOpaqueDirectoryWithNewSealId(directoryArtifactRoot);

        /// <inheritdoc />
        public IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip) => Enumerable.Empty<Pip>();

        /// <inheritdoc />
        public IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip) => Enumerable.Empty<Pip>();

        /// <inheritdoc />
        public IEnumerable<Pip> RetrieveScheduledPips() => m_pips;

        /// <inheritdoc />
        public void SetSpecsToIgnore(IEnumerable<AbsolutePath> specsToIgnore)
        {
        }
    }
}