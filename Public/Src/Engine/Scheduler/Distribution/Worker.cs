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
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Threading;
using static BuildXL.Utilities.FormattableStringEx;
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

        private const string CommitSemaphoreName = "BuildXL.Scheduler.Worker.TotalCommit";

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
        /// Whether the worker has finished all pending requests after stop is initiated.
        /// </summary>
        protected readonly TaskSourceSlim<bool> DrainCompletion;

        /// <summary>
        /// Whether scheduler decided to release this worker early.
        /// </summary>
        public bool IsEarlyReleaseInitiated { get; protected set; }

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

        /// <summary>
        /// Name of the RAM semaphore
        /// </summary>
        private StringId m_ramSemaphoreNameId;

        private int m_ramSemaphoreIndex = -1;

        /// <summary>
        /// The total amount of available ram on the worker at the beginning of the build.
        /// </summary>
        public int? TotalRamMb
        {
            get => m_totalMemoryMb;

            set
            {
                var oldValue = m_totalMemoryMb;
                m_totalMemoryMb = value;
                OnWorkerResourcesChanged(WorkerResource.AvailableMemoryMb, increased: value > oldValue);
            }
        }

        private int? m_totalMemoryMb;

        /// <summary>
        /// The total amount of available memory on the worker during the build.
        /// </summary>
        public int? ActualFreeMemoryMb;

        /// <summary>
        /// Name of the RAM semaphore
        /// </summary>
        private StringId m_commitSemaphoreNameId;

        private int m_commitSemaphoreIndex = -1;

        /// <summary>
        /// The total amount of commit memory on the worker.
        /// </summary>
        public int? TotalCommitMb
        {
            get
            {
                return m_totalCommitMb;
            }

            set
            {
                var oldValue = m_totalCommitMb;
                m_totalCommitMb = value;
                OnWorkerResourcesChanged(WorkerResource.AvailableCommitMb, increased: value > oldValue);
            }
        }

        private int? m_totalCommitMb;

        /// <summary>
        /// The total amount of available commit on the worker during the build.
        /// </summary>
        public int? ActualFreeCommitMb;

        /// <summary>
        /// Gets the estimate RAM usage on the machine
        /// </summary>
        public int EstimatedFreeRamMb
        {
            get
            {
                if (TotalRamMb == null || m_ramSemaphoreIndex < 0)
                {
                    return 0;
                }

                var availablePercentFactor = ProcessExtensions.PercentageResourceLimit - m_workerSemaphores.GetUsage(m_ramSemaphoreIndex);

                return (int)(((long)availablePercentFactor * TotalRamMb.Value) / ProcessExtensions.PercentageResourceLimit);
            }
        }

        /// <summary>
        /// Gets the estimate RAM usage on the machine
        /// </summary>
        public int EstimatedFreeCommitMb
        {
            get
            {
                if (TotalCommitMb == null || m_commitSemaphoreIndex < 0)
                {
                    return 0;
                }

                var availablePercentFactor = ProcessExtensions.PercentageResourceLimit - m_workerSemaphores.GetUsage(m_commitSemaphoreIndex);

                return (int)(((long)availablePercentFactor * TotalCommitMb.Value) / ProcessExtensions.PercentageResourceLimit);
            }
        }

        /// <summary>
        /// Default memory usage for process pips in case of no historical ram usage info 
        /// </summary>
        /// <remarks>
        /// If there is no historical ram usage for the process pips, we assume that 80% of memory is used if all process slots are occupied.
        /// </remarks>
        internal int DefaultWorkingSetMbPerProcess => (int)((TotalRamMb ?? 0) * 0.8 / Math.Max(TotalProcessSlots, Environment.ProcessorCount));

        /// <summary>
        /// Defaulf commit size usage
        /// </summary>
        /// <remarks>
        /// As commit size is the total virtual address space used by process, it needs to be larger than working set.
        /// We use 1.5 multiplier to make it larger than working set.
        /// </remarks>
        internal int DefaultCommitSizeMbPerProcess => (int)(DefaultWorkingSetMbPerProcess * 1.5);

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
        /// The number of the build requests waiting to be sent
        /// </summary>
        public virtual int WaitingBuildRequestsCount => 0;

        /// <nodoc/>
        public virtual int CurrentBatchSize => 0;

        /// <summary>
        /// Gets the name of the worker
        /// </summary>
        public virtual string Name { get; }

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

        /// <summary>
        /// Constructor
        /// </summary>
        protected Worker(uint workerId, PipExecutionContext context)
        {
            WorkerId = workerId;
            m_workerSemaphores = new SemaphoreSet<StringId>();

            m_workerOperationKind = OperationKind.Create("Worker " + Name);
            DrainCompletion = TaskSourceSlim.Create<bool>();
            PipExecutionContext = context;
            InitSemaphores(context);
            m_isDistributedBuild = false;
        }

        /// <summary>
        /// Initializes the worker
        /// </summary>
        public virtual void Start()
        {
            Status = WorkerNodeStatus.Running;
        }

        private void InitSemaphores(PipExecutionContext context)
        {
            m_ramSemaphoreNameId = context.StringTable.AddString(RamSemaphoreName);
            m_ramSemaphoreIndex = m_workerSemaphores.CreateSemaphore(m_ramSemaphoreNameId, ProcessExtensions.PercentageResourceLimit);

            m_commitSemaphoreNameId = context.StringTable.AddString(CommitSemaphoreName);
            m_commitSemaphoreIndex = m_workerSemaphores.CreateSemaphore(m_commitSemaphoreNameId, ProcessExtensions.PercentageResourceLimit);
        }

        /// <summary>
        /// Signals that build is finished and that worker should exit
        /// </summary>
#pragma warning disable 1998 // Disable the warning for "This async method lacks 'await'"
        public virtual async Task FinishAsync(string buildFailure, [CallerMemberName] string callerName = null)
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
        /// Gets the currently acquired slots for process pips.
        /// </summary>
        public int AcquiredSlotsForProcessPips => AcquiredProcessSlots + AcquiredCacheLookupSlots;

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

        internal void TrackStatusOperation(OperationContext parent)
        {
            m_workerStatusOperation = parent.StartAsyncOperation(m_workerOperationKind);
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
        internal bool TryAcquireProcess(ProcessRunnablePip processRunnablePip, out WorkerResource? limitingResource, double loadFactor = 1, bool moduleAffinityEnabled = false)
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

            if (processRunnablePip.TryAcquireResources(m_workerSemaphores, GetAdditionalResourceInfo(processRunnablePip, expectedMemoryCounters), out limitingResourceName))
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
            else if (limitingResourceName == m_commitSemaphoreNameId)
            {
                limitingResource = WorkerResource.AvailableCommitMb;
            }
            else
            {
                limitingResource = WorkerResource.CreateSemaphoreResource(limitingResourceName.ToString(processRunnablePip.Environment.Context.StringTable));
            }

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

        private ProcessSemaphoreInfo[] GetAdditionalResourceInfo(ProcessRunnablePip runnableProcess, ProcessMemoryCounters expectedMemoryCounters)
        {
            using (var semaphoreInfoListWrapper = s_semaphoreInfoListPool.GetInstance())
            {
                var semaphores = semaphoreInfoListWrapper.Instance;

                if (runnableProcess.Process.RequiresAdmin
                    && runnableProcess.Environment.Configuration.Sandbox.AdminRequiredProcessExecutionMode.ExecuteExternalVm()
                    && runnableProcess.Environment.Configuration.Sandbox.VmConcurrencyLimit > 0)
                {
                    semaphores.Add(new ProcessSemaphoreInfo(
                        runnableProcess.Environment.Context.StringTable.AddString(PipInVmSemaphoreName),
                        value: 1,
                        limit: runnableProcess.Environment.Configuration.Sandbox.VmConcurrencyLimit));
                }

                if (TotalRamMb == null || runnableProcess.Environment.Configuration.Schedule.UseHistoricalRamUsageInfo != true)
                {
                    // Not tracking working set
                    return semaphores.ToArray();
                }

                bool enableLessAggresiveMemoryProjection = runnableProcess.Environment.Configuration.Schedule.EnableLessAggresiveMemoryProjection;
                var ramSemaphoreInfo = ProcessExtensions.GetNormalizedPercentageResource(
                        m_ramSemaphoreNameId,
                        usage: enableLessAggresiveMemoryProjection ? expectedMemoryCounters.AverageWorkingSetMb : expectedMemoryCounters.PeakWorkingSetMb,
                        total: TotalRamMb.Value);

                semaphores.Add(ramSemaphoreInfo);

                if (runnableProcess.Environment.Configuration.Schedule.EnableHistoricCommitMemoryProjection)
                {
                    var commitSemaphoreInfo = ProcessExtensions.GetNormalizedPercentageResource(
                        m_commitSemaphoreNameId,
                        usage: enableLessAggresiveMemoryProjection ? expectedMemoryCounters.AverageCommitSizeMb : expectedMemoryCounters.PeakCommitSizeMb,
                        total: TotalCommitMb.Value);

                    semaphores.Add(commitSemaphoreInfo);
                }

                return semaphores.ToArray();
            }
        }

        /// <summary>
        /// Gets the estimated memory counters for the process
        /// </summary>
        public ProcessMemoryCounters GetExpectedMemoryCounters(ProcessRunnablePip runnableProcess)
        {
            if (TotalRamMb == null || TotalCommitMb == null)
            {
                return ProcessMemoryCounters.CreateFromMb(0, 0, 0, 0);
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
                averageWorkingSetMb: runnableProcess.Process.IsLight ? 0 : DefaultWorkingSetMbPerProcess,
                peakCommitSizeMb: runnableProcess.Process.IsLight ? 0 : DefaultCommitSizeMbPerProcess,
                averageCommitSizeMb: runnableProcess.Process.IsLight ? 0 : DefaultCommitSizeMbPerProcess);
        }

        /// <summary>
        /// Release pip's resources after worker is done with the task
        /// </summary>
        public void ReleaseResources(RunnablePip runnablePip, bool isCancelledOrFailed = false)
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
                        if (isCancelledOrFailed)
                        {
                            releaseExecuteProcessSlots();
                            releasePostProcessSlots();
                        }

                        break;
                    }
                    case PipExecutionStep.ExecuteProcess:
                    {
                        releaseExecuteProcessSlots();
                        if (isCancelledOrFailed)
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
                if (stepCompleted == PipExecutionStep.ExecuteNonProcessPip || isCancelledOrFailed)
                {
                    Interlocked.Decrement(ref m_acquiredIpcSlots);
                    runnablePip.SetWorker(null);
                    runnablePip.AcquiredResourceWorker = null;
                }
            }

            if (AcquiredSlots == 0 && Status == WorkerNodeStatus.Stopping)
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
        /// Initializes the worker
        /// </summary>
        public virtual void InitializeForDistribution(IScheduleConfiguration scheduleConfig, PipGraph pipGraph, IExecutionLogTarget executionLogTarget, TaskSourceSlim<bool> schedulerCompletion)
        {
            m_isDistributedBuild = true;

            // Content tracking is needed when calculating setup cost per pip on each worker.
            // That's an expensive calculation, so it is disabled by default.
            m_isContentTrackingEnabled = scheduleConfig.EnableSetupCostWhenChoosingWorker;
            m_availableContent = new ContentTrackingSet(pipGraph);
            m_availableHashes = new ContentTrackingSet(pipGraph);
        }

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
