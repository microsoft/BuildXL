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

        private readonly PipGraphFragmentContext m_fragmentContext;

        private readonly LoggingContext m_loggingContext;

        private readonly ConcurrentBigMap<ModuleId, Lazy<bool>> m_modulePipUnify = new ConcurrentBigMap<ModuleId, Lazy<bool>>();

        private readonly ConcurrentBigMap<FileArtifact, Lazy<bool>> m_specFilePipUnify = new ConcurrentBigMap<FileArtifact, Lazy<bool>>();

        private readonly ConcurrentBigMap<(FullSymbol, QualifierId), Lazy<bool>> m_valuePipUnify = new ConcurrentBigMap<(FullSymbol, QualifierId), Lazy<bool>>();

        private readonly ConcurrentBigMap<long, Lazy<bool>> m_pipUnify = new ConcurrentBigMap<long, Lazy<bool>>();

        private readonly ConcurrentBigMap<long, PipId> m_semiStableHashToPipId = new ConcurrentBigMap<long, PipId>();

        /// <summary>
        /// PipGraphFragmentManager
        /// </summary>
        public PipGraphFragmentManager(LoggingContext loggingContext, PipExecutionContext context, IPipGraph pipGraph)
        {
            m_loggingContext = loggingContext;
            m_context = context;
            m_pipGraph = pipGraph;
            m_fragmentContext = new PipGraphFragmentContext();
        }

        /// <summary>
        /// Add a single pip graph fragment to the graph.
        /// </summary>
        public Task<bool> AddFragmentFileToGraph(int id, AbsolutePath filePath, int[] dependencyIds, string description)
        {
            var deserializer = new PipGraphFragmentSerializer(m_context, m_fragmentContext);

            Task<bool> readFragmentTask = Task.Run(() =>
            {
                Task.WaitAll(dependencyIds.Select(dependencyId => m_readFragmentTasks[dependencyId].Item2).ToArray());

                if (dependencyIds.Any(dependencyId => !m_readFragmentTasks[dependencyId].Item2.Result))
                {
                    return false;
                }

                try
                {
                    return deserializer.Deserialize(filePath, pip => AddPipToGraph(description, pip), description);
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

        private bool AddPipToGraph(string description, Pip pip)
        {
            try
            {
                PipId originalPipId = pip.PipId;
                pip.ResetPipId();
                bool added = false;
                PipId newPipId = default;

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
                        var process = pip as Process;
                        (added, newPipId) = AddPip(process, p => m_pipGraph.AddProcess(p, default));

                        if (process.IsService)
                        {
                            m_fragmentContext.AddPipIdValueMapping(originalPipId.Value, newPipId.Value);
                        }

                        break;
                    case PipType.CopyFile:
                        (added, _) = AddPip(pip as CopyFile, c => m_pipGraph.AddCopyFile(c, default));
                        break;
                    case PipType.WriteFile:
                        (added, _) = AddPip(pip as WriteFile, w => m_pipGraph.AddWriteFile(w, default));
                        break;
                    case PipType.SealDirectory:
                        var sealDirectory = pip as SealDirectory;
                        if (sealDirectory.Kind == SealDirectoryKind.Opaque || sealDirectory.Kind == SealDirectoryKind.SharedOpaque)
                        {
                            return true;
                        }

                        added = true;
                        var oldDirectory = sealDirectory.Directory;
                        sealDirectory.ResetDirectoryArtifact();
                        var mappedDirectory = m_pipGraph.AddSealDirectory(sealDirectory, default);
                        m_fragmentContext.AddDirectoryMapping(oldDirectory, mappedDirectory);
                        break;
                    case PipType.Ipc:
                        (added, _) = AddPip(pip as IpcPip, i => m_pipGraph.AddIpcPip(i, default));
                        break;
                    default:
                        Contract.Assert(false, "Pip graph fragment tried to add an unknown pip type to the graph: " + pip.PipType);
                        break;
                }

                if (!added)
                {
                    Logger.Log.FailedToAddFragmentPipToGraph(m_loggingContext, description, pip.GetDescription(m_context));
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                Logger.Log.FailedToAddFragmentPipToGraph(m_loggingContext, description, pip.GetDescription(m_context));
                throw;
            }
        }

        private bool AddModulePip(ModulePip modulePip) => 
            m_modulePipUnify.GetOrAdd(
                modulePip.Module, 
                false, 
                (mid, data) => new Lazy<bool>(() => m_pipGraph.AddModule(modulePip))).Item.Value.Value;

        private bool AddSpecFilePip(SpecFilePip specFilePip) => 
            m_specFilePipUnify.GetOrAdd(
                specFilePip.SpecFile,
                false, 
                (file, data) => new Lazy<bool>(() => m_pipGraph.AddSpecFile(specFilePip))).Item.Value.Value;

        private bool AddValuePip(ValuePip valuePip) => 
            m_valuePipUnify.GetOrAdd(
                (valuePip.Symbol, valuePip.Qualifier),
                false,
                (file, data) => new Lazy<bool>(() => m_pipGraph.AddOutputValue(valuePip))).Item.Value.Value;

        private (bool, PipId) AddPip<T>(T pip, Func<T, bool> addPip) where T : Pip
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
                    m_semiStableHashToPipId[pip.SemiStableHash] = pip.PipId;
                    return addInner;
                })).Item.Value.Value;

            return (added, m_semiStableHashToPipId[pip.SemiStableHash]);
        }
    }
}
