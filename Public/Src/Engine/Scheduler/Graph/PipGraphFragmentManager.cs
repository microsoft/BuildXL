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
        private ConcurrentDictionary<string, (PipGraphFragmentSerializer, Task<bool>)> m_readFragmentTasks = new ConcurrentDictionary<string, (PipGraphFragmentSerializer, Task<bool>)>();

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
        /// <param name="fragmentName">Name of the fragment, for use with specifying dependencyNames</param>
        /// <param name="filePath">Path of binary fragment file</param>
        /// <param name="dependencyNames">Names of fragments it depends on</param>
        /// <returns>Task that returns true if successfully added all pips in the fragment to the graph</returns>
        public Task<bool> AddFragmentFileToGraph(string fragmentName, AbsolutePath filePath, string[] dependencyNames)
        {
            foreach (var dependencyName in dependencyNames)
            {
                Contract.Assert(m_readFragmentTasks.ContainsKey(dependencyName), "Fragment has not yet been added for dependency: " + dependencyName);
            }

            var deserializer = new PipGraphFragmentSerializer();
            Task<bool> readFragmentTask = Task.Run(() =>
            {
                Task.WaitAll(dependencyNames.Select(dependencyName => m_readFragmentTasks[dependencyName].Item2).ToArray());
                return deserializer.Deserialize(fragmentName, m_context, m_fragmentContext, filePath, (Pip p) => AddPipToGraph(fragmentName, p));
            });

            m_readFragmentTasks[fragmentName] = (deserializer, readFragmentTask);
            return readFragmentTask;
        }

        /// <summary>
        /// GetAllFragmentTasks
        /// </summary>
        public IReadOnlyCollection<(PipGraphFragmentSerializer, Task<bool>)> GetAllFragmentTasks()
        {
            return m_readFragmentTasks.Select(x => x.Value).ToList();
        }

        private bool AddPipToGraph(string fragmentName, Pip pip)
        {
            try
            {
                PipId originalPipId = pip.PipId;
                pip.ResetPipIdForTesting();
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
                            m_fragmentContext.AddPipIdMapping(originalPipId.Value, p.PipId.Value);
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
                }

                if (!added)
                {
                    Logger.Log.FailedToAddFragmentPipToGraph(m_loggingContext, fragmentName, pip.GetDescription(m_context));
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                Logger.Log.FailedToAddFragmentPipToGraph(m_loggingContext, fragmentName, pip.GetDescription(m_context));
                throw;
            }
        }
    }
}
