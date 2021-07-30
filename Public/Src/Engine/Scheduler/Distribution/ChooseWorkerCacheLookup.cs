// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Handles choose worker computation for <see cref="Scheduler"/>
    /// </summary>
    internal class ChooseWorkerCacheLookup : ChooseWorkerContext
    {
        private long m_cacheLookupWorkerRoundRobinCounter;

        private readonly ReadOnlyArray<double> m_workerBalancedLoadFactors;

        public ChooseWorkerCacheLookup(
            LoggingContext loggingContext,
            IScheduleConfiguration scheduleConfig,
            IReadOnlyList<Worker> workers,
            IPipQueue pipQueue) : base(loggingContext, workers, pipQueue, DispatcherKind.ChooseWorkerCacheLookup, scheduleConfig.MaxChooseWorkerCacheLookup, scheduleConfig.ModuleAffinityEnabled())
        {
            m_workerBalancedLoadFactors = ReadOnlyArray<double>.FromWithoutCopy(0.5, 1, 2, 3);
        }

        /// <summary>
        /// Choose a worker
        /// </summary>
        protected override Task<Worker> ChooseWorkerCore(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.PipType == PipType.Process);

            var processRunnable = (ProcessRunnablePip)runnablePip;
            if (AnyRemoteWorkers)
            {
                var startWorkerOffset = Interlocked.Increment(ref m_cacheLookupWorkerRoundRobinCounter);
                foreach (var loadFactor in m_workerBalancedLoadFactors)
                {
                    for (int i = 0; i < Workers.Count; i++)
                    {
                        var workerId = (i + startWorkerOffset) % Workers.Count;
                        var worker = Workers[(int)workerId];
                        if (worker.TryAcquireCacheLookup(processRunnable, force: false, loadFactor: loadFactor))
                        {
                            return Task.FromResult(worker);
                        }
                    }
                }

                return Task.FromResult((Worker)null);
            }

            var acquired = LocalWorker.TryAcquireCacheLookup(processRunnable, force: true);
            Contract.Assert(acquired, "The local worker must be acquired for cache lookup when force=true");

            return Task.FromResult((Worker)LocalWorker);
        }
    }
}
