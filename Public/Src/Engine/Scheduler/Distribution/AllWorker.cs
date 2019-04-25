// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Defines a worker which broadcasts to all workers
    /// </summary>
    public sealed class AllWorker : RemoteWorkerBase
    {
        /// <nodoc />
        public const uint Id = uint.MaxValue;
        private readonly Worker[] m_workers;
        private readonly SemaphoreSlim m_localMaterializeOutputsSemaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

        /// <nodoc />
        public AllWorker(Worker[] workers)
            : base(workerId: Id, name: "All Workers")
        {
            Contract.Requires(workers.Length > 0);

            m_workers = workers;
            Start();
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeOutputsAsync(RunnablePip runnablePip)
        {
            using (var operationContext = runnablePip.OperationContext.StartAsyncOperation(PipExecutorCounter.ExecuteStepOnAllRemotesDuration))
            using (runnablePip.EnterOperation(operationContext))
            {
                Task<PipResultStatus>[] tasks = new Task<PipResultStatus>[m_workers.Length];

                // Start from the remote workers
                for (int i = m_workers.Length - 1; i >= 0; i--)
                {
                    var worker = m_workers[i];
                    if (worker.IsLocal)
                    {
                        await m_localMaterializeOutputsSemaphore.WaitAsync();

                    }

                    tasks[i] = Task.Run(() => worker.MaterializeOutputsAsync(runnablePip));
                }

                // Await the local worker first to release the semaphore. 
                await tasks[LocalWorkerIndex];
                m_localMaterializeOutputsSemaphore.Release();

                var results = await Task.WhenAll(tasks);

                foreach (var result in results)
                {
                    if (result.IndicatesFailure())
                    {
                        return result;
                    }
                }

                return results[0];
            }
        }
    }
}
