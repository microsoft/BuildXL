// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Pip graph builder that supports graph patching.
    /// <see cref="PartiallyReloadGraph"/>
    /// <see cref="SetSpecsToIgnore"/>
    /// </summary>
    public sealed class PatchablePipGraph : IPipGraphBuilder
    {
        private static readonly HashSet<PipType> s_reloadablePipTypes = new HashSet<PipType>
        {
            PipType.CopyFile,
            PipType.WriteFile,
            PipType.Process,
            PipType.Ipc,
            PipType.SealDirectory,
        };

        private readonly IReadonlyDirectedGraph m_oldPipGraph;
        private readonly PipTable m_oldPipTable;
        private readonly PipGraph.Builder m_builder;
        private readonly int m_maxDegreeOfParallelism;
        private readonly ConcurrentBigMap<long, DirectoryArtifact> m_reloadedSealDirectories;
        private readonly ConcurrentBigMap<long, PipId> m_reloadedServicePips;
        private readonly ConcurrentBigMap<PipId, PipId> m_pipIdMap;
        private HashSet<AbsolutePath> m_specsToIgnore;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="oldPipGraph">Old pip graph.</param>
        /// <param name="oldPipTable">Old pip table.</param>
        /// <param name="graphBuilder">Pip graph builder to which to delegate all "add pip" operations.</param>
        /// <param name="maxDegreeOfParallelism">Max concurrency for graph reloading (<see cref="PartiallyReloadGraph"/>).</param>
        public PatchablePipGraph(
            IReadonlyDirectedGraph oldPipGraph,
            PipTable oldPipTable,
            PipGraph.Builder graphBuilder,
            int maxDegreeOfParallelism)
        {
            m_oldPipGraph = oldPipGraph;
            m_oldPipTable = oldPipTable;
            m_builder = graphBuilder;
            m_maxDegreeOfParallelism = maxDegreeOfParallelism;
            m_reloadedSealDirectories = new ConcurrentBigMap<long, DirectoryArtifact>();
            m_reloadedServicePips = new ConcurrentBigMap<long, PipId>();
            m_pipIdMap = new ConcurrentBigMap<PipId, PipId>();
        }

        /// <inheritdoc />
        public bool IsImmutable => m_builder.IsImmutable;

        /// <summary>
        /// Builds the graph: <see cref="PipGraph.Builder.Build"/>.
        /// </summary>
        public PipGraph Build()
        {
            return m_builder.Build();
        }

        /// <inheritdoc />
        public GraphPatchingStatistics PartiallyReloadGraph(HashSet<AbsolutePath> affectedSpecs)
        {
            Contract.Requires(affectedSpecs != null);

            var startTime = DateTime.UtcNow;
            var mustSkipDueToTransitivity = new VisitationTracker(m_oldPipGraph);
            var mustPostponeDueToServicePips = new VisitationTracker(m_oldPipGraph);
            var toPostpone = new SortedList<uint, Pip>();

            MultiValueDictionary<int, NodeId> nodesByHeight = m_oldPipGraph.TopSort();
            int numReloadedPips = 0;
            int numNotReloadablePips = 0;

            m_builder.SealDirectoryTable.StartPatching();

            // go one level at a time:
            //   - process nodes at the same height in parallel
            //   - postpone service-related pips because they must be added in the order of creation
            //     (because, for example, pip builder adds forward edges from service client to service finalization pips)
            for (int height = 0; height < nodesByHeight.Count; height++)
            {
                Parallel.ForEach(
                    nodesByHeight[height],
                    new ParallelOptions { MaxDegreeOfParallelism = m_maxDegreeOfParallelism },
                    body: (node) =>
                    {
                        var pipId = node.ToPipId();
                        var pipType = m_oldPipTable.GetPipType(pipId);

                        // skip non-reloadable pips
                        if (!s_reloadablePipTypes.Contains(pipType))
                        {
                            Interlocked.Increment(ref numNotReloadablePips);
                            return;
                        }

                        var pip = HydratePip(pipId);
                        AbsolutePath? producingSpec = GetProducerSpecFile(pip);

                        // check if this node must be skipped due to its spec file being affected
                        if (producingSpec == null || affectedSpecs.Contains(producingSpec.Value))
                        {
                            mustSkipDueToTransitivity.MarkVisited(node);
                            MarkAllDependentsVisited(mustSkipDueToTransitivity, node);
                            return;
                        }

                        // check if this pip is a service-related pip which must be postponed
                        if (ServiceKind(pip) != ServicePipKind.None)
                        {
                            SynchronizedAddToSortedList(toPostpone, pip);
                            MarkAllDependentsVisited(mustPostponeDueToServicePips, node);
                            return;
                        }

                        // check if this node must be postponed because it depends on a node that was already postponed
                        if (mustPostponeDueToServicePips.WasVisited(node))
                        {
                            SynchronizedAddToSortedList(toPostpone, pip);
                            MarkAllDependentsVisited(mustPostponeDueToServicePips, node);
                            return;
                        }

                        // check if this node must be skipped due to transitivity
                        ThrowIfVisited(mustSkipDueToTransitivity, pip);

                        // everything passed: reload this node
                        ReloadPip(pip, ref numReloadedPips);
                    });
            }

            // add postponed nodes sequentially in the order of creation
            foreach (var pip in toPostpone.Values)
            {
                var serviceKind = ServiceKind(pip);
                if (serviceKind != ServicePipKind.ServiceShutdown && serviceKind != ServicePipKind.ServiceFinalization)
                {
                    // 'shutdown' and 'finalization' are exception because of forward edges that are added
                    // during construction from service client pips to service shutdown/finalization pips.
                    ThrowIfVisited(mustSkipDueToTransitivity, pip);
                }

                ReloadPip(pip, ref numReloadedPips);
            }

            m_builder.SealDirectoryTable.FinishPatching();

            return new GraphPatchingStatistics
                   {
                ElapsedMilliseconds = (int)DateTime.UtcNow.Subtract(startTime).TotalMilliseconds,
                NumPipsReloaded = numReloadedPips,
                NumPipsAutomaticallyAdded = m_builder.PipCount - numReloadedPips,
                NumPipsNotReloadable = numNotReloadablePips,
                NumPipsSkipped = mustSkipDueToTransitivity.VisitedCount,
                AffectedSpecs = affectedSpecs,
            };
        }

        /// <inheritdoc />
        public void SetSpecsToIgnore([CanBeNull] IEnumerable<AbsolutePath> specsToIgnore)
        {
            m_specsToIgnore = specsToIgnore != null
                ? new HashSet<AbsolutePath>(specsToIgnore)
                : null;
        }

        /// <inheritdoc />
        public DirectoryArtifact ReserveSharedOpaqueDirectory(AbsolutePath directoryArtifactRoot)
        {
            return m_builder.ReserveSharedOpaqueDirectory(directoryArtifactRoot);
        }

        /// <inheritdoc />
        public int PipCount => m_builder.PipCount;

        /// <inheritdoc />
        public bool AddCopyFile(CopyFile copyFile, PipId valuePip)
        {
            Contract.Requires(copyFile != null, "Argument copyFile cannot be null");
            return PipAlreadyReloaded(copyFile) || m_builder.AddCopyFile(copyFile);
        }

        /// <inheritdoc />
        public bool AddIpcPip(IpcPip ipcPip, PipId valuePip)
        {
            Contract.Requires(ipcPip != null, "Argument pip cannot be null");
            return PipAlreadyReloaded(ipcPip) || m_builder.AddIpcPip(ipcPip, valuePip);
        }

        /// <inheritdoc />
        public bool AddProcess(Process process, PipId valuePip)
        {
            Contract.Requires(process != null, "Argument process cannot be null");
            return PipAlreadyReloaded(process) || m_builder.AddProcess(process, valuePip);
        }

        /// <inheritdoc />
        public bool AddWriteFile(WriteFile writeFile, PipId valuePip)
        {
            Contract.Requires(writeFile != null, "Argument writeFile cannot be null");
            return PipAlreadyReloaded(writeFile) || m_builder.AddWriteFile(writeFile);
        }

        /// <inheritdoc />
        public DirectoryArtifact AddSealDirectory(SealDirectory sealDirectory, PipId valuePip)
        {
            Contract.Requires(sealDirectory != null);
            if (PipAlreadyReloaded(sealDirectory))
            {
                DirectoryArtifact dir;
                return m_reloadedSealDirectories.TryGetValue(sealDirectory.SemiStableHash, out dir)
                    ? dir
                    : DirectoryArtifact.Invalid;
            }
            else
            {
                return m_builder.AddSealDirectory(sealDirectory);
            }
        }

        /// <inheritdoc />
        public bool AddModule(ModulePip module)
        {
            Contract.Requires(module != null, "Argument module cannot be null");
            return m_builder.AddModule(module);
        }

        /// <inheritdoc />
        public bool AddModuleModuleDependency(ModuleId moduleId, ModuleId dependency)
        {
            return m_builder.AddModuleModuleDependency(moduleId, dependency);
        }

        /// <inheritdoc />
        public bool AddOutputValue(ValuePip value)
        {
            Contract.Requires(value != null, "Argument outputValue cannot be null");
            return m_builder.AddOutputValue(value);
        }

        /// <inheritdoc />
        public bool AddSpecFile(SpecFilePip specFile)
        {
            Contract.Requires(specFile != null, "Argument specFile cannot be null");
            return m_builder.AddSpecFile(specFile);
        }

        /// <inheritdoc />
        public bool AddValueValueDependency(in ValuePip.ValueDependency valueDependency)
        {
            Contract.Requires(valueDependency.ParentIdentifier.IsValid);
            Contract.Requires(valueDependency.ChildIdentifier.IsValid);
            return m_builder.AddValueValueDependency(valueDependency);
        }

        /// <inheritdoc />
        public IIpcMoniker GetApiServerMoniker()
        {
            return m_builder.GetApiServerMoniker();
        }

        /// <inheritdoc />
        public IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip)
        {
            return m_builder.RetrievePipImmediateDependencies(pip);
        }

        /// <inheritdoc />
        public IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip)
        {
            return m_builder.RetrievePipImmediateDependents(pip);
        }

        /// <inheritdoc />
        public IEnumerable<Pip> RetrieveScheduledPips()
        {
            return m_builder.RetrieveScheduledPips();
        }

        private string GetPipDescription(Pip pip)
        {
            return pip.GetDescription(m_builder.Context);
        }

        private bool PipAlreadyReloaded(Pip pip)
        {
            if (m_specsToIgnore == null)
            {
                return false;
            }

            var producerSpec = GetProducerSpecFile(pip);
            if (producerSpec == null)
            {
                return false;
            }

            if (!m_specsToIgnore.Contains(producerSpec.Value))
            {
                return false;
            }

            if (IsServiceStartShutdownOrFinalizationPip(pip))
            {
                pip.PipId = m_reloadedServicePips[pip.SemiStableHash];
            }

            return true;
        }

        private void ReloadPip(Pip pip, ref int counter)
        {
            var oldPipId = pip.PipId;
            pip = TranslatePipIds(pip);
            pip.ResetPipId();
            bool success = AddPipToBuilder(pip, PipId.Invalid);
            Contract.Assert(success, "Expected to be able to reload pip");
            Interlocked.Increment(ref counter);

            if (IsServiceStartShutdownOrFinalizationPip(pip))
            {
                m_reloadedServicePips[pip.SemiStableHash] = pip.PipId;
                m_pipIdMap[oldPipId] = pip.PipId;
            }
        }

        private static bool IsServiceStartShutdownOrFinalizationPip(Pip pip)
        {
            var serviceKind = ServiceKind(pip);
            return
                serviceKind == ServicePipKind.Service ||
                serviceKind == ServicePipKind.ServiceShutdown ||
                serviceKind == ServicePipKind.ServiceFinalization;
        }

        private static ServicePipKind ServiceKind(Pip pip)
        {
            if (pip.PipType == PipType.Process)
            {
                return ServiceKind((Process)pip);
            }

            if (pip.PipType == PipType.Ipc)
            {
                return ((IpcPip)pip).IsServiceFinalization
                    ? ServicePipKind.ServiceFinalization
                    : ServicePipKind.ServiceClient;
            }

            return ServicePipKind.None;
        }

        private static ServicePipKind ServiceKind(Process pip)
        {
            return pip.ServiceInfo?.Kind ?? ServicePipKind.None;
        }

        private bool AddPipToBuilder(Pip pip, PipId valuePip)
        {
            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    return m_builder.AddCopyFile((CopyFile)pip, valuePip);
                case PipType.WriteFile:
                    return m_builder.AddWriteFile((WriteFile)pip, valuePip);
                case PipType.Ipc:
                    return m_builder.AddIpcPip((IpcPip)pip, valuePip);
                case PipType.Process:
                    return m_builder.AddProcess((Process)pip, valuePip);
                case PipType.SealDirectory:
                    var dir = m_builder.AddSealDirectory((SealDirectory)pip, valuePip);
                    m_reloadedSealDirectories.Add(pip.SemiStableHash, dir);
                    return dir.IsValid;
                default:
                    throw Contract.AssertFailure(I($"Cannot reload pip type {pip.PipType} to graph."));
            }
        }

        private Pip TranslatePipIds(Pip pip)
        {
            return
                pip.PipType == PipType.Ipc
                    ? TranslatePipIds((IpcPip) pip)
                    : pip.PipType == PipType.Process
                        ? TranslatePipIds((Process) pip)
                        : pip;
        }

        private Process TranslatePipIds(Process process)
        {
            return ServiceKind(process) == ServicePipKind.None
                ? process
                : process.Override(serviceInfo: TranslatePipIds(process.ServiceInfo));
        }

        private ServiceInfo TranslatePipIds(ServiceInfo info)
        {
            return new ServiceInfo(
                info.Kind,
                TranslatePipIds(info.ServicePipDependencies),
                TranslatePipId(info.ShutdownPipId),
                TranslatePipIds(info.FinalizationPipIds));
        }

        private IpcPip TranslatePipIds(IpcPip pip)
        {
            return pip.Override(servicePipDependencies: TranslatePipIds(pip.ServicePipDependencies));
        }

        private ReadOnlyArray<PipId> TranslatePipIds(ReadOnlyArray<PipId> pipIds)
        {
            return pipIds.Length == 0
                ? pipIds
                : ReadOnlyArray<PipId>.FromWithoutCopy(pipIds.SelectArray(id => TranslatePipId(id)));
        }

        private PipId TranslatePipId(PipId pipId)
        {
            return pipId.IsValid ? m_pipIdMap[pipId] : pipId;
        }

        private void MarkAllDependentsVisited(VisitationTracker nodeTracker, NodeId node)
        {
            MarkAllVisited(nodeTracker, GetDependentNodes(m_oldPipGraph, node));
        }

        private Pip HydratePip(PipId pipId)
        {
            return m_oldPipTable.HydratePip(pipId, PipQueryContext.SchedulerPartialGraphReload);
        }

        private void ThrowIfVisited(VisitationTracker tracker, Pip pip)
        {
            if (tracker.WasVisited(pip.PipId.ToNodeId()))
            {
                var pipDescription = GetPipDescription(pip);
                var producingSpecPath = GetProducerSpecFile(pip).Value.ToString(m_builder.Context.PathTable);
                throw new BuildXLException(
                    I($"'Affected specs' do not form a transitive closure:  [{pipDescription}] must be ") +
                    I($"skipped because one of its dependencies was already skipped, but its spec ({producingSpecPath}) ") +
                    I($"is not listed as affected."));
            }
        }

        /// <summary>
        /// Reads <paramref name="pip"/>'s provenance data and returns the absolute path of the spec
        /// where the pip is defined (or null, if no provenance is present).
        /// </summary>
        internal static AbsolutePath? GetProducerSpecFile(Pip pip)
        {
            return pip.Provenance?.Token.Path;
        }

        private static IEnumerable<NodeId> GetDependentNodes(IReadonlyDirectedGraph graph, NodeId node)
        {
            return graph.GetOutgoingEdges(node).Select(e => e.OtherNode);
        }

        private static void MarkAllVisited(VisitationTracker nodeTracker, IEnumerable<NodeId> nodes)
        {
            foreach (var node in nodes)
            {
                nodeTracker.MarkVisited(node);
            }
        }

        private static void SynchronizedAddToSortedList(SortedList<uint, Pip> list, Pip pip)
        {
            lock (list)
            {
                list.Add(pip.PipId.Value, pip);
            }
        }

        /// <inheritdoc />
        public bool ApplyCurrentOsDefaults(ProcessBuilder processBuilder)
        {
            return m_builder.ApplyCurrentOsDefaults(processBuilder);
        }
    }
}
