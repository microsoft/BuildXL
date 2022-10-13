// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Tracing;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Defines an in-process worker.
    /// </summary>
    public class LocalWorker : Worker
    {
        /// <summary>
        /// Machine name 
        /// </summary>
        public static string MachineName = Environment.MachineName;

        /// <summary>
        /// Set of pips that are currently executing. Executing here means running under PipExecutor.
        /// </summary>
        public ConcurrentDictionary<PipId, Unit> RunningPipExecutorProcesses = new ConcurrentDictionary<PipId, Unit>();

        /// <summary>
        /// The number of processes that are currently running (i.e., the associated OS-process is still alive and running)
        /// </summary>
        public int RunningProcesses => Volatile.Read(ref m_currentlyRunningPipCount);

        private int m_currentlyRunningPipCount = 0;
        private readonly IDetoursEventListener m_detoursListener;
        private readonly IPipQueue m_pipQueue;

        /// <inheritdoc />
        public override int TotalProcessSlots
        {
            get
            {
                return MemoryResourceAvailable && CpuResourceAvailable ? base.TotalProcessSlots : 1;
            }
        }

        /// <summary>
        /// Whether Cpu resource is available.
        /// </summary>
        public bool CpuResourceAvailable = true;

        /// <summary>
        /// The state of the memory resource availability.
        /// </summary>
        public MemoryResource MemoryResource
        {
            get
            {
                return m_memoryResource;
            }

            set
            {
                var oldValue = m_memoryResource;
                m_memoryResource = value;
                OnWorkerResourcesChanged(WorkerResource.MemoryResourceAvailable, increased: value == MemoryResource.Available && oldValue != MemoryResource.Available);
            }
        }

        private MemoryResource m_memoryResource = MemoryResource.Available;

        /// <summary>
        /// Gets or sets whether sufficient resources are available.
        /// </summary>
        public bool MemoryResourceAvailable => MemoryResource == MemoryResource.Available;

        private readonly ConcurrentBigSet<PipId> m_pipsSourceHashed;

        /// <summary>
        /// Constructor
        /// </summary>
        public LocalWorker(IScheduleConfiguration scheduleConfig, IPipQueue pipQueue, IDetoursEventListener detoursListener, PipExecutionContext context)
            : base(workerId: 0, name: "#0 (Local)", context: context, workerIpAddress: MachineName)
        {
            TotalProcessSlots = scheduleConfig.EffectiveMaxProcesses;
            TotalCacheLookupSlots = scheduleConfig.MaxCacheLookup;
            TotalLightSlots = scheduleConfig.MaxLightProcesses;
            TotalMaterializeInputSlots = scheduleConfig.MaxMaterialize;
            m_detoursListener = detoursListener;
            m_pipQueue = pipQueue;
            m_pipsSourceHashed = new ConcurrentBigSet<PipId>();
            Start();
        }

        /// <summary>
        /// Adjusts the total process slots
        /// </summary>
        public void AdjustTotalProcessSlots(int newTotalSlots)
        {
            // Slots in worker control how many pips should be assigned to the workers for the corresponding PipExecutionStep. 
            // When a pip is assigned to a worker, it does not mean that the pip will start running.
            // Whether the pip will run or not depends on the limits of the corresponding dispatcher. 
            // That's why, when we update slots of the worker, we also update the dispatcher limit to sync.

            TotalProcessSlots = newTotalSlots;
            m_pipQueue.SetMaxParallelDegreeByKind(DispatcherKind.CPU, newTotalSlots);
        }

        /// <summary>
        /// Adjusts the total cache lookup slots
        /// </summary>
        public void AdjustTotalCacheLookupSlots(int newTotalSlots)
        {
            TotalCacheLookupSlots = newTotalSlots;
            m_pipQueue.SetMaxParallelDegreeByKind(DispatcherKind.CacheLookup, newTotalSlots);
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeInputsAsync(ProcessRunnablePip runnablePip)
        {
            using (OnPipExecutionStarted(runnablePip))
            {
                var result = await PipExecutor.MaterializeInputsAsync(runnablePip.OperationContext, runnablePip.Environment, runnablePip.Pip);
                return result;
            }
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeOutputsAsync(RunnablePip runnablePip)
        {
            // Need to create separate operation context since there may be concurrent operations on representing executions on remote workers
            using (var operationContext = runnablePip.OperationContext.StartAsyncOperation(OperationKind.PassThrough))
            using (OnPipExecutionStarted(runnablePip, operationContext))
            {
                return await PipExecutor.MaterializeOutputsAsync(operationContext, runnablePip.Environment, runnablePip.Pip);
            }
        }

        /// <inheritdoc />
        public override async Task<ExecutionResult> ExecuteProcessAsync(ProcessRunnablePip processRunnable)
        {
            using (OnPipExecutionStarted(processRunnable))
            {
                RunningPipExecutorProcesses.TryAdd(processRunnable.PipId, Unit.Void);

                var environment = processRunnable.Environment;
                var process = processRunnable.Process;
                var operationContext = processRunnable.OperationContext;

                ContentFingerprint? fingerprint = processRunnable.CacheResult?.Fingerprint;

                ExecutionResult executionResult;
                if (await TryHashSourceDependenciesAsync(processRunnable))
                {
                    executionResult = await PipExecutor.ExecuteProcessAsync(
                        operationContext,
                        environment,
                        environment.State.GetScope(process),
                        process,
                        fingerprint,
                        processIdListener: UpdateCurrentlyRunningPipsCount,
                        expectedMemoryCounters: processRunnable.ExpectedMemoryCounters.Value,
                        detoursEventListener: m_detoursListener,
                        runLocation: processRunnable.RunLocation);
                }
                else
                {
                    executionResult = ExecutionResult.GetFailureNotRunResult(operationContext);
                }

                processRunnable.SetExecutionResult(executionResult);

                Unit ignore;
                RunningPipExecutorProcesses.TryRemove(processRunnable.PipId, out ignore);

                return executionResult;
            }
        }

        private void UpdateCurrentlyRunningPipsCount(int pipProcessId)
        {
            if (pipProcessId > 0)
            {
                // process started
                Interlocked.Increment(ref m_currentlyRunningPipCount);
            }
            else if (pipProcessId < 0)
            {
                // process exited
                Interlocked.Decrement(ref m_currentlyRunningPipCount);
            }
        }

        /// <inheritdoc />
        public override async Task<ExecutionResult> PostProcessAsync(ProcessRunnablePip runnablePip)
        {
            using (OnPipExecutionStarted(runnablePip))
            {
                var pipScope = runnablePip.Environment.State.GetScope(runnablePip.Process);
                var cacheableProcess = runnablePip.CacheableProcess ?? pipScope.GetCacheableProcess(runnablePip.Process, runnablePip.Environment);

                return await PipExecutor.PostProcessExecutionAsync(
                    operationContext: runnablePip.OperationContext,
                    environment: runnablePip.Environment,
                    state: pipScope,
                    cacheableProcess: cacheableProcess,
                    processExecutionResult: runnablePip.ExecutionResult);
            }
        }

        /// <inheritdoc />
        public override async Task<PipResult> ExecuteIpcAsync(RunnablePip runnablePip)
        {
            using (OnPipExecutionStarted(runnablePip))
            {
                var environment = runnablePip.Environment;
                var ipcPip = (IpcPip)runnablePip.Pip;
                var operationContext = runnablePip.OperationContext;
                ExecutionResult executionResult;

                var result = await PipExecutor.MaterializeInputsAsync(runnablePip.OperationContext, runnablePip.Environment, runnablePip.Pip);
                if (result == PipResultStatus.Failed)
                {
                    executionResult = ExecutionResult.GetFailureNotRunResult(operationContext);
                }
                else if (await TryHashSourceDependenciesAsync(runnablePip))
                {
                    executionResult = await PipExecutor.ExecuteIpcAsync(operationContext, environment, ipcPip);
                }
                else
                {
                    executionResult = ExecutionResult.GetFailureNotRunResult(operationContext);
                }

                runnablePip.SetExecutionResult(executionResult);
                return RunnablePip.CreatePipResultFromExecutionResult(runnablePip.StartTime, executionResult);
            }
        }

        /// <inheritdoc />
        public override async Task<(RunnableFromCacheResult, PipResultStatus)> CacheLookupAsync(
            ProcessRunnablePip runnablePip,
            PipExecutionState.PipScopeState state,
            CacheableProcess cacheableProcess,
            bool avoidRemoteLookups = false)
        {
            using (OnPipExecutionStarted(runnablePip))
            {
                RunnableFromCacheResult cacheResult = null;
                if (await TryHashSourceDependenciesAsync(runnablePip))
                {
                    cacheResult = await PipExecutor.TryCheckProcessRunnableFromCacheAsync(runnablePip, state, cacheableProcess, avoidRemoteLookups);
                }

                return ValueTuple.Create(cacheResult, cacheResult == null ? PipResultStatus.Failed : PipResultStatus.Succeeded);
            }
        }

        private async Task<bool> TryHashSourceDependenciesAsync(RunnablePip runnable)
        {
            var environment = runnable.Environment;
            var operationContext = runnable.OperationContext;

            if (!environment.Configuration.EnableDistributedSourceHashing()
                || m_pipsSourceHashed.Contains(runnable.PipId))
            {
                return true;
            }

            using (operationContext.StartOperation(PipExecutorCounter.HashSourceFileDependenciesDuration))
            {
                var maybeHashed = await environment.State.FileContentManager.TryHashSourceDependenciesAsync(runnable.Pip, operationContext);
                if (!maybeHashed.Succeeded)
                {
                    Logger.Log.PipFailedDueToSourceDependenciesCannotBeHashed(
                        operationContext,
                        runnable.Description);
                    return false;
                }
            }

            m_pipsSourceHashed.Add(runnable.PipId);
            return true;
        }
    }
}
