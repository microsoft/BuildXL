// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
#if PLATFORM_OSX
using BuildXL.Interop;
#endif
using BuildXL.Interop.Unix;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;
using BuildXL.Plugin;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Cache;
using BuildXL.Scheduler.Diagnostics;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.FileSystem;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Scheduler.Tracing;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.InputChange;
using BuildXL.Storage.Fingerprints;
using BuildXL.Tracing;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.VmCommandProxy;
using BuildXL.ViewModel;
using JetBrains.Annotations;
using static BuildXL.Processes.SandboxedProcessFactory;
using static BuildXL.Utilities.FormattableStringEx;
using Logger = BuildXL.Scheduler.Tracing.Logger;
using Process = BuildXL.Pips.Operations.Process;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Class implementing the scheduler.
    /// </summary>
    /// <remarks>
    /// All public methods are thread-safe.
    /// </remarks>
    [SuppressMessage("Microsoft.Maintainability", "CA1506")]
    public partial class Scheduler : IPipScheduler, IPipExecutionEnvironment, IFileContentManagerHost, IOperationTrackerHost, IDisposable
    {
        #region Constants

        /// <summary>
        /// The limit for I/O pip execution step to log a warning message.
        /// </summary>
        private const int PipExecutionIOStepDelayedLimitMin = 30;

        /// <summary>
        /// Ref count for pips which have executed already (distinct from ref count 0; ready to execute)
        /// </summary>
        private const int CompletedRefCount = -1;

        /// <summary>
        /// How many bits of priority are assigned to critical path portion.  The rest are assigned to the priority in the spec files
        /// </summary>
        private const int CriticalPathPriorityBitCount = 24;

        /// <summary>
        /// The max priority assigned to pips in the initial critical path
        /// prioritization
        /// </summary>
        private const int MaxInitialPipPriority = (1 << CriticalPathPriorityBitCount) - 1;

        /// <summary>
        /// The priority of IPC pips when entering the ChooseWorker queue. This is greater than
        /// <see cref="MaxInitialPipPriority"/> to ensure IPC takes priority over processes in the
        /// ChooseWorker queue and are not blocked waiting for highest priority process to acquire a worker
        /// </summary>
        private const int IpcPipChooseWorkerPriority = int.MaxValue;

        /// <summary>
        /// The piptypes we want to report stats for.
        /// </summary>
        private static readonly PipType[] s_pipTypesToLogStats =
        {
            PipType.Process, PipType.SealDirectory,
            PipType.CopyFile, PipType.WriteFile, PipType.Ipc,
        };

        /// <summary>
        /// The piptypes we want to report stats for.
        /// </summary>
        private static readonly PipType[] s_processPipTypesToLogStats =
        {
            PipType.Process,
        };

        /// <summary>
        /// Prefix used by the IDE integration for the name of EventHandles marking a value's successful completion
        /// </summary>
        public const string IdeSuccessPrefix = "Success";

        /// <summary>
        /// Prefix used by the IDE integration for the name of EventHandles marking a value's failed completion
        /// </summary>
        public const string IdeFailurePrefix = "Failure";

        /// <summary>
        /// The CPU utilization that gets logged when performance data isn't available
        /// </summary>
        public const long UtilizationWhenCountersNotAvailable = -1;

        /// <summary>
        /// Interval used to capture the status snapshot
        /// </summary>
        private const long StatusSnapshotInterval = 60;

        /// <summary>
        /// Dirty nodes file name for incremental scheduling.
        /// </summary>
        public const string DefaultIncrementalSchedulingStateFile = "SchedulerIncrementalSchedulingState";

        /// <summary>
        /// File change tracker file name.
        /// </summary>
        public const string DefaultSchedulerFileChangeTrackerFile = "SchedulerFileChangeTracker";

        /// <summary>
        /// <see cref="FingerprintStore"/> directory name.
        /// </summary>
        public const string FingerprintStoreDirectory = "FingerprintStore";

        /// <summary>
        /// <see cref="ILayoutConfiguration.SharedOpaqueSidebandDirectory"/> directory name.
        /// </summary>
        public const string SharedOpaqueSidebandDirectory = "SharedOpaqueSidebandState";

        private const int SealDirectoryContentFilterTimeoutMs = 1_000; // 1s

        #endregion Constants

        #region State

        /// <summary>
        /// Configuration. Ideally shouldn't be used because it reads config state not related to the scheduler.
        /// </summary>
        private readonly IConfiguration m_configuration;

        /// <summary>
        /// Configuration for schedule settings.
        /// </summary>
        private readonly IScheduleConfiguration m_scheduleConfiguration;

        /// <summary>
        /// The operation tracker. Internal for use by distribution
        /// </summary>
        internal readonly OperationTracker OperationTracker;

        /// <summary>
        /// File content manager for handling file materialization/hashing/content state tracking
        /// </summary>
        private readonly FileContentManager m_fileContentManager;

        /// <summary>
        /// Tracker of output materializations.
        /// </summary>
        private PipOutputMaterializationTracker m_pipOutputMaterializationTracker;

        /// <summary>
        /// RootMappings converted to string to be resued by pipExecutor
        /// </summary>
        /// <remarks>
        /// We should see if we can make the sandbox code take AbsolutePaths.
        /// Barring that change, we'd need to convert it constantly for each process
        /// This is the only 'reacheable' place to return a single instance for a given build
        /// through the IPipExecutionEnvironment.
        /// This is not ideal, but baby steps :)
        /// </remarks>
        private readonly IReadOnlyDictionary<string, string> m_rootMappings;

        /// <summary>
        /// Object that will simulate cache misses.
        /// </summary>
        /// <remarks>
        /// This is null when none are configured
        /// </remarks>
        private readonly ArtificialCacheMissOptions m_artificialCacheMissOptions;

        private readonly List<Worker> m_workers;

        /// <summary>
        /// Indicates if processes should be scheduled using the macOS sandbox when BuildXL is executing
        /// </summary>
        protected virtual bool MacOsSandboxingEnabled =>
            OperatingSystemHelper.IsUnixOS &&
            m_configuration.Sandbox.UnsafeSandboxConfiguration.SandboxKind != SandboxKind.None;

        /// <summary>
        /// A kernel extension connection object for macOS sandboxing
        /// </summary>
        [CanBeNull]
        protected ISandboxConnection SandboxConnection;

        /// <summary>
        /// Workers
        /// </summary>
        /// <remarks>
        /// There is at least one worker in the list of workers: LocalWorker.
        /// LocalWorker must be at the beginning of the list. All other workers must be remote.
        /// </remarks>
        public IList<Worker> Workers => m_workers;

        private AllWorker m_allWorker;

        /// <summary>
        /// Encapsulates data and logic for choosing a worker for cpu queue in a distributed build
        /// NOTE: this will be null before <see cref="Start(LoggingContext)"/> is called
        /// </summary>
        private ChooseWorkerCpu m_chooseWorkerCpu;

        private ChooseWorkerCacheLookup m_chooseWorkerCacheLookup;

        /// <summary>
        /// Local worker
        /// </summary>
        public LocalWorker LocalWorker { get; }

        /// <summary>
        /// Available workers count
        /// </summary>
        public int AvailableWorkersCount => Workers.Count(a => a.IsAvailable);

        /// <summary>
        /// Cached delegate for the main method which executes the pips
        /// </summary>
        private readonly Func<RunnablePip, Task> m_executePipFunc;

        /// <summary>
        /// Cleans temp directories in background
        /// </summary>
        public ITempCleaner TempCleaner { get; }

        /// <summary>
        /// The pip graph
        /// </summary>
        public readonly PipGraph PipGraph;

        /// <summary>
        /// Underlying data-flow graph for the pip graph.
        /// </summary>
        public IReadonlyDirectedGraph DirectedGraph => PipGraph.DirectedGraph;

        /// <summary>
        /// Test hooks.
        /// </summary>
        private readonly SchedulerTestHooks m_testHooks;

        /// <summary>
        /// Whether the current BuildXL instance serves as a master node in the distributed build and has workers attached.
        /// </summary>
        public bool AnyRemoteWorkers => m_workers.Count > 1;

        private readonly ConcurrentDictionary<PipId, RunnablePipPerformanceInfo> m_runnablePipPerformance;

        private readonly AbsolutePath m_fileChangeTrackerFile;

        private readonly AbsolutePath m_incrementalSchedulingStateFile;

        private readonly bool m_shouldCreateIncrementalSchedulingState;

        private readonly HashSet<PathAtom> m_outputFileExtensionsForSequentialScan;

        private int m_unresponsivenessFactor = 0;
        private int m_maxUnresponsivenessFactor = 0;
        private DateTime m_statusLastCollected = DateTime.MaxValue;

        private readonly PipRetryInfo m_pipRetryInfo = new PipRetryInfo();
        private readonly PipPropertyInfo m_pipPropertyInfo = new PipPropertyInfo();

        private readonly HashSet<string> m_writableDrives;

        private readonly TaskSourceSlim<bool> m_schedulerCompletionExceptMaterializeOutputs = TaskSourceSlim.Create<bool>();

        /// <summary>
        /// Top N Pip performance info for telemetry logging
        /// </summary>
        private readonly PerProcessPipPerformanceInformationStore m_perPipPerformanceInfoStore;


        private const double BytesInMb = 1024 * 1024;

        /// <summary>
        /// Enables distribution for the master node
        /// </summary>
        public void EnableDistribution(Worker[] remoteWorkers)
        {
            Contract.Requires(remoteWorkers != null);

            Contract.Assert(m_workers.Count == 1, "Local worker must exist");
            Contract.Assert(IsDistributedMaster, I($"{nameof(EnableDistribution)} can be called only for the master node"));

            // Ensure that the resource mappings match between workers
            foreach (var worker in remoteWorkers)
            {
                worker.SyncResourceMappings(LocalWorker);
            }

            m_workers.AddRange(remoteWorkers);
            PipExecutionCounters.AddToCounter(PipExecutorCounter.RemoteWorkerCount, remoteWorkers.Length);
        }

        private void StartWorkers(LoggingContext loggingContext)
        {
            m_workersStatusOperation = OperationTracker.StartOperation(Worker.WorkerStatusParentOperationKind, loggingContext);

            // The first of the workers must be local and all others must be remote.
            Contract.Assert(m_workers[0] is LocalWorker && m_workers.Skip(1).All(w => w.IsRemote));

            foreach (var worker in m_workers)
            {
                // Create combined log target for remote workers
                IExecutionLogTarget workerExecutionLogTarget = worker.IsLocal ?
                    ExecutionLog :
                    ExecutionLog?.CreateWorkerTarget((uint)worker.WorkerId);

                worker.TrackStatusOperation(m_workersStatusOperation);
                worker.Initialize(PipGraph, workerExecutionLogTarget, m_schedulerCompletionExceptMaterializeOutputs);
                worker.AdjustTotalCacheLookupSlots(m_scheduleConfiguration.MaxCacheLookup * (worker.IsLocal ? 1 : 5)); // Oversubscribe the cachelookup step for remote workers
                worker.StatusChanged += OnWorkerStatusChanged;
                worker.Start();
            }

            m_allWorker = new AllWorker(m_workers.ToArray());

            ExecutionLog?.WorkerList(new WorkerListEventData { Workers = m_workers.SelectArray(w => w.Name) });
        }

        private bool AnyPendingPipsExceptMaterializeOutputs()
        {
            // We check here whether the scheduler is busy only with materializeOutputs.
            // Because retrieving pip states is expensive, we first calculate how many pips there are in non-materialize queues.
            // If it is 0, then we get the pip states. If there is no ready, waiting, and running pips; it means that the scheduler is done with all work
            // or it is only busy with materializeOutput step. As we mark the pips as completed if materializeOutputsInBackground is enabled, they have "Done" state.

            long numRunningOrQueued = m_pipQueue.NumRunningOrQueued;
            long numRunningOrQueuedExceptMaterialize = numRunningOrQueued - m_pipQueue.GetNumRunningByKind(DispatcherKind.Materialize) - m_pipQueue.GetNumQueuedByKind(DispatcherKind.Materialize);

            if (numRunningOrQueuedExceptMaterialize == 0)
            {
                RetrievePipStateCounts(out long totalPips, out long readyPips, out long waitingPips, out long runningPips, out long donePips, out long failedPips, out long skippedPips, out long ignoredPips);

                if (readyPips + waitingPips + runningPips == 0)
                {
                    // It means that there are only pips materializing outputs in the background.
                    return false;
                }
            }

            return true;
        }

        private readonly object m_workerStatusLock = new object();
        private OperationContext m_workersStatusOperation;

        private void OnWorkerStatusChanged(Worker worker)
        {
            lock (m_workerStatusLock)
            {
                worker.UpdateStatusOperation();
                AdjustLocalWorkerSlots();
            }
        }

        private void AdjustLocalWorkerSlots()
        {
            int availableWorkersCount = AvailableWorkersCount;
            if (availableWorkersCount == 0)
            {
                return;
            }

            int targetProcessSlots = m_scheduleConfiguration.MaxProcesses;
            int targetCacheLookupSlots = m_scheduleConfiguration.MaxCacheLookup;

            // If the user does not pass masterCpuMultiplier, then the local worker slots are configured
            // based on the number of available workers.
            // If only local worker is available, then the multiplier would be 1.
            // If there is one available remote worker, then the multiplier would be 0.5; meaning that
            //  the local worker will do the half work.
            double cpuMultiplier = m_scheduleConfiguration.MasterCpuMultiplier ?? 1.0 / availableWorkersCount;

            double cacheLookupMultiplier = m_scheduleConfiguration.MasterCacheLookupMultiplier ?? 1.0 / availableWorkersCount;

            int newProcessSlots = (int)(targetProcessSlots * cpuMultiplier);
            int newCacheLookupSlots = (int)(targetCacheLookupSlots * cacheLookupMultiplier);

            LocalWorker.AdjustTotalProcessSlots(newProcessSlots);
            LocalWorker.AdjustTotalCacheLookupSlots(newCacheLookupSlots);

            int totalProcessSlots = Workers.Where(w => w.IsAvailable).Sum(w => w.TotalProcessSlots);

            m_pipQueue.SetTotalProcessSlots(totalProcessSlots);
        }

        private void SetQueueMaxParallelDegreeByKind(DispatcherKind kind, int maxConcurrency)
        {
            m_pipQueue.SetMaxParallelDegreeByKind(kind, maxConcurrency);
        }

        /// <summary>
        /// The pip runtime information
        /// </summary>
        private PipRuntimeInfo[] m_pipRuntimeInfos;

        private HistoricPerfDataTable m_historicPerfDataTable;
        private readonly AsyncLazy<HistoricPerfDataTable> m_historicPerfDataTableTask;

        /// <summary>
        /// The last node in the currently computed critical path
        /// </summary>
        private int m_criticalPathTailPipIdValue = unchecked((int)PipId.Invalid.Value);

        /// <summary>
        /// Historical estimation for duration of each pip, indexed by semi stable hashes
        /// </summary>
        public HistoricPerfDataTable HistoricPerfDataTable => (m_historicPerfDataTable = m_historicPerfDataTable ?? (m_historicPerfDataTableTask?.Value ?? new HistoricPerfDataTable(m_loggingContext)));

        /// <summary>
        /// Nodes that are explicitly scheduled by filtering.
        /// </summary>
        /// <remarks>
        /// Only includes the pips matching the filter itself, not their dependencies or dependents that may be included
        /// based on the filter's dependency selection settings
        /// </remarks>
        private HashSet<NodeId> m_explicitlyScheduledNodes;

        /// <summary>
        /// Process nodes that are explicitly scheduled by filtering.
        /// </summary>
        /// <remarks>
        /// Only includes the pips matching the filter itself, not their dependencies or dependents that may be included
        /// based on the filter's dependency selection settings
        /// </remarks>
        private HashSet<NodeId> m_explicitlyScheduledProcessNodes;

        /// <summary>
        /// Nodes that must be executed when dirty build is enabled(/unsafe_forceSkipDeps+)
        /// </summary>
        /// <remarks>
        /// During scheduling, dirty build already skips some pips whose inputs are present.
        /// However, there are some pips cannot be skipped during scheduling even though their inputs are present.
        /// Those pips are in the transitive dependency chain between explicitly scheduled nodes.
        /// This list only contains Process and Copy file pips.
        /// </remarks>
        private HashSet<NodeId> m_mustExecuteNodesForDirtyBuild;

        /// <summary>
        /// Service manager.
        /// </summary>
        private readonly SchedulerServiceManager m_serviceManager;

        /// <summary>
        /// External API server.
        /// </summary>
        [CanBeNull]
        private ApiServer m_apiServer;

        [CanBeNull]
        private PluginManager m_pluginManager;

        /// <summary>
        /// Tracker for drop pips.
        /// </summary>
        [CanBeNull]
        private readonly DropPipTracker m_dropPipTracker;

        /// <summary>
        /// Pip table holding all known pips.
        /// </summary>
        private readonly PipTable m_pipTable;

        /// <summary>
        /// Set to true when the scheduler should stop scheduling further pips.
        /// </summary>
        /// <remarks>
        /// It is volatile because all threads accessing this variable should read latest values.
        /// Reading and writing to a boolean are atomic operations.
        /// </remarks>
        private volatile bool m_scheduleTerminating;

        /// <summary>
        /// Indicates if there are failures in any of the scheduled pips.
        /// </summary>
        /// <remarks>
        /// It is volatile because all threads accessing this variable should read latest values.
        /// Reading and writing to a boolean are atomic operations.
        /// </remarks>
        private volatile bool m_hasFailures;

        /// <summary>
        /// A dedicated thread to schedule pips in the PipQueue.
        /// </summary>
        private Thread m_drainThread;

        /// <summary>
        /// Optional analyzer for post-processing file monitoring violations. Exposed as <see cref="IPipExecutionEnvironment.FileMonitoringViolationAnalyzer" />
        /// for use by executing pips.
        /// </summary>
        private readonly FileMonitoringViolationAnalyzer m_fileMonitoringViolationAnalyzer;

        /// <summary>
        /// Dictionary of number of cache descriptor hits by cache name.
        /// </summary>
        private readonly ConcurrentDictionary<string, int> m_cacheIdHits = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// Whether the scheduler is initialized with pip stats and priorities
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Time the process started. Used for reporting
        /// </summary>
        private DateTime? m_processStartTimeUtc;

        /// <summary>
        /// Tracks time when the status snapshot was last updated
        /// </summary>
        private DateTime m_statusSnapshotLastUpdated;

        /// <summary>
        /// Indicates that the scheduler is disposed
        /// </summary>
        private bool m_isDisposed;

        /// <summary>
        /// Nodes to schedule after filtering (not including the dependents of filter passing nodes)
        /// </summary>
        public RangedNodeSet FilterPassingNodes { get; private set; }

        /// <summary>
        /// The graph representing all scheduled nodes after filtering
        /// </summary>
        /// <remarks>
        /// If filtering is not used, this equals to DataflowGraph
        /// </remarks>
        public IReadonlyDirectedGraph ScheduledGraph { get; private set; }

        /// <summary>
        /// Root filter
        /// </summary>
        public RootFilter RootFilter { get; private set; }

        /// <summary>
        /// Whether the first pip is started processing (checking for cache hit) (0: no, 1: yes)
        /// </summary>
        private int m_firstPip;

        /// <summary>
        /// Whether the first pip is started executing (external process launch) (0: no, 1: yes)
        /// </summary>
        private int m_firstExecutedPip;

        /// <summary>
        /// Retrieve the count of pips in all the different states
        /// </summary>
        /// <param name="totalPips">Total number of pips</param>
        /// <param name="readyPips">Number of pending pips</param>
        /// <param name="waitingPips">Number of queued pips</param>
        /// <param name="runningPips">Number of running pips</param>
        /// <param name="donePips">Number of completed pips</param>
        /// <param name="failedPips">Number of failed pips</param>
        /// <param name="skippedPipsDueToFailedDependencies">Number of skipped pips due to failed dependencies</param>
        /// <param name="ignoredPips">Number of ignored pips</param>
        public void RetrievePipStateCounts(
            out long totalPips,
            out long readyPips,
            out long waitingPips,
            out long runningPips,
            out long donePips,
            out long failedPips,
            out long skippedPipsDueToFailedDependencies,
            out long ignoredPips)
        {
            lock (m_statusLock)
            {
                m_pipStateCounters.CollectSnapshot(s_pipTypesToLogStats, m_pipTypesToLogCountersSnapshot);

                readyPips = m_pipTypesToLogCountersSnapshot[PipState.Ready];
                donePips = m_pipTypesToLogCountersSnapshot.DoneCount;
                failedPips = m_pipTypesToLogCountersSnapshot[PipState.Failed];
                skippedPipsDueToFailedDependencies = m_pipTypesToLogCountersSnapshot.SkippedDueToFailedDependenciesCount;
                ignoredPips = m_pipTypesToLogCountersSnapshot.IgnoredCount;
                waitingPips = m_pipTypesToLogCountersSnapshot[PipState.Waiting];
                runningPips = m_pipTypesToLogCountersSnapshot.RunningCount;
            }

            totalPips = m_pipTable.Count;
        }

        /// <summary>
        /// Saves file change tracker and its associates, e.g., incremental scheduling state.
        /// </summary>
        /// <remarks>
        /// This operation requires that the schedule is quiescent, i.e., has completed and nothing else has been queued (end of a build).
        /// </remarks>
        public async Task SaveFileChangeTrackerAsync(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);

            FileEnvelopeId fileEnvelopeId = m_fileChangeTracker.GetFileEnvelopeToSaveWith();
            string fileChangeTrackerPath = m_fileChangeTrackerFile.ToString(Context.PathTable);

            // Unblock caller.
            await Task.Yield();

            Parallel.Invoke(
                async () =>
                {
                    m_fileChangeTracker.SaveTrackingStateIfChanged(fileChangeTrackerPath, fileEnvelopeId);
                    if (m_configuration.Logging.LogExecution && m_configuration.Engine.ScanChangeJournal)
                    {
                        await TryDuplicateSchedulerFileToLogDirectoryAsync(loggingContext, m_fileChangeTrackerFile, DefaultSchedulerFileChangeTrackerFile);
                    }
                },
                async () =>
                {
                    if (IncrementalSchedulingState != null)
                    {
                        string dirtyNodePath = m_incrementalSchedulingStateFile.ToString(Context.PathTable);
                        IncrementalSchedulingState.SaveIfChanged(fileEnvelopeId, dirtyNodePath);

                        if (m_configuration.Logging.LogExecution)
                        {
                            await TryDuplicateSchedulerFileToLogDirectoryAsync(loggingContext, m_incrementalSchedulingStateFile, DefaultIncrementalSchedulingStateFile);
                        }
                    }
                });
        }

        private async Task TryDuplicateSchedulerFileToLogDirectoryAsync(LoggingContext loggingContext, AbsolutePath filePath, string destinationFileName)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(filePath.IsValid);

            var sourcePath = filePath.ToString(Context.PathTable);
            var logDirectory = m_configuration.Logging.EngineCacheLogDirectory.ToString(Context.PathTable);
            var destinationPath = Path.Combine(logDirectory, destinationFileName);

            try
            {
                await FileUtilities.TryDuplicateOneFileAsync(sourcePath, destinationPath);
            }
            catch (BuildXLException ex)
            {
                Logger.Log.FailedToDuplicateSchedulerFile(loggingContext, sourcePath, destinationPath, (ex.InnerException ?? ex).Message);
            }
        }

        /// <summary>
        /// Tries get pip ref-count.
        /// </summary>
        public bool GetPipRefCount(PipId pipId, out int refCount)
        {
            refCount = -1;

            if (pipId.IsValid && m_pipTable.IsValid(pipId))
            {
                refCount = GetPipRuntimeInfo(pipId).RefCount;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get the current state of a pip
        /// </summary>
        /// <returns>The pip state</returns>
        public PipState GetPipState(PipId pipId) => GetPipRuntimeInfo(pipId).State;

        /// <summary>
        /// Get the pip type from <see cref="PipId"/>
        /// </summary>
        public PipType GetPipType(PipId pipId) => m_pipTable.GetPipType(pipId);

        private bool IsPipCleanMaterialized(PipId pipId)
        {
            return IncrementalSchedulingState != null && IncrementalSchedulingState.DirtyNodeTracker.IsNodeCleanAndMaterialized(pipId.ToNodeId());
        }

        #endregion State

        #region Members: Fingerprinting, Content Hashing, and Output Caching

        /// <summary>
        /// Manages materialization and tracking of source and output content.
        /// </summary>
        /// <remarks>
        /// May be null, if pip caching is disabled.
        /// </remarks>
        private LocalDiskContentStore m_localDiskContentStore;

        /// <summary>
        /// File content table.
        /// </summary>
        private readonly FileContentTable m_fileContentTable;

        /// <summary>
        /// Tracks 'dirtied' nodes that need to re-run, build-over-build. The dirty-node set can be updated by scanning volumes for file changes.
        /// (to do so, this records the identities of files used within graph execution
        /// </summary>
        public IIncrementalSchedulingState IncrementalSchedulingState { get; private set; }

        /// <summary>
        /// File change tracker.
        /// </summary>
        private FileChangeTracker m_fileChangeTracker;

        /// <summary>
        /// Journal state.
        /// </summary>
        private readonly JournalState m_journalState;

        /// <summary>
        /// File access allowlist.
        /// </summary>
        private readonly FileAccessAllowlist m_fileAccessAllowlist;

        /// <summary>
        /// Directory membership fingerprinter rule set.
        /// </summary>
        private readonly DirectoryMembershipFingerprinterRuleSet m_directoryMembershipFingerprinterRules;

        /// <summary>
        /// Previous inputs salt.
        /// </summary>
        private readonly PreserveOutputsInfo m_previousInputsSalt;

        /// <summary>
        /// Pip content fingerprinter.
        /// </summary>
        private readonly PipContentFingerprinter m_pipContentFingerprinter;

        /// <summary>
        /// Pip fragment renderer.
        /// </summary>
        private readonly PipFragmentRenderer m_pipFragmentRenderer;

        /// <summary>
        /// IpcProvider for executing IPC pips.
        /// </summary>
        private readonly IpcProviderWithMemoization m_ipcProvider;

        /// <summary>
        /// Fingerprinter for the membership of directories, for generating and validating cache assertions on directories.
        /// </summary>
        private readonly DirectoryMembershipFingerprinter m_directoryMembershipFingerprinter;

        /// <summary>
        /// Expander used when a path string should be machine / configuration independent.
        /// </summary>
        private readonly SemanticPathExpander m_semanticPathExpander;

        /// <summary>
        /// The Execute phase logging context - used during the pip execution only.
        /// </summary>
        private LoggingContext m_executePhaseLoggingContext;

        /// <summary>
        /// Logging interval in ms for performance information. A time interval of 0 represents no restrictions to logging (always log)
        /// </summary>
        private readonly int m_loggingIntervalPeriodMs;

        /// <summary>
        /// Previous UTC time when the UpdateStatus logs where logged
        /// </summary>
        private DateTime m_previousStatusLogTimeUTC;

        /// <summary>
        /// The fingerprint of the build engine.
        /// </summary>
        private readonly string m_buildEngineFingerprint;

        /// <summary>
        /// Gets whether the machine represents a distributed worker
        /// </summary>
        private bool IsDistributedWorker => m_configuration.Distribution.BuildRole == DistributedBuildRoles.Worker;

        /// <summary>
        /// Gets whether the machine represents a distributed master
        /// </summary>
        private bool IsDistributedMaster => m_configuration.Distribution.BuildRole == DistributedBuildRoles.Master;

        /// <summary>
        /// Gets whether inputs are lazily materialized
        /// </summary>
        private bool InputsLazilyMaterialized =>
            m_scheduleConfiguration.EnableLazyOutputMaterialization
            || IsDistributedBuild
            || m_scheduleConfiguration.OutputMaterializationExclusionRoots.Count != 0;

        /// <summary>
        /// Indicates if outputs should be materialized in background rather than inline
        /// </summary>
        private bool MaterializeOutputsInBackground => InputsLazilyMaterialized && IsDistributedBuild;

        /// <summary>
        /// Gets whether the machine represents a distributed master or worker
        /// </summary>
        private bool IsDistributedBuild => IsDistributedWorker || IsDistributedMaster;

        /// <summary>
        /// PipTwoPhaseCache
        /// </summary>
        private readonly PipTwoPhaseCache m_pipTwoPhaseCache;

        /// <summary>
        /// Checks if incremental scheduling is enabled in the scheduler.
        /// </summary>
        public bool IsIncrementalSchedulingEnabled => IncrementalSchedulingState != null;

        /// <summary>
        /// Logging context
        /// </summary>
        private readonly LoggingContext m_loggingContext;

        #endregion

        #region Ready Queue

        /// <summary>
        /// Ready queue of executable pips.
        /// </summary>
        private readonly IPipQueue m_pipQueue;

        private PipQueue OptionalPipQueueImpl => m_pipQueue as PipQueue;

        #endregion

        #region Statistics

        private ulong m_totalPeakWorkingSetMb;
        private ulong m_totalAverageWorkingSetMb;

        private ulong m_totalPeakCommitSizeMb;
        private ulong m_totalAverageCommitSizeMb;

        private readonly object m_statusLock = new object();

        /// <summary>
        /// Live counters for the number of pips in each state.
        /// </summary>
        /// <remarks>
        /// For these counters to be accurate, all pip transitions must be via the extension methods on
        /// <see cref="PipRuntimeInfoCounterExtensions" />.
        /// </remarks>
        private readonly PipStateCounters m_pipStateCounters = new PipStateCounters();

        /// <summary>
        /// A pre-allocated container for snapshots of per-state pip counts.
        /// </summary>
        /// <remarks>
        /// Must be updated and read under <see cref="m_statusLock" /> for consistent results.
        /// </remarks>
        private readonly PipStateCountersSnapshot m_pipTypesToLogCountersSnapshot = new PipStateCountersSnapshot();
        private readonly PipStateCountersSnapshot m_processStateCountersSnapshot = new PipStateCountersSnapshot();
        private readonly PipStateCountersSnapshot[] m_pipStateCountersSnapshots = new PipStateCountersSnapshot[(int)PipType.Max];

        /// <summary>
        /// This is the total number of process pips that were run through the scheduler. They may be hit, miss, pass,
        /// fail, skip, etc.
        /// </summary>
        private long m_numProcessPipsCompleted;

        /// <summary>
        /// This is the count of processes that were cache hits and not launched.
        /// </summary>
        private long m_numProcessPipsSatisfiedFromCache;

        /// <summary>
        /// The count of processes that were determined up to date by incremental scheduling and didn't flow
        /// through the scheduler. This count is included in <see cref="m_numProcessPipsSatisfiedFromCache"/>. This count
        /// does not include the "frontier" which do flow through the scheduler to ensure their outputs are cached.
        /// </summary>
        private long m_numProcessesIncrementalSchedulingPruned;

        /// <summary>
        /// This is the count of processes that were cache misses and had the external process launched.
        /// </summary>
        private long m_numProcessPipsUnsatisfiedFromCache;

        /// <summary>
        /// This is the count of processes that were skipped due to failed dependencies.
        /// </summary>
        private long m_numProcessPipsSkipped;

        /// <summary>
        /// This is the number of process pips that were delayed due to semaphore constraints.
        /// </summary>
        private long m_numProcessPipsSemaphoreQueued;

        /// <summary>
        /// This is the total number of IPC pips that were run through the scheduler. They may be pass, fail, skip, etc.
        /// </summary>
        private long m_numIpcPipsCompleted;

        /// <summary>
        /// The total number of service pips scheduled (i.e. not in the Ignored state)
        /// </summary>
        private long m_numServicePipsScheduled;

        /// <summary>
        /// Number of pips which produced tool warnings from cache.
        /// </summary>
        private int m_numPipsWithWarningsFromCache;

        /// <summary>
        /// How many tool warnings were replayed from cache.
        /// </summary>
        private long m_numWarningsFromCache;

        /// <summary>
        /// Number of pips which produced tool warnings (excluding those from cache).
        /// </summary>
        private int m_numPipsWithWarnings;

        /// <summary>
        /// How many tool warnings occurred (excluding those from cache).
        /// </summary>
        private long m_numWarnings;

        /// <summary>
        /// What is the maximum critical path based on historical and suggested data, and what is the good-ness (origin) of critical path info.
        /// </summary>
        private CriticalPathStats m_criticalPathStats;

        /// <summary>
        /// <see cref="PipExecutionState.LazyDeletionOfSharedOpaqueOutputsEnabled"/>
        /// </summary>
        private bool m_lazyDeletionOfSharedOpaqueOutputsEnabled = false;

        /// <summary>
        /// Gets counters for the details of pip execution and cache interaction.
        /// These counters are thread safe, but are only complete once all pips have executed.
        /// </summary>
        public CounterCollection<PipExecutorCounter> PipExecutionCounters { get; } = new CounterCollection<PipExecutorCounter>();

        /// <summary>
        /// Counter collections aggregated by in-filter (explicitly scheduled) or dependencies-of-filter (implicitly scheduled).
        /// </summary>
        /// <remarks>
        /// These counters exclude service start or shutdown process pips.
        /// </remarks>
        public PipCountersByFilter ProcessPipCountersByFilter { get; private set; }

        /// <summary>
        /// Counter collections aggregated by telemetry tag.
        /// </summary>
        /// <remarks>
        /// These counters exclude service start or shutdown process pips.
        /// </remarks>
        public PipCountersByTelemetryTag ProcessPipCountersByTelemetryTag { get; private set; }

        private PipCountersByGroupAggregator m_groupedPipCounters;
        private readonly CounterCollection<PipExecutionStep> m_pipExecutionStepCounters = new CounterCollection<PipExecutionStep>();
        private readonly CounterCollection<FingerprintStoreCounters> m_fingerprintStoreCounters = new CounterCollection<FingerprintStoreCounters>();

        /// <summary>
        /// Counts the number of Pips failing due to network failures 0 times, 1 time, 2 times, etc. upto Configuration.Distribution.NumRetryFailedPipsOnAnotherWorker
        /// </summary>
        private readonly int[] m_pipRetryCountersDueToNetworkFailures;

        private readonly ConcurrentDictionary<int, int> m_pipRetryCountersDueToLowMemory = new ConcurrentDictionary<int, int>();

        private sealed class CriticalPathStats
        {
            /// <summary>
            /// Number of nodes for which critical path duration suggestions were available
            /// </summary>
            public long NumHits;

            /// <summary>
            /// Number of nodes for which a critical path duration suggestions have been guessed by a default heuristic
            /// </summary>
            public long NumWildGuesses;

            /// <summary>
            /// Longest critical path length.
            /// </summary>
            public long LongestPath;
        }

        private readonly PerformanceCollector.Aggregator m_performanceAggregator;

        /// <summary>
        /// Last machine performance info collected
        /// </summary>
        private PerformanceCollector.MachinePerfInfo m_perfInfo;

        /// <summary>
        /// Samples performance characteristics of the execution phase
        /// </summary>
        public ExecutionSampler ExecutionSampler { get; }

        /// <summary>
        /// Whether a low ram memory perf smell was reached
        /// </summary>
        private volatile bool m_hitLowRamMemoryPerfSmell;

        /// <summary>
        /// Whether a low commit memory perf smell was reached
        /// </summary>
        private volatile bool m_hitLowCommitMemoryPerfSmell;

        private int m_historicPerfDataMisses;
        private int m_historicPerfDataZeroMemoryHits;
        private int m_historicPerfDataNonZeroMemoryHits;

        /// <summary>
        /// Maps modules to the number of process pips and the list of workers assigned.
        /// </summary>
        /// <remarks>
        /// This is populated only with /maxWorkersPerModule is passed with a value greater than 0.
        /// </remarks>
        private readonly Dictionary<ModuleId, (int NumPips, List<Worker> Workers)> m_moduleWorkerMapping = new Dictionary<ModuleId, (int, List<Worker>)>();

        #endregion Statistics

        /// <summary>
        /// Sets the process start time
        /// </summary>
        public void SetProcessStartTime(DateTime processStartTimeUtc)
        {
            m_processStartTimeUtc = processStartTimeUtc;
        }

        #region Constructor

        /// <summary>
        /// Constructs a scheduler for an immutable pip graph.
        /// </summary>
        public Scheduler(
            PipGraph graph,
            IPipQueue pipQueue,
            PipExecutionContext context,
            FileContentTable fileContentTable,
            EngineCache cache,
            IConfiguration configuration,
            FileAccessAllowlist fileAccessAllowlist,
            LoggingContext loggingContext,
            string buildEngineFingerprint,
            DirectoryMembershipFingerprinterRuleSet directoryMembershipFingerprinterRules = null,
            ITempCleaner tempCleaner = null,
            AsyncLazy<HistoricPerfDataTable> runningTimeTable = null,
            PerformanceCollector performanceCollector = null,
            string fingerprintSalt = null,
            PreserveOutputsInfo? previousInputsSalt = null,
            DirectoryTranslator directoryTranslator = null,
            IIpcProvider ipcProvider = null,
            PipTwoPhaseCache pipTwoPhaseCache = null,
            JournalState journalState = null,
            VmInitializer vmInitializer = null,
            SchedulerTestHooks testHooks = null)
        {
            Contract.Requires(graph != null);
            Contract.Requires(pipQueue != null);
            Contract.Requires(cache != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(configuration != null);
            Contract.Requires(fileAccessAllowlist != null);
            // Only allow this to be null in testing
            if (tempCleaner == null)
            {
                Contract.Requires(testHooks != null);
            }

            // FIX: Change to assert to work around bug in rewriter
            Contract.Assert(context != null);

            m_buildEngineFingerprint = buildEngineFingerprint;

            fingerprintSalt = fingerprintSalt ?? string.Empty;
            m_configuration = configuration;
            m_scheduleConfiguration = configuration.Schedule;
            PipFingerprintingVersion fingerprintVersion = PipFingerprintingVersion.TwoPhaseV2;
            var extraFingerprintSalts = new ExtraFingerprintSalts(
                    configuration,
                    fingerprintVersion,
                    fingerprintSalt,
                    searchPathToolsHash: directoryMembershipFingerprinterRules?.ComputeSearchPathToolsHash());

            Logger.Log.PipFingerprintData(loggingContext, fingerprintVersion: (int)fingerprintVersion, fingerprintSalt: extraFingerprintSalts.FingerprintSalt);

            PipGraph = graph;
            m_semanticPathExpander = PipGraph.SemanticPathExpander;
            m_pipTable = PipGraph.PipTable;

            m_performanceAggregator = performanceCollector?.CreateAggregator();
            ExecutionSampler = new ExecutionSampler(IsDistributedBuild, pipQueue.MaxProcesses);

            m_pipQueue = pipQueue;
            Context = context;

            m_pipContentFingerprinter = new PipContentFingerprinter(
                context.PathTable,
                artifact => m_fileContentManager.GetInputContent(artifact).FileContentInfo,
                extraFingerprintSalts,
                m_semanticPathExpander,
                PipGraph.QueryFileArtifactPipData,
                process => m_fileContentManager.SourceChangeAffectedInputs.GetChangeAffectedInputs(process),
                pipId => PipGraph.TryGetPipFingerprint(pipId, out var fingerprint) ? fingerprint.Hash : default);
            m_historicPerfDataTableTask = runningTimeTable;

            // Prepare Root Map redirection table. see m_rootMappings comment on why this is happening here.
            var rootMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (m_configuration.Engine.RootMap != null)
            {
                foreach (var rootMapping in m_configuration.Engine.RootMap)
                {
                    rootMappings.Add(rootMapping.Key, rootMapping.Value.ToString(context.PathTable));
                }
            }

            m_rootMappings = rootMappings;

            // Prepare artificial cache miss.
            var artificalCacheMissConfig = configuration.Cache.ArtificialCacheMissOptions;
            if (artificalCacheMissConfig != null)
            {
                m_artificialCacheMissOptions = new ArtificialCacheMissOptions(
                    artificalCacheMissConfig.Rate / (double)ushort.MaxValue,
                    artificalCacheMissConfig.IsInverted,
                    artificalCacheMissConfig.Seed);
            }

            m_fileContentTable = fileContentTable;
            m_journalState = journalState ?? JournalState.DisabledJournal;
            DirectoryTranslator = directoryTranslator;
            m_directoryMembershipFingerprinterRules = directoryMembershipFingerprinterRules;
            m_previousInputsSalt = previousInputsSalt ?? UnsafeOptions.PreserveOutputsNotUsed;
            m_fileAccessAllowlist = fileAccessAllowlist;

            // Done setting up tracking of local disk state.

            // Caching artifact content and fingerprints:
            // - We always have a cache of artifact content (we want one path to materialize content at any location, by hash)
            // - We always have a store for 'fingerprint' -> prior run information (PipCacheDescriptor)
            Cache = cache;

            // Prime the dummy provenance since its creation requires adding a string to the TokenText table, which gets frozen after scheduling
            // is complete. GetDummyProvenance may be called during execution (after the schedule phase)
            GetDummyProvenance();

            TempCleaner = tempCleaner;

            // Ensure that when the cancellationToken is signaled, we respond with the
            // internal cancellation process.
            m_cancellationTokenRegistration = context.CancellationToken.Register(() => RequestTermination());

            m_serviceManager = new SchedulerServiceManager(graph, context);
            m_pipFragmentRenderer = this.CreatePipFragmentRenderer();
            m_ipcProvider = new IpcProviderWithMemoization(
                ipcProvider ?? IpcFactory.GetProvider(),
                defaultClientLogger: CreateLoggerForIpcClients(loggingContext));

            m_dropPipTracker = new DropPipTracker(Context);

            OperationTracker = new OperationTracker(loggingContext, this);
            m_fileContentManager = new FileContentManager(this, OperationTracker);
            m_apiServer = null;
            m_pluginManager = null;

            m_writableDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var reverseDirectoryTranslator = directoryTranslator?.GetReverseTranslator();
            foreach (AbsolutePath path in m_semanticPathExpander.GetWritableRoots())
            {
                var driveName = !OperatingSystemHelper.IsUnixOS
                    ? GetRootDriveForPath(path, reverseDirectoryTranslator, context)
                    : IO.GetMountNameForPath(path.ToString(Context.PathTable));
                if (driveName != null)
                {
                    m_writableDrives.Add(driveName);
                }
            }

            var sealContentsById = new ConcurrentBigMap<DirectoryArtifact, int[]>();

            // Cache delegate for ExecutePip to avoid creating delegate everytime you pass ExecutePip to PipQueue.
            m_executePipFunc = ExecutePip;

            for (int i = 0; i < m_pipStateCountersSnapshots.Length; i++)
            {
                m_pipStateCountersSnapshots[i] = new PipStateCountersSnapshot();
            }

            m_testHooks = testHooks;
            LocalWorker = new LocalWorker(m_scheduleConfiguration, m_testHooks?.DetoursListener);
            m_workers = new List<Worker> { LocalWorker };

            m_statusSnapshotLastUpdated = DateTime.UtcNow;

            m_loggingIntervalPeriodMs = GetLoggingPeriodInMsForExecution(configuration);
            m_previousStatusLogTimeUTC = DateTime.UtcNow.AddMilliseconds(-1 * m_loggingIntervalPeriodMs); // Reducing by loggingIntervalPeriodMs to enable logging in the first call to UpdateStatus
            m_pipTwoPhaseCache = pipTwoPhaseCache ?? new PipTwoPhaseCache(loggingContext, cache, context, m_semanticPathExpander);
            m_runnablePipPerformance = new ConcurrentDictionary<PipId, RunnablePipPerformanceInfo>();

            m_fileChangeTrackerFile = m_configuration.Layout.SchedulerFileChangeTrackerFile;
            m_incrementalSchedulingStateFile = m_configuration.Layout.IncrementalSchedulingStateFile;

            var numChangedGvfsProjections = m_journalState.VolumeMap?.ChangedGvfsProjections.Count ?? 0;

            m_shouldCreateIncrementalSchedulingState =
                m_journalState.IsEnabled &&
                m_configuration.Schedule.IncrementalScheduling &&
                m_configuration.Distribution.BuildRole == DistributedBuildRoles.None &&
                m_configuration.Schedule.ForceSkipDependencies == ForceSkipDependenciesMode.Disabled &&
                numChangedGvfsProjections == 0;

            if (numChangedGvfsProjections > 0)
            {
                Logger.Log.IncrementalSchedulingDisabledDueToGvfsProjectionChanges(
                    m_loggingContext,
                    string.Join(", ", m_journalState.VolumeMap.ChangedGvfsProjections));
            }

            // Execution log targets
            m_executionLogFileTarget = CreateExecutionLog(
                    configuration,
                    context,
                    graph,
                    extraFingerprintSalts,
                    loggingContext);

            Contract.Assert(configuration.Logging.StoreFingerprints.HasValue, "Configuration.Logging.StoreFingerprints should be assigned some value before constructing the scheduler.");

            m_fingerprintStoreTarget = CreateFingerprintStoreTarget(
                    loggingContext,
                    configuration,
                    context,
                    graph.PipTable,
                    m_pipContentFingerprinter,
                    cache,
                    DirectedGraph,
                    m_fingerprintStoreCounters,
                    m_runnablePipPerformance,
                    m_testHooks?.FingerprintStoreTestHooks);

            // create the directory where shared opaque outputs journals will be stored
            FileUtilities.CreateDirectoryWithRetry(configuration.Layout.SharedOpaqueSidebandDirectory.ToString(Context.PathTable));

            MasterSpecificExecutionLogTarget masterTarget = null;
            WeakFingerprintAugmentationExecutionLogTarget fingerprintAugmentationTarget = null;

            if (!IsDistributedWorker)
            {
                masterTarget = new MasterSpecificExecutionLogTarget(loggingContext, this);

                // Fingerprint augmentation monitoring must be running only on the master (it's the only worker that will observe
                // both ProcessFingerprintComputed events for the same pip).
                if (configuration.Cache.MonitorAugmentedPathSets > 0)
                {
                    fingerprintAugmentationTarget = new WeakFingerprintAugmentationExecutionLogTarget(loggingContext, this, configuration.Cache.MonitorAugmentedPathSets);
                }

                if (configuration.Schedule.GenerateBuildManifest)
                {
                    m_buildManifestStoreTarget = new BuildManifestStoreTarget(context.StringTable);
                }
            }

            m_multiExecutionLogTarget = MultiExecutionLogTarget.CombineTargets(
                m_executionLogFileTarget,
                m_fingerprintStoreTarget,
                new ObservedInputAnomalyAnalyzer(loggingContext, graph),
                masterTarget,
                fingerprintAugmentationTarget,
                m_buildManifestStoreTarget);

            // Things that use execution log targets
            m_directoryMembershipFingerprinter = new DirectoryMembershipFingerprinter(
                loggingContext,
                context,
                ExecutionLog);

            m_fileMonitoringViolationAnalyzer = new FileMonitoringViolationAnalyzer(
                    loggingContext,
                    context,
                    graph,
                    m_fileContentManager,
                    configuration.Distribution.ValidateDistribution,
                    configuration.Sandbox.UnsafeSandboxConfiguration.UnexpectedFileAccessesAreErrors,
                    configuration.Sandbox.UnsafeSandboxConfiguration.IgnoreDynamicWritesOnAbsentProbes,
                    ExecutionLog);

            m_outputFileExtensionsForSequentialScan = new HashSet<PathAtom>(configuration.Schedule.OutputFileExtensionsForSequentialScanHandleOnHashing);

            m_loggingContext = loggingContext;
            m_groupedPipCounters = new PipCountersByGroupAggregator(loggingContext);
            m_pipRetryCountersDueToNetworkFailures = new int[(m_configuration.Distribution.NumRetryFailedPipsOnAnotherWorker ?? 0) + 1];

            ProcessInContainerManager = new ProcessInContainerManager(loggingContext, Context.PathTable);
            VmInitializer = vmInitializer;
            m_perPipPerformanceInfoStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);

            // Only initialize the symlink resolver if the corresponding flag is on.
            SymlinkedAccessResolver = m_configuration.Sandbox.UnsafeSandboxConfiguration.ProcessSymlinkedAccesses() ?
                new SymlinkedAccessResolver(context, directoryTranslator) :
                null;
        }

        private static int GetLoggingPeriodInMsForExecution(IConfiguration configuration)
        {
            return configuration.Logging.StatusFrequencyMs != 0 ?
                configuration.Logging.StatusFrequencyMs :
                configuration.Logging.GetTimerUpdatePeriodInMs();
        }

        /// <summary>
        /// Returns the pre subst root drive for given path.
        /// </summary>
        private static string GetRootDriveForPath(AbsolutePath path, DirectoryTranslator reverseDirectoryTranslator, PipExecutionContext context)
        {
            string drive;
            if (FileUtilities.TryGetSubstSourceAndTarget(path.GetRoot(context.PathTable).ToString(context.PathTable), out string substSource, out string substTarget))
            {
                drive = substSource;
            }
            else
            {
                AbsolutePath translatedPath = reverseDirectoryTranslator != null
                    ? reverseDirectoryTranslator.Translate(path, context.PathTable)
                    : path;
                drive = translatedPath.GetRoot(context.PathTable).ToString(context.PathTable);
            }

            if (drive.Length == 3 && drive.EndsWith(@":\", StringComparison.OrdinalIgnoreCase))      // Example "D:\"
            {
                return drive.Substring(0, 1);
            }

            return null;
        }

        private static IIpcLogger CreateLoggerForIpcClients(LoggingContext loggingContext)
        {
            return new LambdaLogger((level, message, args) =>
                Logger.Log.IpcClientForwardedMessage(
                    loggingContext,
                    level.ToString(),
                    args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, message, args) : message));
        }

        private static IIpcLogger CreateLoggerForApiServer(LoggingContext loggingContext)
        {
            return new LambdaLogger((level, message, args) =>
                Logger.Log.ApiServerForwardedIpcServerMessage(
                    loggingContext,
                    level.ToString(),
                    args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, message, args) : message));
        }

        #endregion Constructor

        #region Execution

        /// <summary>
        /// Returns a Boolean indicating if the scheduler has so far been successful in executing pips.
        /// If the pip queue is empty and the scheduler has failed, then the final value of this flag is known.
        /// </summary>
        public bool HasFailed => m_hasFailures;

        /// <summary>
        /// Returns a Boolean indicating if the scheduler has received a request for cancellation.
        /// </summary>
        public bool IsTerminating => m_scheduleTerminating;

        /// <summary>
        /// Start running.
        /// </summary>
        public void Start(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);

            m_executePhaseLoggingContext = loggingContext;
            m_serviceManager.Start(loggingContext, OperationTracker);

            if (PipGraph.ApiServerMoniker.IsValid)
            {
                // To reduce the time between rendering the server moniker and starting a server using that moniker,
                // we create the server here and not in the Scheduler's ctor.
                m_apiServer = new ApiServer(
                    m_ipcProvider,
                    PipGraph.ApiServerMoniker.ToString(Context.StringTable),
                    m_fileContentManager,
                    Context,
                    new ServerConfig
                    {
                        MaxConcurrentClients = 10, // not currently based on any science or experimentation
                        StopOnFirstFailure = false,
                        Logger = CreateLoggerForApiServer(loggingContext),
                    },
                    Cache,
                    ExecutionLog,
                    m_configuration.Schedule.GenerateBuildManifest);
                m_apiServer.Start(loggingContext);
            }

            if (m_configuration.Schedule.EnablePlugin == true)
            {
                m_pluginManager = new PluginManager(loggingContext, m_configuration.Logging.LogsDirectory.ToString(Context.PathTable),
                                                    m_configuration.Schedule.PluginLocations.Select(path => path.ToString(Context.PathTable)));
                m_pluginManager.Start();
            }

            m_chooseWorkerCpu = new ChooseWorkerCpu(
                loggingContext,
                m_configuration.Schedule,
                m_workers,
                m_pipQueue,
                PipGraph,
                m_fileContentManager,
                Context.PathTable,
                m_moduleWorkerMapping);

            m_chooseWorkerCacheLookup = new ChooseWorkerCacheLookup(
                loggingContext,
                m_configuration.Schedule,
                m_configuration.Distribution.DistributeCacheLookups,
                m_workers,
                m_pipQueue);

            ExecutionLog?.BxlInvocation(new BxlInvocationEventData(m_configuration));

            m_drainThread = new Thread(m_pipQueue.DrainQueues);

            if (!m_scheduleTerminating)
            {
                // UpdateStatus() checks if all writable drives have specified disk space available and calls RequestTermination for low disk space
                // Start the draining thread if scheduler isn't in terminating state
                m_drainThread.Start();
            }
        }

        /// <summary>
        /// Marks that a pip was executed. This logs a stat the first time it is called
        /// </summary>
        private void MarkPipStartExecuting()
        {
            if (Interlocked.CompareExchange(ref m_firstExecutedPip, 1, 0) == 0)
            {
                // Time to first pip only has meaning if we know when the process started
                if (m_processStartTimeUtc.HasValue)
                {
                    LogStatistic(
                        m_executePhaseLoggingContext,
                        Statistics.TimeToFirstPipExecuted,
                        (int)(DateTime.UtcNow - m_processStartTimeUtc.Value).TotalMilliseconds);
                }
            }
        }

        /// <summary>
        /// Returns a task representing the completion of all the scheduled pips
        /// </summary>
        /// <returns>Result of task is true if pips completed successfully. Otherwise, false.</returns>
        public async Task<bool> WhenDone()
        {
            Contract.Assert(m_drainThread != null, "Scheduler has not been started");

            if (m_drainThread.IsAlive)
            {
                EnsureMinimumWorkers(m_configuration.Distribution.MinimumWorkers);
                m_drainThread.Join();
            }

            Contract.Assert(!HasFailed || m_executePhaseLoggingContext.ErrorWasLogged, "Scheduler encountered errors during execution, but none were logged.");

            // We want TimeToFirstPipExecuted to always have a value. Mark the end of the execute phase as when the first
            // pip was executed in case all pips were cache hits
            MarkPipStartExecuting();

            using (PipExecutionCounters.StartStopwatch(PipExecutorCounter.AfterDrainingWhenDoneDuration))
            {
                LogWorkerStats();
                string[] perProcessPipPerf = m_perPipPerformanceInfoStore.GenerateTopPipPerformanceInfoJsonArray();
                foreach (string processPipPerf in perProcessPipPerf)
                {
                    Logger.Log.TopPipsPerformanceInfo(m_loggingContext, processPipPerf);
                }

                await State.Cache.CloseAsync();

                var shutdownServicesSucceeded = await m_serviceManager.ShutdownStartedServices();
                Contract.Assert(
                    shutdownServicesSucceeded || m_executePhaseLoggingContext.ErrorWasLogged,
                    "ServiceManager encountered errors during shutdown, but none were logged.");

                if (m_apiServer != null)
                {
                    await m_apiServer.Stop();
                }

                if (m_pluginManager != null)
                {
                    await m_pluginManager.Stop();
                }

                await StopIpcProvider();

                foreach (var worker in m_workers)
                {
                    await worker.FinishAsync(HasFailed ? "Distributed build failed. See errors on master." : null);
                }

                // Wait for all workers to confirm that they have stopped.
                while (m_workers.Any(w => w.Status != WorkerNodeStatus.Stopped))
                {
                    await Task.Delay(50);
                }

                if (m_fingerprintStoreTarget != null)
                {
                    // Dispose the fingerprint store to allow copying the files
                    m_fingerprintStoreTarget.Dispose();

                    // After the FingerprintStoreExecutionLogTarget is disposed and store files are no longer locked,
                    // create fingerprint store copy in logs.
                    if (m_configuration.Logging.SaveFingerprintStoreToLogs.GetValueOrDefault())
                    {
                        await FingerprintStore.CopyAsync(
                            m_loggingContext,
                            m_testHooks?.FingerprintStoreTestHooks,
                            Context.PathTable,
                            m_configuration,
                            m_fingerprintStoreCounters);
                    }

                    m_fingerprintStoreCounters.LogAsStatistics("FingerprintStore", m_loggingContext);
                    if (m_testHooks?.FingerprintStoreTestHooks != null)
                    {
                        m_testHooks.FingerprintStoreTestHooks.Counters = m_fingerprintStoreCounters;
                    }
                }

                if (m_configuration.Schedule.ModuleAffinityEnabled())
                {
                    StringBuilder strBuilder = new StringBuilder();
                    foreach (var kvp in m_moduleWorkerMapping.OrderByDescending(a => a.Value.NumPips))
                    {
                        string workerList = string.Join(",", kvp.Value.Workers.Select(a => a.WorkerId.ToString()));
                        strBuilder.AppendLine($"{kvp.Key.Value.ToString(Context.StringTable)}: {kvp.Value.NumPips} pips executed on [{workerList}]");
                    }

                    Logger.Log.ModuleWorkerMapping(m_loggingContext, strBuilder.ToString());
                }


                return !HasFailed && shutdownServicesSucceeded;
            }
        }

        private void LogWorkerStats()
        {
            PipExecutionCounters.AddToCounter(PipExecutorCounter.AvailableWorkerCountAtEnd, AvailableWorkersCount);

            int everAvailableWorkerCount = Workers.Count(a => a.EverAvailable);
            PipExecutionCounters.AddToCounter(PipExecutorCounter.EverAvailableWorkerCount, everAvailableWorkerCount);

            var workerOpKinds = Worker.WorkerStatusOperationKinds;

            var runningOpKing = workerOpKinds[(int)WorkerNodeStatus.Running];
            long totalWorkerRunningDuration = SafeConvert.ToLong(OperationTracker.TryGetAggregateCounter(runningOpKing)?.Duration.TotalMilliseconds ?? 0);

            PipExecutionCounters.AddToCounter(PipExecutorCounter.WorkerAverageRunningDurationMs, totalWorkerRunningDuration / everAvailableWorkerCount);

            var pendingOpKinds = new OperationKind[] { workerOpKinds[(int)WorkerNodeStatus.Starting], workerOpKinds[(int)WorkerNodeStatus.Started], workerOpKinds[(int)WorkerNodeStatus.Attached] };
            long totalWorkerPendingDuration = 0;
            foreach (var opKind in pendingOpKinds)
            {
                totalWorkerPendingDuration += SafeConvert.ToLong(OperationTracker.TryGetAggregateCounter(opKind)?.Duration.TotalMilliseconds ?? 0);
            }

            PipExecutionCounters.AddToCounter(PipExecutorCounter.WorkerAveragePendingDurationMs, totalWorkerPendingDuration / everAvailableWorkerCount);
        }

        private void EnsureMinimumWorkers(int minimumWorkers)
        {
            bool isDrainingCompleted = m_drainThread.Join(EngineEnvironmentSettings.WorkerAttachTimeout);

            bool anyRemoteWorkerReleased = Workers.Any(a => a.IsEarlyReleaseInitiated);
            int availableWorkers = AvailableWorkersCount;

            // If any remote worker is released due to insufficient amount of work left, do not attempt to cancel the build
            // even though minimum workers is not satisfied.
            if (availableWorkers < minimumWorkers && !isDrainingCompleted && !anyRemoteWorkerReleased)
            {
                Logger.Log.MinimumWorkersNotSatisfied(m_executePhaseLoggingContext, minimumWorkers, availableWorkers);
                m_hasFailures = true;
                RequestTermination(cancelQueue: false);
            }
        }

        private async Task<bool> StopIpcProvider()
        {
            try
            {
                await m_ipcProvider.Stop();
                return true;
            }
            catch (Exception e)
            {
                Logger.Log.IpcClientFailed(m_executePhaseLoggingContext, e.ToStringDemystified());
                return false;
            }
        }

        /// <summary>
        /// Reports schedule stats that are relevant at the completion of a build.
        /// </summary>
        /// <remarks>
        /// This is called after all pips have been added and the pip queue has emptied.
        /// Warning: Some variables may be null if scheduler's Init() is not called.
        /// </remarks>
        public SchedulerPerformanceInfo LogStats(LoggingContext loggingContext, [CanBeNull] BuildSummary buildSummary)
        {
            Dictionary<string, long> statistics = new Dictionary<string, long>();
            lock (m_statusLock)
            {
                m_fileContentManager.LogStats(loggingContext);

                OperationTracker.Stop(Context, m_configuration.Logging, PipExecutionCounters, Worker.WorkerStatusOperationKinds);

                LogCriticalPath(statistics, buildSummary);

                int processPipsStartOrShutdownService = m_serviceManager.TotalServicePipsCompleted + m_serviceManager.TotalServiceShutdownPipsCompleted;

                // Overall caching summary
                if (m_numProcessPipsCompleted > 0)
                {
                    // Grab a snapshot just looking at processes so we can log the count that were ignored
                    PipStateCountersSnapshot snapshot = new PipStateCountersSnapshot();
                    m_pipStateCounters.CollectSnapshot(new[] { PipType.Process }, snapshot);
                    long totalProcessesNotIgnoredOrService = snapshot.Total - (snapshot.IgnoredCount + processPipsStartOrShutdownService);
                    double cacheRate = (double)m_numProcessPipsSatisfiedFromCache / totalProcessesNotIgnoredOrService;

                    Logger.Log.IncrementalBuildSavingsSummary(
                        loggingContext,
                        // Make sure not to show 100% due to rounding when there are any misses
                        cacheRate: cacheRate == 1 ? cacheRate : Math.Min(cacheRate, .9999),
                        totalProcesses: totalProcessesNotIgnoredOrService,
                        ignoredProcesses: snapshot.IgnoredCount);

                    long processPipsSatisfiedFromRemoteCache =
                        PipExecutionCounters.GetCounterValue(PipExecutorCounter.RemoteCacheHitsForProcessPipDescriptorAndContent);
                    long remoteContentDownloadedBytes =
                        PipExecutionCounters.GetCounterValue(PipExecutorCounter.RemoteContentDownloadedBytes);
                    if (processPipsSatisfiedFromRemoteCache > 0)
                    {
                        if (processPipsSatisfiedFromRemoteCache <= m_numProcessPipsSatisfiedFromCache)
                        {
                            double relativeCacheRate = (double)processPipsSatisfiedFromRemoteCache / m_numProcessPipsSatisfiedFromCache;
                            string remoteContentDownloadedBytesHumanReadable = ByteSizeFormatter.Format(remoteContentDownloadedBytes);

                            Logger.Log.IncrementalBuildSharedCacheSavingsSummary(
                                loggingContext,
                                relativeCacheRate: relativeCacheRate,
                                remoteProcesses: processPipsSatisfiedFromRemoteCache,
                                contentDownloaded: remoteContentDownloadedBytesHumanReadable);
                        }
                        else
                        {
                            Logger.Log.RemoteCacheHitsGreaterThanTotalCacheHits(
                                loggingContext,
                                processPipsSatisfiedFromRemoteCache,
                                m_numProcessPipsSatisfiedFromCache);
                        }
                    }

                    if (m_configuration.Engine.Converge && cacheRate < 1 && m_configuration.Logging.ExecutionLog.IsValid)
                    {
                        Logger.Log.SchedulerDidNotConverge(
                            loggingContext,
                            m_configuration.Logging.ExecutionLog.ToString(Context.PathTable),
                            Path.Combine(Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly())), Branding.AnalyzerExecutableName),
                            m_configuration.Logging.LogsDirectory.ToString(Context.PathTable) + Path.DirectorySeparatorChar + "CacheMiss");
                    }
                }

                m_pipStateCounters.CollectSnapshot(s_pipTypesToLogStats, m_pipTypesToLogCountersSnapshot);
                statistics.Add(Statistics.TotalPips, m_pipTypesToLogCountersSnapshot.Total);

                long succeededCount = m_pipTypesToLogCountersSnapshot.DoneCount;
                Logger.Log.PipsSucceededStats(loggingContext, succeededCount);
                statistics.Add(Statistics.PipsSucceeded, succeededCount);
                Logger.Log.PipsFailedStats(loggingContext, m_pipTypesToLogCountersSnapshot[PipState.Failed]);
                statistics.Add(Statistics.PipsFailed, m_pipTypesToLogCountersSnapshot[PipState.Failed]);
                statistics.Add(Statistics.PipsIgnored, m_pipTypesToLogCountersSnapshot.IgnoredCount);

                var statsName = "PipStats.{0}_{1}";

                // Log the stats for each pipType.
                foreach (var pipType in Enum.GetValues(typeof(PipType)).Cast<PipType>())
                {
                    if (pipType == PipType.Max)
                    {
                        continue;
                    }

                    var detailedSnapShot = new PipStateCountersSnapshot();
                    m_pipStateCounters.CollectSnapshot(new[] { pipType }, detailedSnapShot);
                    Logger.Log.PipDetailedStats(
                        loggingContext,
                        pipType.ToString(),
                        detailedSnapShot.DoneCount,
                        detailedSnapShot[PipState.Failed],
                        detailedSnapShot.SkippedDueToFailedDependenciesCount,
                        detailedSnapShot.IgnoredCount,
                        detailedSnapShot.Total);

                    statistics.Add(string.Format(statsName, pipType, "Done"), detailedSnapShot.DoneCount);
                    statistics.Add(string.Format(statsName, pipType, "Failed"), detailedSnapShot[PipState.Failed]);
                    statistics.Add(string.Format(statsName, pipType, "Skipped"), detailedSnapShot.SkippedDueToFailedDependenciesCount);
                    statistics.Add(string.Format(statsName, pipType, "Ignored"), detailedSnapShot.IgnoredCount);
                    statistics.Add(string.Format(statsName, pipType, "Total"), detailedSnapShot.Total);
                }

                Logger.Log.ProcessesCacheMissStats(loggingContext, m_numProcessPipsUnsatisfiedFromCache);
                Logger.Log.ProcessesCacheHitStats(loggingContext, m_numProcessPipsSatisfiedFromCache);
                statistics.Add(Statistics.TotalProcessPips, m_numProcessPipsCompleted);

                // Below stats sum to num process pips completed
                statistics.Add(Statistics.ProcessPipCacheHits, m_numProcessPipsSatisfiedFromCache);
                statistics.Add(Statistics.ProcessPipCacheMisses, m_numProcessPipsUnsatisfiedFromCache);
                statistics.Add(Statistics.ProcessPipStartOrShutdownService, processPipsStartOrShutdownService);
                statistics.Add(Statistics.ProcessPipsSkippedDueToFailedDependencies, m_numProcessPipsSkipped);
                statistics.Add(Statistics.ProcessPipsIncrementalSchedulingPruned, m_numProcessesIncrementalSchedulingPruned);

                // Verify the stats sum correctly
                long processPipsSum = m_numProcessPipsSatisfiedFromCache + m_numProcessPipsUnsatisfiedFromCache + m_numProcessPipsSkipped;
                if (m_numProcessPipsCompleted != processPipsSum)
                {
                    BuildXL.Tracing.UnexpectedCondition.Log(loggingContext, $"Total process pips != (pip cache hits + pip cache misses + service start/shutdown pips). Total: { m_numProcessPipsCompleted }, Sum: { processPipsSum }");
                }

                m_numProcessPipsSemaphoreQueued = m_pipQueue.TotalNumSemaphoreQueued;
                Logger.Log.ProcessesSemaphoreQueuedStats(loggingContext, m_numProcessPipsSemaphoreQueued);
                statistics.Add(Statistics.ProcessDelayedBySemaphore, m_numProcessPipsSemaphoreQueued);
            }

            if (m_criticalPathStats != null)
            {
                statistics.Add("HistoricalCriticalPath.NumWildGuesses", m_criticalPathStats.NumWildGuesses);
                statistics.Add("HistoricalCriticalPath.NumHits", m_criticalPathStats.NumHits);
                statistics.Add("HistoricalCriticalPath.LongestPathMs", m_criticalPathStats.LongestPath);
            }

            statistics.Add("HistoricPerfData.Misses", m_historicPerfDataMisses);
            statistics.Add("HistoricPerfData.ZeroMemoryHits", m_historicPerfDataZeroMemoryHits);
            statistics.Add("HistoricPerfData.NonZeroMemoryHits", m_historicPerfDataNonZeroMemoryHits);

            statistics.Add("MaxUnresponsivenessFactor", m_maxUnresponsivenessFactor);

            m_historicPerfDataTable?.LogStats(loggingContext);

            Logger.Log.WarningStats(
                loggingContext,
                Volatile.Read(ref m_numPipsWithWarnings),
                Volatile.Read(ref m_numWarnings),
                Volatile.Read(ref m_numPipsWithWarningsFromCache),
                Volatile.Read(ref m_numWarningsFromCache));
            statistics.Add(Statistics.ExecutedPipsWithWarnings, m_numPipsWithWarnings);
            statistics.Add(Statistics.WarningsFromExecutedPips, m_numWarnings);
            statistics.Add(Statistics.CachedPipsWithWarnings, m_numPipsWithWarningsFromCache);
            statistics.Add(Statistics.WarningsFromCachedPips, m_numWarningsFromCache);

            statistics.Add("DirectoryMembershipFingerprinter.RegexObjectCacheMisses", RegexDirectoryMembershipFilter.RegexCache.Misses);
            statistics.Add("DirectoryMembershipFingerprinter.RegexObjectCacheHits", RegexDirectoryMembershipFilter.RegexCache.Hits);
            statistics.Add("DirectoryMembershipFingerprinter.DirectoryContentCacheMisses", m_directoryMembershipFingerprinter.CachedDirectoryContents.Misses);
            statistics.Add("DirectoryMembershipFingerprinter.DirectoryContentCacheHits", m_directoryMembershipFingerprinter.CachedDirectoryContents.Hits);

            int numOfRetires = 1;
            var pipRetryCountersDueToNetworkFailures = m_pipRetryCountersDueToNetworkFailures.Skip(1); // Removing the pips with 0 retires (Successful in 1st attempt)
            foreach (int retryCount in pipRetryCountersDueToNetworkFailures)
            {
                statistics.Add("RetriedDueToStoppedWorker_" + numOfRetires, retryCount);
                numOfRetires++;
            }

            SortedDictionary<int, int> sortedLowMemoryRetryCounters = new SortedDictionary<int, int>(m_pipRetryCountersDueToLowMemory);
            foreach (var current in sortedLowMemoryRetryCounters)
            {
                statistics.Add("RetriedDueToLowMemory_" + current.Key, current.Value);
            }

            Logger.Log.CacheFingerprintHitSources(loggingContext, m_cacheIdHits);

            List<CacheLookupPerfInfo> cacheLookupPerfInfos = m_runnablePipPerformance.Values.Where(a => a.CacheLookupPerfInfo != null).Select(a => a.CacheLookupPerfInfo).ToList();
            List<CacheLookupPerfInfo> cacheLookupPerfInfosForHits = cacheLookupPerfInfos.Where(a => a.CacheMissType == PipCacheMissType.Invalid).DefaultIfEmpty().ToList();
            List<CacheLookupPerfInfo> cacheLookupPerfInfosForMisses = cacheLookupPerfInfos.Where(a => a.CacheMissType != PipCacheMissType.Invalid).DefaultIfEmpty().ToList();

            PipExecutionCounters.AddToCounter(PipExecutorCounter.MaxCacheEntriesVisitedForHit, cacheLookupPerfInfosForHits.Max(a => a?.NumCacheEntriesVisited) ?? -1);
            PipExecutionCounters.AddToCounter(PipExecutorCounter.MinCacheEntriesVisitedForHit, cacheLookupPerfInfosForHits.Min(a => a?.NumCacheEntriesVisited) ?? -1);
            PipExecutionCounters.AddToCounter(PipExecutorCounter.MaxCacheEntriesVisitedForMiss, cacheLookupPerfInfosForMisses.Max(a => a?.NumCacheEntriesVisited) ?? -1);
            PipExecutionCounters.AddToCounter(PipExecutorCounter.MinCacheEntriesVisitedForMiss, cacheLookupPerfInfosForMisses.Min(a => a?.NumCacheEntriesVisited) ?? -1);

            PipExecutionCounters.AddToCounter(PipExecutorCounter.MaxPathSetsDownloadedForHit, cacheLookupPerfInfosForHits.Max(a => a?.NumPathSetsDownloaded) ?? -1);
            PipExecutionCounters.AddToCounter(PipExecutorCounter.MinPathSetsDownloadedForHit, cacheLookupPerfInfosForHits.Min(a => a?.NumPathSetsDownloaded) ?? -1);
            PipExecutionCounters.AddToCounter(PipExecutorCounter.MaxPathSetsDownloadedForMiss, cacheLookupPerfInfosForMisses.Max(a => a?.NumPathSetsDownloaded) ?? -1);
            PipExecutionCounters.AddToCounter(PipExecutorCounter.MinPathSetsDownloadedForMiss, cacheLookupPerfInfosForMisses.Min(a => a?.NumPathSetsDownloaded) ?? -1);

            var currentTime = DateTime.UtcNow;
            var earlyReleaseSavingDurationMs = Workers.Where(a => a.WorkerEarlyReleasedTime != null).Select(a => (currentTime - a.WorkerEarlyReleasedTime.Value).TotalMilliseconds).Sum();
            PipExecutionCounters.AddToCounter(PipExecutorCounter.RemoteWorker_EarlyReleaseSavingDurationMs, (long)earlyReleaseSavingDurationMs);

            PipExecutionCounters.LogAsStatistics("PipExecution", loggingContext);

            m_groupedPipCounters.LogAsPipCounters();

            // Verify counters for different types of cache misses sum to pips executed due to cache misses
            IEnumerable<PipExecutorCounter> cacheMissTypes = PipExecutor.GetListOfCacheMissTypes();
            long cacheMissSum = 0;
            foreach (var missType in cacheMissTypes)
            {
                cacheMissSum += PipExecutionCounters.GetCounterValue(missType);
            }

            long processPipsExecutedDueToCacheMiss = PipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessPipsExecutedDueToCacheMiss);
            long processPipsSkippedExecutionDueToCacheOnly = PipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessPipsSkippedExecutionDueToCacheOnly);
            // The master keeps track of total cache miss counter across workers but not for individual miss reasons,
            // so don't check the sum for distributed builds
            if (!IsDistributedBuild && (processPipsExecutedDueToCacheMiss + processPipsSkippedExecutionDueToCacheOnly) != cacheMissSum)
            {
                BuildXL.Tracing.UnexpectedCondition.Log(loggingContext, $"ProcessPipsExecutedDueToCacheMiss + ProcessPipsSkippedExecutionDueToCacheOnly != sum of counters for all cache miss types. " +
                    "ProcessPipsExecutedDueToCacheMiss: {processPipsExecutedDueToCacheMiss}, ProcessPipsSkippedExecutionDueToCacheOnly: {processPipsSkippedExecutionDueToCacheOnly}, Sum: {cacheMissSum}");
            }

            // Log details about pips skipped under /CacheOnly mode only if pips were actually skipped.
            if (m_configuration.Schedule.CacheOnly && processPipsSkippedExecutionDueToCacheOnly > 0)
            {
                // Log the total number of pips skipped including downstream pips.
                // processPipsSkippedExecutionDueToCacheOnly only contains the pips where cache lookup was performed, not downstream pips that were skipped
                Logger.Log.CacheOnlyStatistics(loggingContext, m_numProcessPipsSkipped);
            }

            State?.Cache.Counters.LogAsStatistics("PipCaching", loggingContext);
            State?.FileSystemView?.Counters.LogAsStatistics("FileSystemView", loggingContext);
            m_localDiskContentStore?.LogStats();
            m_fileChangeTracker?.Counters.LogAsStatistics("FileChangeTracker", loggingContext);
            m_fileContentTable.Counters.LogAsStatistics("FileContentTable", loggingContext);
            FileUtilities.Counters?.LogAsStatistics("Storage", loggingContext);
            m_fileMonitoringViolationAnalyzer?.Counters.LogAsStatistics("FileMonitoringViolationAnalysis", loggingContext);
            m_pipExecutionStepCounters.LogAsStatistics("PipExecutionStep", loggingContext);
            m_executionLogFileTarget?.Counters.LogAsStatistics("ExecutionLogFileTarget", loggingContext);
            SandboxedProcessFactory.Counters.LogAsStatistics("SandboxedProcess", loggingContext);
            statistics.AddRange(ContentHashingUtilities.GetContentHasher(ContentHashingUtilities.HashInfo.HashType).GetCounters().ToDictionaryIntegral());

            m_pipPropertyInfo.LogPipPropertyInfo(loggingContext);
            m_pipRetryInfo.LogPipRetryInfo(loggingContext, PipExecutionCounters);

            m_apiServer?.LogStats(loggingContext);
            m_dropPipTracker?.LogStats(loggingContext);

            if (m_configuration.InCloudBuild())
            {
                Contract.Assert(m_dropPipTracker != null, "Must use DropPipTracker when running in CloudBuild");
                CloudBuildEventSource.Log.DominoFinalStatisticsEvent(new DominoFinalStatisticsEvent
                {
                    LastDropPipCompletionUtcTicks = m_dropPipTracker.LastDropPipCompletionTime.Ticks,
                    LastNonDropPipCompletionUtcTicks = m_dropPipTracker.LastNonDropPipCompletionTime.Ticks,
                });
            }

            var totalQueueDurations = new long[(int)DispatcherKind.Materialize + 1];
            var totalStepDurations = new long[(int)PipExecutionStep.Done + 1];
            var totalRemoteStepDurations = new long[(int)PipExecutionStep.Done + 1];
            var totalQueueRequestDurations = new long[(int)PipExecutionStep.Done + 1];
            var totalSendRequestDurations = new long[(int)PipExecutionStep.Done + 1];

            foreach (var perfData in m_runnablePipPerformance)
            {
                for (int i = 0; i < perfData.Value.QueueDurations.Value.Length; i++)
                {
                    totalQueueDurations[i] += (long)perfData.Value.QueueDurations.Value[i].TotalMilliseconds;
                }

                for (int i = 0; i < perfData.Value.StepDurations.Length; i++)
                {
                    totalStepDurations[i] += (long)perfData.Value.StepDurations[i].TotalMilliseconds;
                }

                for (int i = 0; i < perfData.Value.RemoteStepDurations.Value.Length; i++)
                {
                    totalRemoteStepDurations[i] += (long)perfData.Value.RemoteStepDurations.Value[i].TotalMilliseconds;
                }

                for (int i = 0; i < perfData.Value.QueueRequestDurations.Value.Length; i++)
                {
                    totalQueueRequestDurations[i] += (long)perfData.Value.QueueRequestDurations.Value[i].TotalMilliseconds;
                }

                for (int i = 0; i < perfData.Value.SendRequestDurations.Value.Length; i++)
                {
                    totalSendRequestDurations[i] += (long)perfData.Value.SendRequestDurations.Value[i].TotalMilliseconds;
                }
            }

            var perfStatsName = "PipPerfStats.{0}_{1}";
            for (int i = 0; i < totalQueueDurations.Length; i++)
            {
                statistics.Add(string.Format(perfStatsName, "Queue", (DispatcherKind)i), totalQueueDurations[i]);
            }

            for (int i = 0; i < totalStepDurations.Length; i++)
            {
                statistics.Add(string.Format(perfStatsName, "Run", (PipExecutionStep)i), totalStepDurations[i]);
            }

            for (int i = 0; i < totalRemoteStepDurations.Length; i++)
            {
                statistics.Add(string.Format(perfStatsName, "RemoteRun", (PipExecutionStep)i), totalRemoteStepDurations[i]);
            }

            for (int i = 0; i < totalQueueRequestDurations.Length; i++)
            {
                statistics.Add(string.Format(perfStatsName, "QueueRequest", (PipExecutionStep)i), totalQueueRequestDurations[i]);
            }

            for (int i = 0; i < totalSendRequestDurations.Length; i++)
            {
                statistics.Add(string.Format(perfStatsName, "SendRequest", (PipExecutionStep)i), totalSendRequestDurations[i]);
            }

            statistics.Add("TotalPeakWorkingSetMb", (long)m_totalPeakWorkingSetMb);
            statistics.Add("TotalAverageWorkingSetMb", (long)m_totalAverageWorkingSetMb);
            statistics.Add("TotalPeakCommitSizeMb", (long)m_totalPeakCommitSizeMb);
            statistics.Add("TotalAverageCommitSizeMb", (long)m_totalAverageCommitSizeMb);

            if (m_pluginManager != null)
            {
                statistics.Add(Statistics.PluginLoadingTime, (long)m_pluginManager.PluginLoadingTime);
                statistics.Add(Statistics.PluginTotalProcessTime, (long)m_pluginManager.PluginTotalProcessTime);
                statistics.Add(Statistics.PluginLoadedSuccessfulCounts, (long)m_pluginManager.PluginLoadedSuccessfulCount);
                statistics.Add(Statistics.PluginLoadedFailureCounts, (long)m_pluginManager.PluginLoadedFailureCount);
                statistics.Add(Statistics.PluginProcessedRequestCounts, (long)m_pluginManager.PluginProcessedRequestCounts);
            }

            m_chooseWorkerCpu?.LogStats(statistics);

            BuildXL.Tracing.Logger.Log.BulkStatistic(loggingContext, statistics);

            return new SchedulerPerformanceInfo
            {
                PipExecutionStepCounters = m_pipExecutionStepCounters,
                ExecuteProcessDurationMs = SafeConvert.ToLong(PipExecutionCounters.GetElapsedTime(PipExecutorCounter.ExecuteProcessDuration).TotalMilliseconds),
                CanceledProcessExecuteDurationMs = SafeConvert.ToLong(PipExecutionCounters.GetElapsedTime(PipExecutorCounter.CanceledProcessExecuteDuration).TotalMilliseconds),
                ProcessPipCacheHits = m_numProcessPipsSatisfiedFromCache,
                ProcessPipIncrementalSchedulingPruned = m_numProcessesIncrementalSchedulingPruned,
                TotalProcessPips = m_numProcessPipsCompleted,
                ProcessPipCacheMisses = m_numProcessPipsUnsatisfiedFromCache,
                ProcessPipsUncacheable = PipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessPipsExecutedButUncacheable),
                CriticalPathTableHits = m_criticalPathStats?.NumHits ?? 0,
                CriticalPathTableMisses = m_criticalPathStats?.NumWildGuesses ?? 0,
                FileContentStats = m_fileContentManager.FileContentStats,
                RunProcessFromCacheDurationMs = SafeConvert.ToLong(PipExecutionCounters.GetElapsedTime(PipExecutorCounter.RunProcessFromCacheDuration).TotalMilliseconds),
                SandboxedProcessPrepDurationMs = PipExecutionCounters.GetCounterValue(PipExecutorCounter.SandboxedProcessPrepDurationMs),
                MachineMinimumAvailablePhysicalMB = SafeConvert.ToLong(((m_performanceAggregator != null && m_performanceAggregator.MachineAvailablePhysicalMB.Count > 2) ? m_performanceAggregator.MachineAvailablePhysicalMB.Minimum : -1)),
                AverageMachineCPU = (m_performanceAggregator != null && m_performanceAggregator.MachineCpu.Count > 2) ? (int)m_performanceAggregator.MachineCpu.Average : 0,
                DiskStatistics = m_performanceAggregator != null ? m_performanceAggregator.DiskStats : null,
                HitLowMemorySmell = m_hitLowRamMemoryPerfSmell,
                ProcessPipCountersByTelemetryTag = ProcessPipCountersByTelemetryTag
            };
        }

        private static void LogStatistic(LoggingContext loggingContext, string key, int value)
        {
            BuildXL.Tracing.Logger.Log.Statistic(loggingContext, new Statistic { Name = key, Value = value });
        }

        /// <summary>
        /// Gets scheduler stats.
        /// </summary>
        public SchedulerStats SchedulerStats => new SchedulerStats
        {
            ProcessPipsCompleted = Volatile.Read(ref m_numProcessPipsCompleted),
            IpcPipsCompleted = Volatile.Read(ref m_numIpcPipsCompleted),
            ProcessPipsSatisfiedFromCache = Volatile.Read(ref m_numProcessPipsSatisfiedFromCache),
            ProcessPipsUnsatisfiedFromCache = Volatile.Read(ref m_numProcessPipsUnsatisfiedFromCache),
            FileContentStats = m_fileContentManager.FileContentStats,
            PipsWithWarnings = Volatile.Read(ref m_numPipsWithWarnings),
            PipsWithWarningsFromCache = Volatile.Read(ref m_numPipsWithWarningsFromCache),
            ServicePipsCompleted = m_serviceManager.TotalServicePipsCompleted,
            ServiceShutdownPipsCompleted = m_serviceManager.TotalServiceShutdownPipsCompleted,
        };

        private StatusRows m_statusRows;
        private readonly PipExecutionStepTracker m_executionStepTracker = new PipExecutionStepTracker();

        private StatusRows GetStatusRows()
        {
            var windowsDiskStats = !OperatingSystemHelper.IsUnixOS ? m_performanceAggregator?.DiskStats : null; // Some disk stats are available only in Windows, we remove these columns from Mac builds for a cleaner status.csv file
            return new StatusRows()
            {
                { "Cpu Percent", data => data.CpuPercent },
                { "Ram Percent", data => data.RamPercent },
                { "EffectiveRam Percent", data => m_perfInfo.EffectiveRamUsagePercentage ?? 0},
                { "Used Ram Mb", data => data.RamUsedMb },
                { "Free Ram Mb", data => data.RamFreeMb },
                { "ModifiedPagelistMb", data => m_perfInfo.ModifiedPagelistMb ?? 0},
                { "Commit Percent", data => data.CommitPercent },
                { "Used Commit Mb", data => data.CommitUsedMb },
                { "Free Commit Mb", data => data.CommitFreeMb },
                { "NetworkBandwidth", data => m_perfInfo.MachineBandwidth },
                { "MachineKbitsPerSecSent", data => (long)m_perfInfo.MachineKbitsPerSecSent },
                { "MachineKbitsPerSecReceived", data => (long)m_perfInfo.MachineKbitsPerSecReceived },
                { "DispatchIterations", data => OptionalPipQueueImpl?.DispatcherIterations ?? 0 },
                { "DispatchTriggers", data => OptionalPipQueueImpl?.TriggerDispatcherCount ?? 0 },
                { "DispatchMs", data => (long)(OptionalPipQueueImpl?.DispatcherLoopTime.TotalMilliseconds ?? 0) },
                { "ChooseQueueFastNextCount", data => OptionalPipQueueImpl?.ChooseQueueFastNextCount ?? 0 },
                { "ChooseQueueRunTimeMs", data => OptionalPipQueueImpl?.ChooseQueueRunTime.TotalMilliseconds ?? 0 },
                { "ChooseLastBlocked", data => m_chooseWorkerCpu.LastBlockedPip?.Pip.SemiStableHash.ToString("X16") ?? "N/A" },
                { "ChooseBlockedCount", data => m_chooseWorkerCpu.ChooseBlockedCount },
                { "ChooseSuccessCount", data => m_chooseWorkerCpu.ChooseSuccessCount },
                { "ChooseIterations", data => m_chooseWorkerCpu.ChooseIterations },
                { "ChooseSeconds", data => m_chooseWorkerCpu.ChooseSeconds },
                { "LastSchedulerConcurrencyLimiter", data => m_chooseWorkerCpu.LastConcurrencyLimiter?.Name ?? "N/A" },
                { "LimitingResource", data => data.LimitingResource},
                { "MemoryResourceAvailability", data => LocalWorker.MemoryResource.ToString().Replace(',', '-')},
                { "ProcessRetriesDueToResourceLimits", data => PipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessRetriesDueToResourceLimits)},
                { "EmptyWorkingSetSucceeded", data => PipExecutionCounters.GetCounterValue(PipExecutorCounter.EmptyWorkingSetSucceeded)},
                { "ResourceManager_TotalUsedWorkingSet", data => State.ResourceManager.TotalUsedWorkingSet},
                { "ResourceManager_TotalUsedPeakWorkingSet", data => State.ResourceManager.TotalUsedPeakWorkingSet},
                { "ResourceManager_TotalRamNeededForResume", data => State.ResourceManager.TotalRamMbNeededForResume},
                { "ResourceManager_LastRequiredSize", data => State.ResourceManager.LastRequiredSizeMb},
                { "ResourceManager_LastManageMemoryMode", data => State.ResourceManager.LastManageMemoryMode?.ToString() ?? ""},
                { "ResourceManager_NumSuspended", data => State.ResourceManager.NumSuspended},
                {
                    EnumTraits<PipState>.EnumerateValues(), (rows, state) =>
                    {
                        rows.Add(I($"State.{state}"), _ => m_pipTypesToLogCountersSnapshot[state]);
                    }
                },
                {
                    EnumTraits<DispatcherKind>.EnumerateValues(), (rows, kind) =>
                    {
                        if (kind != DispatcherKind.None)
                        {
                            rows.Add(I($"{kind} Queued"), _ => m_pipQueue.GetNumQueuedByKind(kind));
                            rows.Add(I($"{kind} Running"), _ => m_pipQueue.GetNumRunningByKind(kind));
                            rows.Add(I($"{kind} CurrentMax"), _ => m_pipQueue.GetMaxParallelDegreeByKind(kind));
                        }
                    }
                },
                { "Running PipExecutor Processes", data => data.RunningPipExecutorProcesses },
                { "Running Processes", data => data.RunningProcesses },
                { "PipTable.ReadCount", data => m_pipTable.Reads },
                { "PipTable.ReadDurationMs", data => m_pipTable.ReadsMilliseconds },
                { "PipTable.WriteCount", data => m_pipTable.Writes },
                { "PipTable.WriteDurationMs", data => m_pipTable.WritesMilliseconds },

                // Drive stats
                { windowsDiskStats, d => I($"Drive \'{d.Drive}\' % Active"), (d, index) => (data => data.DiskPercents[index]) },
                { windowsDiskStats, d => I($"Drive \'{d.Drive}\' QueueDepth"), (d, index) => (data => data.DiskQueueDepths[index]) },
                { m_performanceAggregator?.DiskStats, d => I($"Drive \'{d.Drive}\' AvailableSpaceGB"), (d, index) => (data => data.DiskAvailableSpaceGb[index]) },

                {
                    EnumTraits<PipType>.EnumerateValues().Where(pipType => pipType != PipType.Max), (rows, pipType) =>
                    {
                        rows.Add(I($"{pipType} Waiting"), _ => m_pipStateCountersSnapshots[(int)pipType][PipState.Waiting]);
                        rows.Add(I($"{pipType} Ready"), _ => m_pipStateCountersSnapshots[(int)pipType][PipState.Ready]);
                        rows.Add(I($"{pipType} Running"), _ => m_pipStateCountersSnapshots[(int)pipType][PipState.Running]);
                        rows.Add(I($"{pipType} Done"), _ => m_pipStateCountersSnapshots[(int)pipType][PipState.Done]);
                    }
                },

                // BuildXL process stats
                { "Domino.CPUPercent", data => data.ProcessCpuPercent },
                { "Domino.WorkingSetMB", data => data.ProcessWorkingSetMB },
                { "UnresponsivenessFactor", data => data.UnresponsivenessFactor },

                // PipExecutionStep counts
                {
                    EnumTraits<PipExecutionStep>.EnumerateValues().Where(step => step != PipExecutionStep.None), (rows, step) =>
                    {
                        rows.Add(I($"{step} Active"), _ => m_executionStepTracker.CurrentSnapshot[step]);
                        rows.Add(I($"{step} Total"), _ => m_executionStepTracker.CurrentSnapshot.GetCumulativeCount(step));
                    }
                },

                { "ProcessPipsPending", data => data.ProcessPipsPending },
                { "ProcessPipsAllocatedSlots", data => data.ProcessPipsAllocatedSlots },
                { "ProcessPipsWaiting", data => data.ProcessPipsPending - data.ProcessPipsAllocatedSlots },
                { "TotalAcquiredProcessSlots", data => Workers.Where(a => a.IsAvailable).Sum(a => a.AcquiredProcessSlots) },
                { "AvailableWorkersCount", data => AvailableWorkersCount },

                // Worker Pip State counts and status
                {
                    m_workers, (rows, worker) =>
                    {
                        rows.Add(I($"W{worker.WorkerId} Total CacheLookup Slots"), _ => worker.TotalCacheLookupSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Used CacheLookup Slots"), _ => worker.AcquiredCacheLookupSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Total MaterializeInput Slots"), _ => worker.TotalMaterializeInputSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Used MaterializeInput Slots"), _ => worker.AcquiredMaterializeInputSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Total Process Slots"), _ => worker.TotalProcessSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Effective Process Slots"), _ => worker.EffectiveTotalProcessSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Used Process Slots"), _ => worker.AcquiredProcessSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Used PostProcess Slots"), _ => worker.AcquiredPostProcessSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Used Ipc Slots"), _ => worker.AcquiredIpcSlots, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Waiting BuildRequests Count"), _ => worker.WaitingBuildRequestsCount, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Total Ram Mb"), _ => worker.TotalRamMb ?? 0, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Estimated Free Ram Mb"), _ => worker.EstimatedFreeRamMb, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Actual Free Ram Mb"), _ => worker.ActualFreeMemoryMb ?? 0, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Total Commit Mb"), _ => worker.TotalCommitMb ?? 0, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Estimated Free Commit Mb"), _ => worker.EstimatedFreeCommitMb, includeInSnapshot: false);
                        rows.Add(I($"W{worker.WorkerId} Actual Free Commit Mb"), _ => worker.ActualFreeCommitMb ?? 0, includeInSnapshot: false);

                        rows.Add(I($"W{worker.WorkerId} Status"), _ => worker.Status, includeInSnapshot: false);

                        var snapshot = worker.PipStateSnapshot;
                        foreach (var workerPipState in EnumTraits<WorkerPipState>.EnumerateValues().Where(wps => wps.IsReportedState()))
                        {
                            rows.Add(I($"W{worker.WorkerId} {workerPipState}"), _ => snapshot[workerPipState], includeInSnapshot: false);
                        }
                    }
                },
            }.Seal();
        }

        /// <summary>
        /// Sends a status update to the log if the minimal interval of time since the last update has passed and updates resource manager
        /// with latest resource utilization if enabled.
        /// </summary>
        public void UpdateStatus(bool overwriteable = true, int expectedCallbackFrequency = 0)
        {
            lock (m_statusLock)
            {
                DateTime utcNow = DateTime.UtcNow;
                bool isLoggingEnabled = !overwriteable || (utcNow > m_previousStatusLogTimeUTC.AddMilliseconds(m_loggingIntervalPeriodMs));
                if (isLoggingEnabled)
                {
                    m_previousStatusLogTimeUTC = utcNow;
                }

                m_unresponsivenessFactor = ComputeUnresponsivenessFactor(expectedCallbackFrequency, m_statusLastCollected, DateTime.UtcNow);
                m_maxUnresponsivenessFactor = Math.Max(m_unresponsivenessFactor, m_maxUnresponsivenessFactor);
                m_statusLastCollected = DateTime.UtcNow;

                // Log a specific message we can query from telemetry if unresponsiveness gets very high
                if (m_unresponsivenessFactor > 10)
                {
                    BuildXL.Tracing.Logger.Log.StatusCallbacksDelayed(m_executePhaseLoggingContext, m_unresponsivenessFactor);
                }

                if (m_statusRows == null)
                {
                    m_statusRows = GetStatusRows();
                    BuildXL.Tracing.Logger.Log.StatusHeader(m_executePhaseLoggingContext, m_statusRows.PrintHeaders());
                }

                OperationTracker.WriteCountersFile(Context, m_configuration.Logging, refreshInterval: TimeSpan.FromSeconds(30));

                // Update snapshots for status reporting
                m_executionStepTracker.CurrentSnapshot.Update();
                foreach (var worker in m_workers)
                {
                    worker.PipStateSnapshot.Update();
                }

                m_pipStateCounters.CollectSnapshotsForEachType(m_pipStateCountersSnapshots);
                m_pipTypesToLogCountersSnapshot.AggregateByPipTypes(m_pipStateCountersSnapshots, s_pipTypesToLogStats);

                var pipsWaiting = m_pipTypesToLogCountersSnapshot[PipState.Waiting];
                var pipsReady = m_pipTypesToLogCountersSnapshot[PipState.Ready];
                long semaphoreQueued = m_pipQueue.NumSemaphoreQueued;

                // The PipQueue might concurrently start to run queued items, so we match the numbers we get back with
                // the current scheduler state to avoid confusing our user looking at the status log message.
                semaphoreQueued = Math.Min(semaphoreQueued, pipsReady);

                // Treat queued semaphores as waiting pips  for status messages
                // rather than ready pips (even though their state is Ready).
                pipsReady -= semaphoreQueued;
                pipsWaiting += semaphoreQueued;

                ExecutionSampler.LimitingResource limitingResource = ExecutionSampler.LimitingResource.Other;
                if (m_performanceAggregator != null)
                {
                    limitingResource = ExecutionSampler.OnPerfSample(m_performanceAggregator, readyProcessPips: m_processStateCountersSnapshot[PipState.Ready], executinProcessPips: LocalWorker.RunningPipExecutorProcesses.Count, lastConcurrencyLimiter: m_chooseWorkerCpu.LastConcurrencyLimiter);
                }

                m_processStateCountersSnapshot.AggregateByPipTypes(m_pipStateCountersSnapshots, s_processPipTypesToLogStats);

                // Only log process counters for distributed build
                if (isLoggingEnabled && IsDistributedBuild)
                {
                    Logger.Log.ProcessStatus(
                        m_executePhaseLoggingContext,
                        pipsSucceeded: m_processStateCountersSnapshot.DoneCount,
                        pipsFailed: m_processStateCountersSnapshot[PipState.Failed],
                        pipsSkippedDueToFailedDependencies: m_processStateCountersSnapshot.SkippedDueToFailedDependenciesCount,
                        pipsRunning: m_processStateCountersSnapshot.RunningCount,
                        pipsReady: m_processStateCountersSnapshot[PipState.Ready] - semaphoreQueued,
                        pipsWaiting: m_processStateCountersSnapshot[PipState.Waiting] + semaphoreQueued,
                        pipsWaitingOnSemaphore: semaphoreQueued);
                }

                m_perfInfo = m_performanceAggregator?.ComputeMachinePerfInfo(ensureSample: m_testHooks != null ) ??
                    (m_testHooks?.GenerateSyntheticMachinePerfInfo != null ? m_testHooks?.GenerateSyntheticMachinePerfInfo(m_executePhaseLoggingContext, this) : null) ??
                    default(PerformanceCollector.MachinePerfInfo);

                UpdateResourceAvailability(m_perfInfo);

                // Of the pips in choose worker, how many could be executing on the local worker but are not due to
                // resource constraints
                int pipsWaitingOnResources = Math.Min(
                    m_executionStepTracker.CurrentSnapshot[PipExecutionStep.ChooseWorkerCpu],
                    Math.Min(
                        LocalWorker.TotalProcessSlots - LocalWorker.EffectiveTotalProcessSlots,
                        LocalWorker.TotalProcessSlots - LocalWorker.AcquiredProcessSlots));

                // Log pip statistics to CloudBuild.
                if (isLoggingEnabled && m_configuration.InCloudBuild())
                {
                    CloudBuildEventSource.Log.DominoContinuousStatisticsEvent(new DominoContinuousStatisticsEvent
                    {
                        // The number of ignored pips should not contribute to the total because Batmon progress depends on this calculation: executedPips / totalPips
                        TotalPips = m_pipTypesToLogCountersSnapshot.Total - m_pipTypesToLogCountersSnapshot[PipState.Ignored],
                        TotalProcessPips = m_processStateCountersSnapshot.Total - m_processStateCountersSnapshot[PipState.Ignored] - m_numServicePipsScheduled,
                        PipsFailed = m_pipTypesToLogCountersSnapshot[PipState.Failed],
                        PipsSkippedDueToFailedDependencies = m_pipTypesToLogCountersSnapshot.SkippedDueToFailedDependenciesCount,
                        PipsSuccessfullyExecuted = m_pipTypesToLogCountersSnapshot.DoneCount,
                        PipsExecuting = m_pipTypesToLogCountersSnapshot.RunningCount,
                        PipsReadyToRun = pipsReady,
                        // Process pips executed only counts pips that went through cache lookup (i.e. service pips are not included)
                        ProcessPipsExecuted = m_numProcessPipsSatisfiedFromCache + m_numProcessPipsUnsatisfiedFromCache,
                        ProcessPipsExecutedFromCache = m_numProcessPipsSatisfiedFromCache,
                    });
                }

                PipStateCountersSnapshot copyFileStats = new PipStateCountersSnapshot();
                copyFileStats.AggregateByPipTypes(m_pipStateCountersSnapshots, new PipType[] { PipType.CopyFile });

                PipStateCountersSnapshot writeFileStats = new PipStateCountersSnapshot();
                writeFileStats.AggregateByPipTypes(m_pipStateCountersSnapshots, new PipType[] { PipType.WriteFile });

                if (isLoggingEnabled)
                {
                    // Log pip statistics to Console
                    LogPipStatus(
                        m_executePhaseLoggingContext,
                        pipsSucceeded: m_pipTypesToLogCountersSnapshot.DoneCount,
                        pipsFailed: m_pipTypesToLogCountersSnapshot[PipState.Failed],
                        pipsSkippedDueToFailedDependencies: m_pipTypesToLogCountersSnapshot.SkippedDueToFailedDependenciesCount,
                        pipsRunning: m_pipTypesToLogCountersSnapshot.RunningCount,
                        pipsReady: pipsReady,
                        pipsWaiting: pipsWaiting,
                        pipsWaitingOnSemaphore: semaphoreQueued,
                        servicePipsRunning: m_serviceManager.RunningServicesCount,
                        perfInfoForConsole: m_perfInfo.ConsoleResourceSummary,
                        pipsWaitingOnResources: pipsWaitingOnResources,
                        procsExecuting: LocalWorker.RunningPipExecutorProcesses.Count,
                        procsSucceeded: m_processStateCountersSnapshot[PipState.Done],
                        procsFailed: m_processStateCountersSnapshot[PipState.Failed],
                        procsSkippedDueToFailedDependencies: m_processStateCountersSnapshot[PipState.Skipped],

                        // This uses a seemingly peculiar calculation to make sure it makes sense regardless of whether pipelining
                        // is on or not. Pending is an intentionally invented state since it doesn't correspond to a real state
                        // in the scheduler. It is basically meant to be a bucket of things that could be run if more parallelism
                        // were available. This technically isn't true because cache lookups fall in there as well, but it's close enough.
                        procsPending: m_processStateCountersSnapshot[PipState.Ready] + m_processStateCountersSnapshot[PipState.Running] - LocalWorker.RunningPipExecutorProcesses.Count,
                        procsWaiting: m_processStateCountersSnapshot[PipState.Waiting],
                        procsCacheHit: m_numProcessPipsSatisfiedFromCache,
                        procsNotIgnored: m_processStateCountersSnapshot.Total - m_processStateCountersSnapshot.IgnoredCount,
                        limitingResource: limitingResource.ToString(),
                        perfInfoForLog: m_perfInfo.LogResourceSummary,
                        overwriteable: overwriteable,
                        copyFileDone: copyFileStats.DoneCount,
                        copyFileNotDone: copyFileStats.Total - copyFileStats.DoneCount - copyFileStats.IgnoredCount,
                        writeFileDone: writeFileStats.DoneCount,
                        writeFileNotDone: writeFileStats.Total - writeFileStats.DoneCount - writeFileStats.IgnoredCount);
                }

                // Number of process pips that are not completed yet.
                long numProcessPipsPending = m_processStateCountersSnapshot[PipState.Waiting] + m_processStateCountersSnapshot[PipState.Ready] + m_processStateCountersSnapshot[PipState.Running];

                // PipState.Running does not mean that the pip is actually running. The pip might be waiting for a slot.
                // That's why, we need to get the actual number of process pips that were allocated a slot on the workers (including localworker).
                long numProcessPipsAllocatedSlots = Workers.Sum(a => a.AcquiredSlotsForProcessPips);

                // Verify available disk space is greater than the minimum available space specified in /minimumDiskSpaceForPipsGb:<int>
                if (m_writableDrives != null &&
                    !m_scheduleTerminating &&
                    m_performanceAggregator != null &&
                    (m_scheduleConfiguration.MinimumDiskSpaceForPipsGb ?? 0) > 0)
                {
                    foreach (var disk in m_performanceAggregator.DiskStats)
                    {
                        if (m_writableDrives.Contains(disk.Drive)
                            && disk.AvailableSpaceGb.Count != 0 // If we ever have a successful collection of the disk space
                            && disk.AvailableSpaceGb.Latest < (double)m_scheduleConfiguration.MinimumDiskSpaceForPipsGb)
                        {
                            Logger.Log.WorkerFailedDueToLowDiskSpace(
                                m_loggingContext,
                                disk.Drive,
                                (int)m_scheduleConfiguration.MinimumDiskSpaceForPipsGb,
                                (int)disk.AvailableSpaceGb.Latest);

                            RequestTermination(cancelQueue: true);
                            break;
                        }
                    }
                }

                var data = new StatusEventData
                {
                    Time = DateTime.UtcNow,
                    CpuPercent = m_perfInfo.CpuUsagePercentage,
                    DiskPercents = m_perfInfo.DiskUsagePercentages ?? new int[0],
                    DiskQueueDepths = m_perfInfo.DiskQueueDepths ?? new int[0],
                    DiskAvailableSpaceGb = m_perfInfo.DiskAvailableSpaceGb ?? new int[0],
                    ProcessCpuPercent = m_perfInfo.ProcessCpuPercentage,
                    ProcessWorkingSetMB = m_perfInfo.ProcessWorkingSetMB,
                    RamPercent = m_perfInfo.RamUsagePercentage ?? 0,
                    RamUsedMb = (m_perfInfo.TotalRamMb.HasValue && m_perfInfo.AvailableRamMb.HasValue) ? m_perfInfo.TotalRamMb.Value - m_perfInfo.AvailableRamMb.Value : 0,
                    RamFreeMb = m_perfInfo.AvailableRamMb ?? 0,
                    CommitPercent = m_perfInfo.CommitUsagePercentage ?? 0,
                    CommitUsedMb = m_perfInfo.CommitUsedMb ?? 0,
                    CommitFreeMb = (m_perfInfo.CommitLimitMb.HasValue && m_perfInfo.CommitUsedMb.HasValue) ? m_perfInfo.CommitLimitMb.Value - m_perfInfo.CommitUsedMb.Value : 0,
                    CpuWaiting = m_pipQueue.GetNumQueuedByKind(DispatcherKind.CPU),
                    CpuRunning = m_pipQueue.GetNumRunningByKind(DispatcherKind.CPU),
                    IoCurrentMax = m_pipQueue.GetMaxParallelDegreeByKind(DispatcherKind.IO),
                    IoWaiting = m_pipQueue.GetNumQueuedByKind(DispatcherKind.IO),
                    IoRunning = m_pipQueue.GetNumRunningByKind(DispatcherKind.IO),
                    LookupWaiting = m_pipQueue.GetNumQueuedByKind(DispatcherKind.CacheLookup),
                    LookupRunning = m_pipQueue.GetNumRunningByKind(DispatcherKind.CacheLookup),
                    LimitingResource = limitingResource,
                    RunningPipExecutorProcesses = LocalWorker.RunningPipExecutorProcesses.Count,
                    RunningProcesses = LocalWorker.RunningProcesses,
                    PipsSucceededAllTypes = m_pipStateCountersSnapshots.SelectArray(a => a.DoneCount),
                    UnresponsivenessFactor = m_unresponsivenessFactor,
                    ProcessPipsPending = numProcessPipsPending,
                    ProcessPipsAllocatedSlots = numProcessPipsAllocatedSlots,
                    EffectiveTotalProcessSlots = LocalWorker.EffectiveTotalProcessSlots,
                };

                // Send resource usage to the execution log
                ExecutionLog?.StatusReported(data);

                BuildXL.Tracing.Logger.Log.Status(m_executePhaseLoggingContext, m_statusRows.PrintRow(data));

                if (DateTime.UtcNow > m_statusSnapshotLastUpdated.AddSeconds(StatusSnapshotInterval))
                {
                    var snapshotData = m_statusRows.GetSnapshot(data);
                    BuildXL.Tracing.Logger.Log.StatusSnapshot(m_executePhaseLoggingContext, snapshotData);

                    m_statusSnapshotLastUpdated = DateTime.UtcNow;
                }

                if (m_scheduleConfiguration.AdaptiveIO)
                {
                    Contract.Assert(m_performanceAggregator != null, "Adaptive IO requires non-null performanceAggregator");
                    m_pipQueue.AdjustIOParallelDegree(m_perfInfo);
                }

                if (m_configuration.Distribution.EarlyWorkerRelease && IsDistributedMaster)
                {
                    PerformEarlyReleaseWorker(numProcessPipsPending, numProcessPipsAllocatedSlots);
                }
            }
        }

        /// <summary>
        /// We have 2 versions of this message for the sake of letting one be overwriteable and the other not.
        /// Other than they should always stay identical. So to enforce that we have them reference the same
        /// set of attribute arguments and go through the same method
        /// </summary>
        public static void LogPipStatus(
            LoggingContext loggingContext,
            long pipsSucceeded,
            long pipsFailed,
            long pipsSkippedDueToFailedDependencies,
            long pipsRunning,
            long pipsReady,
            long pipsWaiting,
            long pipsWaitingOnSemaphore,
            long servicePipsRunning,
            string perfInfoForConsole,
            long pipsWaitingOnResources,
            long procsExecuting,
            long procsSucceeded,
            long procsFailed,
            long procsSkippedDueToFailedDependencies,
            long procsPending,
            long procsWaiting,
            long procsCacheHit,
            long procsNotIgnored,
            string limitingResource,
            string perfInfoForLog,
            bool overwriteable,
            long copyFileDone,
            long copyFileNotDone,
            long writeFileDone,
            long writeFileNotDone)
        {
            // Noop if no process information is included. This can happen for the last status event in a build using
            // incremental scheduling if it goes through the codepath where zero files changed. All other codepaths
            // compute the actual process count and can be logged
            if (procsExecuting + procsSucceeded + procsFailed + procsSkippedDueToFailedDependencies + procsPending + procsWaiting + procsCacheHit == 0)
            {
                return;
            }

            if (overwriteable)
            {
                Logger.Log.PipStatus(
                    loggingContext,
                    pipsSucceeded,
                    pipsFailed,
                    pipsSkippedDueToFailedDependencies,
                    pipsRunning,
                    pipsReady,
                    pipsWaiting,
                    pipsWaitingOnSemaphore,
                    servicePipsRunning,
                    perfInfoForConsole,
                    pipsWaitingOnResources,
                    procsExecuting,
                    procsSucceeded,
                    procsFailed,
                    procsSkippedDueToFailedDependencies,
                    procsPending,
                    procsWaiting,
                    procsCacheHit,
                    procsNotIgnored,
                    limitingResource,
                    perfInfoForLog,
                    copyFileDone,
                    copyFileNotDone,
                    writeFileDone,
                    writeFileNotDone);
            }
            else
            {
                Logger.Log.PipStatusNonOverwriteable(
                    loggingContext,
                    pipsSucceeded,
                    pipsFailed,
                    pipsSkippedDueToFailedDependencies,
                    pipsRunning,
                    pipsReady,
                    pipsWaiting,
                    pipsWaitingOnSemaphore,
                    servicePipsRunning,
                    perfInfoForConsole,
                    pipsWaitingOnResources,
                    procsExecuting,
                    procsSucceeded,
                    procsFailed,
                    procsSkippedDueToFailedDependencies,
                    procsPending,
                    procsWaiting,
                    procsCacheHit,
                    procsNotIgnored,
                    limitingResource,
                    perfInfoForLog,
                    copyFileDone,
                    copyFileNotDone,
                    writeFileDone,
                    writeFileNotDone);
            }
        }

        /// <summary>
        /// Decide whether we can release a remote worker. This method is executed every 2 seconds depending on the frequency of LogStatus timer.
        /// </summary>
        private void PerformEarlyReleaseWorker(long numProcessPipsPending, long numProcessPipsAllocatedSlots)
        {
            long numProcessPipsWaiting = numProcessPipsPending - numProcessPipsAllocatedSlots;

            // Try releasing the remote  worker which has the lowest acquired slots for process execution.
            // It is intentional that we do not include cachelookup slots here as cachelookup step is a lot faster than execute step.
            var workerToReleaseCandidate = Workers.Where(a => a.IsRemote && a.IsAvailable).OrderBy(a => a.AcquiredProcessSlots).FirstOrDefault();
            if (workerToReleaseCandidate == null)
            {
                return;
            }

            // If the available remote workers perform at that multiplier capacity in future, how many process pips we can concurrently execute:
            int totalProcessSlots = LocalWorker.TotalProcessSlots +
               (int)Math.Ceiling(m_configuration.Distribution.EarlyWorkerReleaseMultiplier * Workers.Where(a => a.IsRemote && a.IsAvailable).Sum(a => a.TotalProcessSlots));

            // Release worker if numProcessPipsWaiting can be satisfied by remaining workers
            if (numProcessPipsWaiting > 0 && (numProcessPipsWaiting < totalProcessSlots - workerToReleaseCandidate.TotalProcessSlots))
            {
                Logger.Log.InitiateWorkerRelease(
                        m_loggingContext,
                        workerToReleaseCandidate.Name,
                        numProcessPipsWaiting,
                        totalProcessSlots,
                        workerToReleaseCandidate.AcquiredCacheLookupSlots,
                        workerToReleaseCandidate.AcquiredProcessSlots,
                        workerToReleaseCandidate.AcquiredIpcSlots);

                var task = workerToReleaseCandidate.EarlyReleaseAsync();
                Analysis.IgnoreResult(task);
            }
        }

        /// <summary>
        /// Compares the time the UpdateStatus timer was invoked against how it was configured as a proxy to how unresponsive the machine is.
        /// </summary>
        /// <returns>A value of 1 means the timer is as often as expected. 2 would be twice as slowly as expected. etc.</returns>
        internal static int ComputeUnresponsivenessFactor(int expectedCallbackFrequencyMs, DateTime statusLastCollected, DateTime currentTime)
        {
            if (expectedCallbackFrequencyMs > 0)
            {
                TimeSpan timeSinceLastUpdate = currentTime - statusLastCollected;
                if (timeSinceLastUpdate.TotalMilliseconds > 0)
                {
                    return (int)(timeSinceLastUpdate.TotalMilliseconds / expectedCallbackFrequencyMs);
                }
            }

            return 0;
        }

        private void UpdateResourceAvailability(PerformanceCollector.MachinePerfInfo perfInfo)
        {
            var resourceManager = State.ResourceManager;
            resourceManager.RefreshMemoryCounters();

            ManageMemoryMode defaultManageMemoryMode = m_scheduleConfiguration.GetManageMemoryMode();
            MemoryResource memoryResource = MemoryResource.Available;

            // RAM (WORKINGSET) USAGE.
            // If ram resources are not available, the scheduler is throttled (effectiveprocessslots becoming 1) and
            // we cancel the running ones.

            if (LocalWorker.TotalRamMb == null && m_perfInfo.AvailableRamMb.HasValue)
            {
                // TotalRam represent the available size at the beginning of the build.
                // Because graph construction can consume a large memory as a part of BuildXL process,
                // we add ProcessWorkingSetMb to the current available ram.
                LocalWorker.TotalRamMb = m_perfInfo.AvailableRamMb + m_perfInfo.ProcessWorkingSetMB;
            }

            if (perfInfo.RamUsagePercentage != null)
            {
                // This is the calculation for the low memory perf smell. This is somewhat of a check against how effective
                // the throttling is. It happens regardless of the throttling limits and is logged when we're pretty
                // sure there is a ram problem
                bool isAvailableRamCritical = perfInfo.AvailableRamMb.Value < 100 || perfInfo.RamUsagePercentage.Value >= 98;
                if (isAvailableRamCritical)
                {
                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.CriticalLowRamMemory);

                    if (!m_hitLowRamMemoryPerfSmell)
                    {
                        m_hitLowRamMemoryPerfSmell = true;
                        // Log the perf smell at the time that it happens since the machine is likely going to get very
                        // bogged down and we want to make sure this gets sent to telemetry before the build is killed.
                        Logger.Log.LowRamMemory(m_executePhaseLoggingContext, perfInfo.AvailableRamMb.Value, perfInfo.RamUsagePercentage.Value);
                    }
                }

                bool exceededMaxRamUtilizationPercentage = perfInfo.EffectiveRamUsagePercentage.Value > m_configuration.Schedule.MaximumRamUtilizationPercentage;
                bool underMinimumAvailableRam = perfInfo.EffectiveAvailableRamMb.Value < m_configuration.Schedule.MinimumTotalAvailableRamMb();

                if (exceededMaxRamUtilizationPercentage && underMinimumAvailableRam)
                {
                    memoryResource |= MemoryResource.LowRam;
                }
                else if (isAvailableRamCritical && perfInfo.ModifiedPagelistPercentage > 50)
                {
                    // Ram >= 98% and ModifiedPageSet > 50%  
                    // Thrashing is an issue
                    memoryResource |= MemoryResource.LowRam;
                    defaultManageMemoryMode = ManageMemoryMode.CancelSuspended;
                }
            }

            /*
             * How COMMIT MEMORY works:
             * Committed Memory is the number of bytes allocated by processes when the OS stores a page frame (from physical memory) or a page slot (from logical/virtual memory) or both into the page file.
             * Process reserves a series of memory addresses (sometimes more that it currently requires, to control a contiguous block of memory)
             * Reserved memory does not necessarily represent real space in the physical memory (RAM) or on disk and a process can reserve more memory that available on the system.
             * To become usable, the memory address needs to correspond to byte space in memory (physical or disk).
             * Commit memory is the association between this reserved memory and it�s physical address (RAM or disk) causing them to be unavailable to other processes in most cases.
             * Since commit memory is a combination of the physical memory and the page file on disk, the used committed memory can exceed the physical memory available to the operating system.
             */

            // If commit memory usage is high, the scheduler is throttled without cancelling any pips.
            if (m_perfInfo.CommitLimitMb.HasValue)
            {
                LocalWorker.TotalCommitMb = m_perfInfo.CommitLimitMb.Value;
            }
            else if (LocalWorker.TotalCommitMb == null)
            {
                // If we cannot get commit usage for Windows, or it is the MacOS, we do not track of swap file usage.
                // That's why, we set it to very high number to disable throttling.
                LocalWorker.TotalCommitMb = int.MaxValue;
            }

            bool isCommitCriticalLevel = false;
            if (perfInfo.CommitUsagePercentage != null)
            {
                int availableCommit = m_perfInfo.CommitLimitMb.Value - m_perfInfo.CommitUsedMb.Value;

                if (perfInfo.CommitUsagePercentage.Value >= 98)
                {
                    isCommitCriticalLevel = true;
                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.CriticalLowCommitMemory);

                    if (!m_hitLowCommitMemoryPerfSmell)
                    {
                        m_hitLowCommitMemoryPerfSmell = true;
                        Logger.Log.LowCommitMemory(m_executePhaseLoggingContext, availableCommit, perfInfo.CommitUsagePercentage.Value);
                    }
                }

                // By default, MaximumCommitUtilizationPercentage is 95%.
                bool exceededMaxCommitUtilizationPercentage = perfInfo.CommitUsagePercentage.Value > m_configuration.Schedule.MaximumCommitUtilizationPercentage;

                if (exceededMaxCommitUtilizationPercentage)
                {
                    memoryResource |= MemoryResource.LowCommit;
                }
            }

            ToggleMemoryResourceAvailability(perfInfo, memoryResource);

            resourceManager.LastRequiredSizeMb = 0;
            resourceManager.LastManageMemoryMode = null;

            if (isCommitCriticalLevel)
            {
                // If commit usage is at the critical level (>= 98%), cancel pips to avoid out-of-page file errors.
                int desiredCommitPercentToFreeSlack = EngineEnvironmentSettings.DesiredCommitPercentToFreeSlack.Value ?? 0;

                // 98-95 = 3 + slack
                int desiredCommitPercentToFree = (perfInfo.CommitUsagePercentage.Value - m_configuration.Schedule.MaximumCommitUtilizationPercentage) + desiredCommitPercentToFreeSlack;

                // Ensure percentage to free is in valid percent range [0, 100]
                desiredCommitPercentToFree = Math.Max(0, Math.Min(100, desiredCommitPercentToFree));

                // Get the megabytes to free
                var desiredCommitMbToFree = (perfInfo.CommitLimitMb.Value * desiredCommitPercentToFree) / 100;

                resourceManager.TryManageResources(desiredCommitMbToFree, ManageMemoryMode.CancellationCommit);
            }
            else if (memoryResource.HasFlag(MemoryResource.LowRam))
            {
#if PLATFORM_OSX
                bool simulateHighMemory = m_testHooks?.SimulateHighMemoryPressure ?? false;
                Memory.PressureLevel pressureLevel = simulateHighMemory ? Memory.PressureLevel.Critical : Memory.PressureLevel.Normal;
                var result = simulateHighMemory ? true : Memory.GetMemoryPressureLevel(ref pressureLevel) == Dispatch.MACOS_INTEROP_SUCCESS;
                var startCancellingPips = false;

                if (result)
                {
                    // If the memory pressure level is above the configured level we start canceling pips to avoid Jetsam to kill our process
                    startCancellingPips = pressureLevel > m_configuration.Schedule.MaximumAllowedMemoryPressureLevel;
                }
                else
                {
                    Logger.Log.UnableToGetMemoryPressureLevel(
                            m_executePhaseLoggingContext,
                            availableRam: perfInfo.AvailableRamMb.Value,
                            minimumAvailableRam: m_configuration.Schedule.MinimumTotalAvailableRamMb(),
                            ramUtilization: perfInfo.RamUsagePercentage.Value,
                            maximumRamUtilization: m_configuration.Schedule.MaximumRamUtilizationPercentage);
                }

                // CancellationRam is the only mode for OSX.
                defaultManageMemoryMode = ManageMemoryMode.CancellationRam;

                if (!m_scheduleConfiguration.DisableProcessRetryOnResourceExhaustion && startCancellingPips)
#else
                // We only retry when the ram memory is not available.
                // When commit memory is not available, we stop scheduling; but we do not cancel the currently running ones
                // because OS can resize the commit memory.
                if (!m_scheduleConfiguration.DisableProcessRetryOnResourceExhaustion)
#endif
                {
                    int desiredRamPercentToFreeSlack = EngineEnvironmentSettings.DesiredRamPercentToFreeSlack.Value ?? 5;

                    // Free down to the specified max RAM utilization percentage with some slack
                    int desiredRamPercentToFree = (perfInfo.EffectiveRamUsagePercentage.Value - m_configuration.Schedule.MaximumRamUtilizationPercentage) + desiredRamPercentToFreeSlack;

                    // Ensure percentage to free is in valid percent range [0, 100]
                    desiredRamPercentToFree = Math.Max(0, Math.Min(100, desiredRamPercentToFree));

                    // Get the megabytes to free, at least 1MB so that we can suspend/cancel/emptyWorkingSet one pip
                    var desiredRamMbToFree = Math.Min(1, (perfInfo.TotalRamMb.Value * desiredRamPercentToFree) / 100);

                    resourceManager.TryManageResources(desiredRamMbToFree, defaultManageMemoryMode);
                }
            }
            else if (perfInfo.RamUsagePercentage.HasValue
                    && m_configuration.Schedule.MaximumRamUtilizationPercentage > perfInfo.RamUsagePercentage.Value
                    && resourceManager.NumSuspended > 0)
            {
                // Use EffectiveAvailableRam when to throttle the scheduler and cancel more.

                // We might use the actual available ram to resume though.
                // If there is available ram, then resume any suspended pips.
                // 90% memory - current percent = availableRamForResume
                // When it is resumed, start from the larger execution time.

                var desiredRamPercentToUse = m_configuration.Schedule.MaximumRamUtilizationPercentage - perfInfo.RamUsagePercentage.Value;

                // Ensure percentage is in valid percent range [0, 100]
                desiredRamPercentToUse = Math.Max(0, Math.Min(100, desiredRamPercentToUse));

                // Get the megabytes to free
                var desiredRamMbToUse = (perfInfo.TotalRamMb.Value * desiredRamPercentToUse) / 100;

                resourceManager.TryManageResources(desiredRamMbToUse, ManageMemoryMode.Resume);
            }

            if (resourceManager.NumActive == 0 && resourceManager.NumSuspended > 0)
            {
                PipExecutionCounters.IncrementCounter(PipExecutorCounter.CancelSuspendedPipDueToNoRunningProcess);

                // If there is no active process pips running, cancel one pip to check whether the scheduler will move forward.
                resourceManager.TryManageResources(1, ManageMemoryMode.CancellationRam);
            }
        }

        private void ToggleMemoryResourceAvailability(PerformanceCollector.MachinePerfInfo perfInfo, MemoryResource memoryResource)
        {
            if (memoryResource.HasFlag(MemoryResource.LowRam))
            {
                PipExecutionCounters.IncrementCounter(PipExecutorCounter.MemoryResourceBecomeUnavailableDueToRam);
            }

            if (memoryResource.HasFlag(MemoryResource.LowCommit))
            {
                PipExecutionCounters.IncrementCounter(PipExecutorCounter.MemoryResourceBecomeUnavailableDueToCommit);
            }

            if (memoryResource == MemoryResource.Available && !LocalWorker.MemoryResourceAvailable)
            {
                // Set resources to available to allow executing further work
                Logger.Log.ResumingProcessExecutionAfterSufficientResources(m_executePhaseLoggingContext);
                LocalWorker.MemoryResource = memoryResource;

                // For distributed workers, the local worker total processes does not control
                // concurrency. It must be set on the CPU queue
                if (IsDistributedWorker)
                {
                    SetQueueMaxParallelDegreeByKind(DispatcherKind.CPU, LocalWorker.EffectiveTotalProcessSlots);
                }
            }

            if (memoryResource != MemoryResource.Available && LocalWorker.MemoryResourceAvailable)
            {
                // Set resources to unavailable to prevent executing further work
                Logger.Log.StoppingProcessExecutionDueToMemory(
                    m_executePhaseLoggingContext,
                    reason: memoryResource.ToString(),
                    availableRam: perfInfo.AvailableRamMb ?? 0,
                    minimumAvailableRam: m_configuration.Schedule.MinimumTotalAvailableRamMb(),
                    ramUtilization: perfInfo.RamUsagePercentage ?? 0,
                    maximumRamUtilization: m_configuration.Schedule.MaximumRamUtilizationPercentage,
                    commitUtilization: perfInfo.CommitUsagePercentage ?? 0,
                    maximumCommitUtilization: m_configuration.Schedule.MaximumCommitUtilizationPercentage);

                LocalWorker.MemoryResource = memoryResource;

                // For distributed workers, the local worker total processes does not control
                // concurrency. It must be set on the CPU queue
                if (IsDistributedWorker)
                {
                    SetQueueMaxParallelDegreeByKind(DispatcherKind.CPU, LocalWorker.EffectiveTotalProcessSlots);
                }
            }
        }

        /// <summary>
        /// Callback event that gets raised when a Pip finished executing
        /// </summary>
        /// <remarks>
        /// Multiple events may be fired concurrently. <code>WhenDone</code> will only complete when all event handlers have
        /// returned.
        /// The event handler should do minimal work, as the queue won't re-use the slot before the event handler returns.
        /// Any exception leaked by the event handler may terminate the process.
        /// </remarks>
        public virtual async Task OnPipCompleted(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip != null);

            var pipLoggingContext = runnablePip.LoggingContext;
            var pip = runnablePip.Pip;
            string pipDescription = runnablePip.Description;
            if (!runnablePip.Result.HasValue)
            {
                // This should happen only in case of cancellation
                Contract.Assert(runnablePip.IsCancelled, "Runnable pip should always have a result unless it was cancelled");
                return;
            }

            if (runnablePip.Performance.RetryCountDueToStoppedWorker < m_pipRetryCountersDueToNetworkFailures.Length)
            {
                m_pipRetryCountersDueToNetworkFailures[runnablePip.Performance.RetryCountDueToStoppedWorker]++;
            }

            if (runnablePip.Performance.RetryCountDueToLowMemory > 0)
            {
                m_pipRetryCountersDueToLowMemory.AddOrUpdate(runnablePip.Performance.RetryCountDueToLowMemory, 1, (id, count) => count + 1);
            }

            using (runnablePip.OperationContext.StartOperation(PipExecutorCounter.OnPipCompletedDuration))
            {
                var result = runnablePip.Result.Value;

                // Queued pip tasks are supposed to return a bool indicating success or failure.
                // Any exception (even a BuildXLException) captured by the task is considered a terminating error,
                // since that indicates a bug in the PipRunner implementation. Consequently, we access Result,
                // which may throw (rather than checking fault status first).
                LogEventPipEnd(pipLoggingContext, pip, result.Status, result.PerformanceInfo == null ? 0 : DateTime.UtcNow.Ticks - result.PerformanceInfo.ExecutionStart.Ticks);

                Contract.Assert((result.PerformanceInfo == null) == !result.Status.IndicatesExecution());

                bool succeeded = !result.Status.IndicatesFailure();
                bool skipped = result.Status == PipResultStatus.Skipped;
                PipId pipId = pip.PipId;
                PipType pipType = pip.PipType;
                var nodeId = pipId.ToNodeId();

                PipRuntimeInfo pipRuntimeInfo = GetPipRuntimeInfo(pipId);

                if (pipType == PipType.Process)
                {
                    State.Cache.CompletePip(runnablePip.PipId);

                    CleanTempDirs(runnablePip);

                    // Don't count service pips in process pip counters
                    var processRunnablePip = runnablePip as ProcessRunnablePip;
                    if (!processRunnablePip.Process.IsStartOrShutdownKind)
                    {
                        var processDuration = runnablePip.RunningTime;
                        PipExecutionCounters.AddToCounter(PipExecutorCounter.ProcessDuration, processDuration);
                        m_groupedPipCounters.IncrementCounter(processRunnablePip.Process, PipCountersByGroup.Count);
                        m_groupedPipCounters.AddToCounter(processRunnablePip.Process, PipCountersByGroup.ProcessDuration, processDuration);

                        if (!succeeded && result.Status == PipResultStatus.Failed)
                        {
                            m_groupedPipCounters.IncrementCounter(processRunnablePip.Process, PipCountersByGroup.Failed);
                        }
                    }

                    // Keep logging the process stats near the Pip's state transition so we minimize having inconsistent
                    // stats like having more cache hits than completed process pips
                    LogProcessStats(runnablePip);
                }
                else if (pipType == PipType.Ipc)
                {
                    Interlocked.Increment(ref m_numIpcPipsCompleted);
                }

                if (!IsDistributedWorker && m_configuration.Schedule.InputChanges.IsValid && (pipType == PipType.CopyFile || pipType == PipType.Process))
                {
                    ReadOnlyArray<FileArtifact> outputContents = ReadOnlyArray<FileArtifact>.Empty;
                    PipResultStatus status = result.Status;

                    if (pipType == PipType.CopyFile)
                    {
                        outputContents = new[] { ((CopyFile)runnablePip.Pip).Destination }.ToReadOnlyArray();
                    }
                    else if (runnablePip.ExecutionResult?.OutputContent != null)
                    {
                        outputContents = runnablePip.ExecutionResult.OutputContent.SelectList(o => o.fileArtifact).ToReadOnlyArray();
                    }

                    m_fileContentManager.SourceChangeAffectedInputs.ReportSourceChangeAffectedFiles(
                        pip,
                        result.DynamicallyObservedFiles,
                        outputContents);
                }

                if (!succeeded)
                {
                    m_hasFailures = true;

                    if (result.Status == PipResultStatus.Failed)
                    {
                        if (pipRuntimeInfo.State != PipState.Running)
                        {
                            Contract.Assume(false, "Prior state assumed to be Running. Was: " + pipRuntimeInfo.State.ToString());
                        }

                        pipRuntimeInfo.Transition(m_pipStateCounters, pipType, PipState.Failed);
                    }
                    else if (result.Status == PipResultStatus.Canceled)
                    {
                        if (pipRuntimeInfo.State != PipState.Running)
                        {
                            Contract.Assume(false, $"Prior state assumed to be {nameof(PipState.Running)}. Was: {pipRuntimeInfo.State}");
                        }

                        pipRuntimeInfo.Transition(m_pipStateCounters, pipType, PipState.Canceled);
                    }
                    else
                    {
                        Contract.Assume(false, "Unhandled failed PipResult");
                        return;
                    }
                }
                else if (skipped)
                {
                    // No state transition in this case (already terminal)
                    if (pipRuntimeInfo.State != PipState.Skipped)
                    {
                        Contract.Assume(false, "Prior state assumed to be skipped. Was: " + pipRuntimeInfo.State.ToString());
                    }
                }
                else
                {
                    Contract.Assert(
                        result.Status == PipResultStatus.DeployedFromCache ||
                        result.Status == PipResultStatus.UpToDate ||
                        result.Status == PipResultStatus.Succeeded ||
                        result.Status == PipResultStatus.NotMaterialized, I($"{result.Status} should not be here at this point"));

                    pipRuntimeInfo.Transition(
                        m_pipStateCounters,
                        pipType,
                        PipState.Done);
                }

                Contract.Assume(pipRuntimeInfo.State.IndicatesFailure() == !succeeded);
                Contract.Assume(pipRuntimeInfo.RefCount == 0 || /* due to output materialization */ pipRuntimeInfo.RefCount == CompletedRefCount);

                // A pip was executed, but then it doesn't materialize its outputs due to lazy materialization.
                // Then, the pip may get executed again to materialize its outputs. For that pip, wasAlreadyCompleted is true.
                var wasAlreadyCompleted = pipRuntimeInfo.RefCount == CompletedRefCount;

                if (!wasAlreadyCompleted)
                {
                    pipRuntimeInfo.RefCount = CompletedRefCount;
                }

                // Possibly begin tearing down the schedule (without executing all pips) on failure.
                // This happens before we traverse the dependents of this failed pip; since SchedulePipIfReady
                // checks m_scheduleTerminating this means that there will not be 'Skipped' pips in this mode
                // (they remain unscheduled entirely).
                if (!succeeded && !IsDistributedWorker)
                {
                    // We stop on the first error only on the master or single-machine builds.
                    // During cancellation, master coordinates with workers to stop the build.


                    //// ErrorsLoggedById is a ConcurrentBag. Its Contains() isn't particularly performant. It copies everything to a new list and then enumerates that.
                    //bool hasMaterializationErrorHappened = m_executePhaseLoggingContext.ErrorsLoggedById.Contains((ushort)EventId.PipMaterializeDependenciesFromCacheFailure)
                    //    || m_executePhaseLoggingContext.ErrorsLoggedById.Contains((ushort)EventId.PipMaterializeDependenciesFailureUnrelatedToCache);

                    // TODO(seokur): It is currently disabled to cancel the pips on the first materialization error.
                    // We just want to see how many materialization errors would occur in total.
                    bool hasMaterializationErrorHappened = false;

                    // Early terminate the build if
                    // (1) StopOnFirstError is enabled or
                    // (2) a materialization error is occurred in a distributed build.
                    bool earlyTerminate = m_scheduleConfiguration.StopOnFirstError || (hasMaterializationErrorHappened && IsDistributedMaster);

                    if (!IsTerminating && earlyTerminate)
                    {
                        Logger.Log.ScheduleTerminatingDueToPipFailure(m_executePhaseLoggingContext, pipDescription);

                        RequestTermination(cancelQueue: false);
                    }

                    Contract.Assert(m_executePhaseLoggingContext.ErrorWasLogged, I($"Should have logged error for pip: {pipDescription}"));
                }

                if (!succeeded && !m_executePhaseLoggingContext.ErrorWasLogged)
                {
                    Contract.Assert(
                        false,
                        I($"Pip failed but no error was logged. Failure kind: {result.Status}. Look through the log for other messages related to this pip: {pipDescription}"));
                }

                if (!wasAlreadyCompleted)
                {
                    if (succeeded && !skipped)
                    {
                        // Incremental scheduling: On success, a pip is 'clean' in that we know its outputs are up to date w.r.t. its inputs.
                        // When incrementally scheduling on the next build, we can skip this pip altogether unless it or a dependency have become dirty (due to file changes).
                        // However, if the pip itself is clean-materialized, then the pip enters this completion method through the CheckIncrementalSkip step.
                        // In that case, the incremental scheduling state should not be modified.
                        if (IncrementalSchedulingState != null && !IsPipCleanMaterialized(pipId))
                        {
                            using (runnablePip.OperationContext.StartOperation(PipExecutorCounter.UpdateIncrementalSchedulingStateDuration))
                            {
                                // TODO: Should IPC pips always be marked perpetually dirty?
                                if (result.MustBeConsideredPerpetuallyDirty)
                                {
                                    IncrementalSchedulingState.PendingUpdates.MarkNodePerpetuallyDirty(nodeId);
                                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.PipMarkPerpetuallyDirty);
                                    Logger.Log.PipIsPerpetuallyDirty(m_executePhaseLoggingContext, pipDescription);
                                }
                                else
                                {
                                    IncrementalSchedulingState.PendingUpdates.MarkNodeClean(nodeId);
                                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.PipMarkClean);
                                    Logger.Log.PipIsMarkedClean(m_executePhaseLoggingContext, pipDescription);
                                }

                                // The pip is clean, but it may have not materialized its outputs, so we track that fact as well.
                                if (result.Status != PipResultStatus.NotMaterialized)
                                {
                                    IncrementalSchedulingState.PendingUpdates.MarkNodeMaterialized(nodeId);
                                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.PipMarkMaterialized);
                                    Logger.Log.PipIsMarkedMaterialized(m_executePhaseLoggingContext, pipDescription);
                                }
                                else
                                {
                                    // Track non materialized pip.
                                    m_pipOutputMaterializationTracker.AddNonMaterializedPip(pip);
                                }

                                // Record dynamic observation outside lock.
                                if (pipType == PipType.Process)
                                {
                                    var processPip = (Process)pip;

                                    if (result.HasDynamicObservations || processPip.DirectoryOutputs.Length > 0)
                                    {
                                        using (runnablePip.OperationContext.StartOperation(PipExecutorCounter.RecordDynamicObservationsDuration))
                                        {
                                            IncrementalSchedulingState.RecordDynamicObservations(
                                                nodeId,
                                                result.DynamicallyObservedFiles.Select(path => path.ToString(Context.PathTable)),
                                                result.DynamicallyProbedFiles.Select(path => path.ToString(Context.PathTable)),
                                                result.DynamicallyObservedEnumerations.Select(path => path.ToString(Context.PathTable)),
                                                processPip.DirectoryOutputs.Select(
                                                    d =>
                                                        (
                                                            d.Path.ToString(Context.PathTable),
                                                            m_fileContentManager.ListSealedDirectoryContents(d)
                                                                .Select(f => f.Path.ToString(Context.PathTable)))));
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // The pip has been completed. We log perf info and opaque directory outputs at most once per pip.
                    if (result.Status.IndicatesExecution() && !pipType.IsMetaPip())
                    {
                        Contract.Assert(result.PerformanceInfo != null);
                        HandleExecutionPerformance(runnablePip, result.PerformanceInfo);

                        if (pipType == PipType.Process)
                        {
                            ReportOpaqueOutputs(runnablePip);
                        }
                    }

                    // Determine the uncacheability impact. There are 3 cases here:
                    if (pipType == PipType.Process)
                    {
                        ProcessPipExecutionPerformance processPerformanceResult = result.PerformanceInfo as ProcessPipExecutionPerformance;
                        if (processPerformanceResult != null)
                        {
                            // 1. The pip ran and had uncacheable file accesses. We set the flag that it is UncacheableImpacted
                            if (processPerformanceResult.FileMonitoringViolations.HasUncacheableFileAccesses)
                            {
                                pipRuntimeInfo.IsUncacheableImpacted = true;
                            }
                            else
                            {
                                // 2. The pip ran but didn't have uncacheable file accesses. We don't know conclusively whether
                                // it ran because it had an uncacheable parent that wasn't deterministic or if a direct input
                                // also changed causing it to run. That's splitting hairs so we leave the flag as-is
                            }
                        }
                        else
                        {
                            // 3. We may have marked this pip as being impacted by uncacheability when it was scheduled if it
                            // depended on an uncacheable pip. But if the uncacheable process was deterministic, this pip may
                            // have actually been a cache hit. In that case we reset the flag
                            pipRuntimeInfo.IsUncacheableImpacted = false;
                        }
                    }

                    // Now increment the counters for uncacheability
                    if (pipRuntimeInfo.IsUncacheableImpacted && pipType == PipType.Process)
                    {
                        PipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessPipsUncacheableImpacted);
                        PipExecutionCounters.AddToCounter(
                            PipExecutorCounter.ProcessPipsUncacheableImpactedDurationMs,
                            (long)(result.PerformanceInfo.ExecutionStop - result.PerformanceInfo.ExecutionStart).TotalMilliseconds);
                        Logger.Log.ProcessDescendantOfUncacheable(
                            m_executePhaseLoggingContext,
                            pipDescription: pipDescription);
                    }
                }

                // Schedule the dependent pips
                if (!wasAlreadyCompleted)
                {
                    using (runnablePip.OperationContext.StartOperation(PipExecutorCounter.ScheduleDependentsDuration))
                    {
                        await ScheduleDependents(result, succeeded, runnablePip, pipRuntimeInfo);
                    }
                }

                // Report pip completed to DropTracker
                m_dropPipTracker?.ReportPipCompleted(pip);
            }
        }

        private void ReportOpaqueOutputs(RunnablePip runnablePip)
        {
            var executionResult = runnablePip.ExecutionResult;
            // The execution result can be null for some tests
            if (executionResult == null)
            {
                return;
            }

            var directoryOutputs = executionResult.DirectoryOutputs.Select(tuple =>
                (tuple.directoryArtifact, ReadOnlyArray<FileArtifact>.From(tuple.fileArtifactArray.Select(faa => faa.ToFileArtifact()))));
            ExecutionLog?.PipExecutionDirectoryOutputs(new PipExecutionDirectoryOutputs
            {
                PipId = runnablePip.PipId,
                DirectoryOutputs = ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)>.From(directoryOutputs),
            });
        }

        private async Task ScheduleDependents(PipResult result, bool succeeded, RunnablePip runnablePip, PipRuntimeInfo pipRuntimeInfo)
        {
            var pipId = runnablePip.PipId;
            var nodeId = pipId.ToNodeId();

            foreach (Edge outEdge in ScheduledGraph.GetOutgoingEdges(nodeId))
            {
                // Light edges do not propagate failure or ref-count changes.
                if (outEdge.IsLight)
                {
                    continue;
                }

                PipId dependentPipId = outEdge.OtherNode.ToPipId();
                PipRuntimeInfo dependentPipRuntimeInfo = GetPipRuntimeInfo(dependentPipId);

                PipState currentDependentState = dependentPipRuntimeInfo.State;
                if (currentDependentState != PipState.Waiting &&
                    currentDependentState != PipState.Ignored &&
                    currentDependentState != PipState.Skipped)
                {
                    Contract.Assume(
                        false,
                        I($"Nodes with pending heavy edges must be pending or skipped already (due to failure or filtering), but its state is '{currentDependentState}'"));
                }

                if (currentDependentState == PipState.Ignored)
                {
                    continue;
                }

                // Mark the dependent as uncacheable impacted if the parent was marked as impacted
                if (pipRuntimeInfo.IsUncacheableImpacted)
                {
                    dependentPipRuntimeInfo.IsUncacheableImpacted = true;
                }

                if (!succeeded || result.Status == PipResultStatus.Skipped)
                {
                    // The current pip failed, so skip the dependent pip.
                    // Note that we decrement the ref count; this dependent pip will eventually have ref count == 0
                    // at which point we will 'run' the pip in ReportSkippedPip (simply to unwind the stack and then
                    // skip further transitive dependents).
                    if (currentDependentState == PipState.Waiting)
                    {
                        do
                        {
                            // There can be a race on calling TryTransition. One thread may lose on Interlocked.CompareExchange
                            // in PipRunTimeInfo.TryTransitionInternal, but before the other thread finishes the method, the former thread
                            // checks in the Contract.Assert below if the state is PipState.Skipped. One need to ensure that both threads
                            // end up with PipState.Skipped.
                            bool transitionToSkipped = dependentPipRuntimeInfo.TryTransition(
                                m_pipStateCounters,
                                m_pipTable.GetPipType(dependentPipId),
                                currentDependentState,
                                PipState.Skipped);

                            if (transitionToSkipped && dependentPipRuntimeInfo.State != PipState.Skipped)
                            {
                                Contract.Assert(
                                    false,
                                    I($"Transition to {nameof(PipState.Skipped)} is successful, but the state of dependent is {dependentPipRuntimeInfo.State}"));
                            }

                            currentDependentState = dependentPipRuntimeInfo.State;
                        }
                        while (currentDependentState != PipState.Skipped);
                    }
                    else
                    {
                        Contract.Assert(
                            dependentPipRuntimeInfo.State.IsTerminal(),
                            "Upon failure, dependent pips must be in a terminal failure state");
                    }
                }

                // Decrement reference count and possibly queue the pip (even if it is doomed to be skipped).
                var readyToSchedule = dependentPipRuntimeInfo.DecrementRefCount();

                if (readyToSchedule)
                {
                    OperationKind scheduledByOperationKind = PipExecutorCounter.ScheduledByDependencyDuration;
                    scheduledByOperationKind = scheduledByOperationKind.GetPipTypeSpecialization(m_pipTable.GetPipType(dependentPipId));

                    using (runnablePip.OperationContext.StartOperation(PipExecutorCounter.ScheduleDependentDuration))
                    using (runnablePip.OperationContext.StartOperation(scheduledByOperationKind))
                    {
                        // If it is ready to schedule, we do not need to call 'SchedulePip' under the lock
                        // because we call SchedulePip only once for pip.
                        await SchedulePip(outEdge.OtherNode, dependentPipId);
                    }
                }
            }
        }

        private void CleanTempDirs(RunnablePip runnablePip)
        {
            if (!m_configuration.Engine.CleanTempDirectories)
            {
                return;
            }

            Contract.Requires(runnablePip.PipType == PipType.Process);
            Contract.Requires(runnablePip.Result.HasValue);
            // Only allow this to be null in testing
            if (TempCleaner == null)
            {
                Contract.Assert(m_testHooks != null);
                return;
            }

            var process = (Process)runnablePip.Pip;
            var resultStatus = runnablePip.Result.Value.Status;

            // Don't delete the temp directories when a pip fails for debugging.
            if (resultStatus != PipResultStatus.Succeeded &&
                resultStatus != PipResultStatus.Canceled)
            {
                return;
            }

            // Roots of temp directories need to be deleted so that we have a consistent behavior with scrubber.
            // If those roots are not deleted and the user enables scrubber, then those roots will get deleted because
            // temp directories are not considered as outputs.
            if (process.TempDirectory.IsValid)
            {
                TempCleaner.RegisterDirectoryToDelete(process.TempDirectory.ToString(Context.PathTable), deleteRootDirectory: true);
            }

            foreach (var additionalTempDirectory in process.AdditionalTempDirectories)
            {
                // Unlike process.TempDirectory, which is invalid for pips without temp directories,
                // AdditionalTempDirectories should not have invalid paths added
                Contract.Requires(additionalTempDirectory.IsValid);
                TempCleaner.RegisterDirectoryToDelete(additionalTempDirectory.ToString(Context.PathTable), deleteRootDirectory: true);
            }

            // Only for successful run scheduling temporary outputs for deletion
            foreach (FileArtifactWithAttributes output in process.FileOutputs)
            {
                // Deleting all the outputs that can't be referenced
                // Non-reference-able outputs are safe to delete, since it would be an error for any concurrent build step to read them.
                // CanBeReferencedOrCached() is false for e.g. 'intermediate' outputs, and deleting them proactively can be a nice space saving
                if (!output.CanBeReferencedOrCached())
                {
                    TempCleaner.RegisterFileToDelete(output.Path.ToString(Context.PathTable));
                }
            }
        }

        private void LogProcessStats(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.PipType == PipType.Process);
            Contract.Requires(runnablePip.Result.HasValue);

            var result = runnablePip.Result.Value;
            var runnableProcess = (ProcessRunnablePip)runnablePip;

            // Start service and shutdown service pips do not go through CacheLookUp,
            // so they shouldn't be considered for cache stats
            if (runnableProcess.Process.IsStartOrShutdownKind)
            {
                return;
            }

            Interlocked.Increment(ref m_numProcessPipsCompleted);
            switch (result.Status)
            {
                case PipResultStatus.DeployedFromCache:
                case PipResultStatus.UpToDate:
                case PipResultStatus.NotMaterialized:
                    // These results describe output materialization state and
                    // can also occur for pips run on distributed workers since the outputs are
                    // not produced on this machine. Distinguish using flag on runnable process indicating
                    // execution
                    if (runnableProcess.Executed)
                    {
                        Interlocked.Increment(ref m_numProcessPipsUnsatisfiedFromCache);
                        m_groupedPipCounters.IncrementCounter(runnableProcess.Process, PipCountersByGroup.CacheMiss);
                    }
                    else
                    {
                        Interlocked.Increment(ref m_numProcessPipsSatisfiedFromCache);
                        m_groupedPipCounters.IncrementCounter(runnableProcess.Process, PipCountersByGroup.CacheHit);
                    }

                    break;
                case PipResultStatus.Failed:
                case PipResultStatus.Succeeded:
                case PipResultStatus.Canceled:
                    Interlocked.Increment(ref m_numProcessPipsUnsatisfiedFromCache);
                    m_groupedPipCounters.IncrementCounter(runnableProcess.Process, PipCountersByGroup.CacheMiss);
                    break;
                case PipResultStatus.Skipped:
                    Interlocked.Increment(ref m_numProcessPipsSkipped);
                    m_groupedPipCounters.IncrementCounter(runnableProcess.Process, PipCountersByGroup.Skipped);
                    break;
                default:
                    throw Contract.AssertFailure("PipResult case not handled");
            }
        }

        /// <summary>
        /// Schedule pip for evaluation. The pip's content fingerprint will be computed
        /// and it will be scheduled for execution.
        /// </summary>
        /// <remarks>
        /// At the call time the given pip must not have any further dependencies to wait on.
        /// </remarks>
        private async Task SchedulePip(NodeId node, PipId pipId)
        {
            Contract.Requires(pipId.IsValid);
            Contract.Requires(ScheduledGraph.ContainsNode(node));

            using (PipExecutionCounters.StartStopwatch(PipExecutorCounter.SchedulePipDuration))
            {
                PipRuntimeInfo pipRuntimeInfo = GetPipRuntimeInfo(pipId);
                Contract.Assert(pipRuntimeInfo.RefCount == 0, "All dependencies of the pip must be completed before the pip is scheduled to run.");

                PipState currentState = pipRuntimeInfo.State;

                Contract.Assume(
                    currentState == PipState.Waiting ||
                    currentState == PipState.Ignored ||
                    currentState == PipState.Skipped);

                // Pips which have not been explicitly scheduled (when that's required) are never 'ready' (even at refcount 0).
                if (currentState == PipState.Ignored)
                {
                    return;
                }

                Contract.Assert(currentState == PipState.Waiting || currentState == PipState.Skipped, "Current pip state should be either waiting or skipped.");

                // With a ref count of zero, all pip dependencies (if any) have already executed, and so we have
                // all needed content hashes (and content on disk) for inputs. This means the pip we can both
                // compute the content fingerprint and execute it (possibly in a cached manner).
                if (IsTerminating)
                {
                    // We're bringing down the schedule quickly. Even pips which become ready without any dependencies will be skipped.
                    // We return early here to skip OnPipNewlyQueuedOrRunning (which prevents m_numPipsQueuedOrRunning from increasing).
                    Pip pip = m_pipTable.HydratePip(pipId, PipQueryContext.SchedulerSchedulePipIfReady);

                    Contract.Assert(m_executePhaseLoggingContext != null, "m_executePhaseLoggingContext should be set at this point. Did you forget to initialize it?");
                    Logger.Log.ScheduleIgnoringPipSinceScheduleIsTerminating(
                        m_executePhaseLoggingContext,
                        pip.GetDescription(Context));
                    return;
                }

                var pipState = m_pipTable.GetMutable(pipId);

                if (currentState != PipState.Skipped)
                {
                    // If the pip is not skipped, then transition its state to Ready.
                    pipRuntimeInfo.Transition(m_pipStateCounters, pipState.PipType, PipState.Ready);
                }

                await SchedulePip(pipId, pipState.PipType);
            }
        }

        private Task SchedulePip(PipId pipId, PipType pipType, RunnablePipObserver observer = null, int? priority = null, PipExecutionStep? step = null)
        {
            Contract.Requires(step == null || IsDistributedWorker, "Step can only be explicitly specified when scheduling pips on distributed worker");
            Contract.Requires(step != null || !IsDistributedWorker, "Step MUST be explicitly specified when scheduling pips on distributed worker");

            // Offload the execution of the pip to one of the queues in the PipQueue.
            // If it is a meta or SealDirectory pip and the PipQueue has started draining, then the execution will be inlined here!
            // Because it is not worth to enqueue the fast operations such as the execution of meta and SealDirectory pips.

            ushort cpuUsageInPercent = m_scheduleConfiguration.UseHistoricalCpuUsageInfo() ? HistoricPerfDataTable[m_pipTable.GetPipSemiStableHash(pipId)].ProcessorsInPercents : (ushort)0;

            var runnablePip = RunnablePip.Create(
                m_executePhaseLoggingContext,
                this,
                pipId,
                pipType,
                priority ?? GetPipPriority(pipId),
                m_executePipFunc,
                cpuUsageInPercent,
                maxRetryLimit: m_configuration.Distribution.NumRetryFailedPipsOnAnotherWorker ?? 0);
            
            runnablePip.SetObserver(observer);
            if (IsDistributedWorker)
            {
                runnablePip.Transition(step.Value, force: true);
                runnablePip.SetWorker(LocalWorker);
                m_executionStepTracker.Transition(pipId, step.Value);
            }
            else
            {
                // Only on master, we keep performance info per pip
                m_runnablePipPerformance.Add(pipId, runnablePip.Performance);
            }

            return ExecuteAsyncOrEnqueue(runnablePip);
        }

        internal void AddExecutionLogTarget(IExecutionLogTarget target)
        {
            Contract.Requires(target != null);

            m_multiExecutionLogTarget.AddExecutionLogTarget(target);
        }

        internal void RemoveExecutionLogTarget(IExecutionLogTarget target)
        {
            Contract.Requires(target != null);

            lock (m_multiExecutionLogTarget)
            {
                m_multiExecutionLogTarget.RemoveExecutionLogTarget(target);
            }
        }

        internal void HandlePipRequest(PipId pipId, RunnablePipObserver observer, PipExecutionStep step, int priority)
        {
            Contract.Assert(IsDistributedWorker, "Only workers can handle distributed pip requests");

            // Start by updating the pip to the ready state
            PipRuntimeInfo pipRuntimeInfo = GetPipRuntimeInfo(pipId);
            pipRuntimeInfo.TryTransition(m_pipStateCounters, m_pipTable.GetPipType(pipId), PipState.Ignored, PipState.Waiting);
            pipRuntimeInfo.TryTransition(m_pipStateCounters, m_pipTable.GetPipType(pipId), PipState.Waiting, PipState.Ready);
            pipRuntimeInfo.TryTransition(m_pipStateCounters, m_pipTable.GetPipType(pipId), PipState.Ready, PipState.Running);

            SchedulePip(pipId, m_pipTable.GetPipType(pipId), observer, step: step, priority: priority).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Inline or enqueue the execution of the pip starting from the step given in the <see cref="RunnablePip.Step"/>
        /// </summary>
        private async Task ExecuteAsyncOrEnqueue(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.Step != PipExecutionStep.None);

            if (runnablePip.Step == PipExecutionStep.Done)
            {
                return;
            }

            var previousQueue = runnablePip.DispatcherKind;
            var nextQueue = DecideDispatcherKind(runnablePip);

            bool inline = false;

            // If the pip should be cancelled, make sure we inline the next step. The pip queue may be also flagged as cancelled and won't dequeue the pip otherwise.
            // The check for cancellation will then happen on ExecutePipStep and the pip will be transitioned to PipExecutionStep.Cancel
            if (ShouldCancelPip(runnablePip))
            {
                inline = true;
            }

            // If the next queue is none or the same as the previous one, do not change the current queue and inline execution here.
            // However, when choosing worker, we should enqueue again even though the next queue is chooseworker again.
            if (nextQueue == DispatcherKind.None)
            {
                inline = true;
            }

            if (previousQueue == nextQueue && nextQueue != DispatcherKind.ChooseWorkerCpu && nextQueue != DispatcherKind.ChooseWorkerCacheLookup)
            {
                // If the dispatcher kind is the same and we start a new pip, our new setting should decide to inline or not.
                if (runnablePip.Step == PipExecutionStep.Start)
                {
                    inline = !EngineEnvironmentSettings.DoNotInlineWhenNewPipRunInSameQueue;
                }
                else
                {
                    inline = true;
                }
            }

            if (runnablePip.Worker?.IsRemote == true)
            {
                inline = true;
                runnablePip.ReleaseDispatcher();
            }

            if (inline)
            {
                await runnablePip.RunAsync();
            }
            else
            {
                runnablePip.SetDispatcherKind(nextQueue);
                m_chooseWorkerCpu?.UnpauseChooseWorkerQueueIfEnqueuingNewPip(runnablePip, nextQueue);

                using (PipExecutionCounters.StartStopwatch(PipExecutorCounter.PipQueueEnqueueDuration))
                {
                    m_pipQueue.Enqueue(runnablePip);
                }
            }
        }

        private async Task ExecutePip(RunnablePip runnablePip)
        {
            var startTime = DateTime.UtcNow;
            runnablePip.StepStartTime = startTime;

            // Execute the current step
            PipExecutionStep nextStep;
            TimeSpan duration;

            if (runnablePip.Step == PipExecutionStep.Start || IsDistributedWorker)
            {
                // Initialize the runnable pip operation context so any
                // operations for the pip are properly attributed
                var loggingContext = LogEventPipStart(runnablePip);
                runnablePip.Start(OperationTracker, loggingContext);
            }

            // Measure the time spent executing pip steps as opposed to the
            // just sitting in the the queue
            using (var operationContext = runnablePip.OperationContext.StartOperation(PipExecutorCounter.ExecutePipStepDuration))
            using (runnablePip.OperationContext.StartOperation(runnablePip.Step))
            {
                runnablePip.Observer.StartStep(runnablePip);

                nextStep = await ExecutePipStep(runnablePip);
                duration = operationContext.Duration.Value;

                if (runnablePip.Worker?.IsLocal ?? true)
                {
                    // For the remote worker, the stepduration is set on the worker and sent it to the master via grpc message.
                    runnablePip.StepDuration = duration;
                }

                // If the duration is larger than EngineEnvironmentSettings.MinStepDurationSecForTracer (30 seconds by default)
                if (runnablePip.IncludeInTracer && (long)runnablePip.StepDuration.TotalSeconds > EngineEnvironmentSettings.MinStepDurationSecForTracer)
                {
                    var durationMs = (long)runnablePip.StepDuration.TotalMilliseconds;
                    BuildXL.Tracing.Logger.Log.TracerCompletedEvent(runnablePip.OperationContext,
                        runnablePip.Pip.FormattedSemiStableHash,
                        runnablePip.Step.ToString(),
                        runnablePip.Worker.Name + " - " + DecideDispatcherKind(runnablePip),
                        runnablePip.ThreadId,
                        runnablePip.StepStartTime.Ticks,
                        durationMs,
                        runnablePip.Pip.GetShortDescription(runnablePip.Environment.Context, withQualifer: false).Replace(@"\", @"\\").Replace("\"", "\\\""));
                }

                if (runnablePip.StepDuration.TotalMinutes > PipExecutionIOStepDelayedLimitMin && runnablePip.Step.IsIORelated())
                {
                    // None of I/O pip execution steps is supposed to take more than 15 minutes. However, there are some large Cosine pips whose inputs are materialized around 20m-25m.
                    // That's why, we chose 30 minutes for the limit to log a warning message, so that we can keep track of the frequency.
                    Logger.Log.PipExecutionIOStepDelayed(runnablePip.OperationContext, runnablePip.Description, runnablePip.Step.ToString(), PipExecutionIOStepDelayedLimitMin, (int)runnablePip.StepDuration.TotalMinutes);
                }

                runnablePip.Observer.EndStep(runnablePip);
            }

            // Store the duration
            m_pipExecutionStepCounters.AddToCounter(runnablePip.Step, duration);

            // Send an Executionlog event
            runnablePip.LogExecutionStepPerformance(runnablePip.Step, startTime, duration);

            if (IsDistributedWorker)
            {
                runnablePip.End();
                m_executionStepTracker.Transition(runnablePip.PipId, PipExecutionStep.None);

                // Distributed workers do not traverse state machine
                return;
            }

            // Release the worker resources if we are done executing
            runnablePip.AcquiredResourceWorker?.ReleaseResources(runnablePip, nextStep);

            // Pip may need to materialize inputs/outputs before the next step
            // depending on the configuration
            MaterializeOutputsNextIfNecessary(runnablePip, ref nextStep);

            // Transition to the next step
            runnablePip.Transition(nextStep);
            m_executionStepTracker.Transition(runnablePip.PipId, nextStep);

            // (a) Execute as inlined here, OR
            // (b) Enqueue the execution of the rest of the steps until another enqueue.
            await ExecuteAsyncOrEnqueue(runnablePip);
        }

        private void FlagSharedOpaqueOutputsOnCancellation(RunnablePip runnablePip)
        {
            Contract.Assert(runnablePip.IsCancelled);
            if (runnablePip is ProcessRunnablePip processRunnable)
            {
                FlagAndReturnScrubbableSharedOpaqueOutputs(runnablePip.Environment, processRunnable);
            }
        }

        /// <summary>
        /// Modifies the next step to one of the materialization steps if required
        /// </summary>
        private void MaterializeOutputsNextIfNecessary(RunnablePip runnablePip, ref PipExecutionStep nextStep)
        {
            if (!runnablePip.Result.HasValue)
            {
                return;
            }

            if (m_scheduleConfiguration.RequiredOutputMaterialization == RequiredOutputMaterialization.All &&
                runnablePip.PipType == PipType.SealDirectory)
            {
                // Seal directories do not need to be materialized when materializing all outputs. Seal directory outputs
                // are composed of other pip outputs which would necessarily be materialized if materializing all outputs
                return;
            }

            if (!runnablePip.Result.Value.Status.IndicatesNoOutput() &&
                PipArtifacts.CanProduceOutputs(runnablePip.PipType) &&
                runnablePip.Step != PipExecutionStep.MaterializeOutputs &&

                // Background output materialize happens after HandleResult (before Done) so that dependents are scheduled before attempting
                // to materialize outputs.
                (MaterializeOutputsInBackground ? nextStep == PipExecutionStep.Done : nextStep == PipExecutionStep.HandleResult) &&

                // Need to run materialize outputs are not materialized or if outputs are replicated to all workers
                (m_configuration.Distribution.ReplicateOutputsToWorkers()
                    || runnablePip.Result.Value.Status == PipResultStatus.NotMaterialized) &&
                RequiresPipOutputs(runnablePip.PipId.ToNodeId()))
            {
                if (AnyRemoteWorkers && m_configuration.Distribution.ReplicateOutputsToWorkers())
                {
                    runnablePip.SetWorker(m_allWorker);
                }
                else
                {
                    runnablePip.SetWorker(LocalWorker);
                }

                if (MaterializeOutputsInBackground)
                {
                    // Background output materialization should yield to other tasks since its not required
                    // unblock anything
                    runnablePip.ChangePriority(0);
                }

                // Prior to completing the pip and handling the result
                // materialize outputs for pips that require outputs to be materialized
                nextStep = PipExecutionStep.MaterializeOutputs;
            }
        }

        /// <summary>
        /// Decide which dispatcher queue needs to execute the given pip in the given step
        /// </summary>
        /// <remarks>
        /// If the result is <see cref="DispatcherKind.None"/>, the execution will be inlined.
        /// </remarks>
        private DispatcherKind DecideDispatcherKind(RunnablePip runnablePip)
        {
            switch (runnablePip.Step)
            {
                case PipExecutionStep.Start:
                    switch (runnablePip.PipType)
                    {
                        case PipType.SpecFile:
                        case PipType.Module:
                        case PipType.Value:
                            // These are fast to execute. They are inlined when they are scheduled during draining.
                            // However, if the queue has not been started draining, we should add them to the queue.
                            // SchedulePip is called as a part of 'schedule' phase.
                            // That's why, we should not inline executions of fast pips when the queue is not draining.
                            return m_pipQueue.IsDraining ? DispatcherKind.None : DispatcherKind.CPU;

                        case PipType.SealDirectory:
                            return DispatcherKind.SealDirs;

                        case PipType.WriteFile:
                        case PipType.CopyFile:
                        case PipType.Ipc:
                            return DispatcherKind.IO;

                        case PipType.Process:
                            var state = (ProcessMutablePipState)m_pipTable.GetMutable(runnablePip.PipId);
                            if (state.IsStartOrShutdown)
                            {
                                // service start and shutdown pips are noop, so they will be inlined if the queue is draining.
                                return m_pipQueue.IsDraining ? DispatcherKind.None : DispatcherKind.CPU;
                            }

                            return DispatcherKind.IO;

                        default:
                            throw Contract.AssertFailure(I($"Invalid pip type: '{runnablePip.PipType}'"));
                    }

                case PipExecutionStep.DelayedCacheLookup:
                    return DispatcherKind.DelayedCacheLookup;

                case PipExecutionStep.ChooseWorkerCacheLookup:
                    // First attempt should be inlined; if it does not acquire a worker, then it should be enqueued to ChooseWorkerCacheLookup queue.
                    return AnyRemoteWorkers && runnablePip.IsWaitingForWorker ? DispatcherKind.ChooseWorkerCacheLookup : DispatcherKind.None;

                case PipExecutionStep.CacheLookup:
                case PipExecutionStep.PostProcess:
                    // DispatcherKind.CacheLookup is mainly for CAS and VSTS resources.
                    // As we store cache entries in PostProcess, so it makes sense to process those in CacheLookup queue.
                    return DispatcherKind.CacheLookup;

                case PipExecutionStep.ChooseWorkerCpu:
                    return DispatcherKind.ChooseWorkerCpu;

                case PipExecutionStep.MaterializeInputs:
                    if (runnablePip.PipType == PipType.Ipc)
                    {
                        // Send IPC pips to light queue to materialize since they don't often materialize much
                        // and in the worst case they would only contend with other IPC pips
                        return DispatcherKind.Light;
                    }

                    return DispatcherKind.Materialize;

                case PipExecutionStep.MaterializeOutputs:
                    return DispatcherKind.Materialize;

                case PipExecutionStep.ExecuteProcess:
                case PipExecutionStep.ExecuteNonProcessPip:
                    return GetExecutionDispatcherKind(runnablePip);

                // INEXPENSIVE STEPS
                case PipExecutionStep.CheckIncrementalSkip:
                case PipExecutionStep.RunFromCache: // Just reports hashes and replay warnings (inline)
                case PipExecutionStep.Cancel:
                case PipExecutionStep.Skip:
                case PipExecutionStep.HandleResult:
                case PipExecutionStep.None:
                case PipExecutionStep.Done:
                    // Do not change the current queue and inline execution.
                    return DispatcherKind.None;

                default:
                    throw Contract.AssertFailure(I($"Invalid pip execution step: '{runnablePip.Step}'"));
            }
        }

        private DispatcherKind GetExecutionDispatcherKind(RunnablePip pip)
        {
            switch (pip.PipType)
            {
                case PipType.WriteFile:
                case PipType.CopyFile:
                    return DispatcherKind.IO;

                case PipType.Process:
                    return IsLightProcess(pip) ? DispatcherKind.Light : DispatcherKind.CPU;

                case PipType.Ipc:
                    return DispatcherKind.Light;

                case PipType.SealDirectory:
                    return DispatcherKind.SealDirs;

                case PipType.Value:
                case PipType.SpecFile:
                case PipType.Module:
                    return DispatcherKind.None;

                default:
                    throw Contract.AssertFailure(I($"Invalid pip type: '{pip.PipType}'"));
            }
        }

        private bool IsLightProcess(RunnablePip pip)
        {
            return pip.PipType == PipType.Process && ((m_pipTable.GetProcessOptions(pip.PipId) & Process.Options.IsLight) != 0);
        }

        /// <summary>
        /// Execute the given pip in the current step and return the next step
        /// </summary>
        /// <remarks>
        /// The state diagram for pip execution steps is in <see cref="PipExecutionStep"/> class.
        /// </remarks>
        private async Task<PipExecutionStep> ExecutePipStep(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip != null);
            Contract.Requires(runnablePip.Step != PipExecutionStep.Done && runnablePip.Step != PipExecutionStep.None);

            ProcessRunnablePip processRunnable = runnablePip as ProcessRunnablePip;
            var pipId = runnablePip.PipId;
            var pipType = runnablePip.PipType;
            var loggingContext = runnablePip.LoggingContext;
            var operationContext = runnablePip.OperationContext;
            var environment = runnablePip.Environment;
            var fileContentManager = environment.State.FileContentManager;
            var step = runnablePip.Step;
            var worker = runnablePip.Worker;

            // If schedule is terminating (e.g., StopOnFirstFailure), cancel the pip
            // as long as (i) 'start' step has been executed, (ii) the pip is in running state, and (iii) the pip has not been cancelled before.
            if (ShouldCancelPip(runnablePip))
            {
                return runnablePip.Cancel();
            }

            switch (step)
            {
                case PipExecutionStep.Start:
                {
                    var state = TryStartPip(runnablePip);
                    if (state == PipState.Skipped)
                    {
                        return PipExecutionStep.Skip;
                    }

                    Contract.Assert(state == PipState.Running, I($"Cannot start pip in state: {state}"));

                    if (pipType.IsMetaPip())
                    {
                        return PipExecutionStep.ExecuteNonProcessPip;
                    }

                    using (operationContext.StartOperation(PipExecutorCounter.HashSourceFileDependenciesDuration))
                    {
                        // Hash source file dependencies
                        var maybeHashed = await fileContentManager.TryHashSourceDependenciesAsync(runnablePip.Pip, operationContext);
                        if (!maybeHashed.Succeeded)
                        {
                            Logger.Log.PipFailedDueToSourceDependenciesCannotBeHashed(
                                loggingContext,
                                runnablePip.Description);
                            return runnablePip.SetPipResult(PipResultStatus.Failed);
                        }
                    }

                    // For module affinity, we need to set the preferred worker id. 
                    // This is intentionally put here after we hydrate the pip for the first time when accessing 
                    // runnablePip.Pip above for hashing dependencies. 
                    if (runnablePip.Pip.Provenance.ModuleId.IsValid && 
                        m_moduleWorkerMapping.TryGetValue(runnablePip.Pip.Provenance.ModuleId, out var tuple) &&
                        tuple.Workers.Count > 0)
                    {
                        runnablePip.PreferredWorkerId = (int)tuple.Workers[0].WorkerId;
                    }

                    switch (pipType)
                    {
                        case PipType.Process:
                            if (processRunnable.Process.IsStartOrShutdownKind)
                            {
                                // Service start and shutdown pips are noop in the scheduler.
                                // They will be run on demand by the service manager which is not tracked directly by the scheduler.
                                return runnablePip.SetPipResult(PipResult.CreateWithPointPerformanceInfo(PipResultStatus.Succeeded));
                            }

                            break;
                        case PipType.Ipc:
                            // Ensure IPC pips take priority over process pips when choosing worker
                            // NOTE: Since they don't require slots they would not be able to block
                            // processes from acquiring a worker
                            runnablePip.ChangePriority(IpcPipChooseWorkerPriority);

                            // IPC pips go to ChooseWorker before checking the incremental state
                            return PipExecutionStep.ChooseWorkerCpu;
                    }

                    return PipExecutionStep.CheckIncrementalSkip; // CopyFile, WriteFile, Process, SealDirectory pips

                }

                case PipExecutionStep.Cancel:
                {
                    // Make sure shared opaque outputs are flagged as such.
                    FlagSharedOpaqueOutputsOnCancellation(runnablePip);

                    Logger.Log.ScheduleCancelingPipSinceScheduleIsTerminating(
                        loggingContext,
                        runnablePip.Description);
                    return runnablePip.SetPipResult(PipResult.CreateWithPointPerformanceInfo(PipResultStatus.Canceled));
                }

                case PipExecutionStep.Skip:
                {
                    // We report skipped pips when all dependencies (failed or otherwise) complete.
                    // This has the side-effect that stack depth is bounded when a pip fails; ReportSkippedPip
                    // reports failure which is then handled in OnPipCompleted as part of the normal queue processing
                    // (rather than recursively abandoning dependents here).
                    LogEventWithPipProvenance(runnablePip, Logger.Log.SchedulePipFailedDueToFailedPrerequisite);
                    return runnablePip.SetPipResult(PipResult.Skipped);
                }

                case PipExecutionStep.MaterializeOutputs:
                {
                    if (m_configuration.Distribution.FireForgetMaterializeOutput && !AnyPendingPipsExceptMaterializeOutputs())
                    {
                        // There is no pips running anything except materializeOutputs.
                        m_schedulerCompletionExceptMaterializeOutputs.TrySetResult(true);
                    }

                    PipResultStatus materializationResult = await worker.MaterializeOutputsAsync(runnablePip);

                    var nextStep = processRunnable?.ExecutionResult != null
                        ? processRunnable.SetPipResult(processRunnable.ExecutionResult.CloneSealedWithResult(materializationResult))
                        : runnablePip.SetPipResult(materializationResult);

                    if (!MaterializeOutputsInBackground)
                    {
                        return nextStep;
                    }

                    if (materializationResult.IndicatesFailure())
                    {
                        m_hasFailures = true;
                    }
                    else
                    {
                        IncrementalSchedulingState?.PendingUpdates.MarkNodeMaterialized(runnablePip.PipId.ToNodeId());
                        Logger.Log.PipIsMarkedMaterialized(loggingContext, runnablePip.Description);
                    }

                    return PipExecutionStep.Done;
                }

                case PipExecutionStep.CheckIncrementalSkip:
                {
                    // Enable incremental scheduling when distributed build role is none, and
                    // dirty build is not used (forceSkipDependencies is false).
                    if (IsPipCleanMaterialized(pipId))
                    {
                        var maybeHashed = await fileContentManager.TryHashOutputsAsync(runnablePip.Pip, operationContext);
                        if (!maybeHashed.Succeeded)
                        {
                            if (maybeHashed.Failure is CancellationFailure)
                            {
                                Contract.Assert(loggingContext.ErrorWasLogged);
                            }
                            else
                            {
                                Logger.Log.PipFailedDueToOutputsCannotBeHashed(
                                    loggingContext,
                                    runnablePip.Description);
                            }
                        }
                        else
                        {
                            PipExecutionCounters.IncrementCounter(PipExecutorCounter.IncrementalSkipPipDueToCleanMaterialized);

                            if (runnablePip.Pip.PipType == PipType.Process)
                            {
                                PipExecutionCounters.IncrementCounter(PipExecutorCounter.IncrementalSkipProcessDueToCleanMaterialized);
                            }

                            Logger.Log.PipIsIncrementallySkippedDueToCleanMaterialized(loggingContext, runnablePip.Description);
                        }

                        return runnablePip.SetPipResult(PipResult.Create(
                            maybeHashed.Succeeded ? PipResultStatus.UpToDate : PipResultStatus.Failed,
                            runnablePip.StartTime));
                    }

                    if (m_scheduleConfiguration.ForceSkipDependencies != ForceSkipDependenciesMode.Disabled && m_mustExecuteNodesForDirtyBuild != null)
                    {
                        if (!m_mustExecuteNodesForDirtyBuild.Contains(pipId.ToNodeId()))
                        {
                            // When dirty build is enabled, we skip the scheduled pips whose outputs are present and are in the transitive dependency chain
                            // The skipped ones during execution are not explicitly scheduled pips at all.
                            return runnablePip.SetPipResult(PipResult.Create(
                                PipResultStatus.UpToDate,
                                runnablePip.StartTime));
                        }

                        using (PipExecutionCounters.StartStopwatch(PipExecutorCounter.HashProcessDependenciesDuration))
                        {
                            // The dependencies may have been skipped, so hash the processes inputs
                            var maybeHashed = await fileContentManager.TryHashDependenciesAsync(runnablePip.Pip, operationContext);
                            if (!maybeHashed.Succeeded)
                            {
                                if (!(maybeHashed.Failure is CancellationFailure))
                                {
                                    Logger.Log.PipFailedDueToDependenciesCannotBeHashed(
                                        loggingContext,
                                        runnablePip.Description);
                                }

                                return runnablePip.SetPipResult(PipResultStatus.Failed);
                            }
                        }
                    }

                    if (pipType == PipType.Process)
                    {
                        return m_configuration.Schedule.DelayedCacheLookupEnabled() ? PipExecutionStep.DelayedCacheLookup : PipExecutionStep.ChooseWorkerCacheLookup;
                    }
                    else
                    {
                        return PipExecutionStep.ExecuteNonProcessPip;
                    }
                }

                case PipExecutionStep.DelayedCacheLookup:
                {
                    return PipExecutionStep.ChooseWorkerCacheLookup;
                }

                case PipExecutionStep.ChooseWorkerCacheLookup:
                {
                    Contract.Assert(pipType == PipType.Process);
                    Contract.Assert(worker == null);

                    worker = await m_chooseWorkerCacheLookup.ChooseWorkerAsync(runnablePip);
                    if (worker == null)
                    {
                        // If none of the workers is available, enqueue again.
                        // We always want to choose a worker for the highest priority item. That's why, we enqueue again
                        return PipExecutionStep.ChooseWorkerCacheLookup;
                    }

                    worker.Transition(runnablePip.PipId, WorkerPipState.ChosenForCacheLookup);
                    runnablePip.SetWorker(worker);

                    return PipExecutionStep.CacheLookup;
                }

                case PipExecutionStep.ChooseWorkerCpu:
                {
                    Contract.Assert(pipType == PipType.Process || pipType == PipType.Ipc);
                    Contract.Assert(worker == null);

                    worker = await ChooseWorkerCpuAsync(runnablePip);
                    if (worker == null)
                    {
                        // If none of the workers is available, enqueue again.
                        // We always want to choose a worker for the highest priority item. That's why, we enqueue again
                        return PipExecutionStep.ChooseWorkerCpu;
                    }

                    worker.Transition(runnablePip.PipId, WorkerPipState.ChosenForExecution);
                    runnablePip.SetWorker(worker);
                    if (InputsLazilyMaterialized)
                    {
                        // Materialize inputs if lazy materialization is enabled or this is a distributed build
                        return PipExecutionStep.MaterializeInputs;
                    }

                    if (pipType == PipType.Process)
                    {
                        return PipExecutionStep.ExecuteProcess;
                    }

                    Contract.Assert(pipType == PipType.Ipc);
                    return PipExecutionStep.ExecuteNonProcessPip;
                }

                case PipExecutionStep.MaterializeInputs:
                {
                    Contract.Assert(pipType == PipType.Process || pipType == PipType.Ipc);

                    PipResultStatus materializationResult = await worker.MaterializeInputsAsync(runnablePip);
                    if (materializationResult.IndicatesFailure())
                    {
                        return runnablePip.SetPipResult(materializationResult);
                    }

                    worker.OnInputMaterializationCompletion(runnablePip.Pip, this);

                    return pipType == PipType.Process ?
                        PipExecutionStep.ExecuteProcess :
                        PipExecutionStep.ExecuteNonProcessPip;
                }

                case PipExecutionStep.ExecuteNonProcessPip:
                {
                    var pipResult = await ExecuteNonProcessPipAsync(runnablePip);

                    if (runnablePip.PipType == PipType.Ipc && runnablePip.Worker?.IsRemote == true)
                    {
                        PipExecutionCounters.IncrementCounter(PipExecutorCounter.IpcPipsExecutedRemotely);
                    }

                    return runnablePip.SetPipResult(pipResult);
                }

                case PipExecutionStep.CacheLookup:
                {
                    Contract.Assert(processRunnable != null);
                    Contract.Assert(worker != null);

                    var process = processRunnable.Process;
                    var pipScope = State.GetScope(process);
                    var cacheableProcess = pipScope.GetCacheableProcess(process, environment);

                    var tupleResult = await worker.CacheLookupAsync(
                        processRunnable,
                        pipScope,
                        cacheableProcess);

                    var cacheResult = tupleResult.Item1;
                    if (cacheResult == null)
                    {
                        Contract.Assert(tupleResult.Item2 == PipResultStatus.Canceled || loggingContext.ErrorWasLogged, "Error should have been logged for dependency pip.");
                        return processRunnable.SetPipResult(tupleResult.Item2);
                    }

                    HandleDeterminismProbe(loggingContext, environment, cacheResult, runnablePip.Description);

                    processRunnable.SetCacheableProcess(cacheableProcess);
                    processRunnable.SetCacheResult(cacheResult);

                    using (operationContext.StartOperation(PipExecutorCounter.ReportRemoteMetadataAndPathSetDuration))
                    {
                        // It only executes on master; but we still acquire the slot on the worker.
                        if (cacheResult.CanRunFromCache && worker.IsRemote)
                        {
                            var cacheHitData = cacheResult.GetCacheHitData();
                            m_pipTwoPhaseCache.ReportRemoteMetadataAndPathSet(
                                cacheHitData.Metadata,
                                cacheHitData.MetadataHash,
                                cacheHitData.PathSet,
                                cacheHitData.PathSetHash,
                                cacheResult.WeakFingerprint,
                                cacheHitData.StrongFingerprint,
                                isExecution: false,
                                process.PreservePathSetCasing);
                        }
                    }

                    if (cacheResult.CanRunFromCache)
                    {
                        // Always execute the process if the determinism probe is enabled.
                        // Pips that must be run due to non-determinism are NOT counted as cache misses.
                        if (!m_configuration.Cache.DeterminismProbe)
                        {
                            return PipExecutionStep.RunFromCache;
                        }
                    }
                    else if (m_configuration.Schedule.CacheOnly)
                    {
                        // CacheOnly mode only wants to perform cache lookups and skip execution for pips that are misses
                        environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipsSkippedExecutionDueToCacheOnly);
                        PipRuntimeInfo pipRuntimeInfo = GetPipRuntimeInfo(pipId);
                        pipRuntimeInfo.Transition(m_pipStateCounters, pipType, PipState.Skipped);
                        return PipExecutionStep.Skip;
                    }
                    else
                    {
                        environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipsExecutedDueToCacheMiss);
                    }

                    return PipExecutionStep.ChooseWorkerCpu;
                }

                case PipExecutionStep.RunFromCache:
                {
                    Contract.Assert(processRunnable != null);

                    Process process = (Process)processRunnable.Pip;
                    var pipScope = State.GetScope(process);
                    var executionResult = await PipExecutor.RunFromCacheWithWarningsAsync(operationContext, environment, pipScope, process, processRunnable.CacheResult, processRunnable.Description);

                    return processRunnable.SetPipResult(executionResult);
                }

                case PipExecutionStep.ExecuteProcess:
                {
                    MarkPipStartExecuting();

                    if (processRunnable.Weight > 1)
                    {
                        // Only log for pips with non-standard process weights
                        Logger.Log.ProcessPipProcessWeight(loggingContext, processRunnable.Description, processRunnable.Weight);
                    }

                    processRunnable.Executed = true;

                    var executionResult = await worker.ExecuteProcessAsync(processRunnable);

                    // Don't count service pips in process pip counters
                    if (!processRunnable.Process.IsStartOrShutdownKind && executionResult.PerformanceInformation != null)
                    {
                        var perfInfo = executionResult.PerformanceInformation;

                        try
                        {
                            m_groupedPipCounters.AddToCounters(processRunnable.Process,
                                new[]
                                {
                                        (PipCountersByGroup.IOReadBytes,  (long) perfInfo.IO.ReadCounters.TransferCount),
                                        (PipCountersByGroup.IOWriteBytes, (long) perfInfo.IO.WriteCounters.TransferCount)
                                },
                                new[] { (PipCountersByGroup.ExecuteProcessDuration, perfInfo.ProcessExecutionTime) }
                            );
                        }
                        catch (OverflowException ex)
                        {
                            Logger.Log.ExecutePipStepOverflowFailure(operationContext, ex.Message);

                            m_groupedPipCounters.AddToCounters(processRunnable.Process,
                                new[] { (PipCountersByGroup.IOReadBytes, 0L), (PipCountersByGroup.IOWriteBytes, 0L) },
                                new[] { (PipCountersByGroup.ExecuteProcessDuration, perfInfo.ProcessExecutionTime) }
                            );
                        }
                    }

                    // The pip was canceled due to retryable failure
                    if (executionResult.Result == PipResultStatus.Canceled && !IsTerminating)
                    {
                        Contract.Requires(executionResult.RetryInfo != null, $"Retry Information is required for all retry cases. IsTerminating: {m_scheduleTerminating}");
                        RetryReason? retryReason = executionResult.RetryInfo.RetryReason;

                        if (worker.IsLocal)
                        {
                            // Because the scheduler will re-run this pip, we have to nuke all outputs created under shared opaque directories
                            var sharedOpaqueOutputs = FlagAndReturnScrubbableSharedOpaqueOutputs(environment, processRunnable);
                            ScrubSharedOpaqueOutputs(sharedOpaqueOutputs);
                        }

                        // If it is a single machine or distributed build master
                        if (!IsDistributedBuild || IsDistributedMaster)
                        {
                            if (retryReason == RetryReason.ResourceExhaustion)
                            {
                                // Use the max of the observed memory and the worker's expected memory (multiplied with 1.25 to increase the expectations) for the pip
                                var expectedCounters = processRunnable.ExpectedMemoryCounters.Value;
                                var actualCounters = executionResult.PerformanceInformation?.MemoryCounters;
                                processRunnable.ExpectedMemoryCounters = ProcessMemoryCounters.CreateFromMb(
                                    peakWorkingSetMb: Math.Max((int)(expectedCounters.PeakWorkingSetMb * 1.25), actualCounters?.PeakWorkingSetMb ?? 0),
                                    averageWorkingSetMb: Math.Max((int)(expectedCounters.AverageWorkingSetMb * 1.25), actualCounters?.AverageWorkingSetMb ?? 0),
                                    peakCommitSizeMb: Math.Max((int)(expectedCounters.PeakCommitSizeMb * 1.25), actualCounters?.PeakCommitSizeMb ?? 0),
                                    averageCommitSizeMb: Math.Max((int)(expectedCounters.AverageCommitSizeMb * 1.25), actualCounters?.AverageCommitSizeMb ?? 0));

                                if (m_scheduleConfiguration.MaxRetriesDueToLowMemory.HasValue &&
                                    processRunnable.Performance.RetryCountDueToLowMemory == m_scheduleConfiguration.MaxRetriesDueToLowMemory)
                                {
                                    Logger.Log.ExcessivePipRetriesDueToLowMemory(operationContext, processRunnable.Description, processRunnable.Performance.RetryCountDueToLowMemory);
                                    return runnablePip.SetPipResult(PipResultStatus.Failed);
                                }
                                else
                                {
                                    Logger.Log.PipRetryDueToLowMemory(operationContext, processRunnable.Description, worker.DefaultWorkingSetMbPerProcess, expectedCounters.PeakWorkingSetMb, actualCounters?.PeakWorkingSetMb ?? 0);
                                }
                            }
                            else if (retryReason.IsPrepOrVmFailure())
                            {
                                if (processRunnable.Performance.RetryCountDueToRetryableFailures == m_scheduleConfiguration.MaxRetriesDueToRetryableFailures)
                                {
                                    Logger.Log.ExcessivePipRetriesDueToRetryableFailures(operationContext, processRunnable.Description,
                                        processRunnable.Performance.RetryCountDueToRetryableFailures, executionResult.RetryInfo.RetryReason.ToString());
                                    return runnablePip.SetPipResult(PipResultStatus.Failed);
                                }
                                else
                                {
                                    Logger.Log.PipRetryDueToRetryableFailures(operationContext, processRunnable.Description, retryReason.ToString());
                                }
                            }
                        }

                        return processRunnable.SetPipResult(executionResult.Result);
                    }

                    m_pipPropertyInfo.UpdatePipPropertyInfo(processRunnable, executionResult);
                    m_pipRetryInfo.UpdatePipRetryInfo(processRunnable, executionResult, PipExecutionCounters);

                    if (runnablePip.Worker?.IsRemote == true)
                    {
                        PipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessesExecutedRemotely);
                    }

                    if (m_configuration.Cache.DeterminismProbe && processRunnable.CacheResult.CanRunFromCache)
                    {
                        // Compare strong fingerprints between execution and cache hit for determinism probe
                        return CheckMatchForDeterminismProbe(processRunnable);
                    }

                    return PipExecutionStep.PostProcess;
                }

                case PipExecutionStep.PostProcess:
                {
                    var executionResult = processRunnable.ExecutionResult;

                    if (executionResult.PerformanceInformation != null)
                    {
                        var perfInfo = executionResult.PerformanceInformation;
                        m_perPipPerformanceInfoStore.AddPip(new PerProcessPipPerformanceInformation(
                            ref processRunnable,
                            (int)perfInfo.ProcessExecutionTime.TotalMilliseconds,
                            perfInfo.MemoryCounters.PeakWorkingSetMb,
                            (int)Math.Ceiling(perfInfo.IO.ReadCounters.TransferCount / BytesInMb),
                            (int)Math.Ceiling(perfInfo.IO.WriteCounters.TransferCount / BytesInMb)));
                    }

                    // Make sure all shared outputs are flagged as such.
                    // We need to do this even if the pip failed, so any writes under shared opaques are flagged anyway.
                    // This allows the scrubber to remove those files as well in the next run.
                    var start = DateTime.UtcNow;
                    var sharedOpaqueOutputs = FlagAndReturnScrubbableSharedOpaqueOutputs(environment, processRunnable);
                    LogSubPhaseDuration(operationContext, runnablePip.Pip, SandboxedProcessFactory.SandboxedProcessCounters.SchedulerPhaseFlaggingSharedOpaqueOutputs, DateTime.UtcNow.Subtract(start), $"(count: {sharedOpaqueOutputs.Count})");

                    // Set the process as executed. NOTE: We do this here rather than during ExecuteProcess to handle
                    // case of processes executed remotely
                    var pipScope = State.GetScope(processRunnable.Process);

                    bool pipIsSafeToCache = true;

                    IReadOnlyDictionary<FileArtifact, (FileMaterializationInfo, ReportedViolation)> allowedSameContentDoubleWriteViolations = null;

                    if (!IsDistributedWorker)
                    {
                        var expectedMemoryCounters = processRunnable.ExpectedMemoryCounters.Value;

                        int peakWorkingSetMb = executionResult.PerformanceInformation?.MemoryCounters.PeakWorkingSetMb ?? 0;
                        int averageWorkingSetMb = executionResult.PerformanceInformation?.MemoryCounters.AverageWorkingSetMb ?? 0;
                        int peakCommitSizeMb = executionResult.PerformanceInformation?.MemoryCounters.PeakCommitSizeMb ?? 0;
                        int averageCommitSizeMb = executionResult.PerformanceInformation?.MemoryCounters.AverageCommitSizeMb ?? 0;

                        try
                        {
                            Logger.Log.ProcessPipExecutionInfo(
                                operationContext,
                                runnablePip.Description,
                                executionResult.PerformanceInformation?.NumberOfProcesses ?? 0,
                                (processRunnable.HistoricPerfData?.DurationInMs ?? 0) / 1000.0,
                                executionResult.PerformanceInformation?.ProcessExecutionTime.TotalSeconds ?? 0,
                                executionResult.PerformanceInformation?.ProcessorsInPercents ?? 0,
                                processRunnable.Weight,
                                worker.DefaultWorkingSetMbPerProcess,
                                expectedMemoryCounters.PeakWorkingSetMb,
                                peakWorkingSetMb,
                                expectedMemoryCounters.AverageWorkingSetMb,
                                averageWorkingSetMb,
                                expectedMemoryCounters.PeakCommitSizeMb,
                                peakCommitSizeMb,
                                expectedMemoryCounters.AverageCommitSizeMb,
                                averageCommitSizeMb,
                                (int)(processRunnable.HistoricPerfData?.DiskIOInMB ?? 0),
                                (int)ByteSizeFormatter.ToMegabytes(executionResult.PerformanceInformation?.IO.GetAggregateIO().TransferCount ?? 0));

                            m_totalPeakWorkingSetMb += (ulong)peakWorkingSetMb;
                            m_totalAverageWorkingSetMb += (ulong)averageWorkingSetMb;

                            m_totalPeakCommitSizeMb += (ulong)peakCommitSizeMb;
                            m_totalAverageCommitSizeMb += (ulong)averageCommitSizeMb;
                        }
                        catch (OverflowException ex)
                        {
                            Logger.Log.ExecutePipStepOverflowFailure(operationContext, ex.Message);
                        }

                        // File violation analysis needs to happen on the master as it relies on
                        // graph-wide data such as detecting duplicate
                        start = DateTime.UtcNow;
                        executionResult = PipExecutor.AnalyzeFileAccessViolations(
                            operationContext,
                            environment,
                            pipScope,
                            executionResult,
                            processRunnable.Process,
                            out pipIsSafeToCache,
                            out allowedSameContentDoubleWriteViolations);
                        LogSubPhaseDuration(operationContext, runnablePip.Pip, SandboxedProcessCounters.SchedulerPhaseAnalyzingFileAccessViolations, DateTime.UtcNow.Subtract(start));

                        processRunnable.SetExecutionResult(executionResult);

                        if (executionResult.Result.IndicatesFailure())
                        {
                            // Dependency analysis failure. Bail out before performing post processing. This prevents
                            // the output from being cached as well as downstream pips from being run.
                            return processRunnable.SetPipResult(executionResult);
                        }
                    }

                    if (pipIsSafeToCache)
                    {
                        // The worker should only cache the pip if the violation analyzer allows it to.
                        executionResult = await worker.PostProcessAsync(processRunnable);
                    }
                    else
                    {
                        Logger.Log.ScheduleProcessNotStoredToCacheDueToFileMonitoringViolations(loggingContext, processRunnable.Description);
                    }

                    // If the result converged we should delete shared opaque outputs where the execution happened. On convergence, the result
                    // will be consumed from the already cached pip and the just produced outputs should be absent.
                    if (executionResult.Converged && worker.IsLocal)
                    {
                        ScrubSharedOpaqueOutputs(sharedOpaqueOutputs);
                    }

                    if (!IsDistributedWorker)
                    {
                        m_chooseWorkerCpu.ReportProcessExecutionOutputs(processRunnable, executionResult);

                        // If the cache converged outputs, we need to check for double writes again, since the configured policy may care about
                        // the content of the (final) outputs
                        if (executionResult.Converged)
                        {
                            start = DateTime.UtcNow;
                            executionResult = PipExecutor.AnalyzeDoubleWritesOnCacheConvergence(
                               operationContext,
                               environment,
                               pipScope,
                               executionResult,
                               processRunnable.Process,
                               allowedSameContentDoubleWriteViolations);
                            LogSubPhaseDuration(operationContext, runnablePip.Pip, SandboxedProcessCounters.SchedulerPhaseAnalyzingDoubleWrites, DateTime.UtcNow.Subtract(start));

                            processRunnable.SetExecutionResult(executionResult);

                            if (executionResult.Result.IndicatesFailure())
                            {
                                // Dependency analysis failure. Even though the pip is already cached, we got a cache converged event, so
                                // it is safe for other downstream pips to consume the cached result. However, some double writes were found based
                                // on the configured policy, so we fail the build
                                return processRunnable.SetPipResult(executionResult);
                            }
                        }
                    }

                    if (runnablePip.Worker?.IsRemote == true)
                    {
                        m_pipTwoPhaseCache.ReportRemoteMetadataAndPathSet(
                            executionResult.PipCacheDescriptorV2Metadata,
                            executionResult.TwoPhaseCachingInfo?.CacheEntry.MetadataHash,
                            executionResult.PathSet,
                            executionResult.TwoPhaseCachingInfo?.PathSetHash,
                            executionResult.TwoPhaseCachingInfo?.WeakFingerprint,
                            executionResult.TwoPhaseCachingInfo?.StrongFingerprint,
                            isExecution: !executionResult.Converged,
                            preservePathCasing: processRunnable.Process.PreservePathSetCasing);
                    }

                    // Output content is reported here to ensure that it happens both on worker executing PostProcess and
                    // master which called worker to execute post process.
                    start = DateTime.UtcNow;
                    PipExecutor.ReportExecutionResultOutputContent(
                        operationContext,
                        environment,
                        processRunnable.Pip.SemiStableHash,
                        executionResult,
                        processRunnable.Process.RewritePolicy.ImpliesDoubleWriteIsWarning());
                    LogSubPhaseDuration(operationContext, runnablePip.Pip, SandboxedProcessCounters.SchedulerPhaseReportingOutputContent, DateTime.UtcNow.Subtract(start), $"(num outputs: {executionResult.OutputContent.Length})");
                    return processRunnable.SetPipResult(executionResult);
                }

                case PipExecutionStep.HandleResult:
                    await OnPipCompleted(runnablePip);
                    return PipExecutionStep.Done;

                default:
                    throw Contract.AssertFailure(I($"Do not know how to run this pip step: '{step}'"));
            }
        }

        private bool ShouldCancelPip(RunnablePip runnablePip)
        {
            return IsTerminating && runnablePip.Step != PipExecutionStep.Start && GetPipRuntimeInfo(runnablePip.PipId).State == PipState.Running && !runnablePip.IsCancelled;
        }

        private List<string> FlagAndReturnScrubbableSharedOpaqueOutputs(IPipExecutionEnvironment environment, ProcessRunnablePip process)
        {
            List<string> outputPaths = new List<string>();

            // Select all declared output files that are not source rewrites (and therefore scrubbable, we don't want to flag what was a source file as a shared opaque
            // since we don't want to delete it next time)
            foreach (var fileArtifact in process.Process.FileOutputs.Where(fa => !fa.IsUndeclaredFileRewrite))
            {
                if (MakeSharedOpaqueOutputIfNeeded(fileArtifact.Path))
                {
                    outputPaths.Add(fileArtifact.Path.ToString(Context.PathTable));
                }
            }

            // The shared dynamic accesses can be null when the pip failed on preparation, in which case it didn't run at all, so there is
            // nothing to flag
            if (process.ExecutionResult?.SharedDynamicDirectoryWriteAccesses != null && !environment.Configuration.Sandbox.UnsafeSandboxConfiguration.SkipFlaggingSharedOpaqueOutputs())
            {
                // Directory outputs are reported only when the pip is successful. So we need to rely on the raw shared dynamic write accesses,
                // since flagging also happens on failed pips
                foreach (IReadOnlyCollection<FileArtifactWithAttributes> writesPerSharedOpaque in process.ExecutionResult.SharedDynamicDirectoryWriteAccesses.Values)
                {
                    // Only add the files that are not source rewrites (and therefore scrubbable, we don't want to flag what was a source file as a shared opaque
                    // since we don't want to delete it next time)
                    foreach (FileArtifactWithAttributes writeInPath in writesPerSharedOpaque.Where(fa => !fa.IsUndeclaredFileRewrite))
                    {
                        var path = writeInPath.Path.ToString(environment.Context.PathTable);
                        SharedOpaqueOutputHelper.EnforceFileIsSharedOpaqueOutput(path);
                        outputPaths.Add(path);
                    }
                }
            }

            return outputPaths;
        }

        private void ScrubSharedOpaqueOutputs(List<string> outputs)
        {
            outputs.ForEach(o => FileUtilities.DeleteFile(o));
        }

        private static void HandleDeterminismProbe(
            LoggingContext loggingContext,
            IPipExecutionEnvironment environment,
            RunnableFromCacheResult cacheResult,
            string pipDescription)
        {
            // The purpose of the determinism probe is to identify nondeterministic PIPs. It works by leveraging the two phase cache,
            // which is highly effective at revealing nondeterminism. As part of its routine interaction with this cache, BuildXL will
            // sometimes encounter a collision when it attempts to publish an entry into the cache because it differs from the entry that
            // is already present. When such a collision occurs, BuildXL chooses to use the entry from the cache rather than the entry it
            // is attempting to publish. Its choice to use the existing entry is key to converging with what is in the cache. "Determinism
            // recovery from cache" is the term coined to describe this behavior, and it provides a big win for downstream PIPs. By not
            // publishing to the cache, BuildXL ensures that a downstream PIP has the same input because the cached output from the upstream
            // PIP remains unchanged. This is essential for the determinism probe to work beyond the first PIPs.
            //
            // To illustrate, consider a C++ compiler that produces the same .obj file each time it compiles unchanged input.  This is
            // an example of a deterministic tool. The first time the compiler runs, BuildXL publishes the .obj directly into the cache.
            // During its next run, BuildXL determines the C++ compiler is runnable from cache (via fingerprinting and cache hits) and
            // consequently does not rerun the compiler, instead using the cached .obj file.
            //
            // Now, consider BuildXL's behavior when the probe is enabled. It tells BuildXL to ignore the fact that the compiler is
            // runnable from cache, thereby causing the compiler to run again. BuildXL then attempts to publish the .obj file into the
            // cache. Because the tool is deterministic, there will be no cache collision, and nothing for the probe to do.
            //
            // Contrast this with a C++ compiler that produces a different .obj file each time it compiles -- even when its input
            // is unchanged.  This is an example of a nondeterministic tool. In this case, the probe will again tell BuildXL to
            // disregard the fact that the compiler is runnable from cache, causing the the compiler to run again. In this case,
            // BuildXL's attempt to publish into cache results in a collision because the compiler is nondeterministic.  This
            // collision triggers the determinism probe to capture the name of the conflicting file and the tool that produced
            // it (C++ compiler). It then logs this information as an instance of nondeterminism.
            //
            // Executive summary:
            //  The determinism probe is:
            //    - Extremely simple, leveraging BuildXL's routine interactions with the two phase cache.
            //    - Active only when enabled via a command line parameter and, even then, is inert until a cache collision occurs.
            //    - Dependent on an existing cache. It triggers off of collisions that occur when BuildXL attempts to
            //      publish an entry into the cache that conflicts with an existing entry.
            //    - Forcing a cache race (the reason for cache collisions in normal use) to get a second running of the
            //      tools without actually running them twice locally. The prior run comes from the "cache" thus making this both faster
            //      (only one run) and more flexible (check across machines, different users, etc when using a working shared cache).
            //    - Provides significant improvement in performance relative to checking for determinism by manually running the tool twice.
            if (!cacheResult.CanRunFromCache && environment.Configuration.Cache.DeterminismProbe)
            {
                // Encountered a process that cannot run from cache, noteworthy because it prevents probing for determinism.
                environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipDeterminismProbeProcessCannotRunFromCache);

                Logger.Log.DeterminismProbeEncounteredProcessThatCannotRunFromCache(loggingContext, pipDescription);
            }
        }

        private PipExecutionStep CheckMatchForDeterminismProbe(ProcessRunnablePip processRunnable)
        {
            Contract.Requires(processRunnable.CacheResult.CanRunFromCache);

            var process = processRunnable.Process;
            var processFiles = process.GetCacheableOutputs().ToList();
            var processFilesInfo = processFiles.ToDictionary(t => t, t => null as FileMaterializationInfo?);
            var loggingContext = processRunnable.LoggingContext;
            var executionResult = processRunnable.ExecutionResult;
            var executionCachingInfo = executionResult.TwoPhaseCachingInfo;
            var cacheHitData = processRunnable.CacheResult.GetCacheHitData();
            var pipDescription = processRunnable.Description;
            var outputContent = executionResult.OutputContent;

            // Log pip failures.
            // This pip behaves nondeterministically. It is currently failing but its presence in the cache indicates a prior success.
            if (executionResult.Result != PipResultStatus.Succeeded)
            {
                Logger.Log.DeterminismProbeEncounteredPipFailure(
                    loggingContext,
                    pipDescription);
                return PipExecutionStep.PostProcess;
            }

            // The pip was cacheable originally (and checked for a cache hit) but uncacheable during this build
            if (executionResult.TwoPhaseCachingInfo == null)
            {
                Logger.Log.DeterminismProbeEncounteredUncacheablePip(
                    loggingContext,
                    pipDescription);
                return PipExecutionStep.PostProcess;
            }

            // Log strong fingerprint inconsistencies.
            // When the determinism probe is enabled, strong fingerprint mismatches occur when BuildXL runs PIPs that have inconsistent input.
            // This inconsistency is exposed because the determinism probe intentionally causes re-execution of PIPs that BuildXL would normally
            // not execute.
            if (executionCachingInfo.StrongFingerprint != cacheHitData.StrongFingerprint)
            {
                Logger.Log.DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch(
                    loggingContext,
                    pipDescription,
                    cacheHitData.StrongFingerprint.Hash.ToHex(),
                    cacheHitData.PathSetHash.ToHex(),
                    executionCachingInfo.StrongFingerprint.Hash.ToHex(),
                    executionCachingInfo.PathSetHash.ToHex()
                );
            }

            // The tables below are snapshots of actual hash content captured when BuildXL invoked the C++ compiler to build HelloWorld.cpp.
            // The table on the left contains hashes of the two files that were just created by the compiler.  The table on the right, their counterparts in the cache.
            //
            //         Files output by the the compiler                                               Cache hits (Files cached during a previous run of the compiler)
            //         --------------------------------                                                --------------------------------------------------------------
            // Slot 0: VSO0: 0E7A6E2773985FBDBAC38BCE94855E44E38B8CC383BBB4C7AC74918549C62BD100        VSO0: 50A24D5D3FF0062440B286A43AC7396CAF75F15AF4CCF2F11ECF9F8DD200100D00
            // Slot 1: VSO0: D46949208400260BB9127488CA6AFC6AE0B92E8F745E70AE5099AF0C86415FC600        VSO0: D46949208400260BB9127488CA6AFC6AE0B92E8F745E70AE5099AF0C86415FC600
            //
            // HelloWorld.nativeCodeAnalysis.Xml == VS0: D46949208400260BB9127488CA6AFC6AE0B92E8F745E70AE5099AF0C86415FC600
            // HelloWorld.obj (just created)     == VS0: 0E7A6E2773985FBDBAC38BCE94855E44E38B8CC383BBB4C7AC74918549C62BD100
            // HelloWorld.obj (from cache)       == VS0: 50A24D5D3FF0062440B286A43AC7396CAF75F15AF4CCF2F11ECF9F8DD200100D00
            //
            // The C++ compiler is running nondeterministicly, producing a different .obj file each time it compiles HelloWorld.cpp even though this file is unchanged.
            // This leads to the differences in the first slot of the tables. The hashes in the second slot, however, are identical.  These hashes correspond to the XML file
            // produced by the PREFast static code analysis tool. It is typically deterministic.
            //
            // The code below iterates through the slots of these tables, comparing the hashes.  For each mismatch, the name of the file associated with the hash is pulled from the
            // hash/fileArtifact tuple contained in cacheHitData.  This information is then logged along with the pip.
            //
            // Noteworthy assumptions used in the code below:
            //
            // 1. The code assumes the number of files output by the process (process.GetCacheableOutputs()) equals the number of files that were cached during its prior execution.
            //    This assumption is asserted below, preventing the index from exceeding the bounds of the processFiles list in the loop below. This assumptions does not include the
            // content of opaque directories, if any.
            // 2. The code assumes the same ordering of files in the list of process files, cache hit files, and execution files. This assumption is asserted in the code contracts
            //    within the loop below.

            // Number of static outputs found outside of Opaque directories

            Contract.Assert(
                processFiles.Count == cacheHitData.CachedArtifactContentHashes.Length - cacheHitData.DynamicDirectoryContents.Sum(opaque => opaque.Length),
                "Count of files output by the current run of the process differs from the count of files cached during prior run"
            );

            for (int outputIndex = 0; outputIndex < processFiles.Count; outputIndex++)
            {
                var artifactContent = cacheHitData.CachedArtifactContentHashes[outputIndex];
                var executionFile = processFiles[outputIndex];
                var executionContent = outputContent[outputIndex];

                // Ensure files match
                Contract.Assert(artifactContent.fileArtifact == executionFile);
                Contract.Assert(executionContent.fileArtifact == executionFile);

                ContentHash cacheHitHash = artifactContent.fileMaterializationInfo.Hash; // hash from artifact in cache
                ContentHash executionHash = executionContent.fileInfo.Hash; // hash from artifact that was just produced from this pip

                processFilesInfo[executionFile] = executionContent.fileInfo;

                // Compare the hashes.  A mismatch is considered nondeterminism.
                if (cacheHitHash == executionHash)
                {
                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessPipDeterminismProbeSameFiles);
                }
                else
                {
                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessPipDeterminismProbeDifferentFiles);

                    // Log this instance of nondeterminism, including the name of the file associated with the mismatched hash.
                    Logger.Log.DeterminismProbeEncounteredNondeterministicOutput(
                        loggingContext,
                        pipDescription,
                        artifactContent.fileArtifact.Path.ToString(Context.PathTable),
                        cacheHitHash.ToHex(),
                        executionHash.ToHex());
                }
            }

            Contract.Assert(
                cacheHitData.DynamicDirectoryContents.Length == process.DirectoryOutputs.Length,
                "Count of directory outputs in the cache hit differs from the list of directory outputs on the Process pip"
            );

            Contract.Assert(
                executionResult.DirectoryOutputs.Length == process.DirectoryOutputs.Length,
                "Count of directory outputs in the execution differs from the list of directory outputs on the Process pip"
            );

            int offset = processFiles.Count;
            for (int outputDirectoryIndex = 0; outputDirectoryIndex < process.DirectoryOutputs.Length; ++outputDirectoryIndex)
            {
                var cacheHitContents = cacheHitData.DynamicDirectoryContents[outputDirectoryIndex];
                var executionContents = executionResult.DirectoryOutputs[outputDirectoryIndex];
                var fileArtifactArray = executionContents.fileArtifactArray.Select(faa => faa.ToFileArtifact()).ToReadOnlyArray();

                var outputs = new List<(FileArtifact fileArtifact, FileMaterializationInfo fileInfo)>();

                foreach (var fileArtifactWithAttributes in executionContents.fileArtifactArray)
                {
                    var fileArtifact = fileArtifactWithAttributes.ToFileArtifact();
                    if (processFilesInfo.TryGetValue(fileArtifact, out var value))
                    {
                        outputs.Add((fileArtifact, value.Value));
                    }
                    else
                    {
                        outputs.Add((fileArtifact, outputContent[offset++].fileInfo));
                    }
                }

                var cacheHitFiles = Enumerable.Range(0, cacheHitContents.Length).ToDictionary(i => cacheHitContents[i].fileArtifact, i => cacheHitContents[i]);
                var executionFiles = Enumerable.Range(0, cacheHitContents.Length).ToDictionary(i => fileArtifactArray[i], i => outputs[i]);

                var intersection = cacheHitFiles.Keys.Where(t => executionFiles.ContainsKey(t)).ToHashSet();

                foreach (var fileArtifact in intersection)
                {
                    var cacheHitFile = cacheHitFiles[fileArtifact];
                    var executionFile = executionFiles[fileArtifact];

                    Contract.Assert(cacheHitFile.fileArtifact == fileArtifact);
                    Contract.Assert(executionFile.fileArtifact == fileArtifact);

                    if (cacheHitFile.fileMaterializationInfo.Hash != executionFile.fileInfo.Hash)
                    {
                        PipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessPipDeterminismProbeDifferentFiles);

                        Logger.Log.DeterminismProbeEncounteredNondeterministicDirectoryOutput(
                            loggingContext,
                            pipDescription,
                            process.DirectoryOutputs[outputDirectoryIndex].Path.ToString(Context.PathTable),
                            fileArtifact.Path.ToString(Context.PathTable),
                            cacheHitFile.fileMaterializationInfo.Hash.ToHex(),
                            executionFile.fileInfo.Hash.ToHex());
                    }
                }

                if (intersection.Count == cacheHitFiles.Count && intersection.Count == executionFiles.Count)
                {
                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessPipDeterminismProbeSameDirectories);
                }
                else
                {
                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessPipDeterminismProbeDifferentDirectories);

                    var cacheHitOnly = cacheHitContents.Select(t => t.fileArtifact).Except(intersection);
                    var executionOnly = fileArtifactArray.Except(intersection);

                    Logger.Log.DeterminismProbeEncounteredOutputDirectoryDifferentFiles(
                        loggingContext,
                        pipDescription,
                        process.DirectoryOutputs[outputDirectoryIndex].Path.ToString(Context.PathTable),
                        string.Join("", cacheHitOnly.Select(f => f.Path.ToString(Context.PathTable)).Select(t => $"\t{t}\n")),
                        string.Join("", executionOnly.Select(f => f.Path.ToString(Context.PathTable)).Select(t => $"\t{t}\n"))
                    );

                }

            }

            return PipExecutionStep.RunFromCache;
        }

        private PipState? TryStartPip(RunnablePip runnablePip)
        {
            if (Interlocked.CompareExchange(ref m_firstPip, 1, 0) == 0)
            {
                // Time to first pip only has meaning if we know when the process started
                if (m_processStartTimeUtc.HasValue)
                {
                    LogStatistic(
                        m_executePhaseLoggingContext,
                        Statistics.TimeToFirstPipMs,
                        (int)(DateTime.UtcNow - m_processStartTimeUtc.Value).TotalMilliseconds);
                }
            }

            PipId pipId = runnablePip.PipId;
            PipType pipType = runnablePip.PipType;
            PipRuntimeInfo pipRuntimeInfo = GetPipRuntimeInfo(pipId);
            PipState state = pipRuntimeInfo.State;

            m_executionStepTracker.Transition(pipId, PipExecutionStep.Start);

            if (state != PipState.Skipped)
            {
                pipRuntimeInfo.Transition(m_pipStateCounters, pipType, PipState.Running);
            }

            // PipState is either Skipped or Running at this point
            state = pipRuntimeInfo.State;
            Contract.Assume(state == PipState.Skipped || state == PipState.Running);

            return state;
        }

        private async Task<PipResult> ExecuteNonProcessPipAsync(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.Pip != null);
            Contract.Requires(runnablePip.OperationContext.IsValid);
            Contract.Requires(runnablePip.Environment != null);

            var pip = runnablePip.Pip;
            var operationContext = runnablePip.OperationContext;
            var environment = runnablePip.Environment;

            switch (runnablePip.PipType)
            {
                case PipType.SealDirectory:
                    // SealDirectory pips are also scheduler internal. Once completed, we can unblock consumers of the corresponding DirectoryArtifact
                    // and mark the contained paths as immutable (thus no longer requiring a rewrite count).
                    return ExecuteSealDirectoryPip(operationContext, environment, (SealDirectory)pip);

                case PipType.Value:
                case PipType.SpecFile:
                case PipType.Module:
                    // Value, specfile, and module pips are noop.
                    return PipResult.CreateWithPointPerformanceInfo(PipResultStatus.Succeeded);

                case PipType.WriteFile:
                    // Don't materialize eagerly (this is handled by the MaterializeOutputs step)
                    return
                        await
                            PipExecutor.ExecuteWriteFileAsync(
                                operationContext,
                                environment,
                                (WriteFile)pip,
                                materializeOutputs: !m_configuration.Schedule.EnableLazyWriteFileMaterialization);

                case PipType.CopyFile:
                    // Don't materialize eagerly (this is handled by the MaterializeOutputs step)
                    return await PipExecutor.ExecuteCopyFileAsync(operationContext, environment, (CopyFile)pip, materializeOutputs: false);

                case PipType.Ipc:
                    var result = await runnablePip.Worker.ExecuteIpcAsync(runnablePip);
                    if (!result.Status.IndicatesFailure())
                    {
                        // Output content is reported here to ensure that it happens both on worker executing IPC pip and
                        // master which called worker to execute IPC pip.
                        PipExecutor.ReportExecutionResultOutputContent(
                            runnablePip.OperationContext,
                            runnablePip.Environment,
                            runnablePip.Pip.SemiStableHash,
                            runnablePip.ExecutionResult);
                    }

                    return result;

                default:
                    throw Contract.AssertFailure("Do not know how to run pip " + pip);
            }
        }

        /// <summary>
        /// Returns whether a node is explicitly scheduled.
        /// </summary>
        /// <remarks>
        /// All nodes are explicitly scheduled unless a filter is applied that does not match the node.
        /// </remarks>
        private bool RequiresPipOutputs(NodeId node)
        {
            // For minimal required materialization, no pip's outputs are required.
            if (m_scheduleConfiguration.RequiredOutputMaterialization == RequiredOutputMaterialization.Minimal)
            {
                return false;
            }

            if (m_scheduleConfiguration.RequiredOutputMaterialization == RequiredOutputMaterialization.All)
            {
                return true;
            }

            // When all nodes are scheduled, the collection is null and all nodes are matched
            if (m_explicitlyScheduledNodes == null)
            {
                return true;
            }

            // Otherwise the node must be checked
            return m_explicitlyScheduledNodes.Contains(node);
        }

        /// <summary>
        /// Chooses a worker to execute the process or IPC pips
        /// </summary>
        /// <remarks>
        /// Do not need to be thread-safe. The concurrency degree of the queue is 1.
        /// </remarks>
        private async Task<Worker> ChooseWorkerCpuAsync(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.PipType == PipType.Process || runnablePip.PipType == PipType.Ipc);

            using (PipExecutionCounters.StartStopwatch(PipExecutorCounter.ChooseWorkerCpuDuration))
            {
                var runnableProcess = runnablePip as ProcessRunnablePip;
                // If there is no historic perf data associated with the process,
                // lookup the historic perf data table.
                if (runnableProcess != null && runnableProcess.HistoricPerfData == null)
                {
                    var perfData = HistoricPerfDataTable[runnableProcess.Process.SemiStableHash];
                    if (perfData != ProcessPipHistoricPerfData.Empty)
                    {
                        runnableProcess.HistoricPerfData = perfData;

                        var memoryCounters = perfData.MemoryCounters;
                        if (memoryCounters.AverageWorkingSetMb == 0 || memoryCounters.PeakWorkingSetMb == 0)
                        {
                            Interlocked.Increment(ref m_historicPerfDataZeroMemoryHits);
                        }
                        else
                        {
                            Interlocked.Increment(ref m_historicPerfDataNonZeroMemoryHits);
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref m_historicPerfDataMisses);
                    }
                }

                // Find the estimated setup time for the pip on each builder.
                return await m_chooseWorkerCpu.ChooseWorkerAsync(runnablePip);
            }
        }

        /// <inheritdoc />
        public PipExecutionContext Context { get; }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IPipExecutionEnvironment.MaterializeOutputsInBackground => MaterializeOutputsInBackground;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IPipExecutionEnvironment.IsTerminating => IsTerminating;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IPipExecutionEnvironment.InputsLazilyMaterialized => InputsLazilyMaterialized;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        PipTable IPipExecutionEnvironment.PipTable => m_pipTable;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        PipContentFingerprinter IPipExecutionEnvironment.ContentFingerprinter => m_pipContentFingerprinter;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        PipFragmentRenderer IPipExecutionEnvironment.PipFragmentRenderer => m_pipFragmentRenderer;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        IIpcProvider IPipExecutionEnvironment.IpcProvider => m_ipcProvider;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        PluginManager IPipExecutionEnvironment.PluginManager => m_pluginManager;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        IConfiguration IPipExecutionEnvironment.Configuration => m_configuration;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        IReadOnlyDictionary<string, string> IPipExecutionEnvironment.RootMappings => m_rootMappings;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        ISandboxConnection IPipExecutionEnvironment.SandboxConnection => !MacOsSandboxingEnabled ? null : SandboxConnection;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IPipExecutionEnvironment.TryGetProducerPip(in FileOrDirectoryArtifact artifact, out PipId producer)
        {
            producer = PipGraph.TryGetProducer(in artifact);
            return producer.IsValid;
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IPipExecutionEnvironment.IsReachableFrom(PipId from, PipId to)
        {
            return PipGraph.IsReachableFrom(from: from.ToNodeId(), to: to.ToNodeId());
        }

        /// <summary>
        /// Content and metadata cache for prior pip outputs.
        /// </summary>
        public EngineCache Cache { get; }

        /// <inheritdoc />
        public LocalDiskContentStore LocalDiskContentStore => m_localDiskContentStore;

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        IFileMonitoringViolationAnalyzer IPipExecutionEnvironment.FileMonitoringViolationAnalyzer => m_fileMonitoringViolationAnalyzer;

        /// <inheritdoc />
        public int GetPipPriority(PipId pipId)
        {
            Contract.Requires(pipId.IsValid);
            Contract.Assert(IsInitialized);

            var pipState = m_pipTable.GetMutable(pipId);

            if (pipState.PipType.IsMetaPip())
            {
                // Meta pips are used for reporting and should run ASAP
                // Give them a maximum priority.
                return MaxInitialPipPriority;
            }

            return GetPipRuntimeInfo(pipId).Priority;
        }

        /// <inheritdoc />
        public DirectoryTranslator DirectoryTranslator { get; }

        /// <summary>
        /// Gets the execution information for the producer pip of the given file.
        /// </summary>
        public string GetProducerInfoForFailedMaterializeFile(in FileArtifact artifact)
        {
            var producer = m_fileContentManager.GetDeclaredProducer(artifact);

            RunnablePipPerformanceInfo perfInfo = m_runnablePipPerformance[producer.PipId];

            PipExecutionStep step = perfInfo.IsExecuted ? PipExecutionStep.ExecuteProcess : PipExecutionStep.RunFromCache;

            var workerId = perfInfo.Workers.Value[(int)step];
            var worker = m_workers[(int)workerId];
            bool isWorkerReleasedEarly = worker.WorkerEarlyReleasedTime != null;

            PipExecutionCounters.IncrementCounter(PipExecutorCounter.NumFilesFailedToMaterialize);
            if (isWorkerReleasedEarly)
            {
                PipExecutionCounters.IncrementCounter(PipExecutorCounter.NumFilesFailedToMaterializeDueToEarlyWorkerRelease);
            }

            string whenWorkerReleased = isWorkerReleasedEarly ?
                $"UTC {worker.WorkerEarlyReleasedTime.Value.ToLongTimeString()} ({(DateTime.UtcNow - worker.WorkerEarlyReleasedTime.Value).TotalMinutes.ToString("0.0")} minutes ago)" :
                "N/A";

            return $"{producer.FormattedSemiStableHash} {step} on Worker #{workerId} - {worker.Name} ({worker.Status} - WhenReleased: {whenWorkerReleased})";
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IPipExecutionEnvironment.IsSourceSealedDirectory(
            DirectoryArtifact directoryArtifact,
            out bool allDirectories,
            out ReadOnlyArray<StringId> pattern)
        {
            Contract.Requires(directoryArtifact.IsValid);
            pattern = ReadOnlyArray<StringId>.Empty;
            var sealDirectoryKind = GetSealDirectoryKind(directoryArtifact);

            if (sealDirectoryKind.IsSourceSeal())
            {
                pattern = GetSourceSealDirectoryPatterns(directoryArtifact);
            }

            switch (sealDirectoryKind)
            {
                case SealDirectoryKind.SourceAllDirectories:
                    allDirectories = true;
                    return true;
                case SealDirectoryKind.SourceTopDirectoryOnly:
                    allDirectories = false;
                    return true;
                default:
                    allDirectories = false;
                    return false;
            }
        }

        /// <inheritdoc />
        public IEnumerable<Pip> GetServicePipClients(PipId servicePipId) => PipGraph.GetServicePipClients(servicePipId);

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        void IPipExecutionEnvironment.ReportWarnings(bool fromCache, int count)
        {
            Contract.Requires(count > 0);
            if (fromCache)
            {
                Interlocked.Increment(ref m_numPipsWithWarningsFromCache);
                Interlocked.Add(ref m_numWarningsFromCache, count);
            }
            else
            {
                Interlocked.Increment(ref m_numPipsWithWarnings);
                Interlocked.Add(ref m_numWarnings, count);
            }
        }

        #region Critical Path Logging

        private void LogCriticalPath(Dictionary<string, long> statistics, [CanBeNull] BuildSummary buildSummary)
        {
            int currentCriticalPathTailPipIdValue;
            PipRuntimeInfo criticalPathRuntimeInfo;

            List<(PipRuntimeInfo pipRunTimeInfo, PipId pipId)> criticalPath = new List<(PipRuntimeInfo pipRunTimeInfo, PipId pipId)>();

            if (TryGetCriticalPathTailRuntimeInfo(out currentCriticalPathTailPipIdValue, out criticalPathRuntimeInfo))
            {
                PipExecutionCounters.AddToCounter(PipExecutorCounter.CriticalPathDuration, TimeSpan.FromMilliseconds(criticalPathRuntimeInfo.CriticalPathDurationMs));

                PipId pipId = new PipId(unchecked((uint)currentCriticalPathTailPipIdValue));
                criticalPath.Add((criticalPathRuntimeInfo, pipId));

                long exeDurationCriticalPathMs = criticalPathRuntimeInfo.ProcessExecuteTimeMs;

                while (true)
                {
                    criticalPathRuntimeInfo = null;
                    foreach (var dependencyEdge in ScheduledGraph.GetIncomingEdges(pipId.ToNodeId()))
                    {
                        var dependencyRuntimeInfo = GetPipRuntimeInfo(dependencyEdge.OtherNode);
                        if (dependencyRuntimeInfo.CriticalPathDurationMs >= (criticalPathRuntimeInfo?.CriticalPathDurationMs ?? 0))
                        {
                            criticalPathRuntimeInfo = dependencyRuntimeInfo;
                            pipId = dependencyEdge.OtherNode.ToPipId();
                        }
                    }

                    if (criticalPathRuntimeInfo != null)
                    {
                        criticalPath.Add((criticalPathRuntimeInfo, pipId));

                        exeDurationCriticalPathMs += criticalPathRuntimeInfo.ProcessExecuteTimeMs;
                    }
                    else
                    {
                        break;
                    }
                }

                IList<long> totalMasterQueueDurations = new long[(int)DispatcherKind.Materialize + 1];
                IList<long> totalRemoteQueueDurations = new long[(int)PipExecutionStep.Done + 1];

                IList<long> totalStepDurations = new long[(int)PipExecutionStep.Done + 1];
                IList<long> totalQueueRequestDurations = new long[(int)PipExecutionStep.Done + 1];
                IList<long> totalSendRequestDurations = new long[(int)PipExecutionStep.Done + 1];
                IList<long> totalCacheLookupStepDurations = new long[OperationKind.TrackedCacheLookupCounterCount];
                long totalCacheMissAnalysisDuration = 0, totalSuspendedDuration = 0, totalRetryCount = 0;
                long totalInputMaterializationExtraCostMbDueToUnavailability = 0;

                var summaryTable = new StringBuilder();
                var detailedLog = new StringBuilder();
                detailedLog.AppendLine(I($"Fine-grained Duration (ms) for Each Pip on the Critical Path (from end to beginning)"));

                int index = 0;

                foreach (var node in criticalPath)
                {
                    RunnablePipPerformanceInfo performance = m_runnablePipPerformance[node.pipId];

                    LogPipPerformanceInfo(detailedLog, node.pipId, performance);

                    Pip pip = PipGraph.GetPipFromPipId(node.pipId);
                    PipRuntimeInfo runtimeInfo = node.pipRunTimeInfo;

                    long pipDurationMs = performance.CalculatePipDurationMs(this);
                    long pipQueueDurationMs = performance.CalculateQueueDurationMs();

                    Logger.Log.CriticalPathPipRecord(m_executePhaseLoggingContext,
                        pipSemiStableHash: pip.SemiStableHash,
                        pipDescription: pip.GetDescription(Context),
                        pipDurationMs: pipDurationMs,
                        exeDurationMs: runtimeInfo.ProcessExecuteTimeMs,
                        queueDurationMs: pipQueueDurationMs,
                        indexFromBeginning: criticalPath.Count - index - 1,
                        isExplicitlyScheduled: (m_explicitlyScheduledProcessNodes == null ? false : m_explicitlyScheduledProcessNodes.Contains(node.Item2.ToNodeId())),
                        executionLevel: runtimeInfo.Result.ToString(),
                        numCacheEntriesVisited: performance.CacheLookupPerfInfo.NumCacheEntriesVisited,
                        numPathSetsDownloaded: performance.CacheLookupPerfInfo.NumPathSetsDownloaded);

                    Func<TimeSpan, string> formatTime = (t) => string.Format("{0:hh\\:mm\\:ss}", t);

                    string scheduledTime = "N/A";
                    string completedTime = "N/A";
                    TimeSpan scheduledTimeTs = TimeSpan.Zero;
                    TimeSpan completedTimeTs = TimeSpan.Zero;

                    if (m_processStartTimeUtc.HasValue)
                    {
                        scheduledTimeTs = performance.ScheduleTime - m_processStartTimeUtc.Value;
                        scheduledTime = formatTime(scheduledTimeTs);
                        completedTimeTs = performance.CompletedTime - m_processStartTimeUtc.Value;
                        completedTime = formatTime(completedTimeTs);
                    }

                    summaryTable.AppendLine(I($"{pipDurationMs,16} | {runtimeInfo.ProcessExecuteTimeMs,15} | {pipQueueDurationMs,18} | {runtimeInfo.Result,12} | {scheduledTime,14} | {completedTime,14} | {pip.GetDescription(Context)}"));

                    if (buildSummary != null)
                    {
                        buildSummary.CriticalPathSummary.Lines.Add(
                            new CriticalPathSummaryLine
                            {
                                PipDuration = TimeSpan.FromMilliseconds(pipDurationMs),
                                ProcessExecuteTime = TimeSpan.FromMilliseconds(runtimeInfo.ProcessExecuteTimeMs),
                                PipQueueDuration = TimeSpan.FromMilliseconds(pipQueueDurationMs),
                                Result = runtimeInfo.Result.ToString(),
                                ScheduleTime = scheduledTimeTs,
                                Completed = completedTimeTs,
                                PipDescription = pip.GetDescription(Context),
                            });
                    }

                    totalStepDurations = totalStepDurations.Zip(performance.StepDurations, (x, y) => (x + (long)y.TotalMilliseconds)).ToList();
                    totalMasterQueueDurations = totalMasterQueueDurations.Zip(performance.QueueDurations.Value, (x, y) => (x + (long)y.TotalMilliseconds)).ToList();
                    totalRemoteQueueDurations = totalRemoteQueueDurations.Zip(performance.RemoteQueueDurations.Value, (x, y) => (x + (long)y.TotalMilliseconds)).ToList();
                    totalSendRequestDurations = totalSendRequestDurations.Zip(performance.SendRequestDurations.Value, (x, y) => (x + (long)y.TotalMilliseconds)).ToList();
                    totalQueueRequestDurations = totalQueueRequestDurations.Zip(performance.QueueRequestDurations.Value, (x, y) => (x + (long)y.TotalMilliseconds)).ToList();

                    totalCacheLookupStepDurations = totalCacheLookupStepDurations
                        .Zip(performance.CacheLookupPerfInfo.CacheLookupStepCounters, (x, y) => (x + (long)(new TimeSpan(y.durationTicks).TotalMilliseconds))).ToList();

                    totalCacheMissAnalysisDuration += (long)performance.CacheMissAnalysisDuration.TotalMilliseconds;
                    totalSuspendedDuration += performance.SuspendedDurationMs;
                    totalInputMaterializationExtraCostMbDueToUnavailability += (performance.InputMaterializationCostMbForChosenWorker - performance.InputMaterializationCostMbForBestWorker);

                    index++;
                }

                // Putting logs together - a summary table followed by a detailed log for each pip

                var builder = new StringBuilder();

                string hr = I($"{Environment.NewLine}======================================================================{Environment.NewLine}");

                builder.AppendLine(I($"Fine-grained Duration (ms) for Top 5 Pips Sorted by Pip Duration (excluding ChooseWorker and Queue durations)"));
                var topPipDurations =
                    (from a in m_runnablePipPerformance
                     let i = a.Value.CalculatePipDurationMs(this)
                     where i > 0
                     orderby i descending
                     select a).Take(5);

                foreach (var kvp in topPipDurations)
                {
                    LogPipPerformanceInfo(builder, kvp.Key, kvp.Value);
                }

                builder.AppendLine(hr);
                builder.AppendLine(I($"Fine-grained Duration (ms) for Top 5 Pips Sorted by CacheLookup Duration"));
                var topCacheLookupDurations =
                    (from a in m_runnablePipPerformance
                     let i = a.Value.StepDurations[(int)PipExecutionStep.CacheLookup].TotalMilliseconds
                     where i > 0
                     orderby i descending
                     select a).Take(5);

                foreach (var kvp in topCacheLookupDurations)
                {
                    LogPipPerformanceInfo(builder, kvp.Key, kvp.Value);
                }

                builder.AppendLine(hr);
                builder.AppendLine(I($"Fine-grained Duration (ms) for Top 5 Pips Sorted by ExecuteProcess Duration"));
                var topExecuteDurations =
                    (from a in m_runnablePipPerformance
                     let i = a.Value.StepDurations[(int)PipExecutionStep.ExecuteProcess].TotalMilliseconds
                     where i > 0
                     orderby i descending
                     select a).Take(5);

                foreach (var kvp in topExecuteDurations)
                {
                    LogPipPerformanceInfo(builder, kvp.Key, kvp.Value);
                }

                builder.AppendLine(hr);

                builder.AppendLine("Critical path:");
                builder.AppendLine(I($"{"Pip Duration(ms)",-16} | {"Exe Duration(ms)",-15}| {"Queue Duration(ms)",-18} | {"Pip Result",-12} | {"Scheduled Time",-14} | {"Completed Time",-14} | Pip"));

                // Total critical path running time is a sum of all steps except ChooseWorker and MaterializeOutput (if it is done in background)
                long totalCriticalPathRunningTime = totalStepDurations.Where((i, j) => ((PipExecutionStep)j).IncludeInRunningTime(this)).Sum();
                long totalMasterQueueTime = totalMasterQueueDurations.Sum();
                long totalRemoteQueueTime = totalRemoteQueueDurations.Sum();
                long totalSendRequestTime = totalSendRequestDurations.Sum();
                long totalQueueRequestTime = totalQueueRequestDurations.Sum();

                long totalChooseWorker = totalStepDurations[(int)PipExecutionStep.ChooseWorkerCpu] + totalStepDurations[(int)PipExecutionStep.ChooseWorkerCacheLookup];

                builder.AppendLine(I($"{totalCriticalPathRunningTime,16} | {exeDurationCriticalPathMs,15} | {totalMasterQueueTime,18} | {string.Empty,12} | {string.Empty,14} | {string.Empty,14} | *Total"));
                builder.AppendLine(summaryTable.ToString());

                if (buildSummary != null)
                {
                    buildSummary.CriticalPathSummary.TotalCriticalPathRuntime = TimeSpan.FromMilliseconds(totalCriticalPathRunningTime);
                    buildSummary.CriticalPathSummary.ExeDurationCriticalPath = TimeSpan.FromMilliseconds(exeDurationCriticalPathMs);
                    buildSummary.CriticalPathSummary.TotalMasterQueueTime = TimeSpan.FromMilliseconds(totalMasterQueueTime);
                }

                builder.AppendLine(detailedLog.ToString());

                statistics.Add("CriticalPath.TotalMasterQueueDurationMs", totalMasterQueueTime);
                builder.AppendLine(I($"Total Master Queue Waiting Time (ms) on the Critical Path"));
                for (int i = 0; i < totalMasterQueueDurations.Count; i++)
                {
                    if (totalMasterQueueDurations[i] != 0)
                    {
                        var queue = (DispatcherKind)i;
                        builder.AppendLine(I($"\t{queue,-98}: {totalMasterQueueDurations[i],10}"));
                        statistics.Add(I($"CriticalPath.{queue}_MasterQueueDurationMs"), totalMasterQueueDurations[i]);
                    }
                }

                builder.AppendLine();

                statistics.Add("CriticalPath.TotalRemoteQueueDurationMs", totalRemoteQueueTime);
                builder.AppendLine(I($"Total Remote Queue Waiting Time (ms) on the Critical Path"));
                for (int i = 0; i < totalRemoteQueueDurations.Count; i++)
                {
                    if (totalRemoteQueueDurations[i] != 0)
                    {
                        var step = (PipExecutionStep)i;
                        builder.AppendLine(I($"\t{step,-98}: {totalRemoteQueueDurations[i],10}"));
                        statistics.Add(I($"CriticalPath.{step}_RemoteQueueDurationMs"), totalRemoteQueueDurations[i]);
                    }
                }

                builder.AppendLine();
                builder.AppendLine(I($"Total Pip Execution Step Duration (ms) on the Critical Path"));
                for (int i = 0; i < totalStepDurations.Count; i++)
                {
                    if (totalStepDurations[i] != 0 && ((PipExecutionStep)i).IncludeInRunningTime(this))
                    {
                        var step = (PipExecutionStep)i;
                        builder.AppendLine(I($"\t{step,-98}: {totalStepDurations[i],10}"));
                        statistics.Add(I($"CriticalPath.{step}DurationMs"), totalStepDurations[i]);
                    }
                }

                builder.AppendLine();
                builder.AppendLine(I($"Total CacheLookup Step Duration (ms) on the Critical Path"));
                for (int i = 0; i < totalCacheLookupStepDurations.Count; i++)
                {
                    var duration = totalCacheLookupStepDurations[i];
                    var name = OperationKind.GetTrackedCacheOperationKind(i).ToString();
                    if (duration != 0)
                    {
                        builder.AppendLine(I($"\t{name,-98}: {duration,10}"));
                        statistics.Add(I($"CriticalPath.{name}DurationMs"), duration);
                    }
                }

                builder.AppendLine();

                statistics.Add("CriticalPath.TotalQueueRequestDurationMs", totalQueueRequestTime);
                builder.AppendLine(I($"Total Queue Request Duration (ms) on the Critical Path"));
                for (int i = 0; i < totalQueueRequestDurations.Count; i++)
                {
                    if (totalQueueRequestDurations[i] != 0)
                    {
                        var step = (PipExecutionStep)i;
                        builder.AppendLine(I($"\t{step,-98}: {totalQueueRequestDurations[i],10}"));
                        statistics.Add(I($"CriticalPath.{step}_QueueRequestDurationMs"), totalQueueRequestDurations[i]);
                    }
                }

                builder.AppendLine();

                statistics.Add("CriticalPath.TotalSendRequestDurationMs", totalSendRequestTime);
                builder.AppendLine(I($"Total Send Request Duration (ms) on the Critical Path"));
                for (int i = 0; i < totalSendRequestDurations.Count; i++)
                {
                    if (totalSendRequestDurations[i] != 0)
                    {
                        var step = (PipExecutionStep)i;
                        builder.AppendLine(I($"\t{step,-98}: {totalSendRequestDurations[i],10}"));
                        statistics.Add(I($"CriticalPath.{step}_SendRequestDurationMs"), totalSendRequestDurations[i]);
                    }
                }

                builder.AppendLine();
                builder.AppendLine(I($"{"Total Worker Selection Overhead (ms) on the Critical Path",-106}: {totalChooseWorker,10}"));
                statistics.Add("CriticalPath.ChooseWorkerDurationMs", totalChooseWorker);

                builder.AppendLine();
                builder.AppendLine(I($"{"Total Cache Miss Analysis Overhead (ms) on the Critical Path",-106}: {totalCacheMissAnalysisDuration,10}"));
                builder.AppendLine(I($"{"Total Suspended Duration (ms) on the Critical Path",-106}: {totalSuspendedDuration,10}"));
                builder.AppendLine(I($"{"Total Retry Count on the Critical Path",-106}: {totalRetryCount,10}"));

                statistics.Add("CriticalPath.CacheMissAnalysisDurationMs", totalCacheMissAnalysisDuration);
                statistics.Add("CriticalPath.TotalSuspendedDurationMs", totalSuspendedDuration);
                statistics.Add("CriticalPath.TotalRetryCount", totalRetryCount);
                statistics.Add("CriticalPath.TotalInputMaterializationExtraCostMbDueToUnavailability", totalInputMaterializationExtraCostMbDueToUnavailability);

                builder.AppendLine();
                builder.AppendLine(I($"{"Total Critical Path Length (including queue waiting time and choosing worker(s)) ms",-106}: {totalMasterQueueTime + totalChooseWorker + totalCriticalPathRunningTime,10}"));

                statistics.Add("CriticalPath.ExeDurationMs", exeDurationCriticalPathMs);
                statistics.Add("CriticalPath.PipDurationMs", totalCriticalPathRunningTime);

                Logger.Log.CriticalPathChain(m_executePhaseLoggingContext, builder.ToString());
            }
        }

        private void LogPipPerformanceInfo(StringBuilder stringBuilder, PipId pipId, RunnablePipPerformanceInfo performanceInfo)
        {
            Pip pip = PipGraph.GetPipFromPipId(pipId);

            stringBuilder.AppendLine(I($"\t{pip.GetDescription(Context)}"));

            if (pip.PipType == PipType.Process)
            {
                bool isExplicitlyScheduled = (m_explicitlyScheduledProcessNodes == null ? false : m_explicitlyScheduledProcessNodes.Contains(pipId.ToNodeId()));
                stringBuilder.AppendLine(I($"\t\t{"Explicitly Scheduled",-90}: {isExplicitlyScheduled,10}"));
            }

            for (int i = 0; i < performanceInfo.QueueDurations.Value.Length; i++)
            {
                var duration = (long)performanceInfo.QueueDurations.Value[i].TotalMilliseconds;
                if (duration != 0)
                {
                    stringBuilder.AppendLine(I($"\t\tQueue - {(DispatcherKind)i,-82}: {duration,10}"));
                }
            }

            for (int i = 0; i < performanceInfo.StepDurations.Length; i++)
            {
                var step = (PipExecutionStep)i;
                var stepDuration = (long)performanceInfo.StepDurations[i].TotalMilliseconds;
                if (stepDuration != 0)
                {
                    stringBuilder.AppendLine(I($"\t\tStep  - {step,-82}: {stepDuration,10}"));
                }

                long remoteStepDuration = 0;
                uint workerId = performanceInfo.Workers.Value[i];
                if (workerId != 0)
                {
                    string workerName = workerId == AllWorker.Id
                        ? "AllWorkers"
                        : $"{$"W{workerId}",10}:{m_workers[(int)workerId].Name}";
                    stringBuilder.AppendLine(I($"\t\t  {"WorkerName",-88}: {workerName}"));

                    var queueRequest = (long)performanceInfo.QueueRequestDurations.Value[i].TotalMilliseconds;
                    stringBuilder.AppendLine(I($"\t\t  {"MasterQueueRequest",-88}: {queueRequest,10}"));

                    var sendRequest = (long)performanceInfo.SendRequestDurations.Value[i].TotalMilliseconds;
                    stringBuilder.AppendLine(I($"\t\t  {"MasterSendRequest",-88}: {sendRequest,10}"));

                    var remoteQueueDuration = (long)performanceInfo.RemoteQueueDurations.Value[i].TotalMilliseconds;
                    stringBuilder.AppendLine(I($"\t\t  {"RemoteQueue",-88}: {remoteQueueDuration,10}"));

                    remoteStepDuration = (long)performanceInfo.RemoteStepDurations.Value[i].TotalMilliseconds;
                    stringBuilder.AppendLine(I($"\t\t  {"RemoteStep",-88}: {remoteStepDuration,10}"));

                }

                if (stepDuration != 0 && step == PipExecutionStep.CacheLookup)
                {
                    stringBuilder.AppendLine(I($"\t\t  {"NumCacheEntriesVisited",-88}: {performanceInfo.CacheLookupPerfInfo.NumCacheEntriesVisited,10}"));
                    stringBuilder.AppendLine(I($"\t\t  {"NumPathSetsDownloaded",-88}: {performanceInfo.CacheLookupPerfInfo.NumPathSetsDownloaded,10}"));

                    for (int j = 0; j < performanceInfo.CacheLookupPerfInfo.CacheLookupStepCounters.Length; j++)
                    {
                        var name = OperationKind.GetTrackedCacheOperationKind(j).ToString();
                        var tuple = performanceInfo.CacheLookupPerfInfo.CacheLookupStepCounters[j];
                        long duration = (long)(new TimeSpan(tuple.durationTicks)).TotalMilliseconds;

                        if (duration != 0)
                        {
                            stringBuilder.AppendLine(I($"\t\t  {name,-88}: {duration,10} - occurred {tuple.occurrences,10} times"));
                        }
                    }
                }

                if (stepDuration != 0 && step == PipExecutionStep.ExecuteProcess)
                {
                    long inputMaterializationExtraCostMbDueToUnavailability = performanceInfo.InputMaterializationCostMbForChosenWorker - performanceInfo.InputMaterializationCostMbForBestWorker;
                    stringBuilder.AppendLine(I($"\t\t  {"InputMaterializationExtraCostMbDueToUnavailability",-88}: {inputMaterializationExtraCostMbDueToUnavailability,10}"));
                    stringBuilder.AppendLine(I($"\t\t  {"InputMaterializationCostMbForChosenWorker",-88}: {performanceInfo.InputMaterializationCostMbForChosenWorker,10}"));

                    if (performanceInfo.CacheMissAnalysisDuration.TotalMilliseconds != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"CacheMissAnalysis",-88}: {(long)performanceInfo.CacheMissAnalysisDuration.TotalMilliseconds,10}"));
                    }

                    if (performanceInfo.SuspendedDurationMs != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"SuspendedDurationMs",-88}: {performanceInfo.SuspendedDurationMs,10}"));
                    }

                    if (performanceInfo.RetryCount != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"RetryCount",-88}: {performanceInfo.RetryCount,10}"));
                    }
                }

                if (stepDuration != 0 && step == PipExecutionStep.MaterializeOutputs)
                {
                    stringBuilder.AppendLine(I($"\t\t  {"InBackground",-88}: {MaterializeOutputsInBackground,10}"));

                    if (performanceInfo.QueueWaitDurationForMaterializeOutputsInBackground.TotalMilliseconds != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"Queue.Materialize.InBackground",-88}: {(long)performanceInfo.QueueWaitDurationForMaterializeOutputsInBackground.TotalMilliseconds,10}"));
                    }
                }
            }
        }

        private bool TryGetCriticalPathTailRuntimeInfo(out int currentCriticalPathTailPipIdValue, out PipRuntimeInfo runtimeInfo)
        {
            runtimeInfo = null;

            currentCriticalPathTailPipIdValue = Volatile.Read(ref m_criticalPathTailPipIdValue);
            if (currentCriticalPathTailPipIdValue == 0)
            {
                return false;
            }

            PipId criticalPathTailId = new PipId(unchecked((uint)currentCriticalPathTailPipIdValue));
            runtimeInfo = GetPipRuntimeInfo(criticalPathTailId);
            return true;
        }

        private void UpdateCriticalPath(RunnablePip runnablePip, PipExecutionPerformance performance)
        {
            var duration = runnablePip.RunningTime;
            var pip = runnablePip.Pip;

            if (pip.PipType.IsMetaPip())
            {
                return;
            }

            long durationMs = (long)duration.TotalMilliseconds;
            long criticalChainMs = durationMs;
            foreach (var dependencyEdge in ScheduledGraph.GetIncomingEdges(pip.PipId.ToNodeId()))
            {
                var dependencyRuntimeInfo = GetPipRuntimeInfo(dependencyEdge.OtherNode);
                criticalChainMs = Math.Max(criticalChainMs, durationMs + dependencyRuntimeInfo.CriticalPathDurationMs);
            }

            var pipRuntimeInfo = GetPipRuntimeInfo(pip.PipId);

            pipRuntimeInfo.Result = performance.ExecutionLevel;
            pipRuntimeInfo.CriticalPathDurationMs = criticalChainMs > int.MaxValue ? int.MaxValue : (int)criticalChainMs;
            ProcessPipExecutionPerformance processPerformance = performance as ProcessPipExecutionPerformance;
            if (processPerformance != null)
            {
                pipRuntimeInfo.ProcessExecuteTimeMs = (int)processPerformance.ProcessExecutionTime.TotalMilliseconds;
            }

            var pipIdValue = unchecked((int)pip.PipId.Value);

            int currentCriticalPathTailPipIdValue;
            PipRuntimeInfo criticalPathRuntimeInfo;

            while (!TryGetCriticalPathTailRuntimeInfo(out currentCriticalPathTailPipIdValue, out criticalPathRuntimeInfo)
                || (criticalChainMs > (criticalPathRuntimeInfo?.CriticalPathDurationMs ?? 0)))
            {
                if (Interlocked.CompareExchange(ref m_criticalPathTailPipIdValue, pipIdValue, currentCriticalPathTailPipIdValue) == currentCriticalPathTailPipIdValue)
                {
                    return;
                }
            }
        }

        #endregion Critical Path Logging

        /// <summary>
        /// Given the execution performance of a just-completed pip, records its performance info for future schedules
        /// and notifies any execution observers.
        /// </summary>
        private void HandleExecutionPerformance(RunnablePip runnablePip, PipExecutionPerformance performance)
        {
            var pip = runnablePip.Pip;
            UpdateCriticalPath(runnablePip, performance);

            ProcessPipExecutionPerformance processPerf = performance as ProcessPipExecutionPerformance;
            if (pip.PipType == PipType.Process &&
                performance.ExecutionLevel == PipExecutionLevel.Executed &&
                processPerf != null)
            {
                HistoricPerfDataTable[pip.SemiStableHash] = new ProcessPipHistoricPerfData(processPerf);
            }

            if (ExecutionLog != null && performance != null)
            {
                ExecutionLog.PipExecutionPerformance(new PipExecutionPerformanceEventData
                {
                    PipId = pip.PipId,
                    ExecutionPerformance = performance,
                });
            }
        }

        /// <summary>
        /// The state required for pip execution
        /// </summary>
        public PipExecutionState State { get; private set; }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        CounterCollection<PipExecutorCounter> IPipExecutionEnvironment.Counters => PipExecutionCounters;

        /// <summary>
        /// Strongly-typed, optionally persisted log of execution events.
        /// </summary>
        public IExecutionLogTarget ExecutionLog => m_multiExecutionLogTarget;

        private readonly ExecutionLogFileTarget m_executionLogFileTarget;
        private readonly FingerprintStoreExecutionLogTarget m_fingerprintStoreTarget;
        private readonly MultiExecutionLogTarget m_multiExecutionLogTarget;
        private readonly BuildManifestStoreTarget m_buildManifestStoreTarget;

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        IPipGraphFileSystemView IPipExecutionEnvironment.PipGraphView => PipGraph;

        /// <inheritdoc />
        public void ReportCacheDescriptorHit(string sourceCache)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(sourceCache));
            m_cacheIdHits.AddOrUpdate(sourceCache, 1, (key, value) => value + 1);
        }

        /// <inheritdoc />
        public bool ShouldHaveArtificialMiss(Pip pip)
        {
            Contract.Requires(pip != null);
            return m_artificialCacheMissOptions != null &&
                   m_artificialCacheMissOptions.ShouldHaveArtificialMiss(pip.SemiStableHash);
        }

        #endregion Execution

        #region Runtime Initialization

        /// <summary>
        /// Initialize runtime state, optionally apply a filter and schedule all ready pips
        /// </summary>
        public bool InitForMaster(LoggingContext loggingContext, RootFilter filter = null, SchedulerState schedulerState = null, ISandboxConnection sandboxConnectionKext = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Assert(!IsInitialized);
            Contract.Assert(!IsDistributedWorker);

            using (var pm = PerformanceMeasurement.Start(
                    loggingContext,
                    Statistics.ApplyFilterAndScheduleReadyNodes,
                    Logger.Log.StartSchedulingPipsWithFilter,
                    Logger.Log.EndSchedulingPipsWithFilter))
            {
                InitSchedulerRuntimeState(pm.LoggingContext, schedulerState: schedulerState);

                // Start workers after scheduler runtime state is successfully established
                if (!HasFailed && IsDistributedMaster)
                {
                    StartWorkers(loggingContext);
                }

                InitPipStates(pm.LoggingContext);

                IEnumerable<NodeId> nodesToSchedule;
                if (filter != null && !filter.IsEmpty)
                {
                    if (!TryGetFilteredNodes(pm.LoggingContext, filter, schedulerState, out nodesToSchedule))
                    {
                        Contract.Assume(loggingContext.ErrorWasLogged);
                        return false;
                    }
                }
                else
                {
                    nodesToSchedule = CalculateNodesToSchedule(loggingContext);
                }

                ProcessPipCountersByFilter = new PipCountersByFilter(loggingContext, m_explicitlyScheduledProcessNodes ?? new HashSet<NodeId>());
                ProcessPipCountersByTelemetryTag = new PipCountersByTelemetryTag(loggingContext, Context.StringTable, m_scheduleConfiguration.TelemetryTagPrefix);

                m_groupedPipCounters = new PipCountersByGroupAggregator(loggingContext, ProcessPipCountersByFilter, ProcessPipCountersByTelemetryTag);

                // This logging context must be set prior to any scheduling, as it might be accessed.
                m_executePhaseLoggingContext = pm.LoggingContext;

                m_hasFailures = m_hasFailures || InitSandboxConnectionKext(loggingContext, sandboxConnectionKext);

                PrioritizeAndSchedule(pm.LoggingContext, nodesToSchedule);

                if (m_configuration.Schedule.ModuleAffinityEnabled())
                {
                    PopulateModuleWorkerMapping(nodesToSchedule);
                }

                Contract.Assert(!HasFailed || loggingContext.ErrorWasLogged, "Scheduler encountered errors during initialization, but none were logged.");
                return !HasFailed;
            }
        }

        private void PopulateModuleWorkerMapping(IEnumerable<NodeId> nodes)
        {
            foreach (var node in nodes)
            {
                var pipId = node.ToPipId();
                if (m_pipTable.GetPipType(pipId) == PipType.Process || m_pipTable.GetPipType(pipId) == PipType.Ipc)
                {
                    var pipState = (ProcessMutablePipState)m_pipTable.GetMutable(pipId);
                    (int NumPips, List<Worker> Workers) tuple;
                    if (!m_moduleWorkerMapping.TryGetValue(pipState.ModuleId, out tuple))
                    {
                        tuple = (0, new List<Worker>());
                    }

                    m_moduleWorkerMapping[pipState.ModuleId] = (tuple.NumPips + 1, tuple.Workers);
                }
            }

            int i = 0;
            foreach (var kvp in m_moduleWorkerMapping.OrderByDescending(a => a.Value.NumPips))
            {
                var worker = m_workers[(i % m_workers.Count)];
                kvp.Value.Workers.Add(worker);
                i++;
            }
        }

        /// <summary>
        /// Initilizes the kernel extension connection if required and reports back success or failure, allowing
        /// for a graceful terminaton of BuildXL.
        /// </summary>
        protected virtual bool InitSandboxConnectionKext(LoggingContext loggingContext, ISandboxConnection sandboxConnection = null)
        {
            if (MacOsSandboxingEnabled)
            {
                try
                {
                    // Setup the kernel extension connection so we can potentially execute pips later
                    if (sandboxConnection == null)
                    {
                        var config = new SandboxConnectionKext.Config
                        {
                            MeasureCpuTimes = m_configuration.Sandbox.MeasureProcessCpuTimes,
                            FailureCallback = sandboxFailureCallback,
                            KextConfig = new Sandbox.KextConfig
                            {
                                ReportQueueSizeMB = m_configuration.Sandbox.KextReportQueueSizeMb,
                                EnableReportBatching = m_configuration.Sandbox.KextEnableReportBatching,
#if !PLATFORM_WIN
                                EnableCatalinaDataPartitionFiltering = OperatingSystemHelper.IsMacWithoutKernelExtensionSupport,
#endif
                                ResourceThresholds = new Sandbox.ResourceThresholds
                                {
                                    CpuUsageBlockPercent = m_configuration.Sandbox.KextThrottleCpuUsageBlockThresholdPercent,
                                    CpuUsageWakeupPercent = m_configuration.Sandbox.KextThrottleCpuUsageWakeupThresholdPercent,
                                    MinAvailableRamMB = m_configuration.Sandbox.KextThrottleMinAvailableRamMB,
                                }
                            }
                        };

                        switch (m_configuration.Sandbox.UnsafeSandboxConfiguration.SandboxKind)
                        {
                            case SandboxKind.LinuxDetours:
                            {
                                sandboxConnection = new SandboxConnectionLinuxDetours(sandboxFailureCallback);
                                break;
                            }
                            case SandboxKind.MacOsEndpointSecurity:
                            case SandboxKind.MacOsDetours:
                            case SandboxKind.MacOsHybrid:
                            {
                                sandboxConnection =
                                    (ISandboxConnection)new SandboxConnection(m_configuration.Sandbox.UnsafeSandboxConfiguration.SandboxKind,
                                        isInTestMode: false, m_configuration.Sandbox.MeasureProcessCpuTimes);

                                break;
                            }
                            default:
                            {
                                sandboxConnection = OperatingSystemHelper.IsLinuxOS
                                    ? new SandboxConnectionLinuxDetours(sandboxFailureCallback)
                                    : (ISandboxConnection)new SandboxConnectionKext(config);
                                break;
                            }
                        }

                        if (m_performanceAggregator != null && config.KextConfig.Value.ResourceThresholds.IsProcessThrottlingEnabled())
                        {
                            m_performanceAggregator.MachineCpu.OnChange += (aggregator) =>
                            {
                                double availableRam = m_performanceAggregator.MachineAvailablePhysicalMB.Latest;
                                uint cpuUsageBasisPoints = Convert.ToUInt32(Math.Round(aggregator.Latest * 100));
                                uint availableRamMB = double.IsNaN(availableRam) || double.IsInfinity(availableRam)
                                    ? 0
                                    : Convert.ToUInt32(Math.Round(availableRam));
                                sandboxConnection.NotifyUsage(cpuUsageBasisPoints, availableRamMB);
                            };
                        }
                    }

                    SandboxConnection = sandboxConnection;
                }
                catch (BuildXLException ex)
                {
                    Logger.Log.KextFailedToInitializeConnectionManager(loggingContext, (ex.InnerException ?? ex).Message);
                    return true; // Indicates error
                }
            }

            return false;

            void sandboxFailureCallback(int status, string description)
            {
                Logger.Log.KextFailureNotificationReceived(loggingContext, status, description);
                RequestTermination();
            }
        }

        private void InitSchedulerRuntimeState(LoggingContext loggingContext, SchedulerState schedulerState)
        {
            using (PipExecutionCounters.StartStopwatch(PipExecutorCounter.InitSchedulerRuntimeStateDuration))
            {
                Contract.Requires(loggingContext != null);

                // Start loading data for pip two phase cache and running time table. No need to wait since any operation against the components
                // will block until the required component is ready.
                m_pipTwoPhaseCache.StartLoading(waitForCompletion: false);
                m_historicPerfDataTableTask?.Start();

                InitFileChangeTracker(loggingContext);
                ProcessFileChanges(loggingContext, schedulerState);

                var fileChangeTrackingSelector = new FileChangeTrackingSelector(
                    pathTable: Context.PathTable,
                    loggingContext: loggingContext,
                    tracker: m_fileChangeTracker,
                    includedRoots: m_configuration.Cache.FileChangeTrackingInclusionRoots,
                    excludedRoots: m_configuration.Cache.FileChangeTrackingExclusionRoots);

                // Set-up tracking of local disk state:
                // - If 'incremental scheduling' is turned on, we have a tracker for file changes
                // - We always have a FileContentTable to remember hashes of files (shared among different build graphs)
                // In aggregate, we manage local disk state with a LocalDiskContentStore (m_localDiskContentStore).
                // It updates the change tracker (specific to this graph) and FileContentTable (shared) in response to pip-related I/O.
                // Additionally, we track pip-related directory enumerations via requests to DirectoryMembershipFingerprinter (which happens
                // to not be related to the LocalDiskContentStore) and in the VFS (which may pass through some probes to the real filesystem).
                // TODO: The VFS, LocalDiskContentStore, and DirectoryMembershipFingerprinter may need to be better reconciled.
                m_localDiskContentStore = new LocalDiskContentStore(
                    loggingContext,
                    Context.PathTable,
                    m_fileContentTable,
                    m_fileChangeTracker,
                    DirectoryTranslator,
                    fileChangeTrackingSelector,
                    vfsCasRoot: m_configuration.Cache.VfsCasRoot);

                m_pipOutputMaterializationTracker = new PipOutputMaterializationTracker(this, IncrementalSchedulingState);

                FileSystemView fileSystemView;
                using (PipExecutionCounters.StartStopwatch(PipExecutorCounter.CreateFileSystemViewDuration))
                {
                    fileSystemView = FileSystemView.Create(
                        Context.PathTable,
                        PipGraph,
                        m_localDiskContentStore,
                        maxInitializationDegreeOfParallelism: m_scheduleConfiguration.MaxProcesses,
                        inferNonExistenceBasedOnParentPathInRealFileSystem: m_scheduleConfiguration.InferNonExistenceBasedOnParentPathInRealFileSystem);
                }

                State = new PipExecutionState(
                    m_configuration,
                    loggingContext,
                    cache: m_pipTwoPhaseCache,
                    directoryMembershipFingerprinter: m_directoryMembershipFingerprinter,
                    fileAccessAllowlist: m_fileAccessAllowlist,
                    pathExpander: m_semanticPathExpander,
                    executionLog: ExecutionLog,
                    fileSystemView: fileSystemView,
                    directoryMembershipFinterprinterRuleSet: m_directoryMembershipFingerprinterRules,
                    fileContentManager: m_fileContentManager,
                    unsafeConfiguration: m_configuration.Sandbox.UnsafeSandboxConfiguration,
                    preserveOutputsSalt: m_previousInputsSalt,
                    lazyDeletionOfSharedOpaqueOutputsEnabled: m_lazyDeletionOfSharedOpaqueOutputsEnabled,
                    serviceManager: m_serviceManager);
            }
        }

        private void ProcessFileChanges(LoggingContext loggingContext, SchedulerState schedulerState)
        {
            InputChangeList inputChangeList = null;

            if (m_configuration.Schedule.InputChanges.IsValid)
            {
                inputChangeList = InputChangeList.CreateFromFile(
                    loggingContext,
                    m_configuration.Schedule.InputChanges.ToString(Context.PathTable),
                    m_configuration.Layout.SourceDirectory.ToString(Context.PathTable),
                    DirectoryTranslator);

                m_fileContentManager.SourceChangeAffectedInputs.InitialAffectedOutputList(inputChangeList, Context.PathTable);
            }

            IncrementalSchedulingStateFactory incrementalSchedulingStateFactory = null;

            if (m_shouldCreateIncrementalSchedulingState)
            {
                incrementalSchedulingStateFactory = new IncrementalSchedulingStateFactory(
                    loggingContext,
                    analysisMode: false,
                    tempDirectoryCleaner: TempCleaner);
            }

            if (m_fileChangeTracker.IsBuildingInitialChangeTrackingSet)
            {
                if (m_shouldCreateIncrementalSchedulingState)
                {
                    Contract.Assert(incrementalSchedulingStateFactory != null);
                    IncrementalSchedulingState = incrementalSchedulingStateFactory.CreateNew(
                        m_fileChangeTracker.FileEnvelopeId,
                        PipGraph,
                        m_configuration,
                        m_previousInputsSalt);
                }
            }
            else if (m_fileChangeTracker.IsTrackingChanges)
            {
                var fileChangeProcessor = new FileChangeProcessor(loggingContext, m_fileChangeTracker, inputChangeList);

                if (m_scheduleConfiguration.UpdateFileContentTableByScanningChangeJournal)
                {
                    fileChangeProcessor.Subscribe(m_fileContentTable);
                }

                if (m_shouldCreateIncrementalSchedulingState)
                {
                    Contract.Assert(incrementalSchedulingStateFactory != null);

                    IncrementalSchedulingState = incrementalSchedulingStateFactory.LoadOrReuse(
                        m_fileChangeTracker.FileEnvelopeId,
                        PipGraph,
                        m_configuration,
                        m_previousInputsSalt,
                        m_incrementalSchedulingStateFile.ToString(Context.PathTable),
                        schedulerState);

                    if (IncrementalSchedulingState != null)
                    {
                        fileChangeProcessor.Subscribe(IncrementalSchedulingState);
                    }
                    else
                    {
                        IncrementalSchedulingState = incrementalSchedulingStateFactory.CreateNew(
                            m_fileChangeTracker.FileEnvelopeId,
                            PipGraph,
                            m_configuration,
                            m_previousInputsSalt);
                    }
                }

                fileChangeProcessor.TryProcessChanges(
                    m_configuration.Engine.ScanChangeJournalTimeLimitInSec < 0
                        ? (TimeSpan?)null
                        : TimeSpan.FromSeconds(m_configuration.Engine.ScanChangeJournalTimeLimitInSec),
                    Logger.Log.JournalProcessingStatisticsForScheduler,
                    Logger.Log.JournalProcessingStatisticsForSchedulerTelemetry);

                if (m_shouldCreateIncrementalSchedulingState)
                {
                    m_testHooks?.ValidateIncrementalSchedulingStateAfterJournalScan(IncrementalSchedulingState);
                }
            }

            if (m_testHooks != null)
            {
                m_testHooks.IncrementalSchedulingState = IncrementalSchedulingState;
            }
        }

        private void InitFileChangeTracker(LoggingContext loggingContext)
        {
            if (!m_journalState.IsDisabled)
            {
                LoadingTrackerResult loadingResult;
                if (m_configuration.Engine.FileChangeTrackerInitializationMode == FileChangeTrackerInitializationMode.ForceRestart)
                {
                    m_fileChangeTracker = FileChangeTracker.StartTrackingChanges(
                        loggingContext,
                        m_journalState.VolumeMap,
                        m_journalState.Journal,
                        m_configuration.Engine.FileChangeTrackerSupersedeMode,
                        m_buildEngineFingerprint);
                    loadingResult = null;
                }
                else
                {
                    loadingResult = FileChangeTracker.ResumeOrRestartTrackingChanges(
                        loggingContext,
                        m_journalState.VolumeMap,
                        m_journalState.Journal,
                        m_configuration.Engine.FileChangeTrackerSupersedeMode,
                        m_fileChangeTrackerFile.ToString(Context.PathTable),
                        m_buildEngineFingerprint,
                        out m_fileChangeTracker);
                }
            }
            else
            {
                m_fileChangeTracker = FileChangeTracker.CreateDisabledTracker(loggingContext);
            }
        }

        /// <summary>
        /// Initialize runtime state but do not apply any filter and do not schedule any pip.
        /// This method is used by the workers only. It is mutually exclusive with StartScheduling
        /// </summary>
        public bool InitForWorker(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);

            InitSchedulerRuntimeState(loggingContext, schedulerState: null);
            InitPipStates(loggingContext);
            m_hasFailures = m_hasFailures || InitSandboxConnectionKext(loggingContext);

            Contract.Assert(!HasFailed || loggingContext.ErrorWasLogged, "Scheduler encountered errors during initialization, but none were logged.");
            return !HasFailed;
        }

        private void InitPipStates(LoggingContext loggingContext)
        {
            using (PerformanceMeasurement.Start(
                loggingContext,
                "InitPipStates",
                Logger.Log.StartSettingPipStates,
                Logger.Log.EndSettingPipStates))
            {
                IsInitialized = true;
                m_pipRuntimeInfos = new PipRuntimeInfo[m_pipTable.Count + 1]; // PipId starts from 1!

                // Note: We need IList<...> in order to get good Parallel.ForEach performance
                IList<PipId> keys = m_pipTable.StableKeys;

                int[] counts = new int[(int)PipType.Max];
                object countsLock = new object();
                Parallel.ForEach(
                    keys,
                    new ParallelOptions { MaxDegreeOfParallelism = m_scheduleConfiguration.MaxProcesses },
                    () =>
                    {
                        return new int[(int)PipType.Max];
                    },
                    (pipId, state, count) =>
                    {
                        count[(int)m_pipTable.GetPipType(pipId)]++;
                        return count;
                    },
                    (count) =>
                    {
                        lock (countsLock)
                        {
                            for (int i = 0; i < counts.Length; i++)
                            {
                                counts[i] = counts[i] + count[i];
                            }
                        }
                    });

                for (int i = 0; i < counts.Length; i++)
                {
                    int count = counts[i];
                    if (count > 0)
                    {
                        m_pipStateCounters.AccumulateInitialStateBulk(PipState.Ignored, (PipType)i, count);
                    }
                }
            }
        }

        /// <summary>
        /// Assigning priorities to the pips
        /// </summary>
        private void PrioritizeAndSchedule(LoggingContext loggingContext, IEnumerable<NodeId> nodes)
        {
            var readyNodes = new List<NodeId>();
            using (PerformanceMeasurement.Start(
                loggingContext,
                "AssigningPriorities",
                Logger.Log.StartAssigningPriorities,
                Logger.Log.EndAssigningPriorities))
            {
                NodeIdDebugView.RuntimeInfos = m_pipRuntimeInfos;

                VisitationTracker nodeFilter = new VisitationTracker(DirectedGraph);
                nodes = nodes.Where(a => m_pipTable.GetPipType(a.ToPipId()) != PipType.HashSourceFile).ToList();
                foreach (var node in nodes)
                {
                    nodeFilter.MarkVisited(node);
                }

                IReadonlyDirectedGraph graph = new FilteredDirectedGraph(PipGraph.DataflowGraph, nodeFilter);
                NodeIdDebugView.AlternateGraph = graph;

                // Store the graph which only contains the scheduled nodes.
                ScheduledGraph = graph;

                m_criticalPathStats = new CriticalPathStats();


                // We walk the graph starting from the sink nodes,
                // computing the critical path of all nodes (in terms of cumulative process execution times).
                // We update the table as we go.

                // TODO: Instead of proceeding in coarse-grained waves, which leaves some potential parallelism on the table,
                // schedule nodes to be processed as soon as all outgoing edges have been processed (tracking refcounts).

                // Phase 1: We order all nodes by height
                MultiValueDictionary<int, NodeId> nodesByHeight = graph.TopSort(nodes);
                var maxHeight = nodesByHeight.Count > 0 ? nodesByHeight.Keys.Max() : -1;

                // Phase 2: For each height, we can process nodes in parallel
                for (int height = maxHeight; height >= 0; height--)
                {
                    IReadOnlyList<NodeId> list;
                    if (!nodesByHeight.TryGetValue(height, out list))
                    {
                        continue;
                    }

                    // Note: It's important that list is an IList<...> in order to get good Parallel.ForEach performance
                    Parallel.ForEach(
                        list,
                        new ParallelOptions { MaxDegreeOfParallelism = m_scheduleConfiguration.MaxProcesses },
                        node =>
                        {
                            var pipId = node.ToPipId();
                            var pipRuntimeInfo = GetPipRuntimeInfo(pipId);
                            var pipState = m_pipTable.GetMutable(pipId);
                            var pipType = pipState.PipType;

                            // Below, we add one or more quanitites in the uint range.
                            // We use a long here to trivially avoid any overflow, and saturate to uint.MaxValue if needed as the last step.
                            long criticalPath = 0;
                            int priorityBase = 0;

                            // quick check to avoid allocation of enumerator (as we are going through an interface, and where everything gets boxed!)
                            if (!graph.IsSinkNode(node))
                            {
                                foreach (var edge in graph.GetOutgoingEdges(node))
                                {
                                    var otherPriority = GetPipRuntimeInfo(edge.OtherNode).Priority;

                                    // Priority consists of given priority in the specs (bits 24-31, and the critical path priority (bits 0-23)
                                    unchecked
                                    {
                                        criticalPath = Math.Max(criticalPath, otherPriority & MaxInitialPipPriority);
                                        priorityBase = Math.Max(priorityBase, otherPriority >> CriticalPathPriorityBitCount);
                                    }
                                }
                            }

                            if (pipType.IsMetaPip())
                            {
                                // We pretend meta pips are themselves free.
                                // We use the critical path calculated from aggregating outgoing edges.
                            }
                            else
                            {
                                // Note that we only try to look up historical runtimes for process pips, since we only record
                                // historical data for that pip type. Avoiding the failed lookup here means that we have more
                                // useful 'hit' / 'miss' counters for the running time table.
                                uint historicalMilliseconds = 0;
                                if (pipType == PipType.Process && HistoricPerfDataTable != null)
                                {
                                    historicalMilliseconds = HistoricPerfDataTable[m_pipTable.GetPipSemiStableHash(pipId)].DurationInMs;
                                }

                                if (historicalMilliseconds != 0)
                                {
                                    Interlocked.Increment(ref m_criticalPathStats.NumHits);
                                    criticalPath += historicalMilliseconds;
                                }
                                else
                                {
                                    // TODO:
                                    // The following wild guesses are subject to further tweaking.
                                    // They are based on no hard data.
                                    Interlocked.Increment(ref m_criticalPathStats.NumWildGuesses);

                                    uint estimatedMilliseconds = (uint)graph.GetIncomingEdgesCount(node);
                                    switch (pipType)
                                    {
                                        case PipType.Process:
                                            estimatedMilliseconds += 10;
                                            break;
                                        case PipType.Ipc:
                                            estimatedMilliseconds += 15;
                                            break;
                                        case PipType.CopyFile:
                                            estimatedMilliseconds += 2;
                                            break;
                                        case PipType.WriteFile:
                                            estimatedMilliseconds += 1;
                                            break;
                                    }

                                    criticalPath += estimatedMilliseconds;
                                }
                            }

                            long currentLongestPath;
                            while ((currentLongestPath = Volatile.Read(ref m_criticalPathStats.LongestPath)) < criticalPath)
                            {
                                if (Interlocked.CompareExchange(ref m_criticalPathStats.LongestPath, criticalPath, comparand: currentLongestPath) == currentLongestPath)
                                {
                                    break;
                                }
                            }

                            priorityBase = Math.Max(m_pipTable.GetPipPriority(pipId), priorityBase) << CriticalPathPriorityBitCount;
                            int criticalPathPriority = (criticalPath < 0 || criticalPath > MaxInitialPipPriority) ? MaxInitialPipPriority : unchecked((int)criticalPath);
                            pipRuntimeInfo.Priority = unchecked(priorityBase + criticalPathPriority);

                            Contract.Assert(pipType != PipType.HashSourceFile);
                            pipRuntimeInfo.Transition(m_pipStateCounters, pipType, PipState.Waiting);
                            if (pipType == PipType.Process && ((ProcessMutablePipState)pipState).IsStartOrShutdown)
                            {
                                Interlocked.Increment(ref m_numServicePipsScheduled);
                            }

                            bool isReady;
                            if (graph.IsSourceNode(node))
                            {
                                isReady = true;
                            }
                            else
                            {
                                int refCount = graph.CountIncomingHeavyEdges(node);
                                pipRuntimeInfo.RefCount = refCount;
                                isReady = refCount == 0;
                            }

                            if (isReady)
                            {
                                lock (readyNodes)
                                {
                                    readyNodes.Add(node);
                                }
                            }
                        });
                }

#if DEBUG
                foreach (var node in nodes)
                {
                    var pipId = node.ToPipId();
                    var pipRuntimeInfo = GetPipRuntimeInfo(pipId);
                    Contract.Assert(pipRuntimeInfo.State != PipState.Ignored);
                }
#endif
            }

            using (PipExecutionCounters.StartStopwatch(PipExecutorCounter.InitialSchedulePipWallTime))
            {
                Parallel.ForEach(
                    readyNodes,

                    // Limit the concurrency here because most work is in PipQueue.Enqueue which immediately has a lock, so this helps some by parellizing the hydratepip.
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(8, m_scheduleConfiguration.MaxProcesses) },
                    (node) => SchedulePip(node, node.ToPipId()).GetAwaiter().GetResult());

                // From this point, only pips that are already scheduled can enqueue new work items
                m_pipQueue.SetAsFinalized();
            }
        }

        #endregion Runtime Initialization

        /// <summary>
        /// Records the final content hashes (by path; no rewrite count) of the given <see cref="SealDirectory" /> pip's contents.
        /// </summary>
        /// <remarks>
        /// The scheduler lock need not be held.
        /// </remarks>
        private PipResult ExecuteSealDirectoryPip(OperationContext operationContext, IPipExecutionEnvironment environment, SealDirectory pip)
        {
            Contract.Requires(pip != null);

            DateTime pipStart = DateTime.UtcNow;

            using (operationContext.StartOperation(PipExecutorCounter.RegisterStaticDirectory))
            {
                // If the pip is a composite opaque directory, then its dynamic content needs to be reported, since the usual reporting of
                // opaque directories happens for process pips only
                if (pip.IsComposite)
                {
                    Contract.Assert(pip.Kind == SealDirectoryKind.SharedOpaque);
                    ReportCompositeOpaqueContents(environment, pip);
                }

                m_fileContentManager.RegisterStaticDirectory(pip.Directory);
            }

            var result = PipResultStatus.NotMaterialized;
            if (pip.Kind == SealDirectoryKind.SourceAllDirectories || pip.Kind == SealDirectoryKind.SourceTopDirectoryOnly)
            {
                result = PipResultStatus.Succeeded;
            }

            return PipResult.Create(result, pipStart);
        }

        private void ReportCompositeOpaqueContents(IPipExecutionEnvironment environment, SealDirectory pip)
        {
            Contract.Assert(pip.IsComposite);
            Contract.Assert(pip.Kind == SealDirectoryKind.SharedOpaque);

            // Aggregates the content of all non-composite directories and report it
            using (var pooledAggregatedContent = Pools.FileArtifactWithAttributesSetPool.GetInstance())
            using (var filteredContentWrapper = Pools.FileArtifactWithAttributesListPool.GetInstance())
            {
                HashSet<FileArtifactWithAttributes> aggregatedContent = pooledAggregatedContent.Instance;
                var filteredContent = filteredContentWrapper.Instance;
                long duration;
                using (var sw = PipExecutionCounters[PipExecutorCounter.ComputeCompositeSharedOpaqueContentDuration].Start())
                {
                    foreach (var directoryElement in pip.ComposedDirectories)
                    {
                        // Regardless whether directoryElement is a non-composite or composite directory, it was
                        // produced by an upstream pip (ProcessPip/SealDirectoryPip respectively). At this point,
                        // FileContentManager knows the content of this directory artifact.
                        var memberContents = m_fileContentManager.ListSealedDirectoryContents(directoryElement);
                        aggregatedContent.AddRange(memberContents.Select(member =>
                            FileArtifactWithAttributes.Create(member, FileExistence.Required, m_fileContentManager.IsAllowedFileRewriteOutput(member.Path))));
                    }

                    // if the filter is specified, restrict the final content
                    if (pip.ContentFilter != null && environment.Configuration.Schedule.DisableCompositeOpaqueFilters != true)
                    {
                        var regex = new Regex(pip.ContentFilter.Value.Regex,
                            RegexOptions.IgnoreCase,
                            TimeSpan.FromMilliseconds(SealDirectoryContentFilterTimeoutMs));
                        var isIncludeFilter = pip.ContentFilter.Value.Kind == SealDirectoryContentFilter.ContentFilterKind.Include;

                        foreach (var fileArtifact in aggregatedContent)
                        {
                            if (regex.IsMatch(fileArtifact.Path.ToString(Context.PathTable)) == isIncludeFilter)
                            {
                                filteredContent.Add(fileArtifact);
                            }
                        }
                    }

                    duration = sw.Elapsed.ToMilliseconds();
                }

                // the directory artifacts that this composite shared opaque consists of might or might not be materialized
                var contents = pip.ContentFilter == null ? aggregatedContent : (IEnumerable<FileArtifactWithAttributes>)filteredContent;
                m_fileContentManager.ReportDynamicDirectoryContents(
                    pip.Directory,
                    contents,
                    PipOutputOrigin.NotMaterialized);

                Logger.Log.CompositeSharedOpaqueContentDetermined(
                    m_loggingContext,
                    pip.GetDescription(environment.Context),
                    pip.ComposedDirectories.Count,
                    aggregatedContent.Count,
                    pip.ContentFilter == null ? aggregatedContent.Count : filteredContent.Count,
                    duration);

                ExecutionLog?.PipExecutionDirectoryOutputs(new PipExecutionDirectoryOutputs
                {
                    PipId = pip.PipId,
                    DirectoryOutputs = ReadOnlyArray<(DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> fileArtifactArray)>.From(
                        new[] {
                            (pip.Directory, ReadOnlyArray<FileArtifact>.From(contents.Select(content => content.ToFileArtifact())))
                        })
                });
            }
        }

        #region IFileContentManagerHost Members

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        LoggingContext IFileContentManagerHost.LoggingContext => m_executePhaseLoggingContext;

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        SemanticPathExpander IFileContentManagerHost.SemanticPathExpander => m_semanticPathExpander;

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        IConfiguration IFileContentManagerHost.Configuration => m_configuration;

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        IArtifactContentCache IFileContentManagerHost.ArtifactContentCache => Cache.ArtifactContentCache;

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        SealDirectoryKind IFileContentManagerHost.GetSealDirectoryKind(DirectoryArtifact directory)
        {
            return GetSealDirectoryKind(directory);
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IFileContentManagerHost.TryGetSourceSealDirectory(DirectoryArtifact directory, out SourceSealWithPatterns sourceSealWithPatterns)
        {
            sourceSealWithPatterns = default;

            if (((IPipExecutionEnvironment)this).IsSourceSealedDirectory(directory, out bool allDirectories, out ReadOnlyArray<StringId> patterns))
            {
                sourceSealWithPatterns = new SourceSealWithPatterns(directory.Path, patterns, !allDirectories);
                return true;
            }

            return false;
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IFileContentManagerHost.ShouldScrubFullSealDirectory(DirectoryArtifact directory)
        {
            return ShouldScrubFullSealDirectory(directory);
        }

        /// <inheritdoc/>
        public bool TryGetCopySourceFile(FileArtifact artifact, out FileArtifact sourceFile)
        {
            var producer = PipGraph.TryGetProducer(artifact);
            if (producer.IsValid)
            {
                var pipType = PipGraph.PipTable.GetPipType(producer);
                if (pipType == PipType.CopyFile)
                {
                    var copyPip = (CopyFile)PipGraph.GetPipFromPipId(producer);
                    sourceFile = copyPip.Source;
                    return true;
                }
            }

            sourceFile = FileArtifact.Invalid;
            return false;
        }

        /// <inheritdoc/>
        public SealDirectoryKind GetSealDirectoryKind(DirectoryArtifact directory)
        {
            Contract.Requires(directory.IsValid);
            var sealDirectoryId = PipGraph.GetSealedDirectoryNode(directory).ToPipId();
            return PipGraph.PipTable.GetSealDirectoryKind(sealDirectoryId);
        }

        /// <inheritdoc/>
        public bool ShouldScrubFullSealDirectory(DirectoryArtifact directory)
        {
            Contract.Requires(directory.IsValid);
            var sealDirectoryId = PipGraph.GetSealedDirectoryNode(directory).ToPipId();
            return PipGraph.PipTable.ShouldScrubFullSealDirectory(sealDirectoryId);
        }

        private ReadOnlyArray<StringId> GetSourceSealDirectoryPatterns(DirectoryArtifact directory)
        {
            var sealDirectoryId = PipGraph.GetSealedDirectoryNode(directory).ToPipId();
            return PipGraph.PipTable.GetSourceSealDirectoryPatterns(sealDirectoryId);
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        Pip IFileContentManagerHost.GetProducer(in FileOrDirectoryArtifact artifact)
        {
            var producerId = PipGraph.GetProducer(artifact);
            return PipGraph.GetPipFromPipId(producerId);
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        PipId IFileContentManagerHost.TryGetProducerId(in FileOrDirectoryArtifact artifact)
        {
            return PipGraph.TryGetProducer(artifact);
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        string IFileContentManagerHost.GetProducerDescription(in FileOrDirectoryArtifact artifact)
        {
            var producerId = PipGraph.GetProducer(artifact);
            var producer = PipGraph.GetPipFromPipId(producerId);
            return producer.GetDescription(Context);
        }

        /// <summary>
        /// Gets the first consumer description associated with a FileOrDirectory artifact.
        /// </summary>
        /// <param name="artifact">The artifact for which to get the first consumer description.</param>
        /// <returns>The first consumer description or null if there is no consumer.</returns>
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        public string GetConsumerDescription(in FileOrDirectoryArtifact artifact)
        {
            var producerId = PipGraph.GetProducer(artifact);
            foreach (var consumerEdge in DirectedGraph.GetOutgoingEdges(producerId.ToNodeId()))
            {
                Pip consumer = PipGraph.GetPipFromPipId(consumerEdge.OtherNode.ToPipId());
                return consumer.GetDescription(Context);
            }

            // No consumer
            return null;
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> IFileContentManagerHost.ListSealDirectoryContents(DirectoryArtifact directory)
        {
            return PipGraph.ListSealedDirectoryContents(directory);
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IFileContentManagerHost.AllowArtifactReadOnly(in FileOrDirectoryArtifact artifact) => !PipGraph.MustArtifactRemainWritable(artifact);

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IFileContentManagerHost.IsPreservedOutputArtifact(in FileOrDirectoryArtifact artifact)
        {
            Contract.Requires(artifact.IsValid);

            if (m_configuration.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs == PreserveOutputsMode.Disabled)
            {
                return false;
            }

            return PipGraph.IsPreservedOutputArtifact(artifact, m_configuration.Sandbox.UnsafeSandboxConfiguration.PreserveOutputsTrustLevel);
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        bool IFileContentManagerHost.IsFileRewritten(in FileArtifact artifact)
        {
            Contract.Requires(artifact.IsValid);

            return IsFileRewritten(artifact);
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        void IFileContentManagerHost.ReportContent(FileArtifact artifact, in FileMaterializationInfo trackedFileContentInfo, PipOutputOrigin origin)
        {
            // NOTE: Artifacts may be materialized as absent path so we need to check here
            PathExistence? existence = trackedFileContentInfo.FileContentInfo.Existence;
            if (trackedFileContentInfo.Hash == WellKnownContentHashes.AbsentFile)
            {
                existence = PathExistence.Nonexistent;
            }

            if (origin != PipOutputOrigin.NotMaterialized && existence != null)
            {
                State.FileSystemView.ReportRealFileSystemExistence(artifact.Path, existence.Value);
            }

            if (artifact.IsValid && artifact.IsOutputFile)
            {
                if (existence != PathExistence.Nonexistent && trackedFileContentInfo.Hash.IsSpecialValue())
                {
                    Contract.Assert(false, I($"Hash={trackedFileContentInfo.Hash}, Length={trackedFileContentInfo.FileContentInfo.SerializedLengthAndExistence}, Existence={existence}, Path={artifact.Path.ToString(Context.PathTable)}, Origin={origin}"));
                }

                // Since it's an output file, force the existence as ExistsAsFile.
                //
                // Note: It is possible to construct FileContentInfo by calling CreateWithUnknownLength(hash, PathExistence.Nonexistent).
                // Calls to Existence property of this struct will return 'null'. This means that we would be 'overriding' the original existence.
                // However, we do not currently create such FileContentInfo's and it's improbable that we'd create them in the future,
                // so forcing the existence here should be fine.
                if (existence == null)
                {
                    existence = PathExistence.ExistsAsFile;
                }

                State.FileSystemView.ReportOutputFileSystemExistence(artifact.Path, existence.Value);
            }

            if (artifact.IsSourceFile && IncrementalSchedulingState != null && origin != PipOutputOrigin.NotMaterialized)
            {
                // Source file artifact may not have a producer because it's part of sealed source directory.
                var producer = PipGraph.TryGetProducer(artifact);

                if (producer.IsValid)
                {
                    IncrementalSchedulingState.PendingUpdates.MarkNodeClean(producer.ToNodeId());
                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.PipMarkClean);

                    IncrementalSchedulingState.PendingUpdates.MarkNodeMaterialized(producer.ToNodeId());
                    PipExecutionCounters.IncrementCounter(PipExecutorCounter.PipMarkMaterialized);
                }
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        void IFileContentManagerHost.ReportMaterializedArtifact(in FileOrDirectoryArtifact artifact)
        {
            if (artifact.IsDirectory && IncrementalSchedulingState != null)
            {
                // Ensure seal directory gets marked as materialized when file content manager reports that
                // the artifact is materialized.
                var sealDirectoryNode = PipGraph.GetSealedDirectoryNode(artifact.DirectoryArtifact);

                IncrementalSchedulingState.PendingUpdates.MarkNodeClean(sealDirectoryNode);
                PipExecutionCounters.IncrementCounter(PipExecutorCounter.PipMarkClean);

                IncrementalSchedulingState.PendingUpdates.MarkNodeMaterialized(sealDirectoryNode);
                PipExecutionCounters.IncrementCounter(PipExecutorCounter.PipMarkMaterialized);
            }

            m_pipOutputMaterializationTracker.ReportMaterializedArtifact(artifact);
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        void IFileContentManagerHost.ReportFileArtifactPlaced(in FileArtifact artifact, bool isAllowedFileRewrite)
        {
            // Don't flag allowed source rewrites as shared opaque outputs since we don't want to delete them
            // in the next build.
            if (!isAllowedFileRewrite)
            {
                MakeSharedOpaqueOutputIfNeeded(artifact.Path);
            }
        }

        private bool MakeSharedOpaqueOutputIfNeeded(AbsolutePath path)
        {
            if (!m_configuration.Sandbox.UnsafeSandboxConfiguration.SkipFlaggingSharedOpaqueOutputs() && IsPathUnderSharedOpaqueDirectory(path))
            {
                SharedOpaqueOutputHelper.EnforceFileIsSharedOpaqueOutput(path.ToString(Context.PathTable));
                return true;
            }

            return false;
        }

        private bool IsPathUnderSharedOpaqueDirectory(AbsolutePath path)
        {
            return
                PipGraph.IsPathUnderOutputDirectory(path, out var isItSharedOpaque) &&
                isItSharedOpaque;
        }

        /// <inheritdoc />
        public bool CanMaterializeFile(FileArtifact artifact)
        {
            if (!m_configuration.Schedule.EnableLazyWriteFileMaterialization)
            {
                return false;
            }

            var producerId = PipGraph.TryGetProducer(artifact);
            return producerId.IsValid && m_pipTable.GetPipType(producerId) == PipType.WriteFile;
        }

        /// <inheritdoc />
        public async Task<Possible<ContentMaterializationOrigin>> TryMaterializeFileAsync(FileArtifact artifact, OperationContext operationContext)
        {
            var producerId = PipGraph.TryGetProducer(artifact);
            Contract.Assert(producerId.IsValid && m_pipTable.GetPipType(producerId) == PipType.WriteFile);

            if (!m_configuration.Schedule.EnableLazyWriteFileMaterialization)
            {
                return new Failure<string>(I($"Failed to materialize write file destination because lazy write file materialization is not enabled"));
            }

            var writeFile = (WriteFile)m_pipTable.HydratePip(producerId, PipQueryContext.SchedulerFileContentManagerHostMaterializeFile);
            var result = await PipExecutor.TryExecuteWriteFileAsync(operationContext, this, writeFile, materializeOutputs: true, reportOutputs: false);
            return result.Then<ContentMaterializationOrigin>(
                status =>
                {
                    if (status.IndicatesFailure())
                    {
                        return new Failure<string>(I($"Failed to materialize write file destination because write file pip execution results in '{status.ToString()}'"));
                    }

                    if (IncrementalSchedulingState != null)
                    {
                        IncrementalSchedulingState.PendingUpdates.MarkNodeMaterialized(producerId.ToNodeId());
                        PipExecutionCounters.IncrementCounter(PipExecutorCounter.PipMarkMaterialized);
                    }

                    return status.ToContentMaterializationOriginHidingExecution();
                });
        }

        #endregion IFileContentManagerHost Members

        #region IOperationTrackerHost Members

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        string IOperationTrackerHost.GetDescription(in FileOrDirectoryArtifact artifact)
        {
            if (artifact.IsValid)
            {
                if (artifact.IsFile)
                {
                    return I($"File: {artifact.Path.ToString(Context.PathTable)} [{artifact.FileArtifact.RewriteCount}]");
                }
                else
                {
                    return I($"Directory: {artifact.Path.ToString(Context.PathTable)} [{artifact.DirectoryArtifact.PartialSealId}]");
                }
            }

            return null;
        }

        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        string IOperationTrackerHost.GetDescription(PipId pipId)
        {
            if (pipId.IsValid)
            {
                return PipGraph.GetPipFromPipId(pipId).GetDescription(Context);
            }

            return null;
        }

        #endregion IOperationTrackerHost Members

        #region Event Logging

        private delegate void PipProvenanceEvent(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDesc,
            string pipValueId);

        private delegate void PipProvenanceEventWithFilePath(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDesc,
            string pipValueId,
            string filePath);

        // Handy for errors related to sealed directories, since there is a directory root associated with the file.
        private delegate void PipProvenanceEventWithFilePathAndDirectoryPath(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDesc,
            string pipValueId,
            string filePath,
            string directoryPath);

        private delegate void PipProvenanceEventWithFilePathAndRelatedPip(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDesc,
            string pipValueId,
            string outputFile,
            string producingPipDesc,
            string producingPipValueId);

        // Handy for errors related to sealed directories, since there is a directory root associated with the file.
        private delegate void PipProvenanceEventWithFilePathAndDirectoryPathAndRelatedPip(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDesc,
            string pipValueId,
            string outputFile,
            string directoryPath,
            string producingPipDesc,
            string producingPipValueId);

        private PipProvenance m_dummyProvenance;

        private CancellationTokenRegistration m_cancellationTokenRegistration;

        private PipProvenance GetDummyProvenance()
        {
            Contract.Ensures(Contract.Result<PipProvenance>() != null);
            return m_dummyProvenance = m_dummyProvenance ?? PipProvenance.CreateDummy(Context);
        }

        private void LogEventWithPipProvenance(RunnablePip runnablePip, PipProvenanceEvent pipEvent)
        {
            Contract.Requires(pipEvent != null);
            Contract.Requires(runnablePip != null);

            PipProvenance provenance = runnablePip.Pip.Provenance ?? GetDummyProvenance();
            pipEvent(
                runnablePip.LoggingContext,
                provenance.Token.Path.ToString(Context.PathTable),
                provenance.Token.Line,
                provenance.Token.Position,
                runnablePip.Pip.GetDescription(Context),
                provenance.OutputValueSymbol.ToString(Context.SymbolTable));
        }

        private delegate void PipStartEvent(
            LoggingContext loggingContext,
            string file,
            int line,
            int column,
            string pipDesc,
            string pipValueId);

        private delegate void PipEndEvent(LoggingContext loggingContext, string pipDesc, string pipValueId, int status, long ticks);

        private LoggingContext LogEventPipStart(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip != null);
            var pip = runnablePip.Pip;

            PipProvenance provenance = pip.Provenance;

            if (provenance == null)
            {
                return m_executePhaseLoggingContext;
            }

            LoggingContext pipLoggingContext = new LoggingContext(
                m_executePhaseLoggingContext,
                IsDistributedWorker ? "remote call" : pip.PipId.ToString(),
                runnablePip.Observer.GetActivityId(runnablePip));

            EventSource.SetCurrentThreadActivityId(pipLoggingContext.ActivityId);

            if (pip.PipType == PipType.Process)
            {
                var process = pip as Process;
                Contract.Assume(process != null);

                string executablePath = process.Executable.Path.ToString(Context.PathTable);

                FileMaterializationInfo executableVersionedHash;
                string executableHashStr =
                    (m_fileContentManager.TryGetInputContent(process.Executable, out executableVersionedHash) &&
                     executableVersionedHash.Hash != WellKnownContentHashes.UntrackedFile)
                        ? executableVersionedHash.Hash.ToHex()
                        : executablePath;

                Logger.Log.ProcessStart(
                    pipLoggingContext,
                    provenance.Token.Path.ToString(Context.PathTable),
                    provenance.Token.Line,
                    provenance.Token.Position,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    executablePath,
                    executableHashStr);
            }
            else
            {
                PipStartEvent startEvent = null;

                switch (pip.PipType)
                {
                    case PipType.WriteFile:
                        startEvent = Logger.Log.WriteFileStart;
                        break;
                    case PipType.CopyFile:
                        startEvent = Logger.Log.CopyFileStart;
                        break;
                }

                if (startEvent != null)
                {
                    startEvent(
                        pipLoggingContext,
                        provenance.Token.Path.ToString(Context.PathTable),
                        provenance.Token.Line,
                        provenance.Token.Position,
                        pip.GetDescription(Context),
                        provenance.OutputValueSymbol.ToString(Context.SymbolTable));
                }
            }

            return pipLoggingContext;
        }

        private void LogEventPipEnd(LoggingContext pipLoggingContext, Pip pip, PipResultStatus status, long ticks)
        {
            Contract.Requires(pip != null);

            PipProvenance provenance = pip.Provenance;

            if (provenance == null)
            {
                return;
            }

            EventSource.SetCurrentThreadActivityId(pipLoggingContext.ActivityId);

            if (pip.PipType == PipType.Process)
            {
                var process = pip as Process;
                Contract.Assume(process != null);

                string executablePath = process.Executable.Path.ToString(Context.PathTable);

                FileMaterializationInfo executableVersionedHash;
                string executableHashStr =
                    (m_fileContentManager.TryGetInputContent(process.Executable, out executableVersionedHash) &&
                     executableVersionedHash.Hash != WellKnownContentHashes.UntrackedFile)
                        ? executableVersionedHash.Hash.ToHex()
                        : executablePath;

                Logger.Log.ProcessEnd(
                    pipLoggingContext,
                    pip.GetDescription(Context),
                    provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                    (int)status,
                    ticks,
                    executableHashStr);
            }
            else
            {
                PipEndEvent endEvent = null;

                switch (pip.PipType)
                {
                    case PipType.WriteFile:
                        endEvent = Logger.Log.WriteFileEnd;
                        break;
                    case PipType.CopyFile:
                        endEvent = Logger.Log.CopyFileEnd;
                        break;
                }

                if (endEvent != null)
                {
                    endEvent(
                        pipLoggingContext,
                        pip.GetDescription(Context),
                        provenance.OutputValueSymbol.ToString(Context.SymbolTable),
                        (int)status,
                        ticks);
                }
            }

            EventSource.SetCurrentThreadActivityId(pipLoggingContext.ParentActivityId);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        private static ExecutionLogFileTarget CreateExecutionLog(
            IConfiguration configuration,
            PipExecutionContext context,
            PipGraph pipGraph,
            ExtraFingerprintSalts salts,
            LoggingContext loggingContext)
        {
            var executionLogPath = configuration.Logging.ExecutionLog;
            if (configuration.Logging.LogExecution && executionLogPath.IsValid && configuration.Engine.Phase.HasFlag(EnginePhases.Execute))
            {
                var executionLogPathString = executionLogPath.ToString(context.PathTable);

                FileStream executionLogStream;

                try
                {
                    FileUtilities.CreateDirectoryWithRetry(Path.GetDirectoryName(executionLogPathString));
                    executionLogStream = File.Open(executionLogPathString, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
                }
                catch (Exception ex)
                {
                    Logger.Log.UnableToCreateLogFile(loggingContext, executionLogPathString, ex.Message);
                    throw new BuildXLException("Unable to create execution log file: ", ex);
                }

                try
                {
                    // The path table is either:
                    // 1. Newly loaded - all paths are serialized so taking the last path value is valid
                    // 2. Populated with all paths for files in constructed scheduled and will be serialized later - taking the current last
                    // path is safe since at least the current set of paths will be serialized
                    var lastStaticAbsolutePathValue = pipGraph.MaxAbsolutePathIndex;

                    var logFile = new BinaryLogger(executionLogStream, context, pipGraph.GraphId, lastStaticAbsolutePathValue);
                    var executionLogTarget = new ExecutionLogFileTarget(logFile, disabledEventIds: configuration.Logging.NoExecutionLog);
                    executionLogTarget.BuildSessionConfiguration(new BuildSessionConfigurationEventData(salts));

                    return executionLogTarget;
                }
                catch
                {
                    executionLogStream.Dispose();
                    throw;
                }
            }

            return null;
        }

        private static FingerprintStoreExecutionLogTarget CreateFingerprintStoreTarget(
            LoggingContext loggingContext,
            IConfiguration configuration,
            PipExecutionContext context,
            PipTable pipTable,
            PipContentFingerprinter fingerprinter,
            EngineCache cache,
            IReadonlyDirectedGraph graph,
            CounterCollection<FingerprintStoreCounters> fingerprintStoreCounters,
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance = null,
            FingerprintStoreTestHooks testHooks = null)
        {
            if (configuration.FingerprintStoreEnabled())
            {
                return FingerprintStoreExecutionLogTarget.Create(
                    context,
                    pipTable,
                    fingerprinter,
                    loggingContext,
                    configuration,
                    cache,
                    graph,
                    fingerprintStoreCounters,
                    runnablePipPerformance,
                    testHooks);
            }

            return null;
        }

        #endregion Event Logging

        #region Helpers

        private PipRuntimeInfo GetPipRuntimeInfo(PipId pipId)
        {
            return GetPipRuntimeInfo(pipId.ToNodeId());
        }

        private PipRuntimeInfo GetPipRuntimeInfo(NodeId nodeId)
        {
            Contract.Assume(IsInitialized);

            var info = m_pipRuntimeInfos[(int)nodeId.Value];
            if (info == null)
            {
                Interlocked.CompareExchange(ref m_pipRuntimeInfos[(int)nodeId.Value], new PipRuntimeInfo(), null);
            }

            info = m_pipRuntimeInfos[(int)nodeId.Value];
            Contract.Assume(info != null);
            return info;
        }

        #endregion Helpers

        #region Schedule Requests

        /// <summary>
        /// Retrieves the list of pips of a particular type that are in the provided state
        /// </summary>
        public IEnumerable<PipReference> RetrievePipReferencesByStateOfType(PipType pipType, PipState state)
        {
            // This method may be called externally after this Scheduler has been disposed, such as when FancyConsole
            // calls it from another thread. Calls should not be honored after it has been disposed because there's
            // no guarantee about the state of the underlying PipTable that gets queried.
            lock (m_statusLock)
            {
                if (!m_isDisposed)
                {
                    foreach (PipId pipId in m_pipTable.Keys)
                    {
                        if (m_pipTable.GetPipType(pipId) == pipType &&
                            GetPipState(pipId) == state)
                        {
                            yield return new PipReference(m_pipTable, pipId, PipQueryContext.PipGraphRetrievePipsByStateOfType);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves a list of all externally executing process pips
        /// </summary>
        public IEnumerable<PipReference> RetrieveExecutingProcessPips()
        {
            lock (m_statusLock)
            {
                if (!m_isDisposed)
                {
                    foreach (var item in LocalWorker.RunningPipExecutorProcesses)
                    {
                        yield return new PipReference(m_pipTable, item.Key, PipQueryContext.PipGraphRetrievePipsByStateOfType);
                    }
                }
            }
        }

        /// <summary>
        /// Evaluates a filter. All nodes satisfying the filter are put in m_explicitlyScheduledNodes.
        /// Returns all nodes that must be scheduled - this includes the explicitly scheduled,
        /// their dependencies and (if filter.DependencySelection == DependencySelection.DependenciesAndDependents)
        /// all their dependents.
        /// </summary>
        private bool TryGetFilteredNodes(LoggingContext loggingContext, RootFilter filter, SchedulerState state, out IEnumerable<NodeId> includedNodes)
        {
            Contract.Requires(filter != null);
            Contract.Assume(IsInitialized);

            RangedNodeSet filterPassingNodesNotYetScheduled;

            // If the previous state is not null and root filter matches, do not need to filter nodes again.
            if (state?.RootFilter != null && state.RootFilter.Matches(filter))
            {
                filterPassingNodesNotYetScheduled = state.FilterPassingNodes;
            }
            else if (!PipGraph.FilterNodesToBuild(
                loggingContext,
                filter,
                out filterPassingNodesNotYetScheduled))
            {
                // Find which nodes are in the set.
                Contract.Assume(loggingContext.ErrorWasLogged, "PipGraph.FilterNodesToBuild returned false but didn't log an error");
                includedNodes = new HashSet<NodeId>();
                return false;
            }

            // Save the filter and passing nodes for future builds (for SchedulerState in EngineState)
            FilterPassingNodes = filterPassingNodesNotYetScheduled.Clone();
            RootFilter = filter;

            m_explicitlyScheduledNodes = new HashSet<NodeId>();
            m_explicitlyScheduledProcessNodes = new HashSet<NodeId>();
            foreach (var filteredNode in filterPassingNodesNotYetScheduled)
            {
                m_explicitlyScheduledNodes.Add(filteredNode);
                if (m_pipTable.GetPipType(filteredNode.ToPipId()) == PipType.Process)
                {
                    m_explicitlyScheduledProcessNodes.Add(filteredNode);
                }
            }

            // Calculate nodes to schedule based off of explicitly scheduled nodes
            var calculatedNodes = CalculateNodesToSchedule(
                loggingContext,
                explicitlySelectedNodes: m_explicitlyScheduledNodes);

            includedNodes = ScheduleServiceFinalizations(calculatedNodes);
            return true;
        }

        private IEnumerable<NodeId> ScheduleServiceFinalizations(IEnumerable<NodeId> calculatedNodes)
        {
            // If there are any service client nodes, make sure corresponding service finalizers are included
            var scheduledServices = new HashSet<PipId>();
            foreach (var node in calculatedNodes)
            {
                var mutable = m_pipTable.GetMutable(node.ToPipId());
                if (mutable.PipType == PipType.Ipc || mutable.PipType == PipType.Process)
                {
                    ProcessMutablePipState processMutable = mutable as ProcessMutablePipState;
                    Contract.Assert(mutable != null, "Unexpected mutable pip type");
                    var nodeServiceInfo = processMutable.ServiceInfo;
                    if (nodeServiceInfo != null && nodeServiceInfo.Kind == ServicePipKind.ServiceClient)
                    {
                        scheduledServices.UnionWith(nodeServiceInfo.ServicePipDependencies);
                    }
                }
            }

            // if there are no services, don't bother creating a union
            if (!scheduledServices.Any())
            {
                return calculatedNodes;
            }

            // else, create a union of calculated nodes and finalization pips of all scheduled services
            var union = new HashSet<NodeId>(calculatedNodes);
            foreach (var servicePipId in scheduledServices)
            {
                ProcessMutablePipState processMutable = m_pipTable.GetMutable(servicePipId) as ProcessMutablePipState;
                Contract.Assert(processMutable != null, "Unexpected mutable pip type");
                var servicePipServiceInfo = processMutable.ServiceInfo;
                if (servicePipServiceInfo != null)
                {
                    foreach (var serviceFinalizationPipId in servicePipServiceInfo.FinalizationPipIds)
                    {
                        union.Add(serviceFinalizationPipId.ToNodeId());
                    }
                }
            }

            return union;
        }

        private IEnumerable<NodeId> CalculateNodesToSchedule(
            LoggingContext loggingContext,
            IEnumerable<NodeId> explicitlySelectedNodes = null,
            bool scheduleDependents = false)
        {
            var forceSkipDepsMode = m_configuration.Schedule.ForceSkipDependencies;

            if (explicitlySelectedNodes == null)
            {
                if (IncrementalSchedulingState == null)
                {
                    // Short cut.
                    return DirectedGraph.Nodes;
                }

                // We don't select nodes explicitly (through filters). This also means that we select all nodes.
                // BuildSetCalculator will add the meta-pips after calculating the nodes to scheduled.
                explicitlySelectedNodes = DirectedGraph.Nodes.Where(node => !m_pipTable.GetPipType(node.ToPipId()).IsMetaPip());
                forceSkipDepsMode = ForceSkipDependenciesMode.Disabled;
            }

            var buildSetCalculator = new SchedulerBuildSetCalculator(loggingContext, this);
            var scheduledNodesResult = buildSetCalculator.GetNodesToSchedule(
                scheduleDependents: scheduleDependents,
                explicitlyScheduledNodes: explicitlySelectedNodes,
                forceSkipDepsMode: forceSkipDepsMode,
                scheduleMetaPips: m_configuration.Schedule.ScheduleMetaPips);

            // Update counters to reflect pips that are marked clean from incremental scheduling
            m_numProcessesIncrementalSchedulingPruned = scheduledNodesResult.IncrementalSchedulingCacheHitProcesses - scheduledNodesResult.CleanMaterializedProcessFrontierCount;
            m_numProcessPipsSatisfiedFromCache += m_numProcessesIncrementalSchedulingPruned;
            for (int i = 0; i < m_numProcessesIncrementalSchedulingPruned; i++)
            {
                m_pipStateCounters.AccumulateTransition(PipState.Ignored, PipState.Done, PipType.Process);
            }

            m_numProcessPipsCompleted += m_numProcessesIncrementalSchedulingPruned;
            m_mustExecuteNodesForDirtyBuild = scheduledNodesResult.MustExecuteNodes;
            return scheduledNodesResult.ScheduledNodes;
        }

        /// <summary>
        /// Maximum number of external processes run concurrently so far.
        /// </summary>
        public long MaxExternalProcessesRan => Volatile.Read(ref m_maxExternalProcessesRan);

        /// <inheritdoc/>
        public ProcessInContainerManager ProcessInContainerManager { get; }

        /// <inheritdoc/>
        public VmInitializer VmInitializer { get; }

        /// <inheritdoc/>
        public SymlinkedAccessResolver SymlinkedAccessResolver { get; }

        private long m_maxExternalProcessesRan;

        /// <inheritdoc/>
        public void SetMaxExternalProcessRan()
        {
            long currentMaxRunning;
            do
            {
                currentMaxRunning = MaxExternalProcessesRan;
            }
            while (Interlocked.CompareExchange(ref m_maxExternalProcessesRan, PipExecutionCounters.GetCounterValue(PipExecutorCounter.ExternalProcessCount), currentMaxRunning) != currentMaxRunning);
        }

#pragma warning disable CA1010 // Collections should implement generic interface
        private sealed class StatusRows : IEnumerable
#pragma warning restore CA1010 // Collections should implement generic interface
        {
            private readonly List<string> m_headers = new List<string>();
            private readonly List<bool> m_includeInSnapshot = new List<bool>();
            private readonly List<Func<StatusEventData, object>> m_rowValueGetters = new List<Func<StatusEventData, object>>();
            private bool m_sealed;

            public void Add(string header, Func<StatusEventData, object> rowValueGetter, bool includeInSnapshot = true)
            {
                Contract.Assert(!m_sealed);
                m_headers.Add(header);
                m_includeInSnapshot.Add(includeInSnapshot);
                m_rowValueGetters.Add(rowValueGetter);
            }

            public void Add(Action<StatusRows> rowAdder)
            {
                rowAdder(this);
            }

            public void Add<T>(IEnumerable<T> items, Action<StatusRows, T> rowAdder)
            {
                foreach (var item in items)
                {
                    rowAdder(this, item);
                }
            }

            public void Add<T>(IEnumerable<T> items, Func<T, string> itemHeaderGetter, Func<T, int, Func<StatusEventData, object>> itemRowValueGetter)
            {
                if (items == null)
                {
                    return;
                }

                int index = 0;
                foreach (var item in items)
                {
                    Add(itemHeaderGetter(item), itemRowValueGetter(item, index));
                    index++;
                }
            }

            public IEnumerator GetEnumerator()
            {
                foreach (var header in m_headers)
                {
                    yield return header;
                }
            }

            public string PrintHeaders()
            {
                Contract.Assert(m_sealed);
                return string.Join(",", m_headers);
            }

            public IDictionary<string, string> GetSnapshot(StatusEventData data)
            {
                Dictionary<string, string> snapshot = new Dictionary<string, string>();
                for (int i = 0; i < m_headers.Count; i++)
                {
                    if (m_includeInSnapshot[i])
                    {
                        snapshot.Add(m_headers[i], m_rowValueGetters[i](data).ToString());
                    }
                }

                return snapshot;
            }

            public string PrintRow(StatusEventData data)
            {
                Contract.Assert(m_sealed);
                return string.Join(",", m_rowValueGetters.Select((rowValueGetter, index) => rowValueGetter(data).ToString().PadLeft(m_headers[index].Length)));
            }

            public StatusRows Seal()
            {
                m_sealed = true;
                return this;
            }
        }

        /// <summary>
        /// Build set calculator which interfaces with the scheduler
        /// </summary>
        private sealed class SchedulerBuildSetCalculator : BuildSetCalculator<Process, AbsolutePath, FileArtifact, DirectoryArtifact>
        {
            private readonly Scheduler m_scheduler;

            public SchedulerBuildSetCalculator(LoggingContext loggingContext, Scheduler scheduler)
                : base(
                    loggingContext,
                    scheduler.PipGraph.DirectedGraph,
                    scheduler.IncrementalSchedulingState?.DirtyNodeTracker,
                    scheduler.PipExecutionCounters)
            {
                m_scheduler = scheduler;
            }

            protected override bool ExistsAsFile(AbsolutePath path)
            {
                Possible<PathExistence> possibleProbeResult = m_scheduler.m_localDiskContentStore.TryProbeAndTrackPathForExistence(path);
                return possibleProbeResult.Succeeded && possibleProbeResult.Result == PathExistence.ExistsAsFile;
            }

            protected override ReadOnlyArray<DirectoryArtifact> GetDirectoryDependencies(Process process)
            {
                return process.DirectoryDependencies;
            }

            protected override ReadOnlyArray<FileArtifact> GetFileDependencies(Process process)
            {
                return process.Dependencies;
            }

            protected override AbsolutePath GetPath(FileArtifact file)
            {
                return file.Path;
            }

            protected override string GetPathString(AbsolutePath path)
            {
                return path.ToString(m_scheduler.Context.PathTable);
            }

            protected override PipType GetPipType(NodeId node)
            {
                return m_scheduler.m_pipTable.GetPipType(node.ToPipId());
            }

            protected override Process GetProcess(NodeId node)
            {
                return (Process)m_scheduler.m_pipTable.HydratePip(node.ToPipId(), PipQueryContext.SchedulerAreInputsPresentForSkipDependencyBuild);
            }

            protected override FileArtifact GetCopyFile(NodeId node)
            {
                return ((CopyFile)m_scheduler.m_pipTable.HydratePip(node.ToPipId(), PipQueryContext.SchedulerAreInputsPresentForSkipDependencyBuild)).Source;
            }

            protected override DirectoryArtifact GetSealDirectoryArtifact(NodeId node)
            {
                return ((SealDirectory)m_scheduler.m_pipTable.HydratePip(node.ToPipId(), PipQueryContext.SchedulerAreInputsPresentForSkipDependencyBuild)).Directory;
            }

            protected override ReadOnlyArray<FileArtifact> ListSealedDirectoryContents(DirectoryArtifact directory)
            {
                return m_scheduler.PipGraph.ListSealedDirectoryContents(directory);
            }

            protected override bool IsFileRequiredToExist(FileArtifact file)
            {
                // Source files are not required to exist and rerunning the hash source file pip
                // will not cause them to exist so this shouldn't invalidate the existence check.
                return !file.IsSourceFile;
            }

            protected override NodeId GetProducer(FileArtifact file)
            {
                return m_scheduler.PipGraph.GetProducerNode(file);
            }

            protected override NodeId GetProducer(DirectoryArtifact directory)
            {
                return m_scheduler.PipGraph.GetSealedDirectoryNode(directory);
            }

            protected override bool IsDynamicKindDirectory(NodeId node)
            {
                return m_scheduler.m_pipTable.GetSealDirectoryKind(node.ToPipId()).IsDynamicKind();
            }

            protected override SealDirectoryKind GetSealedDirectoryKind(NodeId node)
            {
                return m_scheduler.m_pipTable.GetSealDirectoryKind(node.ToPipId());
            }

            protected override ModuleId GetModuleId(NodeId node)
            {
                return m_scheduler.m_pipTable.HydratePip(node.ToPipId(), PipQueryContext.SchedulerAreInputsPresentForSkipDependencyBuild).Provenance?.ModuleId ?? ModuleId.Invalid;
            }

            protected override string GetModuleName(ModuleId moduleId)
            {
                if (!moduleId.IsValid)
                {
                    return "Invalid";
                }

                var pip = (ModulePip)m_scheduler.m_pipTable.HydratePip(
                    m_scheduler.PipGraph.Modules[moduleId].ToPipId(),
                    PipQueryContext.SchedulerAreInputsPresentForSkipDependencyBuild);
                return pip.Identity.ToString(m_scheduler.Context.StringTable);
            }

            protected override string GetDescription(NodeId node)
            {
                var pip = m_scheduler.m_pipTable.HydratePip(
                    node.ToPipId(),
                    PipQueryContext.SchedulerAreInputsPresentForSkipDependencyBuild);
                var moduleId = pip.Provenance?.ModuleId ?? ModuleId.Invalid;
                return pip.GetDescription(m_scheduler.Context) + " - Module: " + GetModuleName(moduleId);
            }

            protected override bool IsRewrittenPip(NodeId node)
            {
                return m_scheduler.PipGraph.IsRewrittenPip(node.ToPipId());
            }
        }

        /// <summary>
        /// Inform the scheduler that we want to terminate ASAP (but with clean shutdown as needed).
        /// </summary>
        private void RequestTermination(bool cancelQueue = true)
        {
            if (m_scheduleTerminating)
            {
                return;
            }

            // This flag prevents normally-scheduled pips (i.e., by refcount) from starting (thus m_numPipsQueuedOrRunning should
            // reach zero quickly). But we do allow further pips to run inline (see RunPipInline); that's safe from an error
            // reporting perspective since m_hasFailures latches to false.
            m_scheduleTerminating = true;

            // A build that got canceled certainly didn't succeed.
            m_hasFailures = true;

            if (cancelQueue)
            {
                // We cancel the queue for more aggressive but still graceful cancellation.
                m_pipQueue.Cancel();
            }
        }

        #endregion

        /// <inheritdoc />
        public void Dispose()
        {
            lock (m_statusLock)
            {
                m_isDisposed = true;
            }

            m_cancellationTokenRegistration.Dispose();

            ExecutionLog?.Dispose();
            SandboxConnection?.Dispose();

            LocalWorker.Dispose();
            m_allWorker?.Dispose();

            m_performanceAggregator?.Dispose();
            m_ipcProvider.Dispose();
            m_apiServer?.Dispose();
            m_pluginManager?.Dispose();

            m_pipTwoPhaseCache?.CloseAsync().GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public bool IsFileRewritten(FileArtifact file)
        {
            var latestFile = PipGraph.TryGetLatestFileArtifactForPath(file.Path);
            return latestFile.IsValid && latestFile.RewriteCount > file.RewriteCount;
        }

        /// <inheritdoc />
        public bool ShouldCreateHandleWithSequentialScan(FileArtifact file)
        {
            if (m_scheduleConfiguration.CreateHandleWithSequentialScanOnHashingOutputFiles
                && file.IsOutputFile
                && PipGraph.TryGetLatestFileArtifactForPath(file.Path) == file
                && m_outputFileExtensionsForSequentialScan.Contains(file.Path.GetExtension(Context.PathTable)))
            {
                PipExecutionCounters.IncrementCounter(PipExecutorCounter.CreateOutputFileHandleWithSequentialScan);
                return true;
            }

            return false;
        }

        /// <summary>
        /// <see cref="PipExecutionState.LazyDeletionOfSharedOpaqueOutputsEnabled"/>
        /// </summary>
        internal void SetLazyDeletionOfSharedOpaqueOutputsEnabled()
        {
            m_lazyDeletionOfSharedOpaqueOutputsEnabled = true;
        }
    }
}
