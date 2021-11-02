// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.SinglePhase;
using BuildXL.Native.IO;
using BuildXL.Pips.Graph;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Tracing
{

    /// <summary>
    /// Utilities for persisting/retrieving fingerprint store data to/from cache
    /// </summary>
    public static class FingerprintStoreCacheUtilities
    {
        /// <summary>
        /// The version for lookups of fingerprint store
        /// </summary>
        public const int FingerprintStoreLookupVersion = 1;

        /// <summary>
        /// Computes a fingerprint for looking up fingerprint store
        /// </summary>
        private static ContentFingerprint ComputeFingerprint(
            string key,
            IConfiguration configuration)
        {
            var extraFingerprintSalts = new ExtraFingerprintSalts(
                configuration,
                configuration.Cache.CacheSalt,
                null);

            using (var hasher = new BasicHashingHelper(recordFingerprintString: false))
            {
                hasher.Add("Type", "FingerprintStoreFingerprint");
                hasher.Add("FormatVersion", FingerprintStore.FormatVersion.Version.ToString());
                hasher.Add("LookupVersion", FingerprintStoreLookupVersion);
                hasher.Add("Key", key);
                hasher.Add("FingerprintSalt", extraFingerprintSalts.FingerprintSalt);

                var fingerprint = new ContentFingerprint(hasher.GenerateHash());
                return fingerprint;
            }
        }

        /// <summary>
        /// Store the fingerprint store directory to the cache
        /// </summary>
        /// <remark>
        /// The order of storing should be:
        /// 1. The actual content(files) of the fingerprint store
        /// 2. The metadata(descriptor) of the content.
        /// 3. Publish the cache entry of the fingerprint store.
        /// </remark>
        public static async Task<Possible<long>> TrySaveFingerprintStoreAsync(
            this EngineCache cache,
            LoggingContext loggingContext,
            AbsolutePath path,
            PathTable pathTable,
            string key,
            IConfiguration configuration)
        {
            var fingerprint = ComputeFingerprint(key, configuration);
            var pathStr = path.ToString(pathTable);
            BoxRef<long> size = 0;

            SemaphoreSlim concurrencyLimiter = new SemaphoreSlim(8);
            var tasks = new List<Task<Possible<StringKeyedHash, Failure>>>();
            FileUtilities.EnumerateDirectoryEntries(pathStr, (name, attr) =>
            {
                var task = Task.Run(async () =>
                {
                    using (await concurrencyLimiter.AcquireAsync())
                    {
                        var filePath = path.Combine(pathTable, name);
                        ExpandedAbsolutePath expandedFilePath = filePath.Expand(pathTable);

                        var storeResult = await cache.ArtifactContentCache.TryStoreAsync(
                            FileRealizationMode.Copy,
                            expandedFilePath);

                        var result = storeResult.Then(result => new StringKeyedHash()
                        {
                            Key = path.ExpandRelative(pathTable, filePath),
                            ContentHash = result.ToBondContentHash()
                        });

                        string message = I($"Saving fingerprint store to cache: Success='{result.Succeeded}', FilePath='{expandedFilePath}'");

                        if (result.Succeeded)
                        {
                            Interlocked.Add(ref size.Value, new FileInfo(filePath.ToString(pathTable)).Length);
                            message += I($", Key='{result.Result.Key}', Hash='{result.Result.ContentHash.ToContentHash()}'");
                        }

                        Logger.Log.GettingFingerprintStoreTrace(loggingContext, message);
                        return result;
                    }
                });

                tasks.Add(task);
            });

            var storedFiles = await Task.WhenAll(tasks);

            if (storedFiles.Length == 0 || size.Value == 0)
            {
                Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Empty fingerprint store is not saved."));
                return 0;
            }

            var failure = storedFiles.Where(p => !p.Succeeded).Select(p => p.Failure).FirstOrDefault();
            Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Saving fingerprint store to cache: Success='{failure == null}', FileCount={storedFiles.Length} Size={size.Value}"));
            if (failure != null)
            {
                return failure;
            }

            PackageDownloadDescriptor descriptor = new PackageDownloadDescriptor()
            {
                TraceInfo = loggingContext.Session.Environment,
                FriendlyName = nameof(FingerprintStore),
                Contents = storedFiles.Select(p => p.Result).ToList()
            };

            var storeDescriptorResult = await cache.ArtifactContentCache.TrySerializeAndStoreContent(descriptor);
            Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Saving fingerprint store descriptor to cache: Success='{storeDescriptorResult.Succeeded}'"));
            if (!storeDescriptorResult.Succeeded)
            {
                return storeDescriptorResult.Failure;
            }

            var associatedFileHashes = descriptor.Contents.Select(s => s.ContentHash.ToContentHash()).ToArray().ToReadOnlyArray().GetSubView(0);
            var cacheEntry = new CacheEntry(storeDescriptorResult.Result, null, associatedFileHashes);

            var publishResult = await cache.TwoPhaseFingerprintStore.TryPublishTemporalCacheEntryAsync(loggingContext, fingerprint, cacheEntry);
            Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Publishing fingerprint store to cache: Fingerprint='{fingerprint}' Hash={storeDescriptorResult.Result}"));
            return size.Value;
        }

        /// <summary>
        /// Retrieve the fingerprint store from the cache
        /// </summary>
        public static async Task<Possible<bool>> TryRetrieveFingerprintStoreAsync(
            this EngineCache cache,
            LoggingContext loggingContext,
            AbsolutePath path,
            PathTable pathTable,
            string key,
            IConfiguration configuration)
        {
            var fingerprint = ComputeFingerprint(key, configuration);
            Logger.Log.GettingFingerprintStoreTrace(
                loggingContext,
                $"Attempting to fetch fingerprint store from cache: Key='{key}'. Resulting fingerprint='{fingerprint}'");

            var possibleCacheEntry = await cache.TwoPhaseFingerprintStore.TryGetLatestCacheEntryAsync(loggingContext, fingerprint);
            if (!possibleCacheEntry.Succeeded)
            {
                Logger.Log.GettingFingerprintStoreTrace(
                    loggingContext,
                    I($"Failed loading fingerprint store cache entry from cache: Key='{key}' Failure: {possibleCacheEntry.Failure.DescribeIncludingInnerFailures()}"));

                return possibleCacheEntry.Failure;
            }

            if (!possibleCacheEntry.Result.HasValue)
            {
                Logger.Log.GettingFingerprintStoreTrace(loggingContext, "Failed to find cache entry for fingerprint store");
                return false;
            }

            Logger.Log.GettingFingerprintStoreTrace(
                loggingContext,
                I($"Loaded fingerprint store entry from cache: Key='{key}' Fingerprint='{fingerprint}' MetadataHash='{possibleCacheEntry.Result?.MetadataHash ?? ContentHashingUtilities.ZeroHash}'"));

            var fingerprintStoreDescriptorHash = possibleCacheEntry.Result.Value.MetadataHash;
            var maybePinned = await cache.ArtifactContentCache.TryLoadAvailableContentAsync(possibleCacheEntry.Result.Value.ToArray());

            var result = await maybePinned.ThenAsync<Unit>(
                async pinResult =>
                {
                    if (!pinResult.AllContentAvailable)
                    {
                        return new Failure<string>(I($"Could not pin content for fingerprint store '{string.Join(", ", pinResult.Results.Where(r => !r.IsAvailable).Select(r => r.Hash))}'"));
                    }

                    var maybeLoadedDescriptor = await cache.ArtifactContentCache.TryLoadAndDeserializeContent<PackageDownloadDescriptor>(fingerprintStoreDescriptorHash);
                    if (!maybeLoadedDescriptor.Succeeded)
                    {
                        return maybeLoadedDescriptor.Failure;
                    }

                    Logger.Log.GettingFingerprintStoreTrace(
                        loggingContext,
                        I($"Loaded fingerprint store cache descriptor from cache: Key='{key}' Hash='{fingerprintStoreDescriptorHash}'"));

                    PackageDownloadDescriptor descriptor = maybeLoadedDescriptor.Result;

                    SemaphoreSlim concurrencyLimiter = new SemaphoreSlim(8);
                    var materializedFiles = await Task.WhenAll(descriptor.Contents.Select(async subPathKeyedHash =>
                    {
                        await Task.Yield();
                        using (await concurrencyLimiter.AcquireAsync())
                        {
                            var filePath = path.Combine(pathTable, subPathKeyedHash.Key);
                            var maybeMaterialized = await cache.ArtifactContentCache.TryMaterializeAsync(
                                FileRealizationMode.Copy,
                                filePath.Expand(pathTable),
                                subPathKeyedHash.ContentHash.ToContentHash());

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
                Logger.Log.GettingFingerprintStoreTrace(
                    loggingContext,
                    I($"Failed loading fingerprint store from cache: Key='{key}' Failure: {result.Failure.DescribeIncludingInnerFailures()}"));

                return result.Failure;
            }

            Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Loaded fingerprint store from cache: Key='{key}' Path='{path.ToString(pathTable)}'"));

            return true;
        }
    }
}
