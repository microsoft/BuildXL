// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.SinglePhase;
using BuildXL.Pips;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.Core.FormattableStringEx;

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
        /// Computes based a stable fingerprint for performance data based on the graph semistable fingerprint.
        /// <see cref="BuildXL.Pips.Graph.PipGraph.Builder.ComputeGraphSemistableFingerprint(LoggingContext, PipTable, PathTable)"/> and <see cref="BuildXL.Pips.Graph.PipGraph.SemistableFingerprint"/>
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
                hasher.Add("FormatVersion", HistoricPerfDataTable.FormatVersion);
                hasher.Add("LookupVersion", PerformanceDataLookupVersion);
                hasher.Add("GraphSemistableFingerprint", graphSemistableFingerprint.ToString());
                hasher.Add("EnvironmentFingerprint", environmentFingerprint ?? string.Empty);

                var fingerprint = new ContentFingerprint(hasher.GenerateHash());
                Logger.Log.HistoricPerfDataCacheTrace(loggingContext, I($"Computed performance fingerprint: {fingerprint}"));
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

            Logger.Log.HistoricPerfDataCacheTrace(loggingContext, I($"Storing running time table to cache: Success='{storeResult.Succeeded}'"));
            if (!storeResult.Succeeded)
            {
                return storeResult.Failure;
            }

            ContentHash hash = storeResult.Result;
            var cacheEntry = new CacheEntry(hash, null, ArrayView<ContentHash>.Empty);

            var publishResult = await cache.TwoPhaseFingerprintStore.TryPublishTemporalCacheEntryAsync(loggingContext, performanceDataFingerprint, cacheEntry, storeTime);
            Logger.Log.HistoricPerfDataCacheTrace(loggingContext, I($"Publishing running time table from cache: Fingerprint='{performanceDataFingerprint}' Hash={hash}"));
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
            CancellationToken cancellationToken,
            DateTime? time = null)
        {
            var possibleCacheEntry =
                await cache.TwoPhaseFingerprintStore.TryGetLatestCacheEntryAsync(loggingContext, graphSemistableFingerprint, time);

            if (!possibleCacheEntry.Succeeded)
            {
                Logger.Log.HistoricPerfDataCacheTrace(
                    loggingContext,
                    I($"Failed loading running time table entry from cache: Failure:{possibleCacheEntry.Failure.DescribeIncludingInnerFailures()}"));
                return possibleCacheEntry.Failure;
            }

            Logger.Log.HistoricPerfDataCacheTrace(
                loggingContext,
                I($"Loaded running time table entry from cache: Fingerprint='{graphSemistableFingerprint}' MetadataHash={possibleCacheEntry.Result?.MetadataHash ?? ContentHashingUtilities.ZeroHash}"));

            if (!possibleCacheEntry.Result.HasValue)
            {
                return false;
            }

            var runningTimeTableHash = possibleCacheEntry.Result.Value.MetadataHash;

            var absolutePath = AbsolutePath.Create(pathTable, path);

            var maybePinned = await cache.ArtifactContentCache.TryLoadAvailableContentAsync(new[] { runningTimeTableHash }, cancellationToken);

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
                              runningTimeTableHash,
                              cancellationToken);
                      });

            if (!result.Succeeded)
            {
                Logger.Log.HistoricPerfDataCacheTrace(
                    loggingContext,
                    I($"Failed loading running time table from cache: Failure:{result.Failure.DescribeIncludingInnerFailures()}"));
                return result.Failure;
            }

            Logger.Log.HistoricPerfDataCacheTrace(loggingContext, I($"Loaded running time table from cache: Path='{path}'"));

            return true;
        }
    }
}
