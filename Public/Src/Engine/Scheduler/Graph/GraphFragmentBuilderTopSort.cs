// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
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
    public class GraphFragmentBuilderTopSort : GraphFragmentBuilder
    {
        private readonly ConcurrentDictionary<PipId, IList<Pip>> m_pipDependents = new ConcurrentDictionary<PipId, IList<Pip>>();
        private readonly ConcurrentBigMap<FileArtifact, PipId> m_fileProducers = new ConcurrentBigMap<FileArtifact, PipId>();
        private readonly ConcurrentBigMap<DirectoryArtifact, PipId> m_opaqueDirectoryProducers = new ConcurrentBigMap<DirectoryArtifact, PipId>();
        private readonly PipStaticFingerprinter m_pipStaticFingerprinter;
        private readonly PipGraphStaticFingerprints m_pipStaticFingerprints = new PipGraphStaticFingerprints();

        /// <summary>
        /// Creates an instance of <see cref="GraphFragmentBuilder"/>.
        /// </summary>
        public GraphFragmentBuilderTopSort(LoggingContext loggingContext, PipExecutionContext pipExecutionContext, IConfiguration configuration, PathExpander pathExpander) : base(loggingContext, pipExecutionContext, configuration)
        {
            if (configuration.Schedule.ComputePipStaticFingerprints)
            {
                var extraFingerprintSalts = new ExtraFingerprintSalts(
                    configuration,
                    PipFingerprintingVersion.TwoPhaseV2,
                    configuration.Cache.CacheSalt,
                    new DirectoryMembershipFingerprinterRuleSet(configuration, pipExecutionContext.StringTable).ComputeSearchPathToolsHash());

                m_pipStaticFingerprinter = new PipStaticFingerprinter(
                    pipExecutionContext.PathTable,
                    GetSealDirectoryFingerprint,
                    GetDirectoryProducerFingerprint,
                    extraFingerprintSalts,
                    pathExpander)
                {
                    FingerprintTextEnabled = configuration.Schedule.LogPipStaticFingerprintTexts
                };
            }
        }

        private ContentFingerprint GetSealDirectoryFingerprint(DirectoryArtifact directory)
        {
            Contract.Requires(directory.IsValid);

            return SealDirectoryTable.TryGetSealForDirectoryArtifact(directory, out PipId pipId)
                   && m_pipStaticFingerprints.TryGetFingerprint(pipId, out ContentFingerprint fingerprint)
                   ? fingerprint
                   : ContentFingerprint.Zero;
        }

        private ContentFingerprint GetDirectoryProducerFingerprint(DirectoryArtifact directory)
        {
            Contract.Requires(directory.IsValid);
            return m_opaqueDirectoryProducers.TryGetValue(directory, out PipId pipId)
                && m_pipStaticFingerprints.TryGetFingerprint(pipId, out ContentFingerprint fingerprint)
                ? fingerprint
                : ContentFingerprint.Zero;
        }

        /// <inheritdoc />
        public override bool AddCopyFile([NotNull] CopyFile copyFile, PipId valuePip)
        {
            var result = base.AddCopyFile(copyFile, valuePip);
            AddFileDependent(copyFile.Source, copyFile);
            m_fileProducers[copyFile.Destination] = copyFile.PipId;
            ComputeStaticFingerprint(copyFile);
            return result;
        }

        /// <inheritdoc />
        public override bool AddProcess([NotNull] Process process, PipId valuePip)
        {
            var result = base.AddProcess(process, valuePip);
            AddFileDependents(process.Dependencies, process);
            AddDirectoryDependents(process.DirectoryDependencies, process);
            AddDependents(process.OrderDependencies, process);

            foreach (var fileOutput in process.FileOutputs)
            {
                m_fileProducers[fileOutput.ToFileArtifact()] = process.PipId;
            }

            foreach (var directoryOutput in process.DirectoryOutputs)
            {
                m_opaqueDirectoryProducers[directoryOutput] = process.PipId;
            }

            ComputeStaticFingerprint(process);
            return result;
        }

        /// <inheritdoc />
        public override DirectoryArtifact AddSealDirectory([NotNull] SealDirectory sealDirectory, PipId valuePip)
        {
            base.AddSealDirectory(sealDirectory, valuePip);
            AddFileDependents(sealDirectory.Contents, sealDirectory);
            AddDirectoryDependents(sealDirectory.ComposedDirectories, sealDirectory);
            ComputeStaticFingerprint(sealDirectory);
            return sealDirectory.Directory;
        }

        /// <inheritdoc />
        public override bool AddWriteFile([NotNull] WriteFile writeFile, PipId valuePip)
        {
            var result = base.AddWriteFile(writeFile, valuePip);
            m_fileProducers[writeFile.Destination] = writeFile.PipId;
            ComputeStaticFingerprint(writeFile);
            return result;
        }

        /// <inheritdoc />
        private void ComputeStaticFingerprint(Pip pip)
        {
            if (Configuration.Schedule.ComputePipStaticFingerprints)
            {
                pip.StaticFingerprint = m_pipStaticFingerprinter.ComputeWeakFingerprint(pip).Hash;
                m_pipStaticFingerprints.AddFingerprint(pip, new ContentFingerprint(pip.StaticFingerprint));
            }
        }

        /// <inheritdoc />
        public void AddDirectoryDependents(IEnumerable<DirectoryArtifact> directories, Pip dependent)
        {
            foreach (var directory in directories)
            {
                SealDirectoryTable.TryGetSealForDirectoryArtifact(directory, out PipId producerId);
                if (producerId.IsValid)
                {
                    AddDependent(producerId, dependent);
                }
                else if (m_opaqueDirectoryProducers.TryGetValue(directory, out PipId opaqueProducerId))
                {
                    AddDependent(opaqueProducerId, dependent);
                }
            }
        }

        /// <inheritdoc />
        public void AddFileDependents(IEnumerable<FileArtifact> files, Pip dependent)
        {
            foreach (var file in files)
            {
                AddFileDependent(file, dependent);
            }
        }

        /// <inheritdoc />
        public void AddFileDependent(FileArtifact file, Pip dependent)
        {
            if (m_fileProducers.TryGetValue(file, out PipId producer))
            {
                AddDependent(producer, dependent);
            }
        }

        /// <inheritdoc />
        private void AddDependents(IEnumerable<PipId> pips, Pip dependent)
        {
            foreach (var pip in pips)
            {
                AddDependent(pip, dependent);
            }
        }

        /// <inheritdoc />
        private void AddDependent(PipId pip, Pip dependent)
        {
            m_pipDependents.AddOrUpdate(pip, new List<Pip>() { dependent }, (key, deps) => { lock (deps) { deps.Add(dependent); return deps; } });
        }

        /// <inheritdoc />
        public override IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip)
        {
            IList<Pip> dependents;
            m_pipDependents.TryGetValue(pip.PipId, out dependents);
            return dependents;
        }
    }
}
