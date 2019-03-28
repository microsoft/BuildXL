// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.SinglePhase;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Performance
{
    /// <summary>
    /// Utitlies for persisting/retrieving historic performance info to/from cache
    /// </summary>
    public static class PerformanceDataUtilities
    {
        /// <summary>
        /// The version performance lookups of performance data
        /// </summary>
        public const int PerformanceDataLookupVersion = 1;

        /// <summary>
        /// Computes a fingerprint for the graph which highly correlates to graphs representing
        /// the same or nearly the sames sets of process pips for performance data
        /// </summary>
        /// <remarks>
        /// This is calculated by taking the first N (randomly chosen as 16) process semistable hashes after sorting.
        /// This provides a stable fingerprint because it is unlikely that modifications to this pip graph
        /// will change those semistable hashes. Further, it is unlikely that pip graphs of different codebases
        /// will share these values.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        public static ContentFingerprint ComputeGraphSemistableFingerprint(
            LoggingContext loggingContext,
            PipTable pipTable,
            PathTable pathTable)
        {
            var processSemistableHashes = pipTable.StableKeys
                .Select(pipId => pipTable.GetMutable(pipId))
                .Where(info => info.PipType == PipType.Process)
                .Select(info => info.SemiStableHash)
                .ToList();

            processSemistableHashes.Sort();

            var indicatorHashes = processSemistableHashes.Take(16).ToArray();

            using (var hasher = new HashingHelper(pathTable, recordFingerprintString: false))
            {
                hasher.Add("Type", "GraphSemistableFingerprint");

                foreach (var indicatorHash in indicatorHashes)
                {
                    hasher.Add("IndicatorPipSemistableHash", indicatorHash);
                }

                var fingerprint = new ContentFingerprint(hasher.GenerateHash());
                Logger.Log.PerformanceDataCacheTrace(loggingContext, I($"Computed graph semistable fingerprint: {fingerprint}"));
                return fingerprint;
            }
        }

        /// <summary>
        /// Computes based a stable fingerprint for performance data based on the graph semistable fingerprint.
        /// <see cref="ComputeGraphSemistableFingerprint(LoggingContext, PipTable, PathTable)"/> and <see cref="PipGraph.SemistableFingerprint"/>
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        public static ContentFingerprint ComputePerformanceDataFingerprint(
            LoggingContext loggingContext,
            PathTable pathTable,
            ContentFingerprint graphSemistableFingerprint,
            string environmentFingerprint)
        {
            using (var hasher = new HashingHelper(pathTable, recordFingerprintString: false))
            {
                hasher.Add("Type", "PerformanceDataFingerprint");
                hasher.Add("FormatVersion", PipRuntimeTimeTable.FormatVersion);
                hasher.Add("LookupVersion", PerformanceDataLookupVersion);
                hasher.Add("GraphSemistableFingerprint", graphSemistableFingerprint.ToString());
                hasher.Add("EnvironmentFingerprint", environmentFingerprint ?? string.Empty);

                var fingerprint = new ContentFingerprint(hasher.GenerateHash());
                Logger.Log.PerformanceDataCacheTrace(loggingContext, I($"Computed performance fingerprint: {fingerprint}"));
                return fingerprint;
            }
        }

        /// <summary>
        /// Store the running time table to the cache
        /// </summary>
        public static async Task<Possible<Unit>> TryStoreRunningTimeTableAsync(
            this EngineCache cache,
            LoggingContext loggingContext,
            string path,
            PathTable pathTable,
            ContentFingerprint performanceDataFingerprint,
            DateTime? storeTime = null)
        {
            var absolutePath = AbsolutePath.Create(pathTable, path);
            var storeResult = await cache.ArtifactContentCache.TryStoreAsync(
                FileRealizationMode.Copy,
                absolutePath.Expand(pathTable));

            Logger.Log.PerformanceDataCacheTrace(loggingContext, I($"Storing running time table to cache: Success='{storeResult.Succeeded}'"));
            if (!storeResult.Succeeded)
            {
                return storeResult.Failure;
            }

            ContentHash hash = storeResult.Result;
            var cacheEntry = new CacheEntry(hash, null, ArrayView<ContentHash>.Empty);

            var publishResult = await cache.TwoPhaseFingerprintStore.TryPublishTemporalCacheEntryAsync(loggingContext, performanceDataFingerprint, cacheEntry, storeTime);
            Logger.Log.PerformanceDataCacheTrace(loggingContext, I($"Publishing running time table from cache: Fingerprint='{performanceDataFingerprint}' Hash={hash}"));
            return publishResult;
        }

        /// <summary>
        /// Retrieve the running time table from the cache
        /// </summary>
        public static async Task<Possible<bool>> TryRetrieveRunningTimeTableAsync(
            this EngineCache cache,
            LoggingContext loggingContext,
            string path,
            PathTable pathTable,
            ContentFingerprint graphSemistableFingerprint,
            DateTime? time = null)
        {
            var possibleCacheEntry =
                await cache.TwoPhaseFingerprintStore.TryGetLatestCacheEntryAsync(loggingContext, graphSemistableFingerprint, time);

            if (!possibleCacheEntry.Succeeded)
            {
                Logger.Log.PerformanceDataCacheTrace(
                    loggingContext,
                    I($"Failed loading running time table entry from cache: Failure:{possibleCacheEntry.Failure.DescribeIncludingInnerFailures()}"));
                return possibleCacheEntry.Failure;
            }

            Logger.Log.PerformanceDataCacheTrace(
                loggingContext,
                I($"Loaded running time table entry from cache: Fingerprint='{graphSemistableFingerprint}' MetadataHash={possibleCacheEntry.Result?.MetadataHash ?? ContentHashingUtilities.ZeroHash}"));

            if (!possibleCacheEntry.Result.HasValue)
            {
                return false;
            }

            var runningTimeTableHash = possibleCacheEntry.Result.Value.MetadataHash;

            var absolutePath = AbsolutePath.Create(pathTable, path);

            var maybePinned = await cache.ArtifactContentCache.TryLoadAvailableContentAsync(new[] {runningTimeTableHash});

            var result = await maybePinned.ThenAsync(
                async pinResult =>
                      {
                          if (!pinResult.AllContentAvailable)
                          {
                              return new Failure<string>(I($"Could not pin content for running time table '{runningTimeTableHash}'"));
                          }

                          return await cache.ArtifactContentCache.TryMaterializeAsync(
                              FileRealizationMode.Copy,
                              absolutePath.Expand(pathTable),
                              runningTimeTableHash);
                      });

            if (!result.Succeeded)
            {
                Logger.Log.PerformanceDataCacheTrace(
                    loggingContext,
                    I($"Failed loading running time table from cache: Failure:{result.Failure.DescribeIncludingInnerFailures()}"));
                return result.Failure;
            }

            Logger.Log.PerformanceDataCacheTrace(loggingContext, I($"Loaded running time table from cache: Path='{path}'"));

            return true;
        }
    }
}
