// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Handles choose worker computation for IPC pips
    /// </summary>
    internal class ChooseWorkerIpc
    {
        private readonly Dictionary<ModuleId, (int, bool[] Workers)> m_moduleWorkerMapping;
        private readonly IReadOnlyList<Worker> m_workers;
        private readonly LocalWorker m_localWorker;
        private bool AnyRemoteWorkers => m_workers.Count > 1;
        private readonly ReadOnlyArray<double> m_workerBalancedLoadFactors;

        public ChooseWorkerIpc(IReadOnlyList<Worker> workers, Dictionary<ModuleId, (int, bool[])> moduleWorkerMapping)
        {
            m_moduleWorkerMapping = moduleWorkerMapping;
            m_workers = workers;
            m_localWorker = (LocalWorker)workers[0];
            m_workerBalancedLoadFactors = ReadOnlyArray<double>.FromWithoutCopy(1, 2, 4, 8, 16, 32, 64, 128);
        }

        /// <summary>
        /// Choose a worker for the ipc pip.
        /// </summary>
        public Worker ChooseWorker(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip.PipType == PipType.Ipc);

            if (!AnyRemoteWorkers || ((IpcPip)runnablePip.Pip).MustRunOnOrchestrator)
            {
                m_localWorker.TryAcquireIpc(runnablePip, force: true);
                return m_localWorker;
            }

            if (runnablePip.PreferredWorkerId.HasValue)
            {
                var preferredWorker = m_workers[runnablePip.PreferredWorkerId.Value];
                if (preferredWorker.TryAcquireIpc(runnablePip, loadFactor: 2))
                {
                    return preferredWorker;
                }
            }

            // If the preferred worker is not available, let's take a look at the
            // list of the workers assigned to this module.
            var moduleId = runnablePip.Pip.Provenance.ModuleId;

            if (m_moduleWorkerMapping.TryGetValue(moduleId, out var assignedWorkers))
            {
                for (int i = 0; i < m_workers.Count; i++)
                {
                    if (assignedWorkers.Workers[i] && m_workers[i].TryAcquireIpc(runnablePip, loadFactor: 2))
                    {
                        return m_workers[i];
                    }
                }
            }

            foreach (var loadFactor in m_workerBalancedLoadFactors)
            {
                for (int i = 0; i < m_workers.Count; i++)
                {
                    if (m_workers[i].TryAcquireIpc(runnablePip, loadFactor: loadFactor))
                    {
                        if (assignedWorkers.Workers != null)
                        {
                            assignedWorkers.Workers[i] = true;
                        }

                        return m_workers[i];
                    }

                }
            }

            return null;
        }
    }
}
