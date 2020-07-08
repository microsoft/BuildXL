// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities.Configuration;
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

        private readonly bool m_tracerEnabled;

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        /// <summary>
        /// Tracks time when the tracer was last updated
        /// </summary>
        private DateTime m_tracerLastUpdated;

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
            m_tracerEnabled = ((IPipExecutionEnvironment)m_scheduler).Configuration.Logging.LogTracer;
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

            if (m_tracerEnabled && DateTime.UtcNow > m_tracerLastUpdated.AddSeconds(EngineEnvironmentSettings.MinStepDurationSecForTracer))
            {
                LogPercentageCounter(worker, "CPU", data.CpuPercent, data.Time.Ticks);
                LogPercentageCounter(worker, "RAM", data.RamPercent, data.Time.Ticks);
                m_tracerLastUpdated = DateTime.UtcNow;
            }
        }

        private void LogPercentageCounter(Worker worker, string name, int percentValue, long ticks)
        {
            if (worker.InitializedTracerCounters.TryAdd(name, 0))
            {
                // To show the counters nicely in the UI, we set percentage counters to 100 for very short time
                // so that UI aligns the rest based on 100% instead of the maximum observed value
                BuildXL.Tracing.Logger.Log.TracerCounterEvent(m_loggingContext, name, worker.Name, ticks, 100);
            }

            BuildXL.Tracing.Logger.Log.TracerCounterEvent(m_loggingContext, name, worker.Name, ticks, percentValue);
        }
    }
}
