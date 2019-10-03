// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
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
        /// <summary>
        /// Seal directory table
        /// </summary>
        protected readonly SealedDirectoryTable SealDirectoryTable;

        /// <summary>
        /// Configuration
        /// </summary>
        protected readonly IConfiguration Configuration;

        /// <summary>
        /// File producers.
        /// </summary>
        protected readonly ConcurrentBigMap<FileArtifact, PipId> FileProducers = new ConcurrentBigMap<FileArtifact, PipId>();

        /// <summary>
        /// Opaque directory producers.
        /// </summary>
        protected readonly ConcurrentBigMap<DirectoryArtifact, PipId> OpaqueDirectoryProducers = new ConcurrentBigMap<DirectoryArtifact, PipId>();

        private readonly PipStaticFingerprinter m_pipStaticFingerprinter;
        
        private readonly PipExecutionContext m_pipExecutionContext;
        private readonly ConcurrentQueue<Pip> m_pips = new ConcurrentQueue<Pip>();
        private readonly Lazy<IIpcMoniker> m_lazyApiServerMoniker;
        private WindowsOsDefaults m_windowsOsDefaults;
        private MacOsDefaults m_macOsDefaults;
        private readonly object m_osDefaultLock = new object();
        private int m_nextPipId = 0;

        /// <summary>
        /// Creates an instance of <see cref="GraphFragmentBuilder"/>.
        /// </summary>
        public GraphFragmentBuilder(
            PipExecutionContext pipExecutionContext, 
            IConfiguration configuration,
            PathExpander pathExpander)
        {
            Contract.Requires(pipExecutionContext != null);

            Configuration = configuration;
            m_pipExecutionContext = pipExecutionContext;
            m_lazyApiServerMoniker = configuration.Schedule.UseFixedApiServerMoniker
                ? Lazy.Create(() => IpcFactory.GetFixedMoniker())
                : Lazy.Create(() => IpcFactory.GetProvider().CreateNewMoniker());
            SealDirectoryTable = new SealedDirectoryTable(m_pipExecutionContext.PathTable);

            if (configuration.Schedule.ComputePipStaticFingerprints)
            {
                var extraFingerprintSalts = new ExtraFingerprintSalts(
                    configuration,
                    PipFingerprintingVersion.TwoPhaseV2,
                    configuration.Cache.CacheSalt,
                    new DirectoryMembershipFingerprinterRuleSet(configuration, pipExecutionContext.StringTable).ComputeSearchPathToolsHash());

                m_pipStaticFingerprinter = new PipStaticFingerprinter(
                    pipExecutionContext.PathTable,
                    sealDirectoryFingerprintLookup: null,
                    directoryProducerFingerprintLookup: null,
                    extraFingerprintSalts: extraFingerprintSalts,
                    pathExpander: pathExpander)
                {
                    FingerprintTextEnabled = configuration.Schedule.LogPipStaticFingerprintTexts
                };
            }
        }

        /// <inheritdoc />
        public int PipCount => m_pips.Count;

        private void AddPip(Pip pip)
        {
            m_pips.Enqueue(pip);
            pip.PipId = new PipId((uint)Interlocked.Increment(ref m_nextPipId));
        }

        /// <inheritdoc />
        public virtual bool AddCopyFile([NotNull] CopyFile copyFile, PipId valuePip)
        {
            AddPip(copyFile);
            FileProducers[copyFile.Destination] = copyFile.PipId;
            ComputeStaticFingerprint(copyFile);
            return true;
        }

        /// <inheritdoc />
        public bool AddIpcPip([NotNull] IpcPip ipcPip, PipId valuePip)
        {
            AddPip(ipcPip);
            return true;
        }

        /// <inheritdoc />
        public bool AddModule([NotNull] ModulePip module)
        {
            AddPip(module);
            return true;
        }

        /// <inheritdoc />
        public bool AddModuleModuleDependency(ModuleId moduleId, ModuleId dependency) => true;

        /// <inheritdoc />
        public bool AddOutputValue([NotNull] ValuePip value)
        {
            AddPip(value);
            return true;
        }

        /// <inheritdoc />
        public virtual bool AddProcess([NotNull] Process process, PipId valuePip)
        {
            AddPip(process);

            foreach (var fileOutput in process.FileOutputs)
            {
                FileProducers[fileOutput.ToFileArtifact()] = process.PipId;
            }

            foreach (var directoryOutput in process.DirectoryOutputs)
            {
                OpaqueDirectoryProducers[directoryOutput] = process.PipId;
            }

            ComputeStaticFingerprint(process);

            return true;
        }

        /// <inheritdoc />
        public virtual DirectoryArtifact AddSealDirectory([NotNull] SealDirectory sealDirectory, PipId valuePip)
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
                    : SealDirectoryTable.ReserveDirectoryArtifact(sealDirectory);
                sealDirectory.SetDirectoryArtifact(artifactForNewSeal);
            }

            SealDirectoryTable.AddSeal(sealDirectory);
            ComputeStaticFingerprint(sealDirectory);

            return artifactForNewSeal;
        }

        /// <inheritdoc />
        public bool AddSpecFile([NotNull] SpecFilePip specFile)
        {
            AddPip(specFile);
            return true;
        }

        /// <inheritdoc />
        public bool AddValueValueDependency(in ValuePip.ValueDependency valueDependency) => true;

        /// <inheritdoc />
        public virtual bool AddWriteFile([NotNull] WriteFile writeFile, PipId valuePip)
        {
            AddPip(writeFile);
            FileProducers[writeFile.Destination] = writeFile.PipId;
            ComputeStaticFingerprint(writeFile);

            return true;
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

        /// <inheritdoc />
        public IIpcMoniker GetApiServerMoniker() => m_lazyApiServerMoniker.Value;

        /// <inheritdoc />
        public GraphPatchingStatistics PartiallyReloadGraph([NotNull] HashSet<AbsolutePath> affectedSpecs) => default;

        /// <inheritdoc />
        public DirectoryArtifact ReserveSharedOpaqueDirectory(AbsolutePath directoryArtifactRoot) => SealDirectoryTable.CreateSharedOpaqueDirectoryWithNewSealId(directoryArtifactRoot);

        /// <inheritdoc />
        public IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip) => throw new NotImplementedException();

        /// <inheritdoc />
        public virtual IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip) => throw new NotImplementedException();

        /// <inheritdoc />
        public IEnumerable<Pip> RetrieveScheduledPips() => m_pips;

        /// <inheritdoc />
        public void SetSpecsToIgnore(IEnumerable<AbsolutePath> specsToIgnore)
        {
        }

        private void ComputeStaticFingerprint(Pip pip)
        {
            if (m_pipStaticFingerprinter != null)
            {
                pip.IndependentStaticFingerprint = m_pipStaticFingerprinter.ComputeWeakFingerprint(pip).Hash;
            }
        }
    }
}