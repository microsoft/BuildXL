// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Manager which controls adding pip fragments to the graph.
    /// </summary>
    public class PipGraphFragmentManager : IPipGraphFragmentManager
    {
        private ConcurrentDictionary<int, (PipGraphFragmentSerializer, Task<bool>)> m_readFragmentTasks = new ConcurrentDictionary<int, (PipGraphFragmentSerializer, Task<bool>)>();

        private IPipGraph m_pipGraph;

        private PipExecutionContext m_context;

        private PipGraphFragmentContext m_fragmentContext;

        private LoggingContext m_loggingContext;

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
            var deserializer = new PipGraphFragmentSerializer();
            Task<bool> readFragmentTask = Task.Run(() =>
            {
                Task.WaitAll(dependencyIds.Select(dependencyId => m_readFragmentTasks[dependencyId].Item2).ToArray());
                return deserializer.Deserialize(description, m_context, m_fragmentContext, filePath, (Pip p) => AddPipToGraph(description, p));
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
                switch (pip.PipType)
                {
                    case PipType.Module:
                        var modulePip = pip as ModulePip;
                        added = m_pipGraph.AddModule(modulePip);
                        break;
                    case PipType.SpecFile:
                        var specFilePip = pip as SpecFilePip;
                        added = m_pipGraph.AddSpecFile(specFilePip);
                        break;
                    case PipType.Value:
                        var valuePIp = pip as ValuePip;
                        added = m_pipGraph.AddOutputValue(valuePIp);
                        break;
                    case PipType.Process:
                        var p = pip as Process;
                        added = m_pipGraph.AddProcess(p, default);
                        if (p.IsService)
                        {
                            m_fragmentContext.AddPipIdValueMapping(originalPipId.Value, p.PipId.Value);
                        }

                        break;
                    case PipType.CopyFile:
                        var copyFile = pip as CopyFile;
                        added = m_pipGraph.AddCopyFile(copyFile, default);
                        break;
                    case PipType.WriteFile:
                        var writeFile = pip as WriteFile;
                        added = m_pipGraph.AddWriteFile(writeFile, default);
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
                        var ipcPip = pip as IpcPip;
                        m_pipGraph.AddIpcPip(ipcPip, default);
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
    }
}
