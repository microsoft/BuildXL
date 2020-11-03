// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Tracing;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Handles choose worker computation for <see cref="Scheduler"/>
    /// </summary>
    internal class ChooseWorkerCpu : ChooseWorkerContext
    {
        /// <summary>
        /// Use this constant to guess the missing file size.
        /// </summary>
        private const long TypicalFileLength = 4096;

        private const double MaxLoadFactor = 2;

        private readonly ReadOnlyArray<double> m_workerBalancedLoadFactors;

        private readonly FileContentManager m_fileContentManager;

        private readonly PipTable m_pipTable;

        private readonly ObjectPool<PipSetupCosts> m_pipSetupCostPool;

        private readonly SemaphoreSlim m_chooseWorkerMutex = TaskUtilities.CreateMutex();

        private RunnablePip m_lastIterationBlockedPip;

        /// <summary>
        /// The last pip blocked on resources
        /// </summary>
        public RunnablePip LastBlockedPip { get; private set; }

        /// <summary>
        /// The last resource limiting acquisition of a worker. 
        /// </summary>
        /// <remarks>
        /// If it is null, there is no resource limiting the worker.
        /// </remarks>
        public WorkerResource? LastConcurrencyLimiter { get; set; }

        /// <summary>
        /// The number of choose worker iterations
        /// </summary>
        public ulong ChooseIterations { get; private set; }

        /// <summary>
        /// The total time spent choosing a worker
        /// </summary>
        public int ChooseSeconds => (int)m_chooseTime.TotalSeconds;

        private TimeSpan m_chooseTime = TimeSpan.Zero;

        /// <summary>
        /// TEMPORARY HACK: Tracks outputs of executed processes which are the sole considered files for IPC pip affinity. This is intended to
        /// address a bug where IPC pips can disproportionately get assigned to a worker simply because it has materialized
        /// cached inputs
        /// </summary>
        private readonly ContentTrackingSet m_executedProcessOutputs;

        protected readonly Dictionary<WorkerResource, BoxRef<ulong>> m_limitingResourceCounts = new Dictionary<WorkerResource, BoxRef<ulong>>();

        private int m_totalAcquiredProcessSlots;

        private int m_totalProcessSlots;

        private readonly IScheduleConfiguration m_scheduleConfig;

        private readonly Dictionary<ModuleId, (int NumPips, List<Worker> Workers)> m_moduleWorkerMapping;
        private readonly PathTable m_pathTable;

        /// <summary>
        /// Number of modules exceeding max workers due to availability
        /// </summary>
        private int m_numModulesExceedingMaxWorkers;

        public ChooseWorkerCpu(
            LoggingContext loggingContext,
            IScheduleConfiguration config,
            IReadOnlyList<Worker> workers,
            IPipQueue pipQueue,
            PipGraph pipGraph,
            FileContentManager fileContentManager,
            PathTable pathTable,
            Dictionary<ModuleId, (int, List<Worker>)> moduleWorkerMapping) : base(loggingContext, workers, pipQueue, DispatcherKind.ChooseWorkerCpu, config.MaxChooseWorkerCpu, config.ModuleAffinityEnabled())
        {
            m_pipTable = pipGraph.PipTable;
            m_executedProcessOutputs = new ContentTrackingSet(pipGraph);
            m_fileContentManager = fileContentManager;
            m_pipSetupCostPool = new ObjectPool<PipSetupCosts>(
                () => new PipSetupCosts(this), 
                costs => costs, 
                size: config.ModuleAffinityEnabled() ? config.MaxChooseWorkerCpu : config.MaxChooseWorkerCpu * workers.Count);
            m_scheduleConfig = config;

            m_pathTable = pathTable;

            if (config.ModuleAffinityEnabled())
            {
                m_moduleWorkerMapping = moduleWorkerMapping;
                // We use load-factor as 2 by default in case of module affinity. There is no rationale behind that. It is just based on MaxLoadFactor. 
                m_workerBalancedLoadFactors = ReadOnlyArray<double>.FromWithoutCopy(EngineEnvironmentSettings.BuildXLModuleAffinityMultiplier.Value ?? MaxLoadFactor);
            }
            else
            {
                // Workers are given progressively heavier loads when acquiring resources
                // in order to load balance between workers by
                m_workerBalancedLoadFactors = ReadOnlyArray<double>.FromWithoutCopy(0.25, 0.5, 1, 1.5, MaxLoadFactor);
            }
        }

        /// <summary>
        /// Reports the outputs of process execution after post process to distinguish between produced outputs and
        /// outputs from cache when assigning affinity for IPC pips
        /// </summary>
        public void ReportProcessExecutionOutputs(ProcessRunnablePip runnableProcess, ExecutionResult executionResult)
        {
            Contract.Assert(runnableProcess.Step == PipExecutionStep.PostProcess);

            if (executionResult.Converged || runnableProcess.Process.IsStartOrShutdownKind)
            {
                // Converged results are cached so they are not considered for as execution outputs
                // Service start/shutdown pip outputs are not considered for IPC pip affinity
                return;
            }

            foreach (var directoryOutput in executionResult.DirectoryOutputs)
            {
                m_executedProcessOutputs.Add(directoryOutput.directoryArtifact);
            }

            foreach (var output in executionResult.OutputContent)
            {
                m_executedProcessOutputs.Add(output.fileArtifact);
            }
        }

        /// <summary>
        /// Choose a worker based on setup cost
        /// </summary>
        protected override async Task<Worker> ChooseWorkerCore(RunnablePip runnablePip)
        {
            using (var pooledPipSetupCost = m_pipSetupCostPool.GetInstance())
            {
                var pipSetupCost = pooledPipSetupCost.Instance;
                if (m_scheduleConfig.EnableSetupCostWhenChoosingWorker)
                {
                    pipSetupCost.EstimateAndSortSetupCostPerWorker(runnablePip);
                }
                else
                {
                    pipSetupCost.InitializeWorkerSetupCost(runnablePip.Pip);
                }

                using (await m_chooseWorkerMutex.AcquireAsync())
                {
                    var startTime = TimestampUtilities.Timestamp;
                    ChooseIterations++;

                    WorkerResource? limitingResource;
                    var chosenWorker = ChooseWorker(runnablePip, pipSetupCost.WorkerSetupCosts, out limitingResource);
                    if (chosenWorker == null)
                    {
                        m_lastIterationBlockedPip = runnablePip;
                        LastBlockedPip = runnablePip;
                        var limitingResourceCount = m_limitingResourceCounts.GetOrAdd(limitingResource.Value, k => new BoxRef<ulong>());
                        limitingResourceCount.Value++;
                    }
                    else
                    {
                        m_lastIterationBlockedPip = null;
                    }
                    
                    // If a worker is successfully chosen, then the limiting resouce would be null.
                    LastConcurrencyLimiter = limitingResource;

                    m_chooseTime += TimestampUtilities.Timestamp - startTime;
                    return chosenWorker;
                }
            }
        }

        /// <summary>
        /// Choose a worker based on setup cost
        /// </summary>
        private Worker ChooseWorker(RunnablePip runnablePip, WorkerSetupCost[] workerSetupCosts, out WorkerResource? limitingResource)
        {
            if (MustRunOnMaster(runnablePip))
            {
                // This is shortcut for the single-machine builds and distributed workers.
                return LocalWorker.TryAcquire(runnablePip, out limitingResource, loadFactor: MaxLoadFactor) ? LocalWorker : null;
            }

            ResetStatus();

            var pendingWorkerSelectionPipCount = PipQueue.GetNumQueuedByKind(DispatcherKind.ChooseWorkerCpu) + PipQueue.GetNumRunningByKind(DispatcherKind.ChooseWorkerCpu);

            bool loadBalanceWorkers = false;
            if (runnablePip.PipType == PipType.Process)
            {
                if (pendingWorkerSelectionPipCount + m_totalAcquiredProcessSlots < (m_totalProcessSlots / 2))
                {
                    // When there is a limited amount of work (less than half the total capacity of
                    // the all the workers). We load balance so that each worker gets
                    // its share of the work and the work can complete faster
                    loadBalanceWorkers = true;
                }
            }

            long setupCostForBestWorker = workerSetupCosts[0].SetupBytes;

            limitingResource = null;
            foreach (var loadFactor in m_workerBalancedLoadFactors)
            {
                if (!loadBalanceWorkers && loadFactor < 1)
                {
                    // Not load balancing so allow worker to be filled to capacity at least
                    continue;
                }

                var moduleId = runnablePip.Pip.Provenance.ModuleId;

                if (m_scheduleConfig.ModuleAffinityEnabled() &&
                    m_moduleWorkerMapping.TryGetValue(moduleId, out var assignedWorkers) &&
                    assignedWorkers.Workers.Count > 0)
                {
                    // If there are no workers assigned to the module, proceed with normal chooseworker logic.
                    return ChooseWorkerForModuleAffinity(runnablePip, workerSetupCosts, loadFactor, out limitingResource);
                }

                for (int i = 0; i < workerSetupCosts.Length; i++)
                {
                    var worker = workerSetupCosts[i].Worker;
                    if (worker.TryAcquire(runnablePip, out limitingResource, loadFactor: loadFactor))
                    {
                        runnablePip.Performance.SetInputMaterializationCost(ByteSizeFormatter.ToMegabytes((ulong)setupCostForBestWorker), ByteSizeFormatter.ToMegabytes((ulong)workerSetupCosts[i].SetupBytes));
                        return worker;
                    }
                }
            }

            return null;
        }

        private Worker ChooseWorkerForModuleAffinity(RunnablePip runnablePip, WorkerSetupCost[] workerSetupCosts, double loadFactor, out WorkerResource? limitingResource)
        {
            limitingResource = null;

            var moduleId = runnablePip.Pip.Provenance.ModuleId;
            var assignedWorkers = m_moduleWorkerMapping[moduleId].Workers;

            int numAssignedWorkers = assignedWorkers.Count;
            foreach (var worker in assignedWorkers.OrderBy(a => a.AcquiredProcessSlots))
            {
                if (worker.TryAcquire(runnablePip, out limitingResource, loadFactor: loadFactor, moduleAffinityEnabled: true))
                {
                    return worker;
                }
            }

            bool isAnyAvailable = assignedWorkers.Any(a => a.IsAvailable);

            var limitingResourceForAssigned = limitingResource;

            var potentialWorkers = workerSetupCosts
                .Select(a => a.Worker)
                .Except(assignedWorkers)
                .Where(a => a.IsAvailable)
                .Where(a => a.AcquiredProcessSlots < a.TotalProcessSlots)
                .Where(a => a.AcquiredMaterializeInputSlots < a.TotalMaterializeInputSlots)
                .OrderBy(a => a.AcquiredProcessSlots);

            // If there are no assigned workers in "Available status", we should choose one regardless not to block scheduler.
            if (numAssignedWorkers < m_scheduleConfig.MaxWorkersPerModule || !isAnyAvailable)
            {
                foreach (var worker in potentialWorkers)
                {
                    if (worker.TryAcquire(runnablePip, out limitingResource, loadFactor: loadFactor, moduleAffinityEnabled: true))
                    {
                        assignedWorkers.Add(worker);
                        Logger.Log.AddedNewWorkerToModuleAffinity(LoggingContext, $"Added a new worker due to {(isAnyAvailable ? "Rebalance" : "Availability")} - {limitingResourceForAssigned}: {runnablePip.Description} - {moduleId.Value.ToString(m_pathTable.StringTable)} - WorkerId: {worker.WorkerId}, MaterializeInputSlots: {worker.AcquiredMaterializeInputSlots}, AcquiredProcessSlots: {worker.AcquiredProcessSlots}");

                        if (assignedWorkers.Count > m_scheduleConfig.MaxWorkersPerModule)
                        {
                            // If none of the assigned workers are available due to worker connection issues or earlyWorkerRelease, 
                            // we need to add a new worker even though it exceeds the max worker count per module. 
                            // This is to prevent Scheduler from being blocked.
                            m_numModulesExceedingMaxWorkers++;
                        }
                        
                        return worker;
                    }
                }
            }
            else if (potentialWorkers.Any())
            {
                limitingResource = WorkerResource.ModuleAffinity;
            }

            return null;
        }

        protected void ResetStatus()
        {
            m_totalAcquiredProcessSlots = 0;
            m_totalProcessSlots = 0;

            for (int i = 0; i < Workers.Count; i++)
            {
                if (Workers[i].IsAvailable)
                {
                    m_totalAcquiredProcessSlots += Workers[i].AcquiredProcessSlots;
                    m_totalProcessSlots += Workers[i].TotalProcessSlots;
                }
            }
        }

        internal void UnpauseChooseWorkerQueueIfEnqueuingNewPip(RunnablePip runnablePip, DispatcherKind nextQueue)
        {
            // If enqueuing a new highest priority pip to queue
            if (nextQueue == DispatcherKind.ChooseWorkerCpu)
            {
                if (runnablePip != m_lastIterationBlockedPip)
                {
                    TogglePauseChooseWorkerQueue(pause: false);
                }

                // Capture the sequence number which will be used to compare if ChooseWorker queue is paused
                // waiting for resources for this pip to avoid race conditions where pip cannot acquire worker
                // resources become available then queue is paused potentially indefinitely (not likely but theoretically
                // possilbe)
                runnablePip.ChooseWorkerSequenceNumber = Volatile.Read(ref WorkerEnableSequenceNumber);
            }
        }

        public void LogStats(Dictionary<string, long> statistics)
        {
            var limitingResourceStats = m_limitingResourceCounts.ToDictionary(kvp => kvp.Key.ToString(), kvp => (long)kvp.Value.Value);

            foreach (var kvp in limitingResourceStats)
            {
                statistics.Add($"LimitingResource_{kvp.Key}", kvp.Value);
            }

            statistics.Add($"NumModulesExceedingMaxWorkers", m_numModulesExceedingMaxWorkers);

            Logger.Log.LimitingResourceStatistics(LoggingContext, limitingResourceStats);
        }

        private class PipSetupCosts
        {
            public readonly WorkerSetupCost[] WorkerSetupCosts;
            private readonly ChooseWorkerCpu m_context;
            private readonly HashSet<ContentHash> m_visitedHashes = new HashSet<ContentHash>();

            public PipSetupCosts(ChooseWorkerCpu context)
            {
                m_context = context;
                WorkerSetupCosts = new WorkerSetupCost[context.Workers.Count];
            }

            /// <summary>
            /// The result contains estimated amount of work for each worker
            /// </summary>
            public void EstimateAndSortSetupCostPerWorker(RunnablePip runnablePip)
            {
                if (m_context.MustRunOnMaster(runnablePip))
                {
                    // Only estimate setup costs for pips which can execute remotely
                    return;
                }

                var pip = runnablePip.Pip;

                InitializeWorkerSetupCost(pip);

                // The block below collects process input file artifacts and hashes
                // Currently there is no logic to keep from sending the same hashes twice
                // Consider a model where hashes for files are requested by worker
                using (var pooledFileSet = Pools.GetFileArtifactSet())
                {
                    var pipInputs = pooledFileSet.Instance;
                    m_context.m_fileContentManager.CollectPipInputsToMaterialize(
                        m_context.m_pipTable,
                        pip,
                        pipInputs,

                        // Service pip cost is not considered as this is shared among many clients and is a one-time cost per worker
                        serviceFilter: servicePipId => false);

                    m_visitedHashes.Clear();

                    foreach (var fileInput in pipInputs)
                    {
                        if (!fileInput.IsOutputFile)
                        {
                            continue;
                        }

                        if (pip.PipType == PipType.Ipc && !m_context.m_executedProcessOutputs.Contains(fileInput))
                        {
                            // Only executed process outputs are considered for IPC pip affinity
                            continue;
                        }

                        FileContentInfo fileContentInfo = m_context.m_fileContentManager.GetInputContent(fileInput).FileContentInfo;
                        if (!m_visitedHashes.Add(fileContentInfo.Hash))
                        {
                            continue;
                        }

                        // How many bytes we have to copy.
                        long fileSize = fileContentInfo.HasKnownLength ? fileContentInfo.Length : TypicalFileLength;

                        for (int idx = 0; idx < m_context.Workers.Count; ++idx)
                        {
                            if (!WorkerSetupCosts[idx].Worker.HasContent(fileInput))
                            {
                                WorkerSetupCosts[idx].SetupBytes += fileSize;
                            }
                        }
                    }
                }

                Array.Sort(WorkerSetupCosts);
            }

            public void InitializeWorkerSetupCost(Pip pip)
            {
                for (int i = 0; i < m_context.Workers.Count; i++)
                {
                    var worker = m_context.Workers[i];
                    WorkerSetupCosts[i] = new WorkerSetupCost()
                    {
                        Worker = worker,
                        AcquiredSlots = pip.PipType == PipType.Ipc ? worker.AcquiredIpcSlots : worker.AcquiredProcessSlots
                    };
                }
            }
        }

        /// <summary>
        /// Worker setup cost structure.
        /// </summary>
        private struct WorkerSetupCost : IComparable<WorkerSetupCost>
        {
            /// <summary>
            /// Total bytes that need to be setup.
            /// </summary>
            public long SetupBytes { get; set; }

            /// <summary>
            /// Number of acquired slots
            /// </summary>
            /// <remarks>
            /// For IPC pips, this means acquired IPC slots, and for process pips, it means acquired process slots.
            /// </remarks>
            public int AcquiredSlots { get; set; }

            /// <summary>
            /// The associated worker
            /// </summary>
            public Worker Worker { get; set; }

            /// <inheritdoc />
            public int CompareTo(WorkerSetupCost other)
            {
                var result = SetupBytes.CompareTo(other.SetupBytes);
                return result == 0 ? AcquiredSlots.CompareTo(other.AcquiredSlots) : result;
            }
        }
    }
}
