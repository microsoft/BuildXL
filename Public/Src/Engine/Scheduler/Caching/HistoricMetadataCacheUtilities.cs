// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.SinglePhase;
using BuildXL.Pips.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using Google.Protobuf;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Scheduler.Cache
{
    /// <summary>
    /// Utilities for persisting/retrieving historic metadata cache Data to/from cache
    /// </summary>
    public static class HistoricMetadataCacheUtilities
    {
        /// <summary>
        /// The version for lookups of historic metadata cache
        /// </summary>
        public const int HistoricMetadataCacheLookupVersion = 1;

        /// <summary>
        /// Computes a fingerprint for looking up historic metadata cache
        /// </summary>
        private static ContentFingerprint ComputeFingerprint(
            LoggingContext loggingContext,
            PathTable pathTable,
            IConfiguration configuration,
            ContentFingerprint performanceDataFingerprint)
        {
            var extraFingerprintSalt = new ExtraFingerprintSalts(
                configuration,
                fingerprintSalt: EngineEnvironmentSettings.DebugHistoricMetadataCacheFingerprintSalt,
                searchPathToolsHash: null);

            using (var hasher = new HashingHelper(pathTable, recordFingerprintString: false))
            {
                hasher.Add("Type", "HistoricMetadataCacheFingerprint");
                hasher.Add("FormatVersion", HistoricMetadataCache.FormatVersion);
                hasher.Add("LookupVersion", HistoricMetadataCacheLookupVersion);
                hasher.Add("PerformanceDataFingerprint", performanceDataFingerprint.Hash);
                hasher.Add("ExtraFingerprintSalt", extraFingerprintSalt.CalculatedSaltsFingerprint);

                var fingerprint = new ContentFingerprint(hasher.GenerateHash());
                Logger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Computed historic metadata cache fingerprint: {fingerprint}. Salt: '{EngineEnvironmentSettings.DebugHistoricMetadataCacheFingerprintSalt.Value}'"));
                return fingerprint;
            }
        }

        /// <summary>
        /// Store the historic metadata cache file to the cache
        /// </summary>
        public static async Task<Possible<long>> TryStoreHistoricMetadataCacheAsync(
            this EngineCache cache,
            LoggingContext loggingContext,
            string path,
            PathTable pathTable,
            IConfiguration configuration,
            ContentFingerprint performanceDataFingerprint)
        {
            var fingerprint = ComputeFingerprint(loggingContext, pathTable, configuration, performanceDataFingerprint);
            var absolutePath = AbsolutePath.Create(pathTable, path);

            BoxRef<long> size = 0;

            SemaphoreSlim concurrencyLimiter = new SemaphoreSlim(8);
            var storedFiles = await Task.WhenAll(Directory.EnumerateFiles(path).Select(async file =>
            {
                await Task.Yield();
                using (await concurrencyLimiter.AcquireAsync())
                {
                    var filePath = AbsolutePath.Create(pathTable, file);
                    var storeResult = await cache.ArtifactContentCache.TryStoreAsync(
                        FileRealizationMode.Copy,
                        filePath.Expand(pathTable));

                    if (storeResult.Succeeded)
                    {
                        Interlocked.Add(ref size.Value, new FileInfo(file).Length);
                    }

                    return storeResult.Then(result => new StringKeyedHash()
                    {
                        Key = absolutePath.ExpandRelative(pathTable, filePath),
                        ContentHash = result.ToByteString()
                    });
                }
            }).ToList());

            var failure = storedFiles.Where(p => !p.Succeeded).Select(p => p.Failure).FirstOrDefault();
            Logger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Storing historic metadata cache to cache: Success='{failure == null}', FileCount={storedFiles.Length} Size={size.Value}"));
            if (failure != null)
            {
                return failure;
            }

            PackageDownloadDescriptor descriptor = new PackageDownloadDescriptor()
            {
                TraceInfo = loggingContext.Session.Environment,
                FriendlyName = nameof(HistoricMetadataCache)
            };
            descriptor.Contents.Add(storedFiles.Select(p => p.Result).ToList());

            var storeDescriptorResult = await cache.ArtifactContentCache.TrySerializeAndStoreContent(descriptor);
            Logger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Storing historic metadata cache descriptor to cache: Success='{storeDescriptorResult.Succeeded}'"));
            if (!storeDescriptorResult.Succeeded)
            {
                return storeDescriptorResult.Failure;
            }

            var associatedFileHashes = descriptor.Contents.Select(s => s.ContentHash.ToContentHash()).ToArray().ToReadOnlyArray().GetSubView(0);
            var cacheEntry = new CacheEntry(storeDescriptorResult.Result, null, associatedFileHashes);

            var publishResult = await cache.TwoPhaseFingerprintStore.TryPublishTemporalCacheEntryAsync(loggingContext, fingerprint, cacheEntry);
            Logger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Publishing historic metadata cache to cache: Fingerprint='{fingerprint}' Hash={storeDescriptorResult.Result}"));
            return size.Value;
        }

        /// <summary>
        /// Retrieve the running time table from the cache
        /// </summary>
        public static async Task<Possible<bool>> TryRetrieveHistoricMetadataCacheAsync(
            this EngineCache cache,
            LoggingContext loggingContext,
            string path,
            PathTable pathTable,
            IConfiguration configuration,
            ContentFingerprint performanceDataFingerprint,
            CancellationToken cancellationToken)
        {
            var fingerprint = ComputeFingerprint(loggingContext, pathTable, configuration, performanceDataFingerprint);
            var possibleCacheEntry = await cache.TwoPhaseFingerprintStore.TryGetLatestCacheEntryAsync(loggingContext, fingerprint);
            if (!possibleCacheEntry.Succeeded)
            {
                Logger.Log.HistoricMetadataCacheTrace(
                    loggingContext,
                    I($"Failed loading historic metadata cache entry from cache: Failure:{possibleCacheEntry.Failure.DescribeIncludingInnerFailures()}"));
                return possibleCacheEntry.Failure;
            }

            Logger.Log.HistoricMetadataCacheTrace(
                loggingContext,
                I($"Loaded historic metadata cache entry from cache: Fingerprint='{fingerprint}' MetadataHash={possibleCacheEntry.Result?.MetadataHash ?? ContentHashingUtilities.ZeroHash}"));

            if (!possibleCacheEntry.Result.HasValue)
            {
                return false;
            }

            var historicMetadataCacheDescriptorHash = possibleCacheEntry.Result.Value.MetadataHash;

            var absolutePath = AbsolutePath.Create(pathTable, path);

            var maybePinned = await cache.ArtifactContentCache.TryLoadAvailableContentAsync(possibleCacheEntry.Result.Value.ToArray(), cancellationToken);

            var result = await maybePinned.ThenAsync<Unit>(
                async pinResult =>
                {
                    if (!pinResult.AllContentAvailable)
                    {
                        return new Failure<string>(I($"Could not pin content for historic metadata cache '{string.Join(", ", pinResult.Results.Where(r => !r.IsAvailable).Select(r => r.Hash))}'"));
                    }

                    var maybeLoadedDescriptor = await cache.ArtifactContentCache.TryLoadAndDeserializeContent<PackageDownloadDescriptor>(historicMetadataCacheDescriptorHash, cancellationToken);
                    if (!maybeLoadedDescriptor.Succeeded)
                    {
                        return maybeLoadedDescriptor.Failure;
                    }

                    Logger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Loaded historic metadata cache descriptor from cache: Hash='{historicMetadataCacheDescriptorHash}'"));

                    PackageDownloadDescriptor descriptor = maybeLoadedDescriptor.Result;

                    SemaphoreSlim concurrencyLimiter = new SemaphoreSlim(8);
                    var materializedFiles = await Task.WhenAll(descriptor.Contents.Select(async subPathKeyedHash =>
                    {
                        await Task.Yield();
                        using (await concurrencyLimiter.AcquireAsync())
                        {
                            var filePath = absolutePath.Combine(pathTable, subPathKeyedHash.Key);
                            var maybeMaterialized = await cache.ArtifactContentCache.TryMaterializeAsync(
                                FileRealizationMode.Copy,
                                filePath.Expand(pathTable),
                                subPathKeyedHash.ContentHash.ToContentHash(),
                                cancellationToken);

                            return maybeMaterialized;
                        }
                    }).ToList());

                    var failure = materializedFiles.Where(p => !p.Succeeded).Select(p => p.Failure).FirstOrDefault();
                    if (failure != null)
                    {
                        return failure;
                    }
                    return Unit.Void;
                });

            if (!result.Succeeded)
            {
                Logger.Log.HistoricMetadataCacheTrace(
                    loggingContext,
                    I($"Failed loading historic metadata cache from cache: Failure:{result.Failure.DescribeIncludingInnerFailures()}"));
                return result.Failure;
            }

            Logger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Loaded historic metadata cache from cache: Path='{path}'"));

            return true;
        }
    }
}
