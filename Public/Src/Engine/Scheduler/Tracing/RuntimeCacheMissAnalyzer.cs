// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Scheduler.Tracing.FingerprintStore;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Logging target for sending inputs to fingerprint computation to fingerprint input store.
    /// Encapsulates the logic for serializing entries for the fingerprint store.
    /// </summary>
    public sealed class RuntimeCacheMissAnalyzer : IDisposable
    {
        /// <summary>
        /// Initiates the task to load the fingerprint store that will be used for cache miss analysis
        /// </summary>
        public static async Task<RuntimeCacheMissAnalyzer> TryCreateAsync(
            FingerprintStoreExecutionLogTarget logTarget,
            LoggingContext loggingContext,
            PipExecutionContext context,
            IConfiguration configuration,
            EngineCache cache,
            IReadonlyDirectedGraph graph,
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance)
        {
            using (logTarget.Counters.StartStopwatch(FingerprintStoreCounters.InitializeCacheMissAnalysisDuration))
            {
                var option = configuration.Logging.CacheMissAnalysisOption;
                if (option.Mode == CacheMissMode.Disabled)
                {
                    return null;
                }

                Possible<FingerprintStore> possibleStore;

                if (option.Mode == CacheMissMode.Local)
                {
                    possibleStore = FingerprintStore.CreateSnapshot(logTarget.ExecutionFingerprintStore, loggingContext);
                }
                else
                {
                    string path = null;
                    if (option.Mode == CacheMissMode.CustomPath)
                    {
                        path = option.CustomPath.ToString(context.PathTable);
                    }
                    else
                    {
                        Contract.Assert(option.Mode == CacheMissMode.Remote);
                        foreach (var key in option.Keys)
                        {
                            var cacheSavePath = configuration.Logging.FingerprintsLogDirectory
                                .Combine(context.PathTable, Scheduler.FingerprintStoreDirectory + "." + key);
#pragma warning disable AsyncFixer02 // This should explicitly happen synchronously since it interacts with the PathTable and StringTable
                            var result = cache.TryRetrieveFingerprintStoreAsync(loggingContext, cacheSavePath, context.PathTable, key, configuration.Schedule.EnvironmentFingerprint).Result;
#pragma warning restore AsyncFixer02
                            if (result.Succeeded && result.Result)
                            {
                                path = cacheSavePath.ToString(context.PathTable);
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(path))
                        {
                            Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Could not find the fingerprint store for any given key: {string.Join(",", option.Keys)}"));
                            return null;
                        }
                    }

                    // Unblock caller
                    // WARNING: The rest can simultenously happen with saving the graph files to disk.
                    // We should not create any paths or strings by using PathTable and StringTable.
                    await Task.Yield();

                    possibleStore = FingerprintStore.Open(path, readOnly: true);
                }

                if (possibleStore.Succeeded)
                {
                    Logger.Log.SuccessLoadFingerprintStoreToCompare(loggingContext, option.Mode.ToString(), possibleStore.Result.StoreDirectory);
                    return new RuntimeCacheMissAnalyzer(logTarget, loggingContext, context, possibleStore.Result, graph, runnablePipPerformance);
                }

                Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Failed to read the fingerprint store to compare. Mode: {option.Mode.ToString()} Failure: {possibleStore.Failure.DescribeIncludingInnerFailures()}"));
                return null;
            }
        }

        private readonly FingerprintStoreExecutionLogTarget m_logTarget;
        private CounterCollection<FingerprintStoreCounters> Counters => m_logTarget.Counters;
        private readonly LoggingContext m_loggingContext;
        private readonly NodeVisitor m_visitor;
        private readonly VisitationTracker m_changedPips;
        private readonly IDictionary<PipId, RunnablePipPerformanceInfo> m_runnablePipPerformance;
        private readonly PipExecutionContext m_context;

        private static readonly int s_maxCacheMissCanPerform = EngineEnvironmentSettings.MaxNumPipsForCacheMissAnalysis.Value;
        private int m_numCacheMissPerformed = 0;

        /// <summary>
        /// Dictionary of cache misses for runtime cache miss analysis.
        /// </summary>
        private readonly ConcurrentDictionary<PipId, PipCacheMissInfo> m_pipCacheMissesDict;

        /// <summary>
        /// A previous build's <see cref="FingerprintStore"/> that can be used for cache miss comparison.
        /// This may also be a snapshot of the current build's main <see cref="FingerprintStore"/> at the beginning of the build.
        /// </summary>
        public FingerprintStore PreviousFingerprintStore { get; }

        private RuntimeCacheMissAnalyzer(
            FingerprintStoreExecutionLogTarget logTarget,
            LoggingContext loggingContext,
            PipExecutionContext context,
            FingerprintStore previousFingerprintStore,
            IReadonlyDirectedGraph graph,
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance)
        {
            m_loggingContext = loggingContext;
            m_logTarget = logTarget;
            m_context = context;
            PreviousFingerprintStore = previousFingerprintStore;
            m_visitor = new NodeVisitor(graph);
            m_changedPips = new VisitationTracker(graph);
            m_pipCacheMissesDict = new ConcurrentDictionary<PipId, PipCacheMissInfo>();
            m_runnablePipPerformance = runnablePipPerformance;
        }

        internal void AddCacheMiss(PipCacheMissInfo cacheMissInfo)
        {
            m_pipCacheMissesDict.Add(cacheMissInfo.PipId, cacheMissInfo);
        }

        internal void AnalyzeForCacheLookup(FingerprintStoreEntry newEntry, Process pip)
        {
            Analyze(newEntry, pip, fromCacheLookup: true);
        }

        internal void AnalyzeForExecution(FingerprintStoreEntry newEntry, Process pip)
        {
            Analyze(newEntry, pip, fromCacheLookup: false);
        }

        private void Analyze(FingerprintStoreEntry newEntry, Process pip, bool fromCacheLookup)
        {
            using (var watch = new CacheMissTimer(pip.PipId, this))
            {
                if (!IsCacheMissEligible(pip.PipId))
                {
                    return;
                }

                TryGetFingerprintStoreEntry(pip, out FingerprintStoreEntry oldEntry);
                PerformCacheMissAnalysis(pip, oldEntry, newEntry, fromCacheLookup);
            }
        }

        private void PerformCacheMissAnalysis(Process pip, FingerprintStoreEntry oldEntry, FingerprintStoreEntry newEntry, bool fromCacheLookup)
        {
            string pipDescription = pip.GetDescription(m_context);
            try
            {
                if (!m_pipCacheMissesDict.TryRemove(pip.PipId, out var missInfo))
                {
                    return;
                }

                MarkPipAsChanged(pip.PipId);

                if (Interlocked.Increment(ref m_numCacheMissPerformed) >= s_maxCacheMissCanPerform)
                {
                    return;
                }

                Counters.IncrementCounter(FingerprintStoreCounters.CacheMissAnalysisAnalyzeCount);

                using (var pool = Pools.StringBuilderPool.GetInstance())
                using (var writer = new StringWriter(pool.Instance))
                {
                    CacheMissAnalysisUtilities.AnalyzeCacheMiss(
                        writer,
                        missInfo,
                        () => new FingerprintStoreReader.PipRecordingSession(PreviousFingerprintStore, oldEntry),
                        () => new FingerprintStoreReader.PipRecordingSession(m_logTarget.ExecutionFingerprintStore, newEntry));

                    // The diff sometimes contains several empty new lines at the end.
                    var reason = writer.ToString().TrimEnd(Environment.NewLine.ToCharArray());

                    pipDescription = pip.GetDescription(m_context);
                    Logger.Log.CacheMissAnalysis(m_loggingContext, pipDescription, reason, fromCacheLookup);
                }
            }
            catch (Exception ex)
            {
                // Cache miss analysis shouldn't fail the build
                Logger.Log.CacheMissAnalysisException(m_loggingContext, pipDescription, ex.ToString(), oldEntry?.PipToFingerprintKeys.ToString(), newEntry?.PipToFingerprintKeys.ToString());
            }
        }

        private bool IsCacheMissEligible(PipId pipId)
        {
            if (m_numCacheMissPerformed >= s_maxCacheMissCanPerform)
            {
                return false;
            }

            if (!m_pipCacheMissesDict.ContainsKey(pipId))
            {
                return false;
            }

            if (!EngineEnvironmentSettings.RuntimeCacheMissAllPips
                && m_changedPips.WasVisited(pipId.ToNodeId()))
            {
                return false;
            }

            return true;
        }

        private void MarkPipAsChanged(PipId pipId)
        {
            m_visitor.VisitTransitiveDependents(pipId.ToNodeId(), m_changedPips, n => true);
        }

        private bool TryGetFingerprintStoreEntry(Process process, out FingerprintStoreEntry entry)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.CacheMissFindOldEntriesTime))
            {
                process.TryComputePipUniqueOutputHash(m_context.PathTable, out var pipUniqueOutputHash, m_logTarget.PipContentFingerprinter.PathExpander);
                return PreviousFingerprintStore.TryGetFingerprintStoreEntry(pipUniqueOutputHash.ToString(), process.FormattedSemiStableHash, out entry);
            }
        }

        /// <nodoc/>
        public void Dispose()
        {
            PreviousFingerprintStore.Dispose();
        }

        private struct CacheMissTimer : IDisposable
        {
            private readonly RuntimeCacheMissAnalyzer m_analyzer;
            private readonly PipId m_pipId;
            private readonly CounterCollection.Stopwatch m_watch;

            public CacheMissTimer(PipId pipId, RuntimeCacheMissAnalyzer analyzer)
            {
                m_analyzer = analyzer;
                m_pipId = pipId;
                m_watch = m_analyzer.Counters.StartStopwatch(FingerprintStoreCounters.CacheMissAnalysisTime);
            }

            public void Dispose()
            {
                RunnablePipPerformanceInfo performance = null;
                if (m_analyzer.m_runnablePipPerformance?.TryGetValue(m_pipId, out performance) == true)
                {
                    performance.PerformedCacheMiss(m_watch.Elapsed);
                }

                m_watch.Dispose();
            }
        }
    }
}
