// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Defines an in-process worker.
    /// </summary>
    public sealed class LocalWorker : Worker
    {
        /// <summary>
        /// Set of pips that are currently executing. Executing here means an external child process is running.
        /// </summary>
        public ConcurrentDictionary<PipId, Unit> CurrentlyExecutingPips = new ConcurrentDictionary<PipId, Unit>();

        /// <summary>
        /// Constructor
        /// </summary>
        public LocalWorker(int totalProcessSlots, int totalCacheLookupSlots)
            : base(workerId: 0, name: "#0 (Local)")
        {
            TotalProcessSlots = totalProcessSlots;
            TotalCacheLookupSlots = totalCacheLookupSlots;
            Start();
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
                CurrentlyExecutingPips.TryAdd(processRunnable.PipId, Unit.Void);

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
                    expectedRamUsageMb: GetExpectedRamUsageMb(processRunnable));
                processRunnable.SetExecutionResult(executionResult);

                Unit ignore;
                CurrentlyExecutingPips.TryRemove(processRunnable.PipId, out ignore);

                return executionResult;
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
