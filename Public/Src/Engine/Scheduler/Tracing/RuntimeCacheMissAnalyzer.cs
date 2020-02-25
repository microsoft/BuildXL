// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Engine.Cache;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance,
            FingerprintStoreTestHooks testHooks = null)
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
                    return new RuntimeCacheMissAnalyzer(
                        logTarget,
                        loggingContext,
                        context,
                        possibleStore.Result,
                        graph,
                        runnablePipPerformance,
                        configuration.Logging.CacheMissDiffFormat,
                        configuration.Logging.CacheMissBatch,
                        testHooks: testHooks);
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

        private readonly int m_maxCacheMissCanPerform = EngineEnvironmentSettings.MaxNumPipsForCacheMissAnalysis.Value;
        private int m_numCacheMissPerformed = 0;

        /// <summary>
        /// Dictionary of cache misses for runtime cache miss analysis.
        /// </summary>
        private readonly ConcurrentDictionary<PipId, PipCacheMissInfo> m_pipCacheMissesDict;

        private readonly NagleQueue<JProperty> m_batchLoggingQueue;

        // According to https://www.aria.ms/developers/deep-dives/input-constraints/, 
        // the event size limit is 2.5MB. Each charater in a string take 2 bytes. 
        // So the maximum length of an event can be set up to 2.5*1024*1024/2.
        // However, we don't want to send an event hit this limit. 
        // The number we set here is big enough for a batch reporting but still far away from hitting the limit.
        private const int MaxLogSizeValue = 800000;
        internal static int MaxLogSize = MaxLogSizeValue;

        /// <summary>
        /// A previous build's <see cref="FingerprintStore"/> that can be used for cache miss comparison.
        /// This may also be a snapshot of the current build's main <see cref="FingerprintStore"/> at the beginning of the build.
        /// </summary>
        public FingerprintStore PreviousFingerprintStore { get; }

        private readonly CacheMissDiffFormat m_cacheMissDiffFormat;
        private readonly FingerprintStoreTestHooks m_testHooks;

        private RuntimeCacheMissAnalyzer(
            FingerprintStoreExecutionLogTarget logTarget,
            LoggingContext loggingContext,
            PipExecutionContext context,
            FingerprintStore previousFingerprintStore,
            IReadonlyDirectedGraph graph,
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance,
            CacheMissDiffFormat cacheMissDiffFormat,
            bool cacheMissBatch,
            FingerprintStoreTestHooks testHooks = null)
        {
            m_loggingContext = loggingContext;
            m_logTarget = logTarget;
            m_context = context;
            PreviousFingerprintStore = previousFingerprintStore;
            m_visitor = new NodeVisitor(graph);
            m_changedPips = new VisitationTracker(graph);
            m_pipCacheMissesDict = new ConcurrentDictionary<PipId, PipCacheMissInfo>();
            m_runnablePipPerformance = runnablePipPerformance;
            m_cacheMissDiffFormat = cacheMissDiffFormat;
            m_maxCacheMissCanPerform = cacheMissBatch ? EngineEnvironmentSettings.MaxNumPipsForCacheMissAnalysis.Value * EngineEnvironmentSettings.MaxMessagesPerBatch : EngineEnvironmentSettings.MaxNumPipsForCacheMissAnalysis.Value;

            m_batchLoggingQueue = cacheMissBatch ? NagleQueue<JProperty>.Create(
                BatchLogging,
                maxDegreeOfParallelism: 1,
                interval: TimeSpan.FromMinutes(1),
                batchSize: EngineEnvironmentSettings.MaxMessagesPerBatch) : null;


            m_testHooks = testHooks;
            m_testHooks?.InitRuntimeCacheMisses();
        }


        /// <summary>
        /// The batch log payload example: 
        /// {"CacheMissAnalysisResults":
        ///     {
        ///         Pip123: {
        ///             Description:
        ///             FromCacheLookUp:
        ///             Detail: {
        ///                ActualMissType: ...
        ///                ReasonFromAnalysis: ...
        ///                Info: ...
        ///             }
        ///         },
        ///         Pip345: {
        ///             Description:
        ///             FromCacheLookUp:
        ///             Detail: {
        ///                ActualMissType: ...
        ///                ReasonFromAnalysis: ...
        ///                Info: ...
        ///             }
        ///         },
        ///     }
        ///}
        /// </summary>
        internal Task<Unit> BatchLogging(JProperty[] results)
        {
            // Use JsonTextWritter for 2 reasons:
            // 1. easily control when to start a new log event and when to end it.
            // 2. according to some research, manually serialization with JsonTextWritter can improve performance.
            using (Counters.StartStopwatch(FingerprintStoreCounters.CacheMissBatchLoggingTime))                
            {
                ProcessResults(results, MaxLogSize, m_loggingContext);
                return Unit.VoidTask;
            }
        }

        internal static void ProcessResults(JProperty[] results, int maxLogSize, LoggingContext loggingContext)
        {
            using (var sbPool = Pools.GetStringBuilder())
            {
                var sb = sbPool.Instance;
                var sw = new StringWriter(sb);
                var writer = new JsonTextWriter(sw);
                var logStarted = false;
                var lenSum = 0;
                for (int i = 0; i < results.Length;)
                {
                    if (!logStarted)
                    {
                        writer.WriteStartObject();
                        writer.WritePropertyName("CacheMissAnalysisResults");
                        writer.WriteStartObject();
                        logStarted = true;
                    }

                    var name = results[i].Name.ToString();
                    var value = results[i].Value.ToString();
                    lenSum += name.Length + value.Length;
                    if (lenSum < maxLogSize)
                    {
                        writeProperty(name, value);
                        i++;
                    }
                    else
                    {
                        // Give warning instead of a single result if max length exceeded, 
                        // otherwise finish this batch without i++. 
                        // So this item will go to next batch.
                        if ((name.Length + value.Length) > maxLogSize)
                        {
                            writeProperty(name, "Warning: The actual cache miss analysis result is too long to present.");
                            i++;
                        }
                        lenSum = 0;
                        endLogging();
                    }
                }

                endLogging();

                void writeProperty(string name, string value)
                {
                    writer.WritePropertyName(name);
                    writer.WriteRawValue(value);
                }

                void endLogging()
                {
                    // Only log when at least one result has been written to the Json string
                    if (logStarted)
                    {
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        logStarted = false;
                        Logger.Log.CacheMissAnalysisBatchResults(loggingContext, sw.ToString());                      
                    }
                }
            }
        }

        internal void AddCacheMiss(PipCacheMissInfo cacheMissInfo) => m_pipCacheMissesDict.Add(cacheMissInfo.PipId, cacheMissInfo);

        internal void AnalyzeForCacheLookup(FingerprintStoreEntry newEntry, Process pip) => Analyze(newEntry, pip, fromCacheLookup: true);

        internal void AnalyzeForExecution(FingerprintStoreEntry newEntry, Process pip) => Analyze(newEntry, pip, fromCacheLookup: false);

        private void Analyze(FingerprintStoreEntry newEntry, Process pip, bool fromCacheLookup)
        {
            Contract.Requires(pip != null);

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
            Contract.Requires(pip != null);
            string pipDescription = pip.GetDescription(m_context);

            try
            {
                if (!m_pipCacheMissesDict.TryRemove(pip.PipId, out var missInfo))
                {
                    return;
                }

                MarkPipAsChanged(pip.PipId);

                if (fromCacheLookup)
                {
                    Counters.IncrementCounter(FingerprintStoreCounters.CacheMissAnalysisAnalyzeCacheLookUpCount);
                }
                else
                {
                    Counters.IncrementCounter(FingerprintStoreCounters.CacheMissAnalysisAnalyzeExecutionCount);
                }

                using (var pool = Pools.StringBuilderPool.GetInstance())
                using (Counters.StartStopwatch(FingerprintStoreCounters.CacheMissAnalysisAnalyzeDuration))
                {
                    var resultAndDetail = CacheMissAnalysisUtilities.AnalyzeCacheMiss(
                        missInfo,
                        () => new FingerprintStoreReader.PipRecordingSession(PreviousFingerprintStore, oldEntry),
                        () => new FingerprintStoreReader.PipRecordingSession(m_logTarget.ExecutionFingerprintStore, newEntry),
                        m_cacheMissDiffFormat);

                    pipDescription = pip.GetDescription(m_context);

                    if (m_batchLoggingQueue != null)
                    {
                        m_batchLoggingQueue.Enqueue(resultAndDetail.Detail.ToJObjectWithPipInfo(pip.FormattedSemiStableHash, pipDescription, fromCacheLookup));
                    }
                    else
                    {
                        var detail = new JObject(
                            new JProperty(nameof(resultAndDetail.Detail.ActualMissType), resultAndDetail.Detail.ActualMissType), 
                            new JProperty(nameof(resultAndDetail.Detail.ReasonFromAnalysis), resultAndDetail.Detail.ReasonFromAnalysis), 
                            new JProperty(nameof(resultAndDetail.Detail.Info), resultAndDetail.Detail.Info)).ToString();
                        Logger.Log.CacheMissAnalysis(m_loggingContext, pipDescription, detail, fromCacheLookup);
                    }                  

                    m_testHooks?.AddCacheMiss(
                        pip.PipId,
                        new FingerprintStoreTestHooks.CacheMissData
                        {
                            DetailAndResult = resultAndDetail,
                            IsFromCacheLookUp = fromCacheLookup
                        });
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
            if (Interlocked.Increment(ref m_numCacheMissPerformed) >= m_maxCacheMissCanPerform)
            {
                Counters.IncrementCounter(FingerprintStoreCounters.CacheMissAnalysisExceedMaxNumAndCannotPerformCount);
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

        private void MarkPipAsChanged(PipId pipId) => m_visitor.VisitTransitiveDependents(pipId.ToNodeId(), m_changedPips, n => true);

        private bool TryGetFingerprintStoreEntry(Process process, out FingerprintStoreEntry entry)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.CacheMissFindOldEntriesTime))
            {
                string pipUniqueOutputHashStr = process.TryComputePipUniqueOutputHash(m_context.PathTable, out var pipUniqueOutputHash, m_logTarget.PipContentFingerprinter.PathExpander)
                    ? pipUniqueOutputHash.ToString()
                    : null;

                return PreviousFingerprintStore.TryGetFingerprintStoreEntry(pipUniqueOutputHashStr, process.FormattedSemiStableHash, out entry);
            }
        }

        /// <nodoc/>
        public void Dispose()
        {
            PreviousFingerprintStore.Dispose();
            using (Counters.StartStopwatch(FingerprintStoreCounters.RuntimeCacheMissBatchLoggingQueueDisposeDuration))
            {
                m_batchLoggingQueue?.Dispose();
            }
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
                    performance.PerformedCacheMissAnalysis(m_watch.Elapsed);
                }

                m_watch.Dispose();
            }
        }
    }
}
