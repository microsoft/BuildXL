// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Defines an in-process worker.
    /// </summary>
    public sealed class LocalWorker : Worker
    {
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

        /// <summary>
        /// Constructor
        /// </summary>
        public LocalWorker(int totalProcessSlots, int totalCacheLookupSlots, IDetoursEventListener detoursListener)
            : base(workerId: 0, name: "#0 (Local)")
        {
            TotalProcessSlots = totalProcessSlots;
            TotalCacheLookupSlots = totalCacheLookupSlots;
            Start();
            m_detoursListener = detoursListener;
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeInputsAsync(RunnablePip runnablePip)
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
                var cachingInfo = runnablePip.ExecutionResult?.TwoPhaseCachingInfo;

                Task cachingInfoAvailableCompletion = Unit.VoidTask;
                PipResultStatus result = await PipExecutor.MaterializeOutputsAsync(operationContext, runnablePip.Environment, runnablePip.Pip);
                return result;
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

                Transition(processRunnable.PipId, WorkerPipState.Executing);
                ExecutionResult executionResult = await PipExecutor.ExecuteProcessAsync(
                    operationContext,
                    environment,
                    environment.State.GetScope(process),
                    process,
                    fingerprint,
                    processIdListener: UpdateCurrentlyRunningPipsCount,
                    expectedMemoryCounters: GetExpectedMemoryCounters(processRunnable),
                    detoursEventListener: m_detoursListener);
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

                Transition(runnablePip.PipId, WorkerPipState.Executing);
                var executionResult = await PipExecutor.ExecuteIpcAsync(operationContext, environment, ipcPip);
                runnablePip.SetExecutionResult(executionResult);

                return RunnablePip.CreatePipResultFromExecutionResult(runnablePip.StartTime, executionResult);
            }
        }

        /// <inheritdoc />
        public override async Task<RunnableFromCacheResult> CacheLookupAsync(
            ProcessRunnablePip runnablePip,
            PipExecutionState.PipScopeState state,
            CacheableProcess cacheableProcess)
        {
            using (OnPipExecutionStarted(runnablePip))
            {
                return await PipExecutor.TryCheckProcessRunnableFromCacheAsync(runnablePip, state, cacheableProcess);
            }
        }
    }
}
