// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
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
            BlobContentHashListWithCacheMetadata existingValue;

            // Try to get the existing value
            var contentHashListCacheDictionary = _cacheNamespaceToContentHashListCacheDictionary.GetOrAdd(
                cacheNamespace,
                key => new ConcurrentDictionary<StrongFingerprint, BlobContentHashListWithCacheMetadata>());
            while (contentHashListCacheDictionary.TryGetValue(strongFingerprint, out existingValue))
            {
                if (!existingValue.Determinism.IsDeterministic)
                {
                    // The value is either tool deterministic or it's cache deterministic and has not expired, so it's usable
                    value = existingValue;
                    return true;
                }

                if (
                    ((ICollection<KeyValuePair<StrongFingerprint, BlobContentHashListWithCacheMetadata>>)contentHashListCacheDictionary).Remove(
                        new KeyValuePair<StrongFingerprint, BlobContentHashListWithCacheMetadata>(strongFingerprint, existingValue)))
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
                key => new ConcurrentDictionary<StrongFingerprint, BlobContentHashListWithCacheMetadata>());
            contentHashListCacheDictionary.AddOrUpdate(
                strongFingerprint,
                newValue,
                (oldStrongFingerprint, oldValue) => oldValue.Determinism.ShouldBeReplacedWith(newValue.Determinism)
                    ? newValue
                    : oldValue);
        }
    }
}
