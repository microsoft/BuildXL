// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Pips.Operations;
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

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        /// <inheritdoc/>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId)
        {
            return this;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public MasterSpecificExecutionLogTarget(
            LoggingContext loggingContext,
            Scheduler scheduler)
        {
            m_loggingContext = loggingContext;
            m_scheduler = scheduler;
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
    }
}
