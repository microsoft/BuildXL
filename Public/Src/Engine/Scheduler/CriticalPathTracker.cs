// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Tracing;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.ViewModel;
using static BuildXL.Utilities.Core.FormattableStringEx;
using Logger = BuildXL.Scheduler.Tracing.Logger;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Tracks and reports the build's critical paths. As pips complete, <see cref="UpdateCriticalPath"/> accumulates
    /// the longest dependency chains on the hot path; at the end of the build <see cref="LogCriticalPathAndTopPips"/>
    /// renders the final report. See <see cref="PipRuntimeInfo.CriticalPathDurationMs"/> and
    /// <see cref="PipRuntimeInfo.WallClockCriticalPathDurationMs"/> for how the two tracked critical paths differ.
    /// </summary>
    /// <remarks>
    /// The only state the tracker holds is what the per-pip hot path needs: the graph, runtime-info and execution-
    /// environment accessors (supplied at construction) and the two critical-path tails it updates as pips complete.
    /// The reporting-only state, which is either not known or not needed until the build completes, is passed to
    /// <see cref="LogCriticalPathAndTopPips"/> via a <see cref="CriticalPathReportContext"/> and threaded through the
    /// reporting helpers as a parameter, so none of it lives on the instance.
    /// </remarks>
    internal sealed class CriticalPathTracker
    {
        private readonly IPipExecutionEnvironment m_environment;
        private readonly Func<IReadonlyDirectedGraph> m_getScheduledGraph;
        private readonly Func<NodeId, PipRuntimeInfo> m_getPipRuntimeInfo;

        /// <summary>
        /// The last node in the currently computed critical path. See <see cref="PipRuntimeInfo.CriticalPathDurationMs"/>.
        /// </summary>
        private int m_criticalPathTailPipIdValue = unchecked((int)PipId.Invalid.Value);

        /// <summary>
        /// The last node in the currently computed wall-clock critical path. See <see cref="PipRuntimeInfo.WallClockCriticalPathDurationMs"/>.
        /// </summary>
        private int m_wallClockCriticalPathTailPipIdValue = unchecked((int)PipId.Invalid.Value);

        private IReadonlyDirectedGraph ScheduledGraph => m_getScheduledGraph();

        /// <summary>
        /// A critical path produced by <see cref="BuildCriticalPath"/>: the ordered list of pips (from end to
        /// beginning) plus whether any pip on it had its execution time injected via a <c>##bxl</c> hint. The latter
        /// widens the Exe Duration column when the path is rendered so the <c>[injected]</c> marker stays aligned.
        /// </summary>
        private sealed record CriticalPath(
            List<(PipRuntimeInfo pipRunTimeInfo, PipId pipId)> Pips,
            bool HasAnyPipInjectedRuntime);

        /// <summary>
        /// Creates a tracker. The supplied dependencies are those needed on the per-pip hot path; they remain valid for
        /// the lifetime of the owning scheduler.
        /// </summary>
        /// <param name="environment">Execution environment used to compute per-pip durations.</param>
        /// <param name="getScheduledGraph">Accessor for the scheduled graph (which is set after construction).</param>
        /// <param name="getPipRuntimeInfo">Accessor for a node's <see cref="PipRuntimeInfo"/>.</param>
        public CriticalPathTracker(
            IPipExecutionEnvironment environment,
            Func<IReadonlyDirectedGraph> getScheduledGraph,
            Func<NodeId, PipRuntimeInfo> getPipRuntimeInfo)
        {
            m_environment = environment;
            m_getScheduledGraph = getScheduledGraph;
            m_getPipRuntimeInfo = getPipRuntimeInfo;
        }

        private PipRuntimeInfo GetPipRuntimeInfo(PipId pipId) => m_getPipRuntimeInfo(pipId.ToNodeId());

        private PipRuntimeInfo GetPipRuntimeInfo(NodeId nodeId) => m_getPipRuntimeInfo(nodeId);

        /// <summary>
        /// Identifies which of the two critical paths <see cref="LogCriticalPathDetails"/> is rendering. See
        /// <see cref="PipRuntimeInfo.CriticalPathDurationMs"/> and <see cref="PipRuntimeInfo.WallClockCriticalPathDurationMs"/>
        /// for how the two differ.
        /// </summary>
        private enum CriticalPathKind
        {
            /// <summary>
            /// See <see cref="PipRuntimeInfo.CriticalPathDurationMs"/>.
            /// </summary>
            Primary,

            /// <summary>
            /// See <see cref="PipRuntimeInfo.WallClockCriticalPathDurationMs"/>.
            /// </summary>
            WallClockCriticalPath,
        }

        /// <summary>
        /// Updates both the <see cref="PipRuntimeInfo.CriticalPathDurationMs"/> and
        /// <see cref="PipRuntimeInfo.WallClockCriticalPathDurationMs"/> for a completed pip. Each accumulates a
        /// longest-chain duration ending at this pip and races to claim the global tail if its chain is the longest
        /// seen so far.
        /// </summary>
        public void UpdateCriticalPath(RunnablePip runnablePip, PipExecutionPerformance performance)
        {
            var totalDurationMs = (long)runnablePip.Performance.TotalDuration.TotalMilliseconds;
            var workBasedDurationMs = runnablePip.Performance.CalculateWorkBasedPipDurationMs(m_environment);
            var pip = runnablePip.Pip;

            if (pip.PipType.IsMetaPip())
            {
                return;
            }

            long criticalChainMs = workBasedDurationMs;
            long wallClockChainMs = totalDurationMs;
            foreach (var dependencyEdge in ScheduledGraph.GetIncomingEdges(pip.PipId.ToNodeId()))
            {
                var dependencyRuntimeInfo = GetPipRuntimeInfo(dependencyEdge.OtherNode);
                criticalChainMs = Math.Max(criticalChainMs, workBasedDurationMs + dependencyRuntimeInfo.CriticalPathDurationMs);
                wallClockChainMs = Math.Max(wallClockChainMs, totalDurationMs + dependencyRuntimeInfo.WallClockCriticalPathDurationMs);
            }

            var pipRuntimeInfo = GetPipRuntimeInfo(pip.PipId);

            pipRuntimeInfo.Result = performance.ExecutionLevel;
            pipRuntimeInfo.CriticalPathDurationMs = criticalChainMs > int.MaxValue ? int.MaxValue : (int)criticalChainMs;
            pipRuntimeInfo.WallClockCriticalPathDurationMs = wallClockChainMs > int.MaxValue ? int.MaxValue : (int)wallClockChainMs;
            ProcessPipExecutionPerformance processPerformance = performance as ProcessPipExecutionPerformance;
            if (processPerformance != null)
            {
                pipRuntimeInfo.ProcessExecuteTimeMs = (int)processPerformance.ProcessExecutionTime.TotalMilliseconds;
                pipRuntimeInfo.OriginalProcessExecuteTimeMs = (int?) processPerformance.OriginalProcessExecutionTime?.TotalMilliseconds;
            }

            var pipIdValue = unchecked((int)pip.PipId.Value);

            // Race to claim each global tail if this pip's chain is the longest seen so far.
            claimTail(ref m_criticalPathTailPipIdValue, criticalChainMs, static info => info.CriticalPathDurationMs);
            claimTail(ref m_wallClockCriticalPathTailPipIdValue, wallClockChainMs, static info => info.WallClockCriticalPathDurationMs);

            void claimTail(ref int tailField, long chainMs, Func<PipRuntimeInfo, int> chainDurationSelector)
            {
                int currentTail;
                PipRuntimeInfo currentTailInfo;

                while (!TryGetTailRuntimeInfo(ref tailField, out currentTail, out currentTailInfo)
                    || (chainMs > (currentTailInfo != null ? chainDurationSelector(currentTailInfo) : 0)))
                {
                    if (Interlocked.CompareExchange(ref tailField, pipIdValue, currentTail) == currentTail)
                    {
                        break;
                    }
                }
            }
        }

        private static PipId ToPipId(int pipIdValue) => new PipId(unchecked((uint)pipIdValue));

        private bool TryGetTailRuntimeInfo(ref int tailField, out int currentTail, out PipRuntimeInfo runtimeInfo)
        {
            currentTail = Volatile.Read(ref tailField);
            runtimeInfo = currentTail == 0 ? null : GetPipRuntimeInfo(ToPipId(currentTail));
            return currentTail != 0;
        }

        /// <summary>
        /// Renders the full report into the build log (the top-5 pip breakdowns plus both critical paths), emits the
        /// per-path statistics suites, and (for the primary <see cref="PipRuntimeInfo.CriticalPathDurationMs"/> path)
        /// populates telemetry and the build summary. The reporting-only state needed for this is supplied via
        /// <paramref name="context"/>.
        /// </summary>
        public void LogCriticalPathAndTopPips(CriticalPathReportContext context)
        {
            Dictionary<string, long> statistics = context.Statistics;
            BuildSummary buildSummary = context.BuildSummary;

            var builder = new StringBuilder();
            Func<long, string> addMin = (duration) => $"{duration}ms ({Math.Round(duration / (double)60000, 1)}m)";
            string hr = I($"{Environment.NewLine}======================================================================{Environment.NewLine}");

            // Report the CriticalPath length via the CriticalPathDuration counter.
            int criticalPathTailPipIdValue = Volatile.Read(ref m_criticalPathTailPipIdValue);
            if (criticalPathTailPipIdValue != 0)
            {
                PipRuntimeInfo criticalPathRuntimeInfo = GetPipRuntimeInfo(ToPipId(criticalPathTailPipIdValue));
                context.PipExecutionCounters.AddToCounter(PipExecutorCounter.CriticalPathDuration, TimeSpan.FromMilliseconds(criticalPathRuntimeInfo.CriticalPathDurationMs));
            }

            // Top 5 pips by various step durations (independent of any particular critical path).
            builder.AppendLine(I($"Fine-grained Duration (ms) for Top 5 Pips Sorted by Pip Duration"));
            foreach (var kvp in (from a in context.RunnablePipPerformance
                                 let i = a.Value.CalculatePipDurationMs(m_environment)
                                 where i > 0
                                 orderby i descending
                                 select a).Take(5))
            {
                LogPipPerformanceInfo(builder, kvp.Key, kvp.Value, context);
            }

            builder.AppendLine(hr);
            builder.AppendLine(I($"Fine-grained Duration (ms) for Top 5 Pips Sorted by CacheLookup Duration (excluding remote queue time)"));
            foreach (var kvp in (from a in context.RunnablePipPerformance
                                 let i = a.Value.GetStepDurationMs(PipExecutionStep.CacheLookup)
                                         - a.Value.GetRemoteQueueDurationMs(PipExecutionStep.CacheLookup)
                                 where i > 0
                                 orderby i descending
                                 select a).Take(5))
            {
                LogPipPerformanceInfo(builder, kvp.Key, kvp.Value, context);
            }

            builder.AppendLine(hr);
            builder.AppendLine(I($"Fine-grained Duration (ms) for Top 5 Pips Sorted by ExecuteProcess Duration"));
            foreach (var kvp in (from a in context.RunnablePipPerformance
                                 let i = a.Value.GetStepDurationMs(PipExecutionStep.ExecuteProcess)
                                 where i > 0
                                 orderby i descending
                                 select a).Take(5))
            {
                LogPipPerformanceInfo(builder, kvp.Key, kvp.Value, context);
            }

            builder.AppendLine(hr);

            // The CriticalPath.
            var criticalPath = BuildCriticalPath(criticalPathTailPipIdValue, info => info.CriticalPathDurationMs);
            LogCriticalPathDetails(
                builder,
                addMin,
                kind: CriticalPathKind.Primary,
                criticalPath: criticalPath,
                context: context);

            builder.AppendLine(hr);

            // The WallClockCriticalPath.
            int wallClockTailPipIdValue = Volatile.Read(ref m_wallClockCriticalPathTailPipIdValue);
            var wallClockCriticalPath = BuildCriticalPath(wallClockTailPipIdValue, info => info.WallClockCriticalPathDurationMs);
            CriticalPathAggregates wallClockAggregates = LogCriticalPathDetails(
                builder,
                addMin,
                kind: CriticalPathKind.WallClockCriticalPath,
                criticalPath: wallClockCriticalPath,
                context: context);

            builder.AppendLine(hr);

            // The scheduler timeline breakdown reconciles against actual elapsed scheduler time (which includes queue
            // and contention time), so it is anchored on the WallClockCriticalPath rather than the work-based CriticalPath.
            if (wallClockAggregates != null)
            {
                long schedulerDurationMs = (long)(context.SchedulerDoneTimeUtc - context.SchedulerStartedTimeUtc).TotalMilliseconds;
                long criticalPathMs = wallClockAggregates.TotalDurationMs;

                long materializeOutputOverhangMs = context.SchedulerCompletionExceptMaterializeOutputsTimeUtc.HasValue ? (long)(context.SchedulerDoneTimeUtc - context.SchedulerCompletionExceptMaterializeOutputsTimeUtc.Value).TotalMilliseconds : 0;

                builder.AppendLine("Scheduler Timeline Breakdown (wall-clock basis):");
                logDuration("Time to First Pip", (long)context.TimeToFirstPip.TotalMilliseconds);
                logDuration("Scheduler", schedulerDurationMs);
                logDuration("Total Critical Path Length", criticalPathMs, indentLevel: 1);
                for (int i = 0; i < wallClockAggregates.StepDurations.Count; i++)
                {
                    var step = (PipExecutionStep)i;
                    if (step != PipExecutionStep.MaterializeOutputs || !context.MaterializeOutputsInBackground)
                    {
                        logDuration($"PipExecutionStep.{(step)}", wallClockAggregates.StepDurations[i], indentLevel: 2);
                        logDuration($"PipBuildRequest Queue", wallClockAggregates.PipBuildRequestQueueDurations[i], indentLevel: 3);
                        logDuration($"PipBuildRequest Send/Receive (gRPC)", wallClockAggregates.PipBuildRequestGrpcDurations[i], indentLevel: 3);
                        logDuration($"Dispatcher Queue on RemoteWorker", wallClockAggregates.RemoteQueueDurations[i], indentLevel: 3);

                        if (step == PipExecutionStep.CacheLookup)
                        {
                            for (int j = 0; j < wallClockAggregates.BeforeExecutionCacheStepDurations.Count; j++)
                            {
                                logDuration(OperationKind.GetTrackedCacheOperationKind(j).ToString(), wallClockAggregates.BeforeExecutionCacheStepDurations[j], indentLevel: 3);
                            }
                        }

                        if (step == PipExecutionStep.ExecuteProcess)
                        {
                            logDuration("Push Outputs to Cache", wallClockAggregates.PushOutputsToCacheDuration, indentLevel: 3);
                            logDuration("Suspend due to Memory", wallClockAggregates.SuspendedDuration, indentLevel: 3);
                            logDuration("Retry Duration", wallClockAggregates.RetryDuration, indentLevel: 3);
                            logDuration("Retry Count", wallClockAggregates.RetryCount, indentLevel: 3);

                            for (int j = 0; j < wallClockAggregates.AfterExecutionCacheStepDurations.Count; j++)
                            {
                                logDuration(OperationKind.GetTrackedCacheOperationKind(j).ToString(), wallClockAggregates.AfterExecutionCacheStepDurations[j], indentLevel: 3);
                            }
                        }
                    }
                }

                for (int i = 0; i < wallClockAggregates.OrchestratorQueueDurations.Count; i++)
                {
                    logDuration($"Dispatcher.{((DispatcherKind)i)} Queue", wallClockAggregates.OrchestratorQueueDurations[i], indentLevel: 2);
                }

                logDuration("Non-Critical Path", schedulerDurationMs - criticalPathMs, indentLevel: 1);
                logDuration("MaterializeOutput Overhang", materializeOutputOverhangMs, indentLevel: 2);

                logDuration("Post Scheduler Tasks", (long)context.PipExecutionCounters.GetElapsedTime(PipExecutorCounter.AfterDrainingWhenDoneDuration).TotalMilliseconds);
            }

            Logger.Log.CriticalPathChain(context.LoggingContext, builder.ToString());

            void logDuration(string desc, long durationMs, int indentLevel = 0)
            {
                long durationSec = durationMs / 1000;
                if (durationSec > 0)
                {
                    desc = "".PadLeft(indentLevel * 4) + desc;
                    builder.AppendLine($"{desc,-120}:{durationSec,10}s");
                }
            }
        }

        /// <summary>
        /// Holds the per-step duration totals accumulated along a single critical path. Returned by
        /// <see cref="LogCriticalPathDetails"/> so the caller can render the scheduler timeline breakdown.
        /// </summary>
        private sealed class CriticalPathAggregates
        {
            public IList<long> StepDurations;
            public IList<long> RemoteQueueDurations;
            public IList<long> PipBuildRequestQueueDurations;
            public IList<long> PipBuildRequestGrpcDurations;
            public IList<long> OrchestratorQueueDurations;
            public IList<long> BeforeExecutionCacheStepDurations;
            public IList<long> AfterExecutionCacheStepDurations;
            public long CacheMissAnalysisDuration;
            public long SuspendedDuration;
            public long RetryDuration;
            public long RetryCount;
            public long PushOutputsToCacheDuration;
            public long ExeDurationMs;
            public long PipDurationMs;
            public long TotalDurationMs;
        }

        /// <summary>
        /// Renders a single critical path (a summary table plus a fine-grained per-pip dump) into
        /// <paramref name="builder"/> and emits its full statistics suite under the prefix named by
        /// <paramref name="kind"/>. The same code renders both critical paths; only <paramref name="kind"/> and
        /// <paramref name="criticalPath"/> differ. For <see cref="CriticalPathKind.Primary"/> the per-pip
        /// <c>CriticalPathPipRecord</c> telemetry is logged and, when the build summary on <paramref name="context"/>
        /// is non-null, its critical path section is populated. Returns the accumulated per-step totals, or null when
        /// <paramref name="criticalPath"/> is empty.
        /// </summary>
        private CriticalPathAggregates LogCriticalPathDetails(
            StringBuilder builder,
            Func<long, string> addMin,
            CriticalPathKind kind,
            CriticalPath criticalPath,
            CriticalPathReportContext context)
        {
            if (criticalPath.Pips.Count == 0)
            {
                return null;
            }

            Dictionary<string, long> statistics = context.Statistics;
            string statPrefix = kind == CriticalPathKind.WallClockCriticalPath ? "WallClockCriticalPath" : "CriticalPath";
            BuildSummary summaryToPopulate = kind == CriticalPathKind.Primary ? context.BuildSummary : null;
            string header = kind == CriticalPathKind.WallClockCriticalPath ? "Wall-Clock Critical Path:" : "Critical Path:";

            IList<long> totalOrchestratorQueueDurations = new long[RunnablePipPerformanceInfo.DispatcherKindCount];
            IList<long> totalRemoteQueueDurations = new long[RunnablePipPerformanceInfo.PipExecutionStepCount];
            IList<long> totalStepDurations = new long[RunnablePipPerformanceInfo.PipExecutionStepCount];
            IList<long> totalPipBuildRequestQueueDurations = new long[RunnablePipPerformanceInfo.PipExecutionStepCount];
            IList<long> totalPipBuildRequestGrpcDurations = new long[RunnablePipPerformanceInfo.PipExecutionStepCount];
            long[] totalBeforeExecutionCacheStepDurations = new long[OperationKind.TrackedCacheLookupCounterCount];
            long[] totalAfterExecutionCacheStepDurations = new long[OperationKind.TrackedCacheLookupCounterCount];

            long totalCacheMissAnalysisDuration = 0, totalSuspendedDuration = 0, totalRetryCount = 0, totalPushOutputsToCacheDuration = 0, totalRetryDuration = 0;

            var summaryTable = new StringBuilder();
            var detailedLog = new StringBuilder();
            detailedLog.AppendLine(I($"Fine-grained Duration (ms) for Each Pip on this Critical Path (from end to beginning)"));

            long exeDurationCriticalPathMs = 0;

            // When a pip on this critical path had its execution time injected via a '##bxl' hint, its Exe Duration cell
            // carries an extra '[injected]' marker (10 chars). If any row is injected, widen the Exe Duration column for
            // every row - including the header and the total row - so the column stays aligned.
            int exeDurationColumnWidth = criticalPath.HasAnyPipInjectedRuntime ? 30 : 20;

            int index = 0;
            foreach (var node in criticalPath.Pips)
            {
                if (!context.RunnablePipPerformance.ContainsKey(node.pipId))
                {
                    continue;
                }

                RunnablePipPerformanceInfo performance = context.RunnablePipPerformance[node.pipId];
                Pip pip = context.PipGraph.GetPipFromPipId(node.pipId);
                PipRuntimeInfo runtimeInfo = node.pipRunTimeInfo;

                LogPipPerformanceInfo(detailedLog, node.pipId, performance, context);

                long pipDurationMs = performance.CalculateWorkBasedPipDurationMs(m_environment);
                long pipQueueDurationMs = performance.CalculateQueueDurationMs();
                long remoteQueueMs = performance.CalculateRemoteQueueDurationMs();
                long totalDurationMs = (long)performance.TotalDuration.TotalMilliseconds;
                long cacheLookupDurationMs = performance.GetStepDurationMs(PipExecutionStep.CacheLookup);

                // Summarization values must reflect only the originally measured runtimes so they stay consistent with the
                // total build time (an injected runtime can point far into the future, which would otherwise make the
                // aggregate exe duration exceed the wall-clock build time). The pip's per-row display still surfaces the
                // injected value; only the accumulated total uses the measured one.
                exeDurationCriticalPathMs += runtimeInfo.OriginalProcessExecuteTimeMs ?? runtimeInfo.ProcessExecuteTimeMs;

                if (kind == CriticalPathKind.Primary)
                {
                    Logger.Log.CriticalPathPipRecord(context.LoggingContext,
                        pipSemiStableHash: pip.SemiStableHash,
                        pipDescription: pip.GetDescription(context.Context),
                        pipDurationMs: pipDurationMs,
                        exeDurationMs: runtimeInfo.ProcessExecuteTimeMs,
                        // When the exe time was injected via a '##bxl[runtimeSecs]' hint, exeDurationMs above is the injected value and
                        // this carries the originally measured one; -1 indicates no injection (exeDurationMs is already the measured value).
                        originalExeDurationMs: runtimeInfo.OriginalProcessExecuteTimeMs ?? -1,
                        queueDurationMs: pipQueueDurationMs,
                        cacheLookupDurationMs: cacheLookupDurationMs,
                        indexFromBeginning: criticalPath.Pips.Count - index - 1,
                        isExplicitlyScheduled: (context.ExplicitlyScheduledProcessNodes == null ? false : context.ExplicitlyScheduledProcessNodes.Contains(node.pipId.ToNodeId())),
                        executionLevel: runtimeInfo.Result.ToString(),
                        numCacheEntriesVisited: performance.CacheLookupPerfInfo?.NumCacheEntriesVisited ?? 0,
                        numPathSetsDownloaded: performance.CacheLookupPerfInfo?.NumPathSetsDownloaded ?? 0);
                }

                TimeSpan scheduledTimeTs = TimeSpan.Zero;
                TimeSpan completedTimeTs = TimeSpan.Zero;

                if (context.ProcessStartTimeUtc.HasValue)
                {
                    scheduledTimeTs = performance.ScheduleTime - context.ProcessStartTimeUtc.Value;
                    completedTimeTs = performance.CompletedTime - context.ProcessStartTimeUtc.Value;
                }

                long queueDurationMs = remoteQueueMs + pipQueueDurationMs;
                string exeDuration = $"{(runtimeInfo.IsInjectedProcessExecuteTime? "[injected]" : string.Empty)}{addMin(runtimeInfo.ProcessExecuteTimeMs)}";

                summaryTable.AppendLine(I($"{addMin(pipDurationMs),20} | {exeDuration.PadLeft(exeDurationColumnWidth)} | {addMin(queueDurationMs),20} | {addMin(totalDurationMs),20} | {runtimeInfo.Result,12} | {pip.GetDescription(context.Context)}"));

                if (summaryToPopulate != null)
                {
                    summaryToPopulate.CriticalPathSummary.Lines.Add(
                        new CriticalPathSummaryLine
                        {
                            PipDuration = TimeSpan.FromMilliseconds(pipDurationMs),
                            ProcessExecuteTime = TimeSpan.FromMilliseconds(runtimeInfo.ProcessExecuteTimeMs),
                            OriginalProcessExecuteTime = runtimeInfo.OriginalProcessExecuteTimeMs.HasValue ? TimeSpan.FromMilliseconds(runtimeInfo.OriginalProcessExecuteTimeMs.Value) : (TimeSpan?)null,
                            PipQueueDuration = TimeSpan.FromMilliseconds(pipQueueDurationMs),
                            Result = runtimeInfo.Result.ToString(),
                            ScheduleTime = scheduledTimeTs,
                            Completed = completedTimeTs,
                            PipDescription = pip.GetDescription(context.Context),
                        });
                }

                performance.AddStepDurationsTo(totalStepDurations);
                performance.AddRemoteQueueDurationsTo(totalRemoteQueueDurations);
                performance.AddPipBuildRequestGrpcDurationsTo(totalPipBuildRequestGrpcDurations);
                performance.AddPipBuildRequestQueueDurationsTo(totalPipBuildRequestQueueDurations);
                performance.AddQueueDurationsTo(totalOrchestratorQueueDurations);

                performance.CacheLookupPerfInfo?.AddBeforeExecutionDurationsTo(totalBeforeExecutionCacheStepDurations);
                performance.CacheLookupPerfInfo?.AddAfterExecutionDurationsTo(totalAfterExecutionCacheStepDurations);

                totalCacheMissAnalysisDuration += (long)performance.CacheMissAnalysisDuration.TotalMilliseconds;
                totalSuspendedDuration += performance.SuspendedDurationMs;
                totalRetryDuration += performance.RetryDurationMs;
                totalRetryCount += performance.RetryCount;
                totalPushOutputsToCacheDuration += performance.PushOutputsToCacheDurationMs;

                index++;
            }

            // Total running time is the sum of all steps except ChooseWorker and MaterializeOutput (if done in background).
            long totalCriticalPathRunningTime = totalStepDurations.Where((i, j) => ((PipExecutionStep)j).IncludeInRunningTime(m_environment)).Sum();
            long totalOrchestratorQueueTime = totalOrchestratorQueueDurations.Sum();
            long totalRemoteQueueTime = totalRemoteQueueDurations.Sum();
            long totalChooseWorker = totalStepDurations[(int)PipExecutionStep.ChooseWorkerCpu] + totalStepDurations[(int)PipExecutionStep.ChooseWorkerCacheLookup] + totalStepDurations[(int)PipExecutionStep.ChooseWorkerIpc];
            long totalCriticalPathLength = totalOrchestratorQueueTime + totalChooseWorker + totalCriticalPathRunningTime;

            // PipDuration is the running-time steps excluding the remote worker queue wait.
            // Total = PipDuration + Remote Queue + Orchestrator Queue + ChooseWorker.
            long workPipDurationMs = totalCriticalPathRunningTime - totalRemoteQueueTime;
            long totalQueueTime = totalRemoteQueueTime + totalOrchestratorQueueTime;

            builder.AppendLine(header);
            builder.AppendLine(I($"{"Pip Duration",-20} | {"Exe Duration".PadRight(exeDurationColumnWidth)} | {"Queue",-20} | {"Total Duration",-20} | {"Pip Result",-12} | Pip"));
            builder.AppendLine(I($"{addMin(workPipDurationMs),20} | {addMin(exeDurationCriticalPathMs).PadLeft(exeDurationColumnWidth)} | {addMin(totalQueueTime),20} | {addMin(totalCriticalPathLength),20} | {string.Empty,12} | *Total"));
            builder.AppendLine(summaryTable.ToString());
            builder.AppendLine(detailedLog.ToString());

            if (summaryToPopulate != null)
            {
                summaryToPopulate.CriticalPathSummary.TotalCriticalPathRuntime = TimeSpan.FromMilliseconds(totalCriticalPathRunningTime);
                summaryToPopulate.CriticalPathSummary.ExeDurationCriticalPath = TimeSpan.FromMilliseconds(exeDurationCriticalPathMs);
                summaryToPopulate.CriticalPathSummary.TotalOrchestratorQueueTime = TimeSpan.FromMilliseconds(totalOrchestratorQueueTime);
            }

            // Full statistics suite for this path (identical key shape under both prefixes).
            statistics.Add(I($"{statPrefix}.TotalOrchestratorQueueDurationMs"), totalOrchestratorQueueTime);
            for (int i = 0; i < totalOrchestratorQueueDurations.Count; i++)
            {
                if (totalOrchestratorQueueDurations[i] != 0)
                {
                    statistics.Add(I($"{statPrefix}.{(DispatcherKind)i}_OrchestratorQueueDurationMs"), totalOrchestratorQueueDurations[i]);
                }
            }

            statistics.Add(I($"{statPrefix}.TotalRemoteQueueDurationMs"), totalRemoteQueueTime);
            for (int i = 0; i < totalRemoteQueueDurations.Count; i++)
            {
                statistics.Add(I($"{statPrefix}.{(PipExecutionStep)i}_RemoteQueueDurationMs"), totalRemoteQueueDurations[i]);
            }

            for (int i = 0; i < totalStepDurations.Count; i++)
            {
                var step = (PipExecutionStep)i;
                if (step != PipExecutionStep.MaterializeOutputs || !context.MaterializeOutputsInBackground)
                {
                    statistics.Add(I($"{statPrefix}.{step}DurationMs"), totalStepDurations[i]);
                }
            }

            for (int i = 0; i < totalBeforeExecutionCacheStepDurations.Length; i++)
            {
                var name = OperationKind.GetTrackedCacheOperationKind(i).ToString();
                statistics.Add(I($"{statPrefix}.BeforeExecution_{name}DurationMs"), totalBeforeExecutionCacheStepDurations[i]);
            }

            for (int i = 0; i < totalAfterExecutionCacheStepDurations.Length; i++)
            {
                var name = OperationKind.GetTrackedCacheOperationKind(i).ToString();
                statistics.Add(I($"{statPrefix}.AfterExecution_{name}DurationMs"), totalAfterExecutionCacheStepDurations[i]);
            }

            statistics.Add(I($"{statPrefix}.TotalQueueRequestDurationMs"), totalPipBuildRequestQueueDurations.Sum());
            statistics.Add(I($"{statPrefix}.TotalGrpcDurationMs"), totalPipBuildRequestGrpcDurations.Sum());
            statistics.Add(I($"{statPrefix}.ChooseWorkerDurationMs"), totalChooseWorker);
            statistics.Add(I($"{statPrefix}.CacheMissAnalysisDurationMs"), totalCacheMissAnalysisDuration);
            statistics.Add(I($"{statPrefix}.TotalSuspendedDurationMs"), totalSuspendedDuration);
            statistics.Add(I($"{statPrefix}.TotalRetryDurationMs"), totalRetryDuration);
            statistics.Add(I($"{statPrefix}.TotalRetryCount"), totalRetryCount);
            statistics.Add(I($"{statPrefix}.TotalPushOutputsToCacheDurationMs"), totalPushOutputsToCacheDuration);
            statistics.Add(I($"{statPrefix}.ExeDurationMs"), exeDurationCriticalPathMs);
            statistics.Add(I($"{statPrefix}.PipDurationMs"), workPipDurationMs);
            statistics.Add(I($"{statPrefix}.TotalDurationMs"), totalCriticalPathLength);

            return new CriticalPathAggregates
            {
                StepDurations = totalStepDurations,
                RemoteQueueDurations = totalRemoteQueueDurations,
                PipBuildRequestQueueDurations = totalPipBuildRequestQueueDurations,
                PipBuildRequestGrpcDurations = totalPipBuildRequestGrpcDurations,
                OrchestratorQueueDurations = totalOrchestratorQueueDurations,
                BeforeExecutionCacheStepDurations = totalBeforeExecutionCacheStepDurations,
                AfterExecutionCacheStepDurations = totalAfterExecutionCacheStepDurations,
                CacheMissAnalysisDuration = totalCacheMissAnalysisDuration,
                SuspendedDuration = totalSuspendedDuration,
                RetryDuration = totalRetryDuration,
                RetryCount = totalRetryCount,
                PushOutputsToCacheDuration = totalPushOutputsToCacheDuration,
                ExeDurationMs = exeDurationCriticalPathMs,
                PipDurationMs = workPipDurationMs,
                TotalDurationMs = totalCriticalPathLength,
            };
        }

        /// <summary>
        /// Walks back from a critical path tail, following the dependency with the largest accumulated duration
        /// (as selected by <paramref name="chainDurationSelector"/>), and returns the path from tail to root.
        /// </summary>
        private CriticalPath BuildCriticalPath(int tailPipIdValue, Func<PipRuntimeInfo, int> chainDurationSelector)
        {
            var path = new List<(PipRuntimeInfo pipRunTimeInfo, PipId pipId)>();

            if (tailPipIdValue == 0)
            {
                return new CriticalPath(path, HasAnyPipInjectedRuntime: false);
            }

            bool hasAnyPipInjectedRuntime = false;
            
            PipId pipId = ToPipId(tailPipIdValue);
            var runtimeInfo = GetPipRuntimeInfo(pipId);
            path.Add((runtimeInfo, pipId));
            hasAnyPipInjectedRuntime |= runtimeInfo.IsInjectedProcessExecuteTime;

            while (true)
            {
                PipRuntimeInfo nextRuntimeInfo = null;
                PipId nextPipId = pipId;

                foreach (var dependencyEdge in ScheduledGraph.GetIncomingEdges(pipId.ToNodeId()))
                {
                    var dependencyRuntimeInfo = GetPipRuntimeInfo(dependencyEdge.OtherNode);
                    if (chainDurationSelector(dependencyRuntimeInfo) >= (nextRuntimeInfo != null ? chainDurationSelector(nextRuntimeInfo) : 0))
                    {
                        nextRuntimeInfo = dependencyRuntimeInfo;
                        nextPipId = dependencyEdge.OtherNode.ToPipId();
                    }
                }

                if (nextRuntimeInfo != null)
                {
                    path.Add((nextRuntimeInfo, nextPipId));
                    hasAnyPipInjectedRuntime |= nextRuntimeInfo.IsInjectedProcessExecuteTime;
                    pipId = nextPipId;
                }
                else
                {
                    break;
                }
            }

            return new CriticalPath(path, hasAnyPipInjectedRuntime);
        }

        private void LogPipPerformanceInfo(StringBuilder stringBuilder, PipId pipId, RunnablePipPerformanceInfo performanceInfo, CriticalPathReportContext context)
        {
            Pip pip = context.PipGraph.GetPipFromPipId(pipId);

            stringBuilder.AppendLine(I($"\t{pip.GetDescription(context.Context)}"));

            if (pip.PipType == PipType.Process)
            {
                bool isExplicitlyScheduled = (context.ExplicitlyScheduledProcessNodes == null ? false : context.ExplicitlyScheduledProcessNodes.Contains(pipId.ToNodeId()));
                stringBuilder.AppendLine(I($"\t\t{"Explicitly Scheduled",-90}: {isExplicitlyScheduled,10}"));
            }

            for (int i = 0; i < RunnablePipPerformanceInfo.DispatcherKindCount; i++)
            {
                var dispatcherKind = (DispatcherKind)i;
                long duration = performanceInfo.GetQueueDurationMs(dispatcherKind);
                if (duration != 0)
                {
                    stringBuilder.AppendLine(I($"\t\tQueue - {dispatcherKind,-82}: {duration,10}"));
                }
            }

            for (int i = 0; i < RunnablePipPerformanceInfo.PipExecutionStepCount; i++)
            {
                var step = (PipExecutionStep)i;
                long stepDuration = performanceInfo.GetStepDurationMs(step);
                if (stepDuration != 0)
                {
                    stringBuilder.AppendLine(I($"\t\tStep  - {step,-82}: {stepDuration,10}"));
                }

                long remoteStepDuration = 0;
                uint workerId = performanceInfo.GetWorkerId(step);
                if (workerId != 0)
                {
                    string workerName = $"{$"W{workerId}",10}:{context.Workers[(int)workerId].Name}";
                    stringBuilder.AppendLine(I($"\t\t  {"WorkerName",-88}: {workerName}"));

                    long queueRequest = performanceInfo.GetPipBuildRequestQueueDurationMs(step);
                    stringBuilder.AppendLine(I($"\t\t  {"OrchestratorQueueRequest",-88}: {queueRequest,10}"));

                    long grpcDuration = performanceInfo.GetPipBuildRequestGrpcDurationMs(step);
                    stringBuilder.AppendLine(I($"\t\t  {"Grpc",-88}: {grpcDuration,10}"));

                    long remoteQueueDuration = performanceInfo.GetRemoteQueueDurationMs(step);
                    stringBuilder.AppendLine(I($"\t\t  {"RemoteQueue",-88}: {remoteQueueDuration,10}"));

                    remoteStepDuration = performanceInfo.GetRemoteStepDurationMs(step);
                    stringBuilder.AppendLine(I($"\t\t  {"RemoteStep",-88}: {remoteStepDuration,10}"));

                }

                if (stepDuration != 0 && step == PipExecutionStep.CacheLookup)
                {
                    PipCachePerfInfo cacheLookupPerfInfo = performanceInfo.CacheLookupPerfInfo;
                    if (cacheLookupPerfInfo != null)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"NumCacheEntriesVisited",-88}: {cacheLookupPerfInfo.NumCacheEntriesVisited,10}"));
                        stringBuilder.AppendLine(I($"\t\t  {"NumPathSetsDownloaded",-88}: {cacheLookupPerfInfo.NumPathSetsDownloaded,10}"));
                        stringBuilder.AppendLine(I($"\t\t  {"NumCacheEntriesAbsent",-88}: {cacheLookupPerfInfo.NumCacheEntriesAbsent,10}"));

                        for (int j = 0; j < OperationKind.TrackedCacheLookupCounterCount; j++)
                        {
                            OperationKind operationKind = OperationKind.GetTrackedCacheOperationKind(j);
                            var counter = cacheLookupPerfInfo.GetBeforeExecutionCounter(operationKind);
                            long duration = counter.DurationTicks / TimeSpan.TicksPerMillisecond;

                            if (duration != 0)
                            {
                                stringBuilder.AppendLine(I($"\t\t  {operationKind,-88}: {duration,10} - occurred {counter.Occurrences,10} times"));
                            }
                        }
                    }
                }

                if (stepDuration != 0 && step == PipExecutionStep.ExecuteProcess)
                {
                    stringBuilder.AppendLine(I($"\t\t  {"PushOutputsToCacheDurationMs",-88}: {performanceInfo.PushOutputsToCacheDurationMs,10}"));

                    if (performanceInfo.CacheMissAnalysisDuration.TotalMilliseconds != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"CacheMissAnalysis",-88}: {(long)performanceInfo.CacheMissAnalysisDuration.TotalMilliseconds,10}"));
                    }

                    if (performanceInfo.SuspendedDurationMs != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"SuspendedDurationMs",-88}: {performanceInfo.SuspendedDurationMs,10}"));
                    }

                    if (performanceInfo.RetryDurationMs != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"RetryDurationMs",-88}: {performanceInfo.RetryDurationMs,10}"));
                    }

                    if (performanceInfo.RetryCount != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"RetryCount",-88}: {performanceInfo.RetryCount,10}"));
                    }

                    if (performanceInfo.ExeDuration.TotalMilliseconds != 0)
                    {
                        string exeDurationValue = performanceInfo.IsInjectedExeDuration
                            ? $"[injected] {(long)performanceInfo.ExeDuration.TotalMilliseconds} (measured {(long)performanceInfo.OriginalExeDuration.Value.TotalMilliseconds})"
                            : $"{(long)performanceInfo.ExeDuration.TotalMilliseconds}";
                        stringBuilder.AppendLine($"\t\t  {"ExeDurationMs",-88}: {exeDurationValue,10}");
                    }
                }

                if (stepDuration != 0 && step == PipExecutionStep.MaterializeOutputs)
                {
                    stringBuilder.AppendLine(I($"\t\t  {"InBackground",-88}: {context.MaterializeOutputsInBackground,10}"));

                    if (performanceInfo.QueueWaitDurationForMaterializeOutputsInBackground.TotalMilliseconds != 0)
                    {
                        stringBuilder.AppendLine(I($"\t\t  {"Queue.Materialize.InBackground",-88}: {(long)performanceInfo.QueueWaitDurationForMaterializeOutputsInBackground.TotalMilliseconds,10}"));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The reporting-only state passed to <see cref="CriticalPathTracker.LogCriticalPathAndTopPips"/>. It bundles state that is
    /// only needed when the end-of-build report is produced: stable references that exist throughout the build but are
    /// not read until the end, plus transient timing values that only become meaningful once the build has completed.
    /// </summary>
    internal sealed class CriticalPathReportContext
    {
        /// <summary>Destination for the critical-path statistics suites.</summary>
        public Dictionary<string, long> Statistics { get; init; }

        /// <summary>Optional build summary whose critical path section is populated for the primary critical path.</summary>
        public BuildSummary BuildSummary { get; init; }

        /// <summary>Per-pip performance information for all completed pips.</summary>
        public IReadOnlyDictionary<PipId, RunnablePipPerformanceInfo> RunnablePipPerformance { get; init; }

        /// <summary>Pip graph, used to resolve pip descriptions.</summary>
        public PipGraph PipGraph { get; init; }

        /// <summary>Execution context, used to format pip descriptions.</summary>
        public PipExecutionContext Context { get; init; }

        /// <summary>Counters used to report the critical path duration and post-scheduler tasks.</summary>
        public CounterCollection<PipExecutorCounter> PipExecutionCounters { get; init; }

        /// <summary>Workers, used to resolve worker names in the per-pip dump.</summary>
        public IReadOnlyList<Worker> Workers { get; init; }

        /// <summary>Whether outputs are materialized in the background (affects running-time accounting).</summary>
        public bool MaterializeOutputsInBackground { get; init; }

        /// <summary>The nodes explicitly requested on the command line, or null.</summary>
        public HashSet<NodeId> ExplicitlyScheduledProcessNodes { get; init; }

        /// <summary>Logging context for the execute phase.</summary>
        public LoggingContext LoggingContext { get; init; }

        /// <summary>UTC time the first process started, or null.</summary>
        public DateTime? ProcessStartTimeUtc { get; init; }

        /// <summary>Time from scheduler start to the first pip.</summary>
        public TimeSpan TimeToFirstPip { get; init; }

        /// <summary>UTC time the scheduler started.</summary>
        public DateTime SchedulerStartedTimeUtc { get; init; }

        /// <summary>UTC time the scheduler completed.</summary>
        public DateTime SchedulerDoneTimeUtc { get; init; }

        /// <summary>UTC time the scheduler completed everything except background output materialization, or null.</summary>
        public DateTime? SchedulerCompletionExceptMaterializeOutputsTimeUtc { get; init; }
    }
}
