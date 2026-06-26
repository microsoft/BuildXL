// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner.Vsts;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Polls the orchestrator's ADO build state on a fixed cadence. Completes <see cref="OrchestratorTerminated"/>
    /// the first time the orchestrator reports a terminal state (Failed or Canceled). Stops when its
    /// <see cref="CancellationToken"/> fires (used when the worker has attached and no longer needs the
    /// monitor).
    /// </summary>
    /// <remarks>
    /// This is best-effort: transient ADO errors are logged at Info level (so they surface on the
    /// worker's agent console by default) and the poll is retried on the next tick. Only a successful
    /// poll that reports a terminal state will complete <see cref="OrchestratorTerminated"/>.
    /// </remarks>
    internal sealed class OrchestratorStatusMonitor
    {
        private readonly AdoBuildRunnerService m_service;
        private readonly int m_orchestratorBuildId;
        private readonly Guid m_orchestratorJobId;
        private readonly ILogger m_logger;
        private readonly TimeSpan m_pollInterval;
        private readonly TaskCompletionSource m_terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes the first time the orchestrator is observed in a terminal state, or as canceled
        /// if the monitor is canceled first, so awaiters are always released. The terminal state itself
        /// is not exposed: callers only need to know the orchestrator is gone, and we log the state here.
        /// </summary>
        public Task OrchestratorTerminated => m_terminated.Task;

        /// <nodoc />
        public OrchestratorStatusMonitor(
            AdoBuildRunnerService service,
            int orchestratorBuildId,
            Guid orchestratorJobId,
            ILogger logger,
            TimeSpan? pollInterval = null)
        {
            m_service = service;
            m_orchestratorBuildId = orchestratorBuildId;
            m_orchestratorJobId = orchestratorJobId;
            m_logger = logger;
            m_pollInterval = pollInterval ?? TimeSpan.FromSeconds(Constants.OrchestratorStatusPollSeconds);
        }

        /// <summary>
        /// Runs the poll loop until either the orchestrator terminates or <paramref name="cancellationToken"/>
        /// is signaled. Never throws; cancellation simply causes a clean return.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            // If we're canceled before the orchestrator terminates, complete the task as canceled so awaiters don't leak.
            using var registration = cancellationToken.Register(() => m_terminated.TrySetCanceled(cancellationToken));

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var state = await m_service.GetOrchestratorStateAsync(m_orchestratorBuildId, m_orchestratorJobId);
                    if (state != OrchestratorState.Running)
                    {
                        m_logger.Info($"Orchestrator build (id={m_orchestratorBuildId}) reached terminal state '{state}'.");
                        m_terminated.TrySetResult();
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                catch (Exception ex)
                {
                    // Transient ADO errors should not abort the monitor; retry on the next tick.
                    m_logger.Info($"OrchestratorStatusMonitor poll failed (will retry): {ex}");
                }
#pragma warning restore ERP022

                try
                {
                    await Task.Delay(m_pollInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
