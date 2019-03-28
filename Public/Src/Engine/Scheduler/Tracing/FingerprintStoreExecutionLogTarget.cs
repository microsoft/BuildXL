// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using static BuildXL.Scheduler.Tracing.FingerprintStore;
using KVP = System.Collections.Generic.KeyValuePair<string, string>;
using PipKVP = System.Collections.Generic.KeyValuePair<string, BuildXL.Scheduler.Tracing.FingerprintStore.PipFingerprintKeys>;
using System.Collections.Generic;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Logging target for sending inputs to fingerprint computation to fingerprint input store.
    /// Encapsulates the logic for serializing entries for the fingerprint store.
    /// </summary>
    public sealed class FingerprintStoreExecutionLogTarget : ExecutionLogTargetBase
    {
        /// <summary>
        /// Pip execution context
        /// </summary>
        private readonly PipExecutionContext m_context;

        /// <summary>
        /// Used to hydrate pips from <see cref="PipId"/>s.
        /// </summary>
        private readonly PipTable m_pipTable;

        /// <summary>
        /// Used to collect the inputs used during weak fingerprint computation.
        /// </summary>
        internal readonly PipContentFingerprinter PipContentFingerprinter;

        /// <summary>
        /// Key-value store for storing fingerprint computation data.
        /// </summary>
        internal readonly FingerprintStore FingerprintStore;

        /// <summary>
        /// Maintains the order of cache misses seen in a build.
        /// </summary>
        private readonly ConcurrentQueue<PipCacheMissInfo> m_pipCacheMissesQueue;

        /// <summary>
        /// Whether the <see cref="Tracing.FingerprintStore"/> should be garbage collected during dispose.
        /// </summary>
        private bool m_fingerprintComputedForExecution = false;

        /// <summary>
        /// Store fingerprint inputs from workers in distributed builds.
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        /// <summary>
        /// Counters, shared with <see cref="Tracing.FingerprintStore"/>.
        /// </summary>
        public CounterCollection<FingerprintStoreCounters> Counters => FingerprintStore.Counters;

        private readonly Task<RuntimeCacheMissAnalyzer> m_runtimeCacheMissAnalyzerTask;
        private RuntimeCacheMissAnalyzer RuntimeCacheMissAnalyzer => m_runtimeCacheMissAnalyzerTask.GetAwaiter().GetResult();

        private bool CacheMissAnalysisEnabled => RuntimeCacheMissAnalyzer != null;

        /// <summary>
        /// Creates a <see cref="FingerprintStoreExecutionLogTarget"/>.
        /// </summary>
        /// <returns>
        /// If successful, a <see cref="FingerprintStoreExecutionLogTarget"/> that logs to
        /// a <see cref="Tracing.FingerprintStore"/> at the provided directory;
        /// otherwise, null.
        /// </returns>
        public static FingerprintStoreExecutionLogTarget Create(
            PipExecutionContext context,
            PipTable pipTable,
            PipContentFingerprinter pipContentFingerprinter,
            string fingerprintStoreDirectory,
            LoggingContext loggingContext,
            IConfiguration configuration,
            EngineCache cache,
            IReadonlyDirectedGraph graph,
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance = null,
            FingerprintStoreTestHooks testHooks = null)
        {
            var maxEntryAge = new TimeSpan(hours: 0, minutes: configuration.Logging.FingerprintStoreMaxEntryAgeMinutes, seconds: 0);
            var possibleStore = FingerprintStore.Open(
                fingerprintStoreDirectory,
                maxEntryAge: maxEntryAge,
                mode: configuration.Logging.FingerprintStoreMode,
                loggingContext: loggingContext,
                testHooks: testHooks);

            if (possibleStore.Succeeded)
            {
                return new FingerprintStoreExecutionLogTarget(
                    loggingContext,
                    context,
                    pipTable,
                    pipContentFingerprinter,
                    possibleStore.Result,
                    configuration,
                    cache,
                    graph,
                    runnablePipPerformance);
            }
            else
            {
                Logger.Log.FingerprintStoreUnableToOpen(loggingContext, possibleStore.Failure.DescribeIncludingInnerFailures());
            }

            return null;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        private FingerprintStoreExecutionLogTarget(
            LoggingContext loggingContext,
            PipExecutionContext context,
            PipTable pipTable,
            PipContentFingerprinter pipContentFingerprinter,
            FingerprintStore fingerprintStore,
            IConfiguration configuration,
            EngineCache cache,
            IReadonlyDirectedGraph graph,
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance)
        {
            m_context = context;
            m_pipTable = pipTable;
            PipContentFingerprinter = pipContentFingerprinter;
            FingerprintStore = fingerprintStore;
            m_pipCacheMissesQueue = new ConcurrentQueue<PipCacheMissInfo>();
            m_runtimeCacheMissAnalyzerTask = RuntimeCacheMissAnalyzer.TryCreateAsync(
                this,
                loggingContext,
                context,
                configuration,
                cache,
                graph,
                runnablePipPerformance);
        }

        /// <summary>
        /// For now the fingerprint store doesn't care about workerId,
        /// so just use the same object instead of making a new object with the same
        /// underlying store.
        /// </summary>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId)
        {
            return this;
        }

        /// <summary>
        /// Adds an entry to the fingerprint store for { directory fingerprint : directory fingerprint inputs }.
        /// </summary>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            if (FingerprintStore.Disabled)
            {
                return;
            }

            using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreLoggingTime))
            {
                Counters.IncrementCounter(FingerprintStoreCounters.NumDirectoryMembershipEvents);

                var stringContentHash = ContentHashToString(data.DirectoryFingerprint.Hash);
                if (!FingerprintStore.ContainsContentHash(stringContentHash))
                {
                    Counters.IncrementCounter(FingerprintStoreCounters.NumDirectoryMembershipEntriesPut);
                    FingerprintStore.PutContentHash(stringContentHash, JsonSerialize(data));
                }
            }
        }

        /// <summary>
        /// Selects the most relevant strong fingerprint computation event to use.
        /// </summary>
        private ProcessStrongFingerprintComputationData? SelectStrongFingerprintComputationData(ProcessFingerprintComputationEventData data)
        {
            var numStrongFingerprints = data.StrongFingerprintComputations.Count;

            if (numStrongFingerprints == 0)
            {
                return null;
            }

            // There are two cases when fingerprints are put into the store:
            //
            // Case 1: If processing a strong fingerprint computed for cache lookup (cache hit, cache misses are ignored until execution), 
            // cache lookup automatically stops on the strong fingerprint match, so the last strong fingerprint is the fingerprint used
            //
            // Case 2: If processing a strong fingerprint computed for execution (cache miss), 
            // there should only be one strong fingerprint, so the last strong fingerprint is the fingerprint used
            return data.StrongFingerprintComputations[numStrongFingerprints - 1];
        }

        /// <summary>
        /// Helper functions for putting entries into the fingerprint store for sub-components.
        /// { pip formatted semi stable hash : weak fingerprint, strong fingerprint, path set hash }
        /// { weak fingerprint hash : weak fingerprint inputs }
        /// { strong fingerprint hash : strong fingerprint inputs }
        /// { path set hash : path set inputs }
        /// </summary>
        private FingerprintStoreEntry CreateAndStoreFingerprintStoreEntry(
            Process pip,
            PipFingerprintKeys pipFingerprintKeys,
            WeakContentFingerprint weakFingerprint,
            ProcessStrongFingerprintComputationData strongFingerprintData)
        {
            // If we got this far, a new pip is being put in the store
            Counters.IncrementCounter(FingerprintStoreCounters.NumPipFingerprintEntriesPut);

            UpdateOrStorePipUniqueOutputHashEntry(pip);

            // A content hash-keyed entry will have the same value as long as the key is the same, so overwriting it is unnecessary
            var mustStorePathEntry = !FingerprintStore.ContainsContentHash(pipFingerprintKeys.PathSetHash) || CacheMissAnalysisEnabled;

            var entry = CreateFingerprintStoreEntry(pip, pipFingerprintKeys, weakFingerprint, strongFingerprintData, mustStorePathEntry);

            FingerprintStore.PutFingerprintStoreEntry(entry, storePathSet: mustStorePathEntry);
            return entry;
        }

        internal FingerprintStoreEntry CreateFingerprintStoreEntry(
            Process pip,
            PipFingerprintKeys pipFingerprintKeys,
            WeakContentFingerprint weakFingerprint,
            ProcessStrongFingerprintComputationData strongFingerprintData,
            bool mustStorePathEntry = true)
        {
            return new FingerprintStoreEntry
            {
                // { pip formatted semi stable hash : weak fingerprint, strong fingerprint, path set hash }
                PipToFingerprintKeys = new PipKVP(pip.FormattedSemiStableHash, pipFingerprintKeys),
                // { weak fingerprint hash : weak fingerprint inputs }
                WeakFingerprintToInputs = new KVP(pipFingerprintKeys.WeakFingerprint, JsonSerialize(pip)),
                StrongFingerprintEntry = new StrongFingerprintEntry
                {
                    // { strong fingerprint hash: strong fingerprint inputs }
                    StrongFingerprintToInputs = new KVP(pipFingerprintKeys.StrongFingerprint, JsonSerialize(weakFingerprint, strongFingerprintData.PathSetHash, strongFingerprintData.ObservedInputs)),
                    // { path set hash : path set inputs }
                    // If fingerprint comparison is enabled, the entry should contain the pathset json.
                    PathSetHashToInputs = mustStorePathEntry ? new KVP(pipFingerprintKeys.PathSetHash, JsonSerialize(strongFingerprintData)) : default,
                }
            };
        }

        /// <summary>
        /// Processes a fingerprint computed for cache lookup. This will put or overwrite an entry in the fingerprint store
        /// if two conditions are met:
        /// 1. The fingerprint computed has a strong fingerprint match from the cache.
        /// 2. The fingerprint computed does not already exist with the same value in the fingerprint store.
        /// </summary>
        private void ProcessFingerprintComputedForCacheLookup(ProcessFingerprintComputationEventData data)
        {
            // Strong fingerprint misses need to be analyzed during cache-lookup to get a precise reason.
            RuntimeCacheMissAnalyzer?.AnalyzeForCacheLookup(data);

            var maybeStrongFingerprintData = SelectStrongFingerprintComputationData(data);

            if (maybeStrongFingerprintData == null)
            {
                // Weak fingerprint miss, relevant fingerprint information will be recorded at execution time
                Counters.IncrementCounter(FingerprintStoreCounters.NumFingerprintComputationSkippedWeakFingerprintMiss);
                return;
            }

            var strongFingerprintData = maybeStrongFingerprintData.Value;

            if (!strongFingerprintData.IsStrongFingerprintHit)
            {
                // Strong fingerprint miss, relevant fingerprint information will be recorded at execution time
                Counters.IncrementCounter(FingerprintStoreCounters.NumFingerprintComputationSkippedStrongFingerprintMiss);
                return;
            }

            Process pip = GetProcess(data.PipId);
            var weakFingerprint = data.WeakFingerprint;
            var strongFingerprint = strongFingerprintData.ComputedStrongFingerprint;

            var pipFingerprintKeys = new PipFingerprintKeys(weakFingerprint, strongFingerprint, ContentHashToString(strongFingerprintData.PathSetHash));
            // Skip overwriting the same value on cache hits
            if (SameValueEntryExists(pip, pipFingerprintKeys))
            {
                // No fingerprint entry needs to be stored for this pip, but it's unique output hash entry might need to be updated
                UpdateOrStorePipUniqueOutputHashEntry(pip);

                Counters.IncrementCounter(FingerprintStoreCounters.NumFingerprintComputationSkippedSameValueEntryExists);
                return;
            }

            CreateAndStoreFingerprintStoreEntry(pip, pipFingerprintKeys, weakFingerprint, strongFingerprintData);
            Counters.IncrementCounter(FingerprintStoreCounters.NumHitEntriesPut);
        }

        /// <summary>
        /// Processes a fingerprint computed for execution. This will always put or overwrite an entry in the 
        /// fingerprint store.
        /// </summary>
        private void ProcessFingerprintComputedForExecution(ProcessFingerprintComputationEventData data)
        {
            m_fingerprintComputedForExecution = true;

            var maybeStrongFingerprintData = SelectStrongFingerprintComputationData(data);
            FingerprintStoreEntry newEntry = null;
            Process pip = GetProcess(data.PipId);

            if (maybeStrongFingerprintData == null)
            {
                // If an executed pip doesn't have a fingerprint computation, don't put it in the fingerprint store
                Counters.IncrementCounter(FingerprintStoreCounters.NumFingerprintComputationSkippedNonCacheablePip);
            }
            else
            {
                var strongFingerprintData = maybeStrongFingerprintData.Value;
                var weakFingerprint = data.WeakFingerprint;
                var strongFingerprint = strongFingerprintData.ComputedStrongFingerprint;
                var pipFingerprintKeys = new PipFingerprintKeys(weakFingerprint, strongFingerprint, ContentHashToString(strongFingerprintData.PathSetHash));
                newEntry = CreateAndStoreFingerprintStoreEntry(pip, pipFingerprintKeys, weakFingerprint, strongFingerprintData);
            }

            RuntimeCacheMissAnalyzer?.AnalyzeForExecution(newEntry, pip);
        }

        /// <summary>
        /// Stores fingerprint computation information once per pip:
        /// If cache hit, store info for the fingerprint match calculated during cache lookup.
        /// If cache miss, store info for the fingerprint calculated at execution time.
        /// 
        /// Adds entries to the fingerprint store for:
        /// { pip semistable hash : weak and strong fingerprint hashes }
        /// { weak fingerprint hash : weak fingerprint inputs }
        /// { strong fingerprint hash : strong fingerprint inputs }
        /// { path set hash : path set inputs }
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (FingerprintStore.Disabled)
            {
                return;
            }

            using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreLoggingTime))
            {
                Counters.IncrementCounter(FingerprintStoreCounters.NumFingerprintComputationEvents);

                if (data.Kind == FingerprintComputationKind.CacheCheck)
                {
                    ProcessFingerprintComputedForCacheLookup(data);
                }
                else
                {
                    ProcessFingerprintComputedForExecution(data);
                }
            }
        }

        /// <summary>
        /// Aggregate a list of the pip cache misses to write to the store at the end of the build.
        /// </summary>
        public override void PipCacheMiss(PipCacheMissEventData data)
        {
            if (FingerprintStore.Disabled)
            {
                return;
            }

            GetProcess(data.PipId).TryComputePipUniqueOutputHash(m_context.PathTable, out var pipUniqueOutputHash, PipContentFingerprinter.PathExpander);

            using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreLoggingTime))
            {
                var cacheMissInfo = new PipCacheMissInfo
                {
                    PipId = data.PipId,
                    CacheMissType = data.CacheMissType,
                };

                m_pipCacheMissesQueue.Enqueue(cacheMissInfo);
                RuntimeCacheMissAnalyzer?.AddCacheMiss(cacheMissInfo);
            }
        }

        /// <summary>
        /// Checks if an entry for the pip with the same fingerprints already exists in the store.
        /// </summary>
        private bool SameValueEntryExists(Process pip, PipFingerprintKeys newKeys)
        {
            var keyFound = FingerprintStore.TryGetPipFingerprintKeys(pip.FormattedSemiStableHash, out PipFingerprintKeys oldKeys);
            return keyFound 
                && oldKeys.WeakFingerprint == newKeys.WeakFingerprint
                && oldKeys.StrongFingerprint == newKeys.StrongFingerprint;
        }

        /// <summary>
        /// Updates the pip unique output hash entry in the fingerprint store to match the pip in the current build.
        /// 
        /// The pip unique output hash is more stable that the pip formatted semi stable hash. If it can be computed
        /// and does not already exist, store an entry to act as the primary lookup key.
        /// </summary>
        private void UpdateOrStorePipUniqueOutputHashEntry(Process pip)
        {
            if (pip.TryComputePipUniqueOutputHash(m_context.PathTable, out var outputHash, PipContentFingerprinter.PathExpander))
            {
                var entryExists = FingerprintStore.TryGetPipUniqueOutputHashValue(outputHash.ToString(), out var oldSemiStableHash);
                if (!entryExists // missing
                    || (entryExists && oldSemiStableHash != pip.FormattedSemiStableHash)) // out-of-date
                {
                    Counters.IncrementCounter(FingerprintStoreCounters.NumPipUniqueOutputHashEntriesPut);
                    FingerprintStore.PutPipUniqueOutputHash(outputHash, pip.FormattedSemiStableHash);
                }
            }
        }

        /// <summary>
        /// Serializes the value to JSON for { weak fingerprint hash : weak fingerprint inputs }.
        /// </summary>
        private string JsonSerialize(Process pip)
        {
            return JsonSerializeHelper((writer) =>
            {
                // Use same logic as fingerprint computation
                PipContentFingerprinter.AddWeakFingerprint(writer, pip);
            },
            pathExpander: PipContentFingerprinter.PathExpander);
        }

        /// <summary>
        /// Serializes the value to JSON for { strong fingerprint hash : strong fingerprint inputs }.
        /// </summary>
        private string JsonSerialize(WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, ReadOnlyArray<ObservedInput> observedInputs)
        {
            return JsonSerializeHelper((writer) =>
            {
                // Use same logic as fingerprint computation
                ObservedInputProcessingResult.AddStrongFingerprintContent(writer, weakFingerprint, pathSetHash, observedInputs);
            });
        }

        /// <summary>
        /// Serializes <see cref="ProcessStrongFingerprintComputationData"/> to JSON, including the path set.
        /// </summary>
        private string JsonSerialize(ProcessStrongFingerprintComputationData data)
        {
            return JsonSerializeHelper((writer) =>
            {
                data.WriteFingerprintInputs(writer);
            },
            pathExpander: PipContentFingerprinter.PathExpander);
        }

        /// <summary>
        /// Serializes <see cref="IFingerprintInputCollection"/> to JSON.
        /// </summary>
        private string JsonSerialize(IFingerprintInputCollection data)
        {
            return JsonSerializeHelper((writer) =>
            {
                data.WriteFingerprintInputs(writer);
            });
        }

        /// <summary>
        /// Hydrates a pip from <see cref="PipId"/>. The pip will still be in-memory at call time.
        /// </summary>
        internal Process GetProcess(PipId pipId)
        {
            return (Process)m_pipTable.HydratePip(pipId, PipQueryContext.FingerprintStore);
        }

        /// <summary>
        /// Convenience wrapper converting JSON fingerprinting ops to string.
        /// </summary>
        private string JsonSerializeHelper(Action<JsonFingerprinter> fingerprintOps, PathExpander pathExpander = null)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.JsonSerializationTime))
            {
                return JsonFingerprinter.CreateJsonString(fingerprintOps, pathTable: m_context.PathTable, pathExpander: pathExpander);
            }
        }

        /// <summary>
        /// Converts a hash to a string. This should be kept in-sync with <see cref="JsonFingerprinter"/> to allow <see cref="Tracing.FingerprintStore"/> look-ups
        /// using content hashes parsed from JSON.
        /// </summary>
        internal static string ContentHashToString(ContentHash hash)
        {
            return JsonFingerprinter.ContentHashToString(hash);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreLoggingTime))
            {
                // Store the ordered pip cache miss list as one blob
                FingerprintStore.PutCacheMissList(m_pipCacheMissesQueue.ToList());
            }

            // We should first dispose the fingerprintStore in the RunCacheMissAnalyzer
            // because that might be the snapshot version of FingerprintStore
            // in case cache miss analysis is in the local-mode.
            RuntimeCacheMissAnalyzer?.Dispose();

            // For performance, cancel garbage collect for builds with no cache misses
            if (!m_fingerprintComputedForExecution)
            {
                FingerprintStore.GarbageCollectCancellationToken.Cancel();
            }

            FingerprintStore.Dispose();
            base.Dispose();
        }
    }
}
