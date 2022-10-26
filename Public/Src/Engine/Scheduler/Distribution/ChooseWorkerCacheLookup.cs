// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Handles choose worker computation for <see cref="Scheduler"/>
    /// </summary>
    internal class ChooseWorkerCacheLookup
    {
        private readonly ReadOnlyArray<double> m_workerBalancedLoadFactors;
        private readonly IReadOnlyList<Worker> m_workers;
        private readonly LocalWorker m_localWorker;
        private bool AnyRemoteWorkers => m_workers.Count > 1;

        public ChooseWorkerCacheLookup(IReadOnlyList<Worker> workers)
        {
            m_workerBalancedLoadFactors = ReadOnlyArray<double>.FromWithoutCopy(1, 2, 4, 8, 16, 32, 64, 128, 256, 512);
            m_workers = workers;
            m_localWorker = (LocalWorker)workers[0];
        }

        /// <summary>
        /// Choose a worker
        /// </summary>
        public Worker ChooseWorker(ProcessRunnablePip processRunnable)
        {
            if (AnyRemoteWorkers)
            {
                foreach (var loadFactor in m_workerBalancedLoadFactors)
                {
                    foreach (var worker in m_workers)
                    {
                        if (worker.TryAcquireCacheLookup(processRunnable, force: false, loadFactor: loadFactor))
                        {
                            return worker;
                        }
                    }
                }

                return null;
            }

            var acquired = m_localWorker.TryAcquireCacheLookup(processRunnable, force: true);
            Contract.Assert(acquired, "The local worker must be acquired for cache lookup when force=true");

            return m_localWorker;
        }
    }
}
