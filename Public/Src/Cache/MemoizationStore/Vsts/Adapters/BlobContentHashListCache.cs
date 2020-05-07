// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob;
using StrongFingerprintToHashListDictionary = System.Collections.Concurrent.ConcurrentDictionary<BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint, BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob.BlobContentHashListWithCacheMetadata>;

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters
{
    /// <summary>
    /// Represents an in-memory cache of blob content hash lists with download URIs to fetch the blob.
    /// </summary>
    public class BlobContentHashListCache
    {
        /// <summary>
        ///     The singleton instance of the content hash list cache.
        /// </summary>
        public static readonly BlobContentHashListCache Instance = new BlobContentHashListCache();

        private readonly ConcurrentDictionary<string, StrongFingerprintToHashListDictionary> _cacheNamespaceToContentHashListCacheDictionary
            = new ConcurrentDictionary<string, StrongFingerprintToHashListDictionary>();

        private BlobContentHashListCache()
        {
        }

        /// <summary>
        /// Tries to get a content hash list for a specific strong fingerprint in the cache.
        /// </summary>
        public bool TryGetValue(string cacheNamespace, StrongFingerprint strongFingerprint, out BlobContentHashListWithCacheMetadata value)
        {
            // Try to get the existing value
            var contentHashListCacheDictionary = _cacheNamespaceToContentHashListCacheDictionary.GetOrAdd(
                cacheNamespace,
                key => new StrongFingerprintToHashListDictionary());

            while (contentHashListCacheDictionary.TryGetValue(strongFingerprint, out BlobContentHashListWithCacheMetadata existingValue))
            {
                if (existingValue.Determinism.IsDeterministic)
                {
                    // The value is either tool deterministic or it's cache deterministic and has not expired, so it's usable
                    value = existingValue;
                    return true;
                }
                
                if (contentHashListCacheDictionary.Remove(strongFingerprint, existingValue))
                {
                    // Removal was successful, so nothing usable
                    value = null;
                    return false;
                }
            }

            // Nothing usable
            value = null;
            return false;
        }

        /// <summary>
        /// Adds a content hash list and its corresponding determinism value into the in-memory cache.
        /// </summary>
        public void AddValue(string cacheNamespace, StrongFingerprint strongFingerprint, BlobContentHashListWithCacheMetadata newValue)
        {
            // If there was a race, keep the value with the better determinism
            var contentHashListCacheDictionary = _cacheNamespaceToContentHashListCacheDictionary.GetOrAdd(
                cacheNamespace,
                key => new StrongFingerprintToHashListDictionary());

            contentHashListCacheDictionary.AddOrUpdate(
                strongFingerprint,
                newValue,
                (oldStrongFingerprint, oldValue) => oldValue.Determinism.ShouldBeReplacedWith(newValue.Determinism)
                    ? newValue
                    : oldValue);
        }
    }
}
