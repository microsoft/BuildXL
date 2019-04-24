// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using StrongFingerprintToHashListDictionary = System.Collections.Concurrent.ConcurrentDictionary<BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint, BuildXL.Cache.MemoizationStore.Interfaces.Sessions.ContentHashListWithDeterminism>;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// Represents an in-memory cache of content hash lists with expiration when communicating with a build cache service.
    /// </summary>
    public class ContentHashListWithDeterminismCache
    {
        /// <summary>
        ///     The singleton instance of the content hash list cache.
        /// </summary>
        public static readonly ContentHashListWithDeterminismCache Instance = new ContentHashListWithDeterminismCache();

        private readonly ConcurrentDictionary<string, StrongFingerprintToHashListDictionary> _cacheNamespaceToContentHashListCacheDictionary
            = new ConcurrentDictionary<string, StrongFingerprintToHashListDictionary>();

        private ContentHashListWithDeterminismCache()
        {
        }

        /// <summary>
        /// Tries to get a content hash list for a specific strong fingerprint in the cache.
        /// </summary>
        public bool TryGetValue(string cacheNamespace, StrongFingerprint strongFingerprint, out ContentHashListWithDeterminism value)
        {
            ContentHashListWithDeterminism existingValue;

            // Try to get the existing value
            var contentHashListCacheDictionary = _cacheNamespaceToContentHashListCacheDictionary.GetOrAdd(cacheNamespace, (key) => new StrongFingerprintToHashListDictionary());
            while (contentHashListCacheDictionary.TryGetValue(strongFingerprint, out existingValue))
            {
                if (!existingValue.Determinism.IsDeterministic)
                {
                    // The value is either tool deterministic or it's cache deterministic and has not expired, so it's usable
                    value = existingValue;
                    return true;
                }

                if (
                    ((ICollection<KeyValuePair<StrongFingerprint, ContentHashListWithDeterminism>>)contentHashListCacheDictionary).Remove(
                        new KeyValuePair<StrongFingerprint, ContentHashListWithDeterminism>(strongFingerprint, existingValue)))
                {
                    // Removal was successful, so nothing usable
                    value = new ContentHashListWithDeterminism(null, CacheDeterminism.None);
                    return false;
                }
            }

            // Nothing usable
            value = new ContentHashListWithDeterminism(null, CacheDeterminism.None);
            return false;
        }

        /// <summary>
        /// Adds a content hash list and its corresponding determinism value into the in-memory cache.
        /// </summary>
        public void AddValue(string cacheNamespace, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism newValue)
        {
            // If there was a race, keep the value with the better determinism
            var contentHashListCacheDictionary = _cacheNamespaceToContentHashListCacheDictionary.GetOrAdd(cacheNamespace, (key) => new StrongFingerprintToHashListDictionary());
            contentHashListCacheDictionary.AddOrUpdate(
                strongFingerprint,
                newValue,
                (oldStrongFingerprint, oldValue) => oldValue.Determinism.ShouldBeReplacedWith(newValue.Determinism)
                    ? newValue
                    : oldValue);
        }
    }
}
