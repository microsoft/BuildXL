// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly ReadOnlyArray<double> m_workerRestrictedLoadFactor;
        private readonly IReadOnlyList<Worker> m_workers;
        private readonly LocalWorker m_localWorker;
        private IList<Task<bool>> m_remoteWorkerAttachments;

        private bool AnyRemoteWorkers => m_workers.Count > 1;

        public ChooseWorkerCacheLookup(IReadOnlyList<Worker> workers)
        {
            m_workerBalancedLoadFactors = ReadOnlyArray<double>.FromWithoutCopy(1, 2, 4, 8, 16, 32, 64, 128, 256, 512);
            m_workerRestrictedLoadFactor = ReadOnlyArray<double>.FromWithoutCopy(1, 2);
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
                // Until all remote worker attachments are completed, we do not oversubscribe the connected workers. 
                // The pips would be waiting in ChooseWorkerCacheLookup until an available slot is found.
                var loadFactors = m_workerRestrictedLoadFactor;

                if (m_remoteWorkerAttachments == null)
                {
                    // First initialization. We do not have the all remote workers added to m_workers during the construction time.
                    m_remoteWorkerAttachments = m_workers.OfType<RemoteWorkerBase>().Select(static w => w.AttachCompletionTask).ToList();
                }

                if (m_remoteWorkerAttachments.All(x => x.IsCompleted))
                {
                    // If all remote worker attachments are completed, we can enable the loadFactor to oversubscribe the remote workers.
                    loadFactors = m_workerBalancedLoadFactors;
                }

                foreach (var loadFactor in loadFactors)
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
