// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Scheduler
{
    public class TestPipExecutor
    {
        /// <summary>
        /// Executes a pip.
        /// </summary>
        public static async Task<PipResult> ExecuteAsync(
            OperationContext operationContext,
            IPipExecutionEnvironment environment,
            Pip pip,
            bool materializeInputs = false)
        {
            Contract.Requires(environment != null);
            Contract.Requires(pip != null);
            PipResult result;

            // This method runs pips in isolation so we need to
            // hash inputs prior to running to ensure content information is populated
            // because report output will not be called for dependencies
            var maybeHashed = await environment.State.FileContentManager.TryHashDependenciesAsync(pip, operationContext);
            XAssert.IsTrue(maybeHashed.Succeeded, "Hashing inputs should succeed");

            ExecutionResult executionResult = null;
            DateTime start = DateTime.UtcNow;
            switch (pip.PipType)
            {
                case PipType.WriteFile:
                    result = await PipExecutor.ExecuteWriteFileAsync(operationContext, environment, (WriteFile)pip);
                    break;
                case PipType.CopyFile:
                    result = await PipExecutor.ExecuteCopyFileAsync(operationContext, environment, (CopyFile)pip);
                    break;
                case PipType.Process:
                    {
                        var pipScope = environment.State.GetScope((Process)pip);
                        var cacheableProcess = pipScope.GetCacheableProcess((Process)pip, environment);

                        var runnablePip = (ProcessRunnablePip)RunnablePip.Create(operationContext, environment, pip, 0, null);
                        runnablePip.Start(new OperationTracker(operationContext), operationContext);

                        var cacheResult = await PipExecutor.TryCheckProcessRunnableFromCacheAsync(runnablePip, pipScope, cacheableProcess);
                        if (cacheResult == null)
                        {
                            executionResult = ExecutionResult.GetFailureNotRunResult(operationContext);
                        }
                        else if (cacheResult.CanRunFromCache)
                        {
                            executionResult = await PipExecutor.RunFromCacheWithWarningsAsync(operationContext, environment, pipScope, (Process)pip, cacheResult, cacheableProcess.Description);
                        }
                        else
                        {
                            executionResult = await PipExecutor.ExecuteProcessAsync(operationContext, environment, pipScope, (Process)pip, cacheResult.Fingerprint);
                            executionResult.Seal();
                        runnablePip.SetExecutionResult(executionResult);

                        executionResult = PipExecutor.AnalyzeFileAccessViolations(
                                operationContext,
                                environment,
                                cacheableProcess.Process,
                                runnablePip.AllExecutionResults,
                                out _,
                                out _);

                            executionResult = await PipExecutor.PostProcessExecutionAsync(operationContext, environment, pipScope, cacheableProcess, executionResult);
                            PipExecutor.ReportExecutionResultOutputContent(operationContext, environment, cacheableProcess.UnderlyingPip.SemiStableHash, executionResult);
                        }

                        result = RunnablePip.CreatePipResultFromExecutionResult(start, executionResult, withPerformanceInfo: true);
                        break;
                    }
                case PipType.Ipc:
                    var ipcResult = await PipExecutor.ExecuteIpcAsync(operationContext, environment, (IpcPip)pip);
                    PipExecutor.ReportExecutionResultOutputContent(
                        operationContext, 
                        environment, 
                        pip.SemiStableHash,
                        ipcResult);
                    result = RunnablePip.CreatePipResultFromExecutionResult(start, ipcResult);
                    break;
                default:
                    throw Contract.AssertFailure("Do not know how to run this pip type:" + pip.PipType);
            }

            if (result.Status == PipResultStatus.NotMaterialized)
            {
                // Ensure outputs are materialized
                var materializationResult = await PipExecutor.MaterializeOutputsAsync(operationContext, environment, pip);
                if (executionResult != null)
                {
                    result = RunnablePip.CreatePipResultFromExecutionResult(start, executionResult.CloneSealedWithResult(materializationResult), withPerformanceInfo: true);
                }
                else
                {
                    result = new PipResult(
                        materializationResult,
                        result.PerformanceInfo,
                        result.MustBeConsideredPerpetuallyDirty,
                        result.DynamicallyObservedFiles,
                        result.DynamicallyProbedFiles,
                        result.DynamicallyObservedEnumerations,
                        result.DynamicallyObservedAbsentPathProbes,
                        result.ExitCode);
                }
            }

            return result;
        }
    }
}