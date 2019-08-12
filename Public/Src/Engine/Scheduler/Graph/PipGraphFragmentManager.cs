// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Manager which controls adding pip fragments to the graph.
    /// </summary>
    public class PipGraphFragmentManager : IPipGraphFragmentManager
    {
        private readonly ConcurrentDictionary<int, (PipGraphFragmentSerializer, Task<bool>)> m_readFragmentTasks = new ConcurrentDictionary<int, (PipGraphFragmentSerializer, Task<bool>)>();

        private readonly IPipGraph m_pipGraph;

        private readonly PipExecutionContext m_context;

        private readonly LoggingContext m_loggingContext;

        private readonly ConcurrentBigMap<ModuleId, Lazy<bool>> m_modulePipUnify = new ConcurrentBigMap<ModuleId, Lazy<bool>>();

        private readonly ConcurrentBigMap<FileArtifact, Lazy<bool>> m_specFilePipUnify = new ConcurrentBigMap<FileArtifact, Lazy<bool>>();

        private readonly ConcurrentBigMap<(FullSymbol, QualifierId), Lazy<bool>> m_valuePipUnify = new ConcurrentBigMap<(FullSymbol, QualifierId), Lazy<bool>>();

        private readonly ConcurrentBigMap<long, Lazy<bool>> m_pipUnify = new ConcurrentBigMap<long, Lazy<bool>>();

        private readonly ConcurrentBigMap<long, PipId> m_semiStableHashToPipId = new ConcurrentBigMap<long, PipId>();
        private readonly ConcurrentBigMap<long, DirectoryArtifact> m_semiStableHashToDirectory = new ConcurrentBigMap<long, DirectoryArtifact>();

        /// <summary>
        /// PipGraphFragmentManager
        /// </summary>
        public PipGraphFragmentManager(LoggingContext loggingContext, PipExecutionContext context, IPipGraph pipGraph)
        {
            m_loggingContext = loggingContext;
            m_context = context;
            m_pipGraph = pipGraph;
        }

        /// <summary>
        /// Add a single pip graph fragment to the graph.
        /// </summary>
        public Task<bool> AddFragmentFileToGraph(int id, AbsolutePath filePath, int[] dependencyIds, string description)
        {
            var deserializer = new PipGraphFragmentSerializer(m_context, new PipGraphFragmentContext());

            Task<bool> readFragmentTask = Task.Run(() =>
            {
                Task.WaitAll(dependencyIds.Select(dependencyId => m_readFragmentTasks[dependencyId].Item2).ToArray());

                if (dependencyIds.Any(dependencyId => !m_readFragmentTasks[dependencyId].Item2.Result))
                {
                    return false;
                }

                try
                {
                    var result = deserializer.Deserialize(
                        filePath, 
                        (fragmentContext, provenance, pipId, pip) => AddPipToGraph(fragmentContext, provenance, pipId, pip), 
                        description);
                    Logger.Log.DeserializationStatsPipGraphFragment(m_loggingContext, deserializer.FragmentDescription, deserializer.Stats.ToString());

                    return result;
                }
                catch (Exception e) when (e is BuildXLException || e is IOException)
                {
                    Logger.Log.ExceptionOnDeserializingPipGraphFragment(m_loggingContext, filePath.ToString(m_context.PathTable), e.ToString());
                    return false;
                }
            });

            m_readFragmentTasks[id] = (deserializer, readFragmentTask);
            return readFragmentTask;
        }

        /// <summary>
        /// GetAllFragmentTasks
        /// </summary>
        public IReadOnlyCollection<(PipGraphFragmentSerializer, Task<bool>)> GetAllFragmentTasks()
        {
            return m_readFragmentTasks.Select(x => x.Value).ToList();
        }

        private bool AddPipToGraph(PipGraphFragmentContext fragmentContext, PipGraphFragmentProvenance provenance, PipId pipId, Pip pip)
        {
            try
            {
                PipId originalPipId = pip.PipId;
                pip.ResetPipId();
                bool added = false;

                switch (pip.PipType)
                {
                    case PipType.Module:
                        added = AddModulePip(pip as ModulePip);
                        break;
                    case PipType.SpecFile:
                        added = AddSpecFilePip(pip as SpecFilePip);
                        break;
                    case PipType.Value:
                        added = AddValuePip(pip as ValuePip);
                        break;
                    case PipType.Process:
                        (added, _) = AddProcessPip(fragmentContext, provenance, pipId, pip as Process);
                        break;
                    case PipType.CopyFile:
                        (added, _) = AddPip(pip as CopyFile, c => m_pipGraph.AddCopyFile(c, default));
                        break;
                    case PipType.WriteFile:
                        (added, _) = AddPip(pip as WriteFile, w => m_pipGraph.AddWriteFile(w, default));
                        break;
                    case PipType.SealDirectory:
                        (added, _) = AddSealDirectory(fragmentContext, pip as SealDirectory);
                        break;
                    case PipType.Ipc:
                        (added, _) = AddIpcPip(fragmentContext, provenance, pipId, pip as IpcPip);
                        break;
                    default:
                        Contract.Assert(false, "Pip graph fragment tried to add an unknown pip type to the graph: " + pip.PipType);
                        break;
                }

                if (!added)
                {
                    Logger.Log.FailedToAddFragmentPipToGraph(m_loggingContext, provenance.Description, pip.GetDescription(m_context));
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.Log.ExceptionOnAddingFragmentPipToGraph(m_loggingContext, provenance.Description, pip.GetDescription(m_context), e.ToString());
                return false;
            }
        }

        private (bool added, PipId newPipId) AddSealDirectory(PipGraphFragmentContext fragmentContext, SealDirectory sealDirectory)
        {
            Contract.Requires(fragmentContext != null);
            Contract.Requires(sealDirectory != null);
            Contract.Requires(sealDirectory.Kind != SealDirectoryKind.SharedOpaque, "Shared opaque is currently not supported");

            if (sealDirectory.Kind == SealDirectoryKind.Opaque)
            {
                // Output directories were added when the producing pips were added.
                return (true, PipId.Invalid);
            }

            var oldDirectory = sealDirectory.Directory;
            sealDirectory.ResetDirectoryArtifact();

            var (added, newPipId, newDirectoryArtifact) = AddSealDirectory(sealDirectory, d => m_pipGraph.AddSealDirectory(d, default));

            fragmentContext.AddDirectoryMapping(oldDirectory, newDirectoryArtifact);

            return (added, newPipId);
        }

        private (bool added, PipId newPipId) AddProcessPip(PipGraphFragmentContext fragmentContext, PipGraphFragmentProvenance provenance, PipId oldPipId, Process process)
        {
            Contract.Requires(fragmentContext != null);
            Contract.Requires(process != null);
            Analysis.IgnoreArgument(provenance, "Debugging purpose");

            var result = AddPip(process, p => m_pipGraph.AddProcess(p, default));

            if (process.ServiceInfo != null)
            {
                if (process.ServiceInfo.IsStartOrShutdownKind)
                {
                    // Debug purpose.
                    // Logger.Log.DebugFragment(m_loggingContext, $"{provenance.Description}: Service pip {process.ServiceInfo.Kind} [{process.GetDescription(m_context)}] map {oldPipId.ToString()} => {process.PipId.ToString()}");

                    fragmentContext.AddPipIdMapping(oldPipId, result.newPipId);
                }
            }

            return result;
        }

        private (bool added, PipId newPipId) AddIpcPip(PipGraphFragmentContext fragmentContext, PipGraphFragmentProvenance provenance, PipId oldPipId, IpcPip ipcPip)
        {
            Contract.Requires(fragmentContext != null);
            Contract.Requires(ipcPip != null);
            Analysis.IgnoreArgument(provenance, "Debugging purpose");

            var result = AddPip(ipcPip, i => m_pipGraph.AddIpcPip(i, default));

            if (ipcPip.IsServiceFinalization)
            {
                // Debug purpose.
                // Logger.Log.DebugFragment(m_loggingContext, $"{provenance.Description}: Finalization pip [{ipcPip.GetDescription(m_context)}] map {oldPipId.ToString()} => {ipcPip.PipId.ToString()}");

                // Service pip needs the mapping upon deserialization. 
                fragmentContext.AddPipIdMapping(oldPipId, result.newPipId);
            }

            return result;
        }

        /// <inheritdoc />
        public bool AddModulePip(ModulePip modulePip) => 
            m_modulePipUnify.GetOrAdd(
                modulePip.Module, 
                false, 
                (mid, data) => new Lazy<bool>(() => m_pipGraph.AddModule(modulePip))).Item.Value.Value;

        /// <inheritdoc />
        public bool AddSpecFilePip(SpecFilePip specFilePip) => 
            m_specFilePipUnify.GetOrAdd(
                specFilePip.SpecFile,
                false, 
                (file, data) => new Lazy<bool>(() => m_pipGraph.AddSpecFile(specFilePip))).Item.Value.Value;

        private bool AddValuePip(ValuePip valuePip) => 
            m_valuePipUnify.GetOrAdd(
                (valuePip.Symbol, valuePip.Qualifier),
                false,
                (file, data) => new Lazy<bool>(() => m_pipGraph.AddOutputValue(valuePip))).Item.Value.Value;

        private (bool added, PipId newPipId) AddPip<T>(T pip, Func<T, bool> addPip) where T : Pip
        {
            if (pip.SemiStableHash == 0)
            {
                return (addPip(pip), pip.PipId);
            }

            bool added = m_pipUnify.GetOrAdd(
                pip.SemiStableHash,
                0,
                (ssh, data) => new Lazy<bool>(() =>
                {
                    bool addInner = addPip(pip);
                    m_semiStableHashToPipId[pip.SemiStableHash] = pip.PipId; // PipId is set by addPip.
                    return addInner;
                })).Item.Value.Value;

            return (added, m_semiStableHashToPipId[pip.SemiStableHash]);
        }

        private (bool added, PipId newPipId, DirectoryArtifact newDirectoryArtifact) AddSealDirectory(SealDirectory sealDirectory, Func<SealDirectory, DirectoryArtifact> addSealDirectory)
        {
            if (sealDirectory.SemiStableHash == 0)
            {
                DirectoryArtifact d = addSealDirectory(sealDirectory);
                return (d.IsValid, sealDirectory.PipId, d);
            }

            bool added = m_pipUnify.GetOrAdd(
                sealDirectory.SemiStableHash,
                0,
                (ssh, data) => new Lazy<bool>(() =>
                {
                    DirectoryArtifact directory = addSealDirectory(sealDirectory);
                    m_semiStableHashToPipId[sealDirectory.SemiStableHash] = sealDirectory.PipId;
                    m_semiStableHashToDirectory[sealDirectory.SemiStableHash] = sealDirectory.Directory; // New directory artifact is set when adding seal directory.
                    return directory.IsValid;
                })).Item.Value.Value;

            return (added, m_semiStableHashToPipId[sealDirectory.SemiStableHash], m_semiStableHashToDirectory[sealDirectory.SemiStableHash]);
        }
    }
}
