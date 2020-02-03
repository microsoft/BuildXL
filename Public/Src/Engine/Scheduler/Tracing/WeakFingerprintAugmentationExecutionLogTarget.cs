// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Observes PathSet/WeakFingerprint augmentation process and logs any paths that might lead to cache misses.
    /// </summary>
    public sealed class WeakFingerprintAugmentationExecutionLogTarget : ExecutionLogTargetBase
    {
        private readonly int m_maxLoggedSuspiciousPathsPerPip;

        /// <summary>
        /// Pool of dictionaries mapping from an AbsolutePath to a tuple (ObservedPathEntry cacheLookup, ObservedPathEntry execution).        
        /// </summary>
        private static readonly ObjectPool<Dictionary<AbsolutePath, (ObservedInput cacheLookupInput, ObservedInput? executionInput)>> s_observedPathEntryDictionaryPool =
           new ObjectPool<Dictionary<AbsolutePath, (ObservedInput, ObservedInput?)>>(
               () => new Dictionary<AbsolutePath, (ObservedInput, ObservedInput?)>(),
               set => { set.Clear(); return set; });

        private readonly LoggingContext m_loggingContext;

        private readonly Scheduler m_scheduler;

        private readonly ConcurrentDictionary<PipId, List<(WeakContentFingerprint, ReadOnlyArray<ObservedInput> ObservedInputs)>> m_augmentedPathSets;

        /// <inheritdoc/>
        public override bool CanHandleWorkerEvents => true;

        /// <inheritdoc/>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId) => this;

        /// <nodoc />
        public WeakFingerprintAugmentationExecutionLogTarget(
            LoggingContext loggingContext,
            Scheduler scheduler,
            int maxLoggedSuspiciousPathsPerPip)
        {
            m_loggingContext = loggingContext;
            m_scheduler = scheduler;
            m_augmentedPathSets = new ConcurrentDictionary<PipId, List<(WeakContentFingerprint, ReadOnlyArray<ObservedInput> ObservedInputs)>>();
            m_maxLoggedSuspiciousPathsPerPip = maxLoggedSuspiciousPathsPerPip;
        }

        /// <inheritdoc/>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            // On a cache lookup, we record all augmented path sets. On execution, we analyze the augmented pathsets and remove
            // them from the dictionary. This way we keep a limited number of path sets in memory at a time.
            if (data.Kind == FingerprintComputationKind.CacheCheck)
            {
                if (data.StrongFingerprintComputations.Count == 0 || data.StrongFingerprintComputations.Any(o => o.IsStrongFingerprintHit))
                {
                    // No strong FP computations / cache hit -> return early
                    return;
                }

                // Do not create the list right away because we might not have any augmented weak fingerprints.
                List<(WeakContentFingerprint augmentedWeakFingerprint, ReadOnlyArray<ObservedInput> ObservedInputs)> augmentedPathSets = null;

                foreach (var strongFpComputation in data.StrongFingerprintComputations)
                {
                    if (strongFpComputation.AugmentedWeakFingerprint.HasValue)
                    {
                        if (augmentedPathSets == null)
                        {
                            augmentedPathSets = new List<(WeakContentFingerprint augmentedWeakFingerprint, ReadOnlyArray<ObservedInput> ObservedInputs)>();
                        }

                        augmentedPathSets.Add((strongFpComputation.AugmentedWeakFingerprint.Value, strongFpComputation.ObservedInputs));
                    }
                }

                if (augmentedPathSets != null)
                {
                    m_augmentedPathSets.TryAdd(data.PipId, augmentedPathSets);
                }
            }
            else if (data.Kind == FingerprintComputationKind.Execution)
            {
                if (!m_augmentedPathSets.TryRemove(data.PipId, out var augmentedPathSets) || augmentedPathSets == null)
                {
                    return;
                }

                // there must be exactly one pathset (using Single() to enforce this requirement)
                var cacheEntry = augmentedPathSets
                    .Where<(WeakContentFingerprint AugmentedWeakFingerprint, ReadOnlyArray<ObservedInput> ObservedInputs)>(o => o.AugmentedWeakFingerprint == data.WeakFingerprint)
                    .Single();
                var executionEntry = data.StrongFingerprintComputations.Single();
                var pathTable = m_scheduler.Context.PathTable;

                using (var pooledDictionary = s_observedPathEntryDictionaryPool.GetInstance())
                {
                    var augmentedPathSet = pooledDictionary.Instance;

                    var executionObservedInputs = executionEntry.ObservedInputs;

                    augmentedPathSet.AddRange(
                        cacheEntry.ObservedInputs.Select(
                            input => new KeyValuePair<AbsolutePath, (ObservedInput, ObservedInput?)>(input.Path, (input, null))));

                    // Remove all paths from the augmented pathset that were observed during pip execution.
                    foreach (var observedInput in executionObservedInputs)
                    {
                        if (augmentedPathSet.TryGetValue(observedInput.Path, out var value))
                        {
                            if (observedInput.PathEntry == value.cacheLookupInput.PathEntry)
                            {
                                augmentedPathSet.Remove(observedInput.Path);
                            }
                            else
                            {
                                augmentedPathSet[observedInput.Path] = (value.cacheLookupInput, observedInput);
                            }
                        }
                    }

                    if (augmentedPathSet.Count == 0)
                    {
                        return;
                    }

                    // Now augmentedPathSet contains the paths that might cause an artificial PipCacheMissType.CacheMissesForDescriptorsDueToAugmentedWeakFingerprints miss.
                    using (var pooledStringBuiler = Pools.GetStringBuilder())
                    {
                        var loggedPaths = pooledStringBuiler.Instance;
                        loggedPaths.AppendLine();
                        int numberLoggedPaths = 0;

                        foreach (var suspiciousPath in augmentedPathSet)
                        {
                            if (numberLoggedPaths < m_maxLoggedSuspiciousPathsPerPip)
                            {
                                numberLoggedPaths++;
                                var cacheInput = suspiciousPath.Value.cacheLookupInput;
                                var executionInput = suspiciousPath.Value.executionInput;
                                if (suspiciousPath.Value.executionInput == null)
                                {
                                    loggedPaths.AppendLine(string.Format(
                                        CultureInfo.InvariantCulture,
                                        "'{0}' ({1}) -- '{2}'",
                                        cacheInput.Path.ToString(pathTable),
                                        ObservedInputFlagsToString("CacheLookup", cacheInput),
                                        cacheInput.Hash.ToShortString()));
                                }
                                else
                                {
                                    // If the path was observed during execution, but the ObservedPathEntries did not match, log more data.
                                    loggedPaths.AppendLine(string.Format(
                                        CultureInfo.InvariantCulture,
                                        "'{0}' ({1}, {3}) -- '{2}'",
                                        cacheInput.Path.ToString(pathTable),
                                        ObservedInputFlagsToString("CacheLookup", cacheInput),
                                        cacheInput.Hash.ToShortString(),
                                        ObservedInputFlagsToString("Execution", executionInput.Value)));
                                }
                            }
                        }

                        var process = m_scheduler.PipGraph.PipTable.HydratePip(data.PipId, PipQueryContext.PathSetAugmentation);

                        Logger.Log.SuspiciousPathsInAugmentedPathSet(
                            m_loggingContext,
                            process.GetDescription(m_scheduler.Context),
                            numberLoggedPaths,
                            augmentedPathSet.Count,
                            loggedPaths.ToString());
                    }
                }
            }
        }

        private static string ObservedInputFlagsToString(string kind, ObservedInput input)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] Flags: {1}{2}",
                kind,
                (byte)input.PathEntry.Flags,
                string.IsNullOrEmpty(input.PathEntry.EnumeratePatternRegex) ? string.Empty : I($", EnumerationRegex: '{input.PathEntry.EnumeratePatternRegex}'"));
        }
    }
}
