// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Class for building graph fragments.
    /// </summary>
    public class GraphFragmentBuilderTopSort : GraphFragmentBuilder
    {
        private readonly ConcurrentDictionary<PipId, IList<Pip>> m_pipDependents = new ConcurrentDictionary<PipId, IList<Pip>>();
        
        /// <summary>
        /// Creates an instance of <see cref="GraphFragmentBuilder"/>.
        /// </summary>
        public GraphFragmentBuilderTopSort(
            PipExecutionContext pipExecutionContext, 
            IConfiguration configuration, 
            PathExpander pathExpander) 
            : base(pipExecutionContext, configuration, pathExpander)
        {
        }

        /// <inheritdoc />
        public override bool AddCopyFile([NotNull] CopyFile copyFile, PipId valuePip)
        {
            var result = base.AddCopyFile(copyFile, valuePip);
            AddFileDependent(copyFile.Source, copyFile);

            return result;
        }

        /// <inheritdoc />
        public override bool AddProcess([NotNull] Process process, PipId valuePip)
        {
            var result = base.AddProcess(process, valuePip);
            AddFileDependents(process.Dependencies, process);
            AddDirectoryDependents(process.DirectoryDependencies, process);
            AddDependents(process.OrderDependencies, process);

            return result;
        }

        /// <inheritdoc />
        public override bool AddIpcPip([NotNull] IpcPip ipcPip, PipId valuePip)
        {
            var result = base.AddIpcPip(ipcPip, valuePip);
            AddFileDependents(ipcPip.FileDependencies, ipcPip);
            AddDirectoryDependents(ipcPip.DirectoryDependencies, ipcPip);
            AddDependents(ipcPip.ServicePipDependencies, ipcPip);

            return result;
        }

        /// <inheritdoc />
        public override DirectoryArtifact AddSealDirectory([NotNull] SealDirectory sealDirectory, PipId valuePip)
        {
            var result = base.AddSealDirectory(sealDirectory, valuePip);
            AddFileDependents(sealDirectory.Contents, sealDirectory);
            AddDirectoryDependents(sealDirectory.ComposedDirectories, sealDirectory);

            return result;
        }

        /// <inheritdoc />
        public override bool AddWriteFile([NotNull] WriteFile writeFile, PipId valuePip)
        {
            return base.AddWriteFile(writeFile, valuePip);
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
                else if (OpaqueDirectoryProducers.TryGetValue(directory, out PipId opaqueProducerId))
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
            if (FileProducers.TryGetValue(file, out PipId producer))
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
            m_pipDependents.TryGetValue(pip.PipId, out var dependents);
            return dependents;
        }
    }
}
