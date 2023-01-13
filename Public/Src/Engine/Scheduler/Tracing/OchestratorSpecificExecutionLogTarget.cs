// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Scheduler.Distribution;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Execution log events that need to be intercepted only on non-worker machines.
    /// </summary>
    public sealed class OchestratorSpecificExecutionLogTarget : ExecutionLogTargetBase
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
            return new OchestratorSpecificExecutionLogTarget(m_loggingContext, m_scheduler, (int)workerId);
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public OchestratorSpecificExecutionLogTarget(
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
            var pip = m_scheduler.PipGraph.PipTable.HydratePip(data.PipId, Pips.PipQueryContext.CacheMaterializationError);

            string descriptionFailure = string.Join(
                Environment.NewLine,
                new[] { I($"Failed files to materialize:") }
                .Concat(data.FailedFiles.Select(f => I($"{f.Item1.Path.ToString(pathTable)} | Hash={f.Item2.ToString()} | ProducedBy={m_scheduler.GetProducerInfoForFailedMaterializeFile(f.Item1)}"))));

            Logger.Log.DetailedPipMaterializeDependenciesFromCacheFailure(
                m_loggingContext,
                pip.GetDescription(m_scheduler.Context),
                descriptionFailure);
        }

        /// <inheritdoc/>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var configuration = ((IPipExecutionEnvironment)m_scheduler).Configuration;
            if (configuration.ProcessSourceFileHashes() && m_workerId != 0)
            {
                using (m_scheduler.PipExecutionCounters.StartStopwatch(PipExecutorCounter.FileArtifactContentDecidedDuration))
                {
                    // When distributed source hashing is enabled, orchestrator does not know the hashes for all source files.
                    // However, runtime cache miss analyzer needs the hashes for source files. That's why, when the runtime
                    // cache miss analyzer is enabled, orchestrator reports the hashes for source files, which come from workers.
                    if (data.FileArtifact.IsSourceFile)
                    {
                        PathAtom fileName = data.FileArtifact.Path.GetName(m_scheduler.Context.PathTable);
                        var materializationInfo = new FileMaterializationInfo(data.FileContentInfo, fileName);
                        m_scheduler.State.FileContentManager.ReportInputContent(data.FileArtifact, materializationInfo, contentMismatchErrorsAreWarnings: true);
                    }
                }
            }
        }
    }
}
