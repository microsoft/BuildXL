// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Scheduler.Distribution;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.Core.FormattableStringEx;
using BuildXL.Scheduler.Cache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.ParallelAlgorithms;
using System.Threading.Tasks;

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

        private readonly PipTwoPhaseCache m_pipTwoPhaseCache;

        private readonly ActionBlockSlim<(ProcessFingerprintComputationEventData Data, ProcessStrongFingerprintComputationData SfComputation)> m_pathSetReporter;

        /// <inheritdoc/>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId)
        {
            return new OchestratorSpecificExecutionLogTarget(m_loggingContext, m_scheduler, m_pipTwoPhaseCache, (int)workerId);
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public OchestratorSpecificExecutionLogTarget(
            LoggingContext loggingContext,
            Scheduler scheduler,
            PipTwoPhaseCache pipTwoPhaseCache,
            int workerId = 0)
        {
            m_loggingContext = loggingContext;
            m_scheduler = scheduler;
            m_pipTwoPhaseCache = pipTwoPhaseCache;
            m_workerId = workerId;

            m_pathSetReporter = ActionBlockSlim.Create<(ProcessFingerprintComputationEventData Data, ProcessStrongFingerprintComputationData SfComputation)>(
                degreeOfParallelism: Environment.ProcessorCount,
                x => m_pipTwoPhaseCache.ReportRemotePathSet(x.SfComputation.PathSet, x.SfComputation.PathSetHash, isExecution: x.Data.Kind == FingerprintComputationKind.Execution, preservePathCasing: x.Data.PreservePathSetCasing));
        }

        /// <summary>
        /// Complete and wait pathset report
        /// </summary>
        public Task CompleteAndWaitPathSetReport()
        {
            m_pathSetReporter.Complete();
            return m_pathSetReporter.Completion;

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

        /// <inheritdoc/>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (m_workerId == 0)
            {
                return;
            }

            using (m_scheduler.PipExecutionCounters.StartStopwatch(PipExecutorCounter.ReportRemotePathSetDuration))
            {
                foreach (var sfComputation in data.StrongFingerprintComputations)
                {
                    // The ordering does not matter when reporting pathsets to HistoricMetadataCache.
                    if (sfComputation.Succeeded)
                    {
                        if ((sfComputation.IsStrongFingerprintHit && data.Kind == FingerprintComputationKind.CacheCheck) ||
                            (!sfComputation.IsStrongFingerprintHit && data.Kind == FingerprintComputationKind.Execution))
                        {
                            m_pathSetReporter.Post((data, sfComputation));
                        }
                    }
                }
            }
        }
    }
}
