// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using static BuildXL.Utilities.Core.FormattableStringEx;
using static BuildXL.Tracing.Diagnostics;
using BuildXL.Pips.Filter;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// A local or remote worker which is responsible for executing process and IPC pips
    /// </summary>
    public abstract class Worker : IDisposable
    {
        /// <summary>
        /// Local worker index in the workers' array
        /// </summary>
        public const int LocalWorkerIndex = 0;

        /// <summary>
        /// Name of the RAM semaphore
        /// </summary>
        private const string RamSemaphoreName = "BuildXL.Scheduler.Worker.TotalMemory";

        private const string CpuSemaphoreName = "BuildXL.Scheduler.Worker.CPU";

        /// <summary>
        /// Name of semaphore that controls the number of pips that execute in VM.
        /// </summary>
        private const string PipInVmSemaphoreName = "BuildXL.Scheduler.Worker.PipInVm";

        private static readonly ObjectPool<List<ProcessSemaphoreInfo>> s_semaphoreInfoListPool = Pools.CreateListPool<ProcessSemaphoreInfo>();

        /// <summary>
        /// Defines event handler for changes in worker resources
        /// </summary>
        internal delegate void WorkerResourceChangedHandler(Worker worker, WorkerResource resource, bool resourceIncreased);

        private int m_acquiredCacheLookupSlots;
        private int m_acquiredProcessSlots;
        private int m_acquiredIpcSlots;
        private int m_acquiredLightProcessSlots;
        private int m_acquiredMaterializeInputSlots;
        private int m_acquiredPostProcessSlots;
        private int m_acquiredProcessPips;

        private ContentTrackingSet m_availableContent;
        private ContentTrackingSet m_availableHashes;
        private SemaphoreSet<StringId> m_workerSemaphores;
        private WorkerNodeStatus m_status;
        private OperationContext m_workerStatusOperation;
        private OperationContext m_currentStatusOperation;

        /// <summary>
        /// ExecutionLogTarget used to retrieve EventStats at the end of build
        /// </summary>
        protected IExecutionLogTarget ExecutionLogTarget;

        /// <summary>
        /// Whether the worker has finished all pending requests after stop is initiated.
        /// </summary>
        public readonly TaskSourceSlim<bool> DrainCompletion;

        /// <summary>
        /// Whether scheduler decided to release this worker early.
        /// </summary>
        public virtual bool IsEarlyReleaseInitiated => false;

        /// <summary>
        /// If the worker is released early, we record the datetime.
        /// </summary>
        public DateTime? WorkerEarlyReleasedTime;

        internal static readonly OperationKind WorkerStatusParentOperationKind = OperationKind.Create("Distribution.WorkerStatus");

        internal static readonly ReadOnlyArray<OperationKind> WorkerStatusOperationKinds = EnumTraits<WorkerNodeStatus>.EnumerateValues()
            .Select(status => OperationKind.Create($"{WorkerStatusParentOperationKind.Name}.{status}"))
            .ToReadOnlyArray();

        /// <summary>
        /// The identifier for the worker.
        /// The local worker always has WorkerId=0
        /// </summary>
        public uint WorkerId { get; }

        /// <summary>
        /// The total amount of slots for process execution (i.e., max degree of pip parallelism).
        /// </summary>
        public virtual int TotalProcessSlots
        {
            get => Volatile.Read(ref m_totalProcessSlots);

            protected set
            {
                var oldValue = Volatile.Read(ref m_totalProcessSlots);
                Volatile.Write(ref m_totalProcessSlots, value);
                OnWorkerResourcesChanged(WorkerResource.TotalProcessSlots, value > oldValue);
            }
        }

        private int m_totalProcessSlots;
        private int m_totalCacheLookupSlots;

        /// <summary>
        /// The total amount of slots for cache lookup (i.e., max degree of pip parallelism)
        /// </summary>
        public int TotalCacheLookupSlots
        {
            get
            {
                return Volatile.Read(ref m_totalCacheLookupSlots);
            }

            protected set
            {
                var oldValue = Volatile.Read(ref m_totalCacheLookupSlots);
                Volatile.Write(ref m_totalCacheLookupSlots, value);
                OnWorkerResourcesChanged(WorkerResource.TotalCacheLookupSlots, value > oldValue);
            }
        }

        private int m_totalLightProcessSlots;

        /// <summary>
        /// The total amount of slots for light process pips (i.e., max degree of pip parallelism)
        /// </summary>
        public int TotalLightProcessSlots
        {
            get => Volatile.Read(ref m_totalLightProcessSlots);

            protected set => Volatile.Write(ref m_totalLightProcessSlots, value);
        }

        private int m_totalIpcSlots;

        /// <summary>
        /// The total amount of slots for IPC pips (i.e., max degree of pip parallelism)
        /// </summary>
        public int TotalIpcSlots
        {
            get => Volatile.Read(ref m_totalIpcSlots);

            protected set => Volatile.Write(ref m_totalIpcSlots, value);
        }

        /// <summary>
        /// The total amount of slots for materialize input
        /// </summary>
        public int TotalMaterializeInputSlots { get; protected set; }

        private readonly StringId m_cpuSemaphoreNameId;
        private readonly int m_cpuSemaphoreIndex = -1;
        private readonly int m_cpuSemaphoreLimit;

        private readonly StringId m_ramSemaphoreNameId;
        private int m_ramSemaphoreIndex = -1;

        /// <summary>
        /// Ram semaphore limit in MB
        /// </summary>
        public int RamSemaphoreLimitMb { get; private set; }
        private int? m_initialAvailableRamMb;

        /// <nodoc/>
        public int? EngineRamMb { get; private set; }

        /// <nodoc/>
        public int? AvailableRamMb => TotalRamMb - UsedRamMb;

        /// <nodoc/>
        public int? TotalRamMb { get; private set; }

        /// <nodoc/>
        public int? UsedRamMb { get; private set; }

        /// <summary>
        /// Gets the projected total RAM usage in megabytes (MB) for all pips currently assigned to this worker.
        /// This represents the RAM that will be used throughout the execution of these pips.
        /// </summary>
        public int ProjectedPipsRamUsageMb => m_ramSemaphoreIndex < 0 ? 0 : m_workerSemaphores.GetUsage(m_ramSemaphoreIndex);

        /// <nodoc/>
        public int CpuUsage { get; private set; }

        /// <nodoc/>
        public int ProjectedPipsCpuUsage => m_cpuSemaphoreIndex < 0 ? 0 : m_workerSemaphores.GetUsage(m_cpuSemaphoreIndex);

        /// <summary>
        /// Semaphore resources acquired by the worker to account for the BuildXL process's RAM and CPU usage.
        /// </summary>
        private ItemResources? m_lastEngineResource;

        /// <summary>
        /// Default memory usage for process pips in case of no historical ram usage info 
        /// </summary>
        /// <remarks>
        /// If there is no historical ram usage for the process pips, we assume that 80% of memory is used if all process slots are occupied.
        /// </remarks>
        internal int DefaultWorkingSetMbPerProcess => (int)((m_initialAvailableRamMb ?? 0) * 0.8 / Math.Max(TotalProcessSlots, Environment.ProcessorCount));

        /// <summary>
        /// Listen for status change events on the worker
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
        public event Action<Worker> StatusChanged;

        /// <summary>
        /// Listen for status change events on the worker
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
        internal event WorkerResourceChangedHandler ResourcesChanged;

        /// <summary>
        /// The status of the worker node
        /// </summary>
        public virtual WorkerNodeStatus Status
        {
            get => m_status;

            set
            {
                m_status = value;
                OnStatusChanged();
            }
        }

        /// <summary>
        /// Whether the worker become available at any time
        /// </summary>
        public virtual bool EverAvailable => true;

        /// <summary>
        /// Whether the worker is connected at any time
        /// </summary>
        public virtual bool EverConnected => true;

        /// <summary>
        /// The number of the build requests waiting to be sent
        /// </summary>
        public virtual int WaitingBuildRequestsCount => 0;

        /// <nodoc/>
        public virtual int CurrentBatchSize => 0;

        /// <summary>
        /// The name of the worker
        /// </summary>
        public string Name {
            get => m_name;

            protected set
            {
                m_name = value;
                m_workerOperationKind.Name = $"Worker {m_name}";
            }
        }
        private string m_name;

        /// <summary>
        /// Gets the worker IP address
        /// </summary>
        public string WorkerIpAddress { get; protected set; }

        /// <summary>
        /// Which counters are being logged for tracer
        /// </summary>
        public readonly ConcurrentDictionary<string, byte> InitializedTracerCounters = new ConcurrentDictionary<string, byte>();

        /// <summary>
        /// Pip execution context.
        /// </summary>
        protected PipExecutionContext PipExecutionContext { init; get; }

        private readonly OperationKind m_workerOperationKind;

        private bool m_isDistributedBuild;
        private readonly IScheduleConfiguration m_scheduleConfig;

        /// <summary>
        /// The value of the last limiting semaphore resource
        /// </summary>
        public int LastLimitingResourceValue { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        protected Worker(uint workerId, PipExecutionContext context, IScheduleConfiguration scheduleConfig)
        {
            WorkerId = workerId;
            m_workerSemaphores = new SemaphoreSet<StringId>();

            m_workerOperationKind = OperationKind.Create(string.IsNullOrEmpty(Name) ? $"Worker {workerId}" : $"Worker {Name}");
            DrainCompletion = TaskSourceSlim.Create<bool>();
            PipExecutionContext = context;
            m_isDistributedBuild = false;

            m_ramSemaphoreNameId = context.StringTable.AddString(RamSemaphoreName);
            m_scheduleConfig = scheduleConfig;

            m_cpuSemaphoreNameId = context.StringTable.AddString(CpuSemaphoreName);

            // For an 8-core machine, the semaphore limit is set to 800 because the pips
            // report their CPU usage per core. If a pip fully utilizes 2 cores, its CPU usage would be 200%.
            m_cpuSemaphoreLimit = 100 * Environment.ProcessorCount;
            m_cpuSemaphoreIndex = m_workerSemaphores.CreateSemaphore(m_cpuSemaphoreNameId, m_cpuSemaphoreLimit);
        }

        /// <summary>
        /// Initializes the worker for the distribution
        /// </summary>
        public virtual void InitializeForDistribution(
            OperationContext parent,
            IConfiguration config,
            PipGraph pipGraph,
            IExecutionLogTarget executionLogTarget,
            Task schedulerCompletion,
            Action<Worker> statusChangedAction)
        {
            m_isDistributedBuild = true;

            // Track Status  Operation
            m_workerStatusOperation = parent.StartAsyncOperation(m_workerOperationKind);

            StatusChanged += statusChangedAction;

            // Content tracking is needed when calculating setup cost per pip on each worker.
            // That's an expensive calculation, so it is disabled by default.
            m_isContentTrackingEnabled = m_scheduleConfig.EnableSetupCostWhenChoosingWorker;
            m_availableContent = new ContentTrackingSet(pipGraph);
            m_availableHashes = new ContentTrackingSet(pipGraph);
            ExecutionLogTarget = executionLogTarget;
        }

        /// <summary>
        /// Initializes the worker
        /// </summary>
        public virtual void Start()
        {
            Status = WorkerNodeStatus.Running;
        }

        /// <summary>
        /// Signals that build is finished and that worker should exit
        /// </summary>
#pragma warning disable 1998 // Disable the warning for "This async method lacks 'await'"
        public virtual async Task FinishAsync([CallerMemberName] string callerName = null)
        {
            Status = WorkerNodeStatus.Stopped;
        }
#pragma warning restore 1998

        /// <summary>
        /// Release worker before build is finished due to the insufficient amount of work left
        /// </summary>
#pragma warning disable 1998 // Disable the warning for "This async method lacks 'await'"
        public virtual async Task EarlyReleaseAsync()
        {
            throw new NotImplementedException("Local worker does not support early release");
        }
#pragma warning restore 1998

        /// <summary>
        /// Returns if true if the worker holds a local node; false otherwise.
        /// </summary>
        public bool IsLocal => WorkerId == LocalWorkerIndex;

        /// <summary>
        /// Returns if true if the worker holds a remote node; false otherwise.
        /// </summary>
        public bool IsRemote => !IsLocal;

        /// <summary>
        /// Whether the worker is available to acquire work items
        /// </summary>
        public virtual bool IsAvailable => Status == WorkerNodeStatus.Running;

        /// <summary>
        /// Gets the currently acquired slots for all operations that can be done on a worker.
        /// </summary>
        public int AcquiredSlots => AcquiredProcessSlots + AcquiredCacheLookupSlots + AcquiredLightProcessSlots + AcquiredIpcSlots + Volatile.Read(ref m_acquiredPostProcessSlots);

        /// <summary>
        /// Gets the currently acquired slots for process pips which is a sum of process slots and cache lookup slots.
        /// </summary>
        public int AcquiredProcessAndCacheLookupSlots => AcquiredProcessSlots + AcquiredCacheLookupSlots;

        /// <summary>
        /// Gets the currently acquired process slots
        /// </summary>
        public int AcquiredProcessSlots => Volatile.Read(ref m_acquiredProcessSlots);

        /// <summary>
        /// Gets the currently acquired postprocess slots
        /// </summary>
        public int AcquiredPostProcessSlots => Volatile.Read(ref m_acquiredPostProcessSlots);

        /// <summary>
        /// Gets the currently acquired process slots
        /// </summary>
        public int AcquiredMaterializeInputSlots => Volatile.Read(ref m_acquiredMaterializeInputSlots);

        /// <summary>
        /// Gets the currently acquired cache lookup slots
        /// </summary>
        public int AcquiredCacheLookupSlots => Volatile.Read(ref m_acquiredCacheLookupSlots);

        /// <summary>
        /// Gets the currently acquired light process slots.
        /// </summary>
        public int AcquiredLightProcessSlots => Volatile.Read(ref m_acquiredLightProcessSlots);

        /// <summary>
        /// Gets the currently acquired Ipc slots.
        /// </summary>
        public int AcquiredIpcSlots => Volatile.Read(ref m_acquiredIpcSlots);

        /// <summary>
        /// Whether the content tracking is enabled.
        /// </summary>
        private bool m_isContentTrackingEnabled;

        /// <summary>
        /// Ensures that this worker instance has the same resource mappings as the given worker
        /// </summary>
        internal void SyncResourceMappings(Worker worker)
        {
            m_workerSemaphores = worker.m_workerSemaphores.CreateSharingCopy();
        }

        internal void UpdateStatusOperation()
        {
            if (m_workerStatusOperation.IsValid)
            {
                var status = Status;
                m_currentStatusOperation.Dispose();
                if (status != WorkerNodeStatus.Stopped)
                {
                    m_currentStatusOperation = m_workerStatusOperation.StartAsyncOperation(WorkerStatusOperationKinds[(int)status]);
                }
            }
        }

        /// <summary>
        /// Raises <see cref="StatusChanged"/> event.
        /// </summary>
        protected void OnStatusChanged()
        {
            StatusChanged?.Invoke(this);
            OnWorkerResourcesChanged(WorkerResource.Status, increased: Status == WorkerNodeStatus.Running);
        }

        /// <summary>
        /// Raises <see cref="ResourcesChanged"/> event.
        /// </summary>
        internal void OnWorkerResourcesChanged(WorkerResource kind, bool increased)
        {
            ResourcesChanged?.Invoke(this, kind, increased);
        }

        /// <summary>
        /// Attempts to acquire a cache lookup slot on the worker
        /// </summary>
        /// <param name="runnablePip">the pip</param>
        /// <param name="force">true to force acquisition of the slot</param>
        /// <param name="loadFactor">load factor to specify the oversubscription rate</param>
        /// <returns>true if the slot was acquired. False, otherwise.</returns>
        public bool TryAcquireCacheLookup(ProcessRunnablePip runnablePip, bool force, double loadFactor = 1)
        {
            if (!IsAvailable)
            {
                return false;
            }

            if (force)
            {
                Interlocked.Increment(ref m_acquiredCacheLookupSlots);
                runnablePip.AcquiredResourceWorker = this;
                return true;
            }

            if (AcquiredCacheLookupSlots < TotalCacheLookupSlots * loadFactor)
            {
                Interlocked.Increment(ref m_acquiredCacheLookupSlots);
                OnWorkerResourcesChanged(WorkerResource.AvailableCacheLookupSlots, increased: false);
                runnablePip.AcquiredResourceWorker = this;
                return true;
            }

            return false;
        }

        internal bool TryAcquireIpc(RunnablePip runnablePip, bool force = false, double loadFactor = 1)
        {
            Contract.Requires(runnablePip.PipType == PipType.Ipc);

            if (!IsAvailable)
            {
                return false;
            }

            if (force || m_acquiredIpcSlots < TotalIpcSlots * loadFactor)
            {
                runnablePip.AcquiredResourceWorker = this;
                Interlocked.Increment(ref m_acquiredIpcSlots);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try acquire given resources on the worker.
        /// </summary>
        internal bool TryAcquireProcess(ProcessRunnablePip processRunnablePip, out WorkerResource? limitingResource, double loadFactor, bool moduleAffinityEnabled = false)
        {
            limitingResource = WorkerResource.Status;

            if (!IsAvailable)
            {
                return false;
            }

            // For integration tests we require some pips to run remotely
            if (IsLocal &&
                processRunnablePip.Process.Priority == Process.IntegrationTestPriority &&
                processRunnablePip.Pip.Tags.Any(a => processRunnablePip.Environment.Context.StringTable.Equals(TagFilter.RunPipRemotely, a)))
            {
                return false;
            }

            if (!moduleAffinityEnabled &&
                AcquiredMaterializeInputSlots >= TotalMaterializeInputSlots)
            {
                // If the module affinity is disabled, we should not choose a worker where there is no available materializeinput slot.
                limitingResource = WorkerResource.AvailableMaterializeInputSlots;
                return false;
            }

            // If there is no process pip assigned to the worker, the resources should be acquired forcefully to prevent deadlocks.
            // There might be some pips requiring more resources than the machine has. In those cases, we should allow one pip to run.
            bool force = m_acquiredProcessPips == 0;

            // We acquire the slots in advance and then perform the checks to avoid the race condition.
            int acquiredSlots = UpdateProcessSlots(processRunnablePip);
            int totalSlots = processRunnablePip.IsLight ? TotalLightProcessSlots : TotalProcessSlots;

            // If a process has a weight higher than the total number of process slots, still allow it to run as long as there are no other
            // processes running (the number of acquired process pips is 1)
            if (m_acquiredProcessPips > 1 && 
                acquiredSlots > totalSlots * loadFactor)
            {
                limitingResource = (!IsLocal || ((LocalWorker)this).MemoryResourceAvailable) ? WorkerResource.AvailableProcessSlots : WorkerResource.MemoryResourceAvailable;
                UpdateProcessSlots(processRunnablePip, release: true);
                return false;
            }


            StringId limitingResourceName = StringId.Invalid;
            var expectedMemoryCounters = GetExpectedMemoryCounters(processRunnablePip);
            var resources = GetAdditionalResourceInfo(processRunnablePip, expectedMemoryCounters);
            if (processRunnablePip.TryAcquireResources(m_workerSemaphores, resources, force, out limitingResourceName))
            {
                OnWorkerResourcesChanged(WorkerResource.AvailableProcessSlots, increased: false);
                processRunnablePip.AcquiredResourceWorker = this;
                processRunnablePip.ExpectedMemoryCounters = expectedMemoryCounters;

                if (processRunnablePip.Environment.InputsLazilyMaterialized)
                {
                    // If inputs are lazily materialized, we need to acquire MaterializeInput slots.
                    // Then, we can stop ChooseWorkerCpu queue when the materialize slots are full.
                    // If inputs are not lazily materialized, there is no need to acquire MaterializeInput slots
                    // because we do not execute MaterializeInput step for those builds such as single-machine builds.
                    // Otherwise, the hang occurs for single machine builds where we do not lazily materialize inputs.
                    Interlocked.Add(ref m_acquiredMaterializeInputSlots, 1);
                }

                Interlocked.Increment(ref m_acquiredPostProcessSlots);

                limitingResource = null;
                return true;
            }

            UpdateProcessSlots(processRunnablePip, release: true);

            if (limitingResourceName == m_ramSemaphoreNameId)
            {
                limitingResource = WorkerResource.AvailableMemoryMb;
            }
            else if (limitingResourceName == m_cpuSemaphoreNameId)
            {
                limitingResource = WorkerResource.AvailableCpu;
            }
            else
            {
                limitingResource = WorkerResource.CreateSemaphoreResource(limitingResourceName.ToString(processRunnablePip.Environment.Context.StringTable));
            }

            LastLimitingResourceValue = resources.FirstOrDefault(a => a.Name == limitingResourceName).Value;

            return false;
        }

        private int UpdateProcessSlots(ProcessRunnablePip processRunnable, bool release = false)
        {
            Interlocked.Add(ref m_acquiredProcessPips, release ? -1 : 1);

            if (processRunnable.IsLight)
            {
                return Interlocked.Add(ref m_acquiredLightProcessSlots, (release ? -1 : 1) * processRunnable.Weight);
            }

            return Interlocked.Add(ref m_acquiredProcessSlots, (release ? -1 : 1) * processRunnable.Weight);
        }

        private void UpdateMachineSemaphores(int? engineRamMb, int? engineCpuUsage)
        {
            if (engineRamMb == null || engineRamMb == 0 || engineCpuUsage == null)
            {
                return;
            }

            using (var semaphoreInfoListWrapper = s_semaphoreInfoListPool.GetInstance())
            {
                var semaphores = semaphoreInfoListWrapper.Instance;

                if (m_scheduleConfig.UseHistoricalCpuThrottling)
                {
                    // The CPU usage of the BuildXL process is reported across all cores. Therefore,
                    // we need to multiply it by the number of processors before using it as a semaphore value.
                    int value = Math.Max(1, engineCpuUsage.Value * Environment.ProcessorCount);
                    var cpuSemaphoreInfo = new ProcessSemaphoreInfo(
                        m_cpuSemaphoreNameId,
                        value: Math.Min(value, m_cpuSemaphoreLimit),
                        limit: m_cpuSemaphoreLimit);
                    semaphores.Add(cpuSemaphoreInfo);
                }

                if (m_scheduleConfig.UseHistoricalRamUsageInfo)
                {
                    var ramSemaphoreInfo = new ProcessSemaphoreInfo(
                        m_ramSemaphoreNameId,
                        value: Math.Min(engineRamMb.Value, RamSemaphoreLimitMb),
                        limit: RamSemaphoreLimitMb);
                    semaphores.Add(ramSemaphoreInfo);
                }

                if (semaphores.Count == 0)
                {
                    return;
                }

                var resources = ProcessExtensions.GetSemaphoreResources(m_workerSemaphores, semaphores);
                // Due to the assigned process pips, the worker's RAM and CPU semaphores might be near the limit.
                // That's why, we force to acquire the buildxl process's semaphores.
                m_workerSemaphores.TryAcquireResources(resources, force: true);

                if (m_lastEngineResource.HasValue)
                {
                    m_workerSemaphores.ReleaseResources(m_lastEngineResource.Value);
                }

                m_lastEngineResource = resources;
            }
        }

        private ProcessSemaphoreInfo[] GetAdditionalResourceInfo(ProcessRunnablePip runnableProcess, ProcessMemoryCounters expectedMemoryCounters)
        {
            var config = runnableProcess.Environment.Configuration;
            using (var semaphoreInfoListWrapper = s_semaphoreInfoListPool.GetInstance())
            {
                var semaphores = semaphoreInfoListWrapper.Instance;

                if (runnableProcess.Process.RequiresAdmin
                    && config.Sandbox.AdminRequiredProcessExecutionMode.ExecuteExternalVm()
                    && config.Sandbox.VmConcurrencyLimit > 0)
                {
                    semaphores.Add(new ProcessSemaphoreInfo(
                        runnableProcess.Environment.Context.StringTable.AddString(PipInVmSemaphoreName),
                        value: 1,
                        limit: config.Sandbox.VmConcurrencyLimit));
                }

                if (runnableProcess.Environment.IsRamProjectionActive)
                {
                    int ramUsage = Math.Max(1, config.Schedule.EnableLessAggressiveMemoryProjection ? expectedMemoryCounters.AverageWorkingSetMb : expectedMemoryCounters.PeakWorkingSetMb);
                    var ramSemaphoreInfo = new ProcessSemaphoreInfo(
                            m_ramSemaphoreNameId,
                            // When we run the pipeline on a less powerful machine in the next run, the RAM usage might exceed the current RAM size. 
                            // Therefore, we limit the value of the semaphore to the current RAM size.
                            value: Math.Min(ramUsage, RamSemaphoreLimitMb),
                            limit: RamSemaphoreLimitMb);
                    semaphores.Add(ramSemaphoreInfo);
                }

                if (config.Schedule.UseHistoricalCpuThrottling)
                {
                    // If there is no historical data available, we use 100 (1-core) as the cpu usage by default.
                    // Sometimes, the historical data shows the cpu usage as 0 if the process is very lightweight and short-running. 
                    // In those cases, we use 1 as the cpu usage.
                    int cpuUsage = Math.Max(1, runnableProcess.HistoricPerfData.Value == ProcessPipHistoricPerfData.Empty ? 100 : runnableProcess.HistoricPerfData.Value.ProcessorsInPercents);
                    var cpuSemaphoreInfo = new ProcessSemaphoreInfo(
                        m_cpuSemaphoreNameId,
                        // When we run the pipeline on a less powerful machine in the next run, the CPU usage might exceed the current CPU max limit. 
                        // Therefore, we limit the value of the semaphore to the current CPU max limit.
                        value: Math.Min(cpuUsage, m_cpuSemaphoreLimit),
                        limit: m_cpuSemaphoreLimit);
                    semaphores.Add(cpuSemaphoreInfo);
                }

                return semaphores.ToArray();
            }
        }

        /// <summary>
        /// Gets the estimated memory counters for the process
        /// </summary>
        private ProcessMemoryCounters GetExpectedMemoryCounters(ProcessRunnablePip runnableProcess)
        {
            if (!runnableProcess.Environment.IsRamProjectionActive)
            {
                return ProcessMemoryCounters.CreateFromMb(0, 0);
            }

            if (runnableProcess.ExpectedMemoryCounters.HasValue)
            {
                // If there is already an expected memory counters for the process,
                // it means that we retry the process with another worker due to 
                // several reasons including stopped worker, memory exhaustion.
                // That's why, we should reuse the expected memory counters that 
                // are updated with recent data from last execution.
                return runnableProcess.ExpectedMemoryCounters.Value;
            }

            // If there is a historic perf data, use it.
            if (runnableProcess.HistoricPerfData != null && runnableProcess.HistoricPerfData.Value != ProcessPipHistoricPerfData.Empty)
            {
                return runnableProcess.HistoricPerfData.Value.MemoryCounters;
            }

            // If there is no historic perf data, use the defaults for the worker.
            // Regarding light process pips, we should use 0 as the default memory usage.
            // Otherwise, we cannot utilize the high concurrency limit of IPC dispatcher. 
            // When there is a historical data for light process pips, we will use the real 
            // memory usage from previous runs, but we still expect low memory for those.
            return ProcessMemoryCounters.CreateFromMb(
                peakWorkingSetMb: runnableProcess.Process.IsLight ? 0 : DefaultWorkingSetMbPerProcess,
                averageWorkingSetMb: runnableProcess.Process.IsLight ? 0 : DefaultWorkingSetMbPerProcess);
        }

        /// <summary>
        /// Release pip's resources after worker is done with the task
        /// </summary>
        public void ReleaseResources(RunnablePip runnablePip, PipExecutionStep nextStep)
        {
            Contract.Assert(runnablePip.AcquiredResourceWorker == this);

            var stepCompleted = runnablePip.Step;

            var processRunnablePip = runnablePip as ProcessRunnablePip;
            if (processRunnablePip != null)
            {
                switch (stepCompleted)
                {
                    case PipExecutionStep.CacheLookup:
                    {
                        Interlocked.Decrement(ref m_acquiredCacheLookupSlots);
                        OnWorkerResourcesChanged(WorkerResource.AvailableCacheLookupSlots, increased: true);
                        runnablePip.SetWorker(null);
                        runnablePip.AcquiredResourceWorker = null;
                        break;
                    }
                    case PipExecutionStep.MaterializeInputs:
                    {
                        if (processRunnablePip.Environment.InputsLazilyMaterialized)
                        {
                            Interlocked.Decrement(ref m_acquiredMaterializeInputSlots);
                        }

                        OnWorkerResourcesChanged(WorkerResource.AvailableMaterializeInputSlots, increased: true);
                        if (nextStep != PipExecutionStep.ExecuteProcess)
                        {
                            releaseExecuteProcessSlots();
                            releasePostProcessSlots();
                        }

                        break;
                    }
                    case PipExecutionStep.ExecuteProcess:
                    {
                        releaseExecuteProcessSlots();
                        if (nextStep != PipExecutionStep.PostProcess)
                        {
                            releasePostProcessSlots();
                        }

                        break;
                    }
                    case PipExecutionStep.PostProcess:
                    {
                        releasePostProcessSlots();
                        break;
                    }
                }
            }

            if (runnablePip.PipType == PipType.Ipc)
            {
                if (stepCompleted == PipExecutionStep.ExecuteNonProcessPip)
                {
                    Interlocked.Decrement(ref m_acquiredIpcSlots);
                    runnablePip.SetWorker(null);
                    runnablePip.AcquiredResourceWorker = null;
                }
            }

            if (AcquiredSlots == 0 && Status.IsStoppingOrStopped())
            {
                DrainCompletion.TrySetResult(true);
            }

            void releaseExecuteProcessSlots()
            {
                Contract.Assert(processRunnablePip.Resources.HasValue);

                UpdateProcessSlots(processRunnablePip, release: true);

                var resources = processRunnablePip.Resources.Value;
                m_workerSemaphores.ReleaseResources(resources);

                // Notify that resources have changed so that choose worker queue can be unblocked
                // We need to do this after all resources are released to prevent race condition
                // where choose worker queue runs and can't acquire resources which have not yet been released
                // in this method.
                // NOTE: Though the WorkerResource is AvailableProcessSlots this is used to signal
                // release of semaphore resources as well.
                OnWorkerResourcesChanged(WorkerResource.AvailableProcessSlots, increased: true);
            }

            void releasePostProcessSlots()
            {
                Interlocked.Decrement(ref m_acquiredPostProcessSlots);
                runnablePip.SetWorker(null);
                runnablePip.AcquiredResourceWorker = null;
            }
        }

        internal void UpdatePerfInfo(LoggingContext loggingContext, int? currentTotalRamMb, int? machineAvailableRamMb, int? engineRamMb, int? engineCpuUsage, int? machineCpuUsage)
        {
            UpdateRamInfo(loggingContext, currentTotalRamMb, machineAvailableRamMb, engineRamMb);

            // Pips and the BuildXL engine run on the same machine, competing for shared resources.
            // Therefore, when scheduling pips, we must consider the RAM and CPU usage of the BuildXL engine. 
            // Since the engine performs tasks such as cache lookups, materialization, hashing, and more,
            // it is logical to represent the engine as a pip from the scheduler's perspective.
            UpdateMachineSemaphores(engineRamMb, engineCpuUsage);

            if (machineCpuUsage.HasValue)
            {
                CpuUsage = machineCpuUsage.Value;
            }
        }

        private void UpdateRamInfo(LoggingContext loggingContext, int? currentTotalRamMb, int? machineAvailableRamMb, int? engineRamMb)
        {
            if (!TotalRamMb.HasValue)
            {
                TotalRamMb = currentTotalRamMb;
            }
            else if (currentTotalRamMb.HasValue && TotalRamMb != currentTotalRamMb)
            {
                Logger.Log.DynamicRamDetected(loggingContext, Name, TotalRamMb.Value, currentTotalRamMb.Value);
                TotalRamMb = currentTotalRamMb;
            }

            if (!m_initialAvailableRamMb.HasValue && machineAvailableRamMb.HasValue)
            {
                // We will add BuildXL's current ram usage to the available ram
                // because we will use the process ram usage as a semaphore.
                m_initialAvailableRamMb = machineAvailableRamMb + (engineRamMb ?? 0);

                RamSemaphoreLimitMb = (int)Math.Round((double)m_initialAvailableRamMb * m_scheduleConfig.RamSemaphoreMultiplier);
                m_ramSemaphoreIndex = m_workerSemaphores.CreateSemaphore(m_ramSemaphoreNameId, RamSemaphoreLimitMb);
            }

            if (TotalRamMb.HasValue && machineAvailableRamMb.HasValue)
            { 
                UsedRamMb = TotalRamMb.Value - machineAvailableRamMb.Value;
            }

            if (engineRamMb.HasValue)
            {
                EngineRamMb = engineRamMb;
            }
        }

        #region Pip Operations

        /// <summary>
        /// Materializes the inputs of the pip
        /// </summary>
        public virtual Task<PipResultStatus> MaterializeInputsAsync(ProcessRunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.Step == PipExecutionStep.MaterializeInputs);
            throw Contract.AssertFailure(I($"MaterializeInputsAsync is not supported for worker {Name}"));
        }

        /// <summary>
        /// Materializes the outputs of the pip
        /// </summary>
        public virtual Task<PipResultStatus> MaterializeOutputsAsync(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.Step == PipExecutionStep.MaterializeOutputs);
            throw Contract.AssertFailure(I($"MaterializeOutputsAsync is not supported for worker {Name}"));
        }

        /// <summary>
        /// Executes a process pip
        /// </summary>
        public virtual Task<ExecutionResult> ExecuteProcessAsync(ProcessRunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.Step == PipExecutionStep.ExecuteProcess);
            throw Contract.AssertFailure(I($"ExecuteProcessAsync is not supported for worker {Name}"));
        }

        /// <summary>
        /// Executes an IPC pip
        /// </summary>
        public virtual Task<PipResult> ExecuteIpcAsync(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.PipType == PipType.Ipc);
            Contract.Requires(runnablePip.Step == PipExecutionStep.ExecuteNonProcessPip);
            throw Contract.AssertFailure(I($"ExecuteIpcAsync is not supported for worker {Name}"));
        }

        /// <summary>
        /// Executes PostProcess on the worker
        /// </summary>
        public virtual Task<ExecutionResult> PostProcessAsync(ProcessRunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.Step == PipExecutionStep.PostProcess);
            throw Contract.AssertFailure(I($"{nameof(PostProcessAsync)} is not supported for worker {Name}"));
        }

        /// <summary>
        /// Performs a cache lookup for the process on the worker
        /// </summary>
        public virtual Task<(RunnableFromCacheResult, PipResultStatus)> CacheLookupAsync(
            ProcessRunnablePip runnablePip,
            PipExecutionState.PipScopeState state,
            CacheableProcess cacheableProcess,
            bool avoidRemoteLookups = true)
        {
            Contract.Requires(runnablePip.Step == PipExecutionStep.CacheLookup);
            throw Contract.AssertFailure(I($"CacheLookupAsync is not supported for worker {Name}"));
        }

        #endregion

        /// <inheritdoc/>
        public virtual void Dispose()
        {
        }

        #region Content Tracking

        /// <summary>
        /// In case of a failed build request call after many retries, we reset available hashes 
        /// to make sure that we do not overestimate what the worker contains.
        /// </summary>
        protected void ResetAvailableHashes(PipGraph pipGraph)
        {
            m_availableHashes = new ContentTrackingSet(pipGraph);
        }

        /// <summary>
        /// Called before worker starts executing the IPC or process pip
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        protected PipExecutionScope OnPipExecutionStarted(RunnablePip runnable, OperationContext operationContext = default(OperationContext))
        {
            operationContext = operationContext.IsValid ? operationContext : runnable.OperationContext;
            var scope = new PipExecutionScope(runnable, this, operationContext);
            if (m_isDistributedBuild && runnable.Step != PipExecutionStep.MaterializeOutputs)
            {
                // Log the start of the pip step on the worker unless it is a materialize output step.
                // For that step, we log in AllWorkers once per pip instead of per worker. 
                Logger.Log.DistributionExecutePipRequest(operationContext, runnable.FormattedSemiStableHash, Name, runnable.Step.AsString());
            }

            return scope;
        }

        /// <summary>
        /// Called after worker finishes executing the IPC or process pip
        /// </summary>
        private void OnPipExecutionCompletion(RunnablePip runnable)
        {
            if (!m_isDistributedBuild)
            {
                // Only perform this operation for distributed orchestrator.
                return;
            }

            var operationContext = runnable.OperationContext;
            var executionResult = runnable.ExecutionResult;

            if (runnable.Step != PipExecutionStep.MaterializeOutputs)
            {
                Logger.Log.DistributionFinishedPipRequest(operationContext, runnable.FormattedSemiStableHash, Name, runnable.Step.AsString());
            }

            if (executionResult == null)
            {
                return;
            }

            if (m_isContentTrackingEnabled &&
                ((runnable.Step == PipExecutionStep.PostProcess && !executionResult.Converged) ||
                (!executionResult.Result.IndicatesNoOutput() && runnable.Step == PipExecutionStep.ExecuteNonProcessPip)))
            {
                // After post process, if process was not converged (i.e. process execution outputs are used
                // as results because there was no conflicting cache entry when storing to cache),
                // report that the worker has the output content
                // IPC pips don't use cache convergence so always report their outputs
                foreach (var outputContent in executionResult.OutputContent)
                {
                    TryAddAvailableContent(outputContent.fileArtifact);
                }

                foreach (var directoryContent in executionResult.DirectoryOutputs)
                {
                    TryAddAvailableContent(directoryContent.directoryArtifact);
                }
            }

            if (IsRemote &&
                (runnable.Step == PipExecutionStep.ExecuteProcess || runnable.Step == PipExecutionStep.ExecuteNonProcessPip) &&
                ETWLogger.Log.IsEnabled(EventLevel.Verbose, Keywords.Diagnostics))
            {
                // Log the outputs reported from the worker for the pip execution
                foreach (var outputFile in executionResult.OutputContent)
                {
                    // NOTE: Available content is not added to the content tracking set here as the content
                    // may be changed due to cache convergence
                    Logger.Log.DistributionOrchestratorWorkerProcessOutputContent(
                        operationContext,
                        runnable.FormattedSemiStableHash,
                        outputFile.fileArtifact.Path.ToString(runnable.Environment.Context.PathTable),
                        outputFile.fileInfo.Hash.ToHex(),
                        outputFile.fileInfo.ReparsePointInfo.ToString(),
                        Name);
                }
            }
        }

        /// <summary>
        /// Called after worker finishes materializing inputs for a pip
        /// </summary>
        public void OnInputMaterializationCompletion(Pip pip, IPipExecutionEnvironment environment)
        {
            Contract.Assert(pip.PipType == PipType.Process || pip.PipType == PipType.Ipc);

            var fileContentManager = environment.State.FileContentManager;

            if (!m_isContentTrackingEnabled)
            {
                return;
            }

            fileContentManager.CollectPipInputsToMaterialize(
                environment.PipTable,
                pip,
                files: null,
                filter: artifact =>
                {
                    if (artifact.IsFile && artifact.FileArtifact.IsSourceFile)
                    {
                        // Do not register the source files as the available content.
                        return false;
                    }

                    bool added = TryAddAvailableContent(artifact);
                    if (artifact.IsFile)
                    {
                        // Don't attempt to add anything. Just need to register the available content
                        return false;
                    }
                    else
                    {
                        // Process directories to visit files unless they were already added
                        return !added;
                    }
                });
        }

        /// <summary>
        /// Gets whether the file's hash sent to the worker
        /// </summary>
        public bool? TryAddAvailableHash(in FileOrDirectoryArtifact artifact)
        {
            return m_availableHashes.Add(artifact);
        }

        /// <summary>
        /// Gets whether the service pip id content hashes sent to the worker
        /// </summary>
        public bool? TryAddAvailableHash(PipId servicePipId)
        {
            return m_availableHashes.Add(servicePipId);
        }

        /// <summary>
        /// Adds the content to the available content for the worker
        /// </summary>
        public bool TryAddAvailableContent(in FileOrDirectoryArtifact artifact)
        {
            return m_availableContent.Add(artifact) ?? false;
        }

        /// <summary>
        /// Gets whether the content is materialized on the worker
        /// </summary>
        public bool HasContent(in FileOrDirectoryArtifact artifact)
        {
            return m_availableContent.Contains(artifact);
        }

        #endregion

        /// <summary>
        /// Tracks the extent of a pip step execution on a worker
        /// </summary>
        protected sealed class PipExecutionScope : IDisposable
        {
            private readonly RunnablePip m_runnablePip;
            private readonly Worker m_worker;
            private readonly OperationContext m_operationContext;

            /// <nodoc />
            public PipExecutionScope(RunnablePip runnablePip, Worker worker, OperationContext operationContext)
            {
                m_runnablePip = runnablePip;
                m_worker = worker;
                m_operationContext = operationContext.StartOperation(worker.m_workerOperationKind);
            }

            /// <nodoc />
            public void Dispose()
            {
                m_worker.OnPipExecutionCompletion(m_runnablePip);
                m_operationContext.Dispose();
            }
        }
    }
}
