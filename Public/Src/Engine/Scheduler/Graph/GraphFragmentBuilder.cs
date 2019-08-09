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
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
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
        private readonly ConcurrentBigMap<ModuleId, ModulePip> m_modules = new ConcurrentBigMap<ModuleId, ModulePip>();
        private readonly ConcurrentBigMap<PipId, IList<Pip>> m_pipDependents = new ConcurrentBigMap<PipId, IList<Pip>>();
        private readonly ConcurrentBigMap<FileArtifact, PipId> m_fileProducers = new ConcurrentBigMap<FileArtifact, PipId>();

        /// <summary>
        ///     A mapping of Service PipId to corresponding Shutdown PipId (<see cref="BuildXL.Pips.Operations.Process.ShutdownProcessPipId"/>).
        /// </summary>
        private readonly ConcurrentBigMap<PipId, ServiceInfo> m_servicePipToServiceInfoMap;

        private readonly Lazy<IIpcMoniker> m_lazyApiServerMoniker;
        private WindowsOsDefaults m_windowsOsDefaults;
        private MacOsDefaults m_macOsDefaults;
        private readonly object m_osDefaultLock = new object();

        /// <summary>
        /// Creates an instance of <see cref="GraphFragmentBuilder"/>.
        /// </summary>
        public GraphFragmentBuilder(LoggingContext loggingContext, PipExecutionContext pipExecutionContext)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pipExecutionContext != null);

            m_loggingContext = loggingContext;
            m_pipExecutionContext = pipExecutionContext;
            m_lazyApiServerMoniker = Lazy.Create(() => IpcFactory.GetProvider().CreateNewMoniker());
            m_sealDirectoryTable = new SealedDirectoryTable(m_pipExecutionContext.PathTable);
            m_servicePipToServiceInfoMap = new ConcurrentBigMap<PipId, ServiceInfo>();
        }

        /// <inheritdoc />
        public int PipCount => m_pips.Count;

        private int m_nextPipId = 0;

        private bool AddPip(Pip pip)
        {
            m_pips.Enqueue(pip); 
            pip.PipId = new PipId((uint)Interlocked.Increment(ref m_nextPipId));
            return true;
        }

        /// <inheritdoc />
        public bool AddCopyFile([NotNull] CopyFile copyFile, PipId valuePip)
        {
            var result = AddPip(copyFile);
            AddDependent(valuePip, copyFile);
            AddFileDependent(copyFile.Source, copyFile);
            m_fileProducers[copyFile.Destination] = copyFile.PipId;
            return result;
        }

        /// <inheritdoc />
        public bool AddIpcPip([NotNull] IpcPip ipcPip, PipId valuePip)
        {
            var result = AddPip(ipcPip);
            AddDependent(valuePip, ipcPip);
            AddFileDependents(ipcPip.FileDependencies, ipcPip);
            AddDirectoryDependents(ipcPip.DirectoryDependencies, ipcPip);
            AddDependents(ipcPip.ServicePipDependencies, ipcPip);
            AddServicePipDependents(ipcPip.ServicePipDependencies, ipcPip);
            return result;
        }

        /// <inheritdoc />
        public bool AddModule([NotNull] ModulePip module)
        {
            m_modules[module.Module] = module;
            return AddPip(module); 
        }

        /// <inheritdoc />
        public bool AddModuleModuleDependency(ModuleId moduleId, ModuleId dependency)
        {
            AddDependent(m_modules[dependency].PipId, m_modules[moduleId]);
            return true;
        }

        /// <inheritdoc />
        public bool AddOutputValue([NotNull] ValuePip value)
        {
            var result = AddPip(value);
            AddFileDependent(value.SpecFile, value);
            return result;
        }

        /// <inheritdoc />
        public bool AddProcess([NotNull] Process process, PipId valuePip)
        {
            var result = AddPip(process);
            AddDependent(valuePip, process);
            AddFileDependents(process.Dependencies, process);
            AddDirectoryDependents(process.DirectoryDependencies, process);
            AddDependents(process.OrderDependencies, process);
            AddDependents(process.ServicePipDependencies, process);
            AddServicePipDependents(process.ServicePipDependencies, process);

            foreach (var fileOutput in process.FileOutputs)
            {
                m_fileProducers[fileOutput.ToFileArtifact()] = process.PipId;
            }

            if (process.IsService)
            {
                m_servicePipToServiceInfoMap[process.PipId] = process.ServiceInfo;
            }

            return result;
        }

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

            AddDependent(valuePip, sealDirectory);
            AddFileDependents(sealDirectory.Contents, sealDirectory);
            AddDirectoryDependents(sealDirectory.ComposedDirectories, sealDirectory);

            m_sealDirectoryTable.AddSeal(sealDirectory);
            return artifactForNewSeal;
        }

        /// <inheritdoc />
        public bool AddSpecFile([NotNull] SpecFilePip specFile)
        {
            var result = AddPip(specFile);
            AddDependent(m_modules[specFile.OwningModule].PipId, specFile);
            m_fileProducers[specFile.SpecFile] = specFile.PipId;
            return result;
        }

        /// <inheritdoc />
        public bool AddValueValueDependency(in ValuePip.ValueDependency valueDependency) => true;

        /// <inheritdoc />
        public bool AddWriteFile([NotNull] WriteFile writeFile, PipId valuePip)
        {
            var result = AddPip(writeFile);
            m_fileProducers[writeFile.Destination] = writeFile.PipId;
            AddDependent(valuePip, writeFile);
            return result;
        }

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

        public void AddServicePipDependents(IEnumerable<PipId> servicePipDependencies, Pip dependent)
        {
            foreach (var servicePip in servicePipDependencies)
            {
                AddDependents(m_servicePipToServiceInfoMap[servicePip].FinalizationPipIds, dependent);
            }
        }

        public void AddDirectoryDependents(IEnumerable<DirectoryArtifact> directories, Pip dependent)
        {
            foreach (var directory in directories)
            {
                m_sealDirectoryTable.TryGetSealForDirectoryArtifact(directory, out PipId producerId);
                if (producerId.IsValid)
                {
                    AddDependent(producerId, dependent);
                }
            }
        }

        public void AddFileDependents(IEnumerable<FileArtifact> files, Pip dependent)
        {
            foreach(var file in files)
            {
                AddFileDependent(file, dependent);
            }
        }

        public void AddFileDependent(FileArtifact file, Pip dependent)
        {
            if (m_fileProducers.TryGetValue(file, out PipId producer))
            {
                AddDependent(producer, dependent);
            }
        }

        private void AddDependents(IEnumerable<PipId> pips, Pip dependent)
        {
            foreach (var pip in pips)
            {
                AddDependent(pip, dependent);
            }
        }

        private void AddDependent(PipId pip, Pip dependent)
        {
            m_pipDependents.AddOrUpdate(pip, new List<Pip>() { dependent }, (key, deps) => { lock (deps) { deps.Add(dependent); return deps; } });
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
        public IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip)
        {
            IList<Pip> dependents;
            m_pipDependents.TryGetValue(pip.PipId, out dependents);
            return dependents;
        }

        /// <inheritdoc />
        public IEnumerable<Pip> RetrieveScheduledPips() => m_pips;

        /// <inheritdoc />
        public void SetSpecsToIgnore(IEnumerable<AbsolutePath> specsToIgnore)
        {
        }
    }
}