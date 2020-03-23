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
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
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
            PathTable pathTable,
            string key,
            string environment)
        {
            using (var hasher = new HashingHelper(pathTable, recordFingerprintString: false))
            {
                hasher.Add("Type", "FingerprintStoreFingerprint");
                hasher.Add("FormatVersion", FingerprintStore.FormatVersion.Version.ToString());
                hasher.Add("LookupVersion", FingerprintStoreLookupVersion);
                hasher.Add("Key", key);
                hasher.Add("Environment", environment);

                var fingerprint = new ContentFingerprint(hasher.GenerateHash());
                return fingerprint;
            }
        }

        /// <summary>
        /// Store the fingerprint store directory to the cache
        /// </summary>
        public static async Task<Possible<long>> TrySaveFingerprintStoreAsync(
            this EngineCache cache,
            LoggingContext loggingContext,
            AbsolutePath path,
            PathTable pathTable,
            string key,
            string environment)
        {
            var fingerprint = ComputeFingerprint(pathTable, key, environment);
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
                        var storeResult = await cache.ArtifactContentCache.TryStoreAsync(
                            FileRealizationMode.Copy,
                            filePath.Expand(pathTable));

                        if (storeResult.Succeeded)
                        {                            
                            Interlocked.Add(ref size.Value, new FileInfo(filePath.ToString(pathTable)).Length);
                        }

                        var result = storeResult.Then(result => new StringKeyedHash()
                        {
                            Key = path.ExpandRelative(pathTable, filePath),
                            ContentHash = result.ToBondContentHash()
                        });

                        Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Saving fingerprint store to cache: Success='{storeResult.Succeeded}', FilePath='{filePath}' Key='{result.Result.Key}' Hash='{result.Result.ContentHash.ToContentHash()}'"));
                        return result;
                    }
                });

                tasks.Add(task);
            });

            var storedFiles = await Task.WhenAll(tasks);

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

            // Publish contents before saving the descriptor to make sure content exist before the descriptor exist to avoid fingerprintstore opening errors
            var storeDescriptorBuffer = BondExtensions.Serialize(descriptor);
            var storeDescriptorHash = ContentHashingUtilities.HashBytes(
                storeDescriptorBuffer.Array,
                storeDescriptorBuffer.Offset,
                storeDescriptorBuffer.Count);

            var associatedFileHashes = descriptor.Contents.Select(s => s.ContentHash.ToContentHash()).ToArray().ToReadOnlyArray().GetSubView(0);
            var cacheEntry = new CacheEntry(storeDescriptorHash, null, associatedFileHashes);
            var publishResult = await cache.TwoPhaseFingerprintStore.TryPublishTemporalCacheEntryAsync(loggingContext, fingerprint, cacheEntry);
            Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Publishing fingerprint store to cache: Fingerprint='{fingerprint}' Hash={storeDescriptorHash} Success='{publishResult.Succeeded}'"));

            var storeDescriptorResult = await cache.ArtifactContentCache.TryStoreContent(storeDescriptorHash, storeDescriptorBuffer);
            Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Saving fingerprint store descriptor to cache: Success='{storeDescriptorResult.Succeeded}'"));
            if (!storeDescriptorResult.Succeeded)
            {
                return storeDescriptorResult.Failure;
            }

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
            string environment)
        {
            var fingerprint = ComputeFingerprint(pathTable, key, environment);
            var possibleCacheEntry = await cache.TwoPhaseFingerprintStore.TryGetLatestCacheEntryAsync(loggingContext, fingerprint);
            if (!possibleCacheEntry.Succeeded)
            {
                Logger.Log.GettingFingerprintStoreTrace(
                    loggingContext,
                    I($"Failed loading fingerprint store cache entry from cache: Key='{key}' Failure: {possibleCacheEntry.Failure.DescribeIncludingInnerFailures()}"));

                return possibleCacheEntry.Failure;
            }

            Logger.Log.GettingFingerprintStoreTrace(
                loggingContext,
                I($"Loaded fingerprint store entry from cache: Key='{key}' Fingerprint='{fingerprint}' MetadataHash='{possibleCacheEntry.Result?.MetadataHash ?? ContentHashingUtilities.ZeroHash}'"));

            if (!possibleCacheEntry.Result.HasValue)
            {
                return false;
            }

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
                            Logger.Log.GettingFingerprintStoreTrace(
                                                    loggingContext,
                                                    I($"Loaded fingerprint store file from cache: Success='{maybeMaterialized.Succeeded}' FilePath='{filePath}' Key='{subPathKeyedHash.Key}' Hash='{subPathKeyedHash.ContentHash.ToContentHash()}'"));
                            return maybeMaterialized;
                        }
                    }).ToList());

                    var failure = materializedFiles.Where(p => !p.Succeeded).Select(p => p.Failure).FirstOrDefault();
                    
                    if (failure != null)
                    {
                        return failure;
                    }

                    if (descriptor.Contents == null || descriptor.Contents.Count == 0 || materializedFiles == null || materializedFiles.Length == 0)
                    {
                        return new Failure<string>(I($"There is no content in fingerprint store. Contents.Count = {(descriptor.Contents != null ? descriptor.Contents.Count : 0)}, materializedFiles.Length = {(materializedFiles != null ? materializedFiles.Length :0)}"));
                    }

                    Logger.Log.GettingFingerprintStoreTrace(loggingContext, I($"Loaded fingerprint store file numbers: Succeeded={materializedFiles.Where(p => p.Succeeded).Count()}, Failed={materializedFiles.Where(p => !p.Succeeded).Count()}"));
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
