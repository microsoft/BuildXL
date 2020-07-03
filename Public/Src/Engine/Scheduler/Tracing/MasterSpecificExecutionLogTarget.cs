// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Execution log events that need to be intercepted only on non-worker machines.
    /// </summary>
    public sealed class MasterSpecificExecutionLogTarget : ExecutionLogTargetBase
    {
        private readonly LoggingContext m_loggingContext;

        private readonly Scheduler m_scheduler;

        private readonly int m_workerId;

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        /// <inheritdoc/>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId)
        {
            return new MasterSpecificExecutionLogTarget(m_loggingContext, m_scheduler, (int)workerId);
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public MasterSpecificExecutionLogTarget(
            LoggingContext loggingContext,
            Scheduler scheduler,
            int workerId = 0)
        {
            m_loggingContext = loggingContext;
            m_scheduler = scheduler;
            m_workerId = workerId;
        }

        /// <inheritdoc/>
        public override void CacheMaterializationError(CacheMaterializationErrorEventData data)
        {
            var pathTable = m_scheduler.Context.PathTable;
            var process = (Process) m_scheduler.PipGraph.PipTable.HydratePip(data.PipId, Pips.PipQueryContext.CacheMaterializationError);

            string descriptionFailure = string.Join(
                Environment.NewLine,
                new[] { I($"Failed files to materialize:") }
                .Concat(data.FailedFiles.Select(f => I($"{f.Item1.Path.ToString(pathTable)} | Hash={f.Item2.ToString()} | ProducedBy={m_scheduler.GetProducerInfoForFailedMaterializeFile(f.Item1)}"))));

            Logger.Log.DetailedPipMaterializeDependenciesFromCacheFailure(
                m_loggingContext,
                process.GetDescription(m_scheduler.Context),
                descriptionFailure);
        }

        /// <inheritdoc/>
        public override void StatusReported(StatusEventData data)
        {
            var worker = m_scheduler.Workers[m_workerId];

            worker.ActualFreeMemoryMb = data.RamFreeMb;
            worker.ActualFreeCommitMb = data.CommitFreeMb;
            if (worker.IsRemote)
            {
                worker.TotalCommitMb = data.CommitUsedMb + data.CommitFreeMb;
                ((RemoteWorkerBase)worker).SetEffectiveTotalProcessSlots(data.EffectiveTotalProcessSlots);
            }
        }
    }
}
