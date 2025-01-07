// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Tracing;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Threading;
using System.Runtime.CompilerServices;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Handles choose worker computation for <see cref="Scheduler"/>
    /// </summary>
    internal class ChooseWorkerCpu
    {
        /// <summary>
        /// Use this constant to guess the missing file size.
        /// </summary>
        private const long TypicalFileLength = 4096;

        /// <summary>
        /// Tracks a sequence number in order to verify if workers resources have changed since
        /// the time it was checked. This is used by ChooseWorker to decide if the worker queue can
        /// be paused
        /// </summary>
        private int m_workerEnableSequenceNumber = 0;

        /// <summary>
        /// Determines the maximum load factor for oversubscribing workers. Allows extra pips to be assigned to workers. 
        /// These extra pips can materialize inputs while waiting in the CPU dispatcher for available slots in the assigned worker.
        /// If <see cref="IScheduleConfiguration.UseHistoricalCpuThrottling"/> is enabled, we set `MaxLoadFactor` to a very high number like 10, so we do not get throttled by the available slots.
        /// When this feature is used, the available CPU slots are not important as the max level of concurrency will be decided based on the cpu semaphore.
        /// If `DeprioritizeOnSemaphoreConstraints` is enabled, we set `MaxLoadFactor` to 1. This is because we do not want to lower the priority of pips when they are throttled by memory constraints.
        /// When oversubscribing is allowed (i.e., `MaxLoadFactor` > 1), the limiting resource becomes memory or other semaphores rather than the available worker slots.
        /// 
        /// **Note:** If module affinity is enabled, `MaxLoadFactor` can be set to a custom value defined in `ModuleAffinityLoadFactor`. Otherwise, it defaults to 2.
        /// In the builds where module affinity is enabled, the materialization cost is high, so oversubscribing the workers helps the scheduler utilize the workers more efficiently.
        /// </summary>
        private double MaxLoadFactor =>
            m_scheduleConfig.UseHistoricalCpuThrottling ? 10 :
            (m_scheduleConfig.DeprioritizeOnSemaphoreConstraints || !IsOrchestrator ? 1 :
            (m_moduleAffinityEnabled ? m_scheduleConfig.ModuleAffinityLoadFactor.Value : 2));

        private readonly FileContentManager m_fileContentManager;
        private readonly PipTable m_pipTable;
        private readonly ObjectPool<PipSetupCosts> m_pipSetupCostPool;
        private RunnablePip m_lastIterationBlockedPip;
        private readonly ConcurrentDictionary<WorkerResource, BoxRef<ulong>> m_limitingResourceCounts = new ConcurrentDictionary<WorkerResource, BoxRef<ulong>>();
        private readonly IScheduleConfiguration m_scheduleConfig;
        private readonly Dictionary<ModuleId, (int NumPips, bool[] Workers)> m_moduleWorkerMapping;
        private readonly LoggingContext m_loggingContext;
        private readonly IPipQueue m_pipQueue;
        private readonly IReadOnlyList<Worker> m_workers;
        private readonly LocalWorker m_localWorker;
        private readonly int m_maxParallelDegree;
        private readonly ReadWriteLock m_chooseWorkerTogglePauseLock = ReadWriteLock.Create();
        private readonly bool m_moduleAffinityEnabled;

        /// <summary>
        /// Whether there is any available remote worker.
        /// </summary>
        private bool IsOrchestrator => m_workers.Count > 1;

        /// <summary>
        /// The last resource limiting acquisition of a worker. 
        /// </summary>
        /// <remarks>
        /// If it is null, there is no resource limiting the worker.
        /// </remarks>
        public WorkerResource? LastConcurrencyLimiter { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ChooseWorkerCpu(
            LoggingContext loggingContext,
            IScheduleConfiguration scheduleConfig,
            IReadOnlyList<Worker> workers,
            IPipQueue pipQueue,
            PipGraph pipGraph,
            FileContentManager fileContentManager,
            Dictionary<ModuleId, (int, bool[])> moduleWorkerMapping)
        {
            m_pipTable = pipGraph.PipTable;
            m_fileContentManager = fileContentManager;
            m_pipSetupCostPool = new ObjectPool<PipSetupCosts>(
                () => new PipSetupCosts(this),
                costs => costs);

            m_scheduleConfig = scheduleConfig;
            m_workers = workers;
            m_pipQueue = pipQueue;
            m_localWorker = (LocalWorker)workers[0];
            m_loggingContext = loggingContext;
            m_maxParallelDegree = scheduleConfig.MaxChooseWorkerCpu;
            m_moduleAffinityEnabled = scheduleConfig.ModuleAffinityEnabled();

            if (scheduleConfig.ModuleAffinityEnabled())
            {
                m_moduleWorkerMapping = moduleWorkerMapping;
            }
        }

        public void SetUpWorkerResourceListeners()
        {
            foreach (var worker in m_workers)
            {
                worker.ResourcesChanged += OnWorkerResourcesChanged;
            }
        }

        public Worker ChooseWorker(ProcessRunnablePip runnablePip)
        {
            Worker chosenWorker = null;
            WorkerResource? limitingResource = null;
            var moduleId = runnablePip.Pip.Provenance.ModuleId;

            if (!IsOrchestrator || runnablePip.MustRunOnOrchestrator)
            {
                // This is shortcut for the single-machine builds.
                chosenWorker = m_localWorker.TryAcquireProcess(runnablePip, out limitingResource, loadFactor: MaxLoadFactor) ? m_localWorker : null;
            }
            else if (m_moduleAffinityEnabled && m_moduleWorkerMapping.TryGetValue(moduleId, out var assignedWorkers))
            {
                chosenWorker = ChooseWorkerWithModuleAffinity(runnablePip, assignedWorkers.Workers, out limitingResource);
            }
            else
            {
                using (var pooledPipSetupCost = m_pipSetupCostPool.GetInstance())
                {
                    var pipSetupCost = pooledPipSetupCost.Instance;
                    pipSetupCost.CalculateSetupCostPerWorker(runnablePip, m_scheduleConfig);
                    chosenWorker = ChooseWorkerWithSetupCost(runnablePip, pipSetupCost.WorkerSetupCosts, out limitingResource);
                }
            }

            // If a worker is successfully chosen, then the limiting resource would be null.
            LastConcurrencyLimiter = limitingResource;

            if (chosenWorker == null)
            {
                var limitingResourceCount = m_limitingResourceCounts.GetOrAdd(limitingResource.Value, k => new BoxRef<ulong>());
                limitingResourceCount.Value++;

                runnablePip.IsWaitingForWorker = true;
                m_lastIterationBlockedPip = runnablePip;

                if (m_scheduleConfig.DeprioritizeOnSemaphoreConstraints && 
                    limitingResource.Value.PrecedenceType == WorkerResource.Precedence.SemaphorePrecedence)
                {
                    // Scheduling dilemma: prioritization conflicts with resource constraints.
                    // When a pip can't be assigned to any worker due to semaphore constraints,
                    // we lower its priority so that other pips without semaphore requirements can be scheduled.
                    runnablePip.ChangePriority((int)Math.Round(runnablePip.Priority * 0.9));

                    // With this feature, we do not block ChooseWorkerCpu if the limiting resource is a custom resource: semaphore.
                    return null;
                }

                TogglePauseChooseWorkerQueue(pause: true, blockedPip: runnablePip);
            }
            else
            {
                runnablePip.IsWaitingForWorker = false;
                m_lastIterationBlockedPip = null;
                TogglePauseChooseWorkerQueue(pause: false);
            }

            return chosenWorker;
        }

        /// <summary>
        /// Choose a worker based on setup cost
        /// </summary>
        private Worker ChooseWorkerWithSetupCost(ProcessRunnablePip runnablePip, WorkerSetupCost[] workerSetupCosts, out WorkerResource? limitingResource)
        {
            limitingResource = null;

            foreach (var workerSetupCost in workerSetupCosts)
            {
                var worker = workerSetupCost.Worker;
                if (worker.TryAcquireProcess(runnablePip, out limitingResource, loadFactor: MaxLoadFactor))
                {
                    return worker;
                }
            }

            if (limitingResource == null)
            {
                limitingResource = WorkerResource.AvailableProcessSlots;
            }

            return null;
        }

        private Worker ChooseWorkerWithModuleAffinity(ProcessRunnablePip runnablePip, bool[] assignedWorkers, out WorkerResource? limitingResource)
        {
            limitingResource = WorkerResource.ModuleAffinity;

            int availableWorkerCount = 0;
            for (int i = 0; i < assignedWorkers.Length; i++)
            {
                if (assignedWorkers[i])
                {
                    if (m_workers[i].IsAvailable)
                    {
                        availableWorkerCount++;
                    }

                    if (m_workers[i].TryAcquireProcess(runnablePip, out limitingResource, loadFactor: MaxLoadFactor, moduleAffinityEnabled: true))
                    {
                        return m_workers[i];
                    }
                }
            }

            // If there are no assigned workers in "Available status", we should choose one regardless not to block scheduler.
            if (availableWorkerCount < m_scheduleConfig.MaxWorkersPerModule)
            {
                for (int i = 0; i < assignedWorkers.Length; i++)
                {
                    if (!assignedWorkers[i])
                    {
                        if (m_workers[i].TryAcquireProcess(runnablePip, out limitingResource, loadFactor: MaxLoadFactor, moduleAffinityEnabled: true))
                        {
                            assignedWorkers[i] = true;
                            return m_workers[i];
                        }
                    }
                }
            }

            return null;
        }

        public void TogglePauseChooseWorkerQueue(bool pause, RunnablePip blockedPip = null)
        {
            Contract.Requires(pause == (blockedPip != null), "Must specify blocked pip if and only if pausing the choose worker queue");

            // Attempt to pause the choose worker queue since resources are not available
            // Do not pause choose worker queue when module affinity is enabled.
            if (EngineEnvironmentSettings.DoNotPauseChooseWorkerThreads || m_moduleAffinityEnabled)
            {
                return;
            }

            if (pause)
            {
                if (blockedPip.IsLight)
                {
                    // Light pips do not block the chooseworker queue.
                    return;
                }

                using (m_chooseWorkerTogglePauseLock.AcquireWriteLock())
                {
                    // Compare with the captured sequence number before the pip re-entered the queue
                    // to avoid race conditions where pip cannot acquire worker resources become available then queue is paused
                    // potentially indefinitely (not likely but theoretically possilbe)
                    if (Volatile.Read(ref m_workerEnableSequenceNumber) == blockedPip.ChooseWorkerSequenceNumber)
                    {
                        SetQueueMaxParallelDegree(0);
                    }
                }
            }
            else
            {
                using (m_chooseWorkerTogglePauseLock.AcquireReadLock())
                {
                    // Update the sequence number. This essentially is called for every increase in resources
                    // and successful acquisition of workers to track changes in resource state that invalidate
                    // decision to pause choose worker queue.
                    Interlocked.Increment(ref m_workerEnableSequenceNumber);

                    // Unpause the queue
                    SetQueueMaxParallelDegree(m_maxParallelDegree);
                }
            }
        }

        private void OnWorkerResourcesChanged(Worker worker, WorkerResource resourceKind, bool increased)
        {
            if (increased)
            {
                TogglePauseChooseWorkerQueue(pause: false);
            }
        }

        private void SetQueueMaxParallelDegree(int maxConcurrency)
        {
            m_pipQueue.SetMaxParallelDegreeByKind(DispatcherKind.ChooseWorkerCpu, maxConcurrency);
        }

        public void LogStats()
        {
            var limitingResourceStats = m_limitingResourceCounts.ToDictionary(kvp => kvp.Key.ToString(), kvp => (long)kvp.Value.Value);
            Logger.Log.LimitingResourceStatistics(m_loggingContext, limitingResourceStats);
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
                runnablePip.ChooseWorkerSequenceNumber = Volatile.Read(ref m_workerEnableSequenceNumber);
            }
        }

        private class PipSetupCosts
        {
            public readonly WorkerSetupCost[] WorkerSetupCosts;
            private readonly ChooseWorkerCpu m_context;
            private readonly HashSet<ContentHash> m_visitedHashes = new HashSet<ContentHash>();

            public PipSetupCosts(ChooseWorkerCpu context)
            {
                m_context = context;
                WorkerSetupCosts = new WorkerSetupCost[context.m_workers.Count];
            }

            /// <summary>
            /// Calculates the execution cost of the given pip on each worker.
            /// 
            /// The cost calculation considers several factors:
            /// *Worker Load*: The number of pips already assigned to the worker or the projected RAM usage.
            ///   - If RAM projection is active, the cost increases with higher projected RAM usage (`ProjectedPipsRamUsageMb`).
            ///   - If RAM projection is not active, the cost increases with more acquired process slots (`AcquiredProcessSlots`).
            /// *Setup Cost*: If `EnableSetupCostWhenChoosingWorker` is enabled in the schedule configuration,
            ///   the cost increases for workers where the pip's inputs are not yet materialized.
            ///   - This involves calculating the additional data that needs to be transferred to the worker (setup bytes).
            /// 
            /// The method sorts the workers based on the calculated costs, which helps in deciding the most efficient
            /// worker to execute the pip.
            /// </summary>
            internal void CalculateSetupCostPerWorker(RunnablePip runnablePip, IScheduleConfiguration scheduleConfig)
            {
                var pip = runnablePip.Pip;

                if (runnablePip.Environment.IsRamProjectionActive)
                {
                    InitializeWorkerSetupCost((w) => w.ProjectedPipsRamUsageMb);
                }
                else
                {
                    InitializeWorkerSetupCost((w) => w.AcquiredProcessSlots);
                }

                if (scheduleConfig.EnableSetupCostWhenChoosingWorker)
                {
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

                            FileContentInfo fileContentInfo = m_context.m_fileContentManager.GetInputContent(fileInput).FileContentInfo;
                            if (!m_visitedHashes.Add(fileContentInfo.Hash))
                            {
                                continue;
                            }

                            // How many bytes we have to copy.
                            long fileSize = fileContentInfo.HasKnownLength ? fileContentInfo.Length : TypicalFileLength;

                            for (int idx = 0; idx < m_context.m_workers.Count; ++idx)
                            {
                                if (!WorkerSetupCosts[idx].Worker.HasContent(fileInput))
                                {
                                    WorkerSetupCosts[idx].SetupBytes += fileSize;
                                }
                            }
                        }
                    }
                }

                Array.Sort(WorkerSetupCosts);
            }

            public void InitializeWorkerSetupCost(Func<Worker, int> calculateCost)
            {
                for (int i = 0; i < m_context.m_workers.Count; i++)
                {
                    var worker = m_context.m_workers[i];
                    WorkerSetupCosts[i] = new WorkerSetupCost()
                    {
                        Worker = worker,
                        Cost = calculateCost(worker)
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
            /// The associated worker
            /// </summary>
            public Worker Worker { get; set; }

            /// <summary>
            /// Cost
            /// </summary>
            public int Cost { get; set; }

            /// <inheritdoc />
            public int CompareTo(WorkerSetupCost other)
            {
                var result = SetupBytes.CompareTo(other.SetupBytes);
                return result == 0 ? Cost.CompareTo(other.Cost) : result;
            }
        }
    }
}
