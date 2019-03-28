// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// A cache for backing content store expiry.
    /// TODO: Clearing this cache relies on restart of the cache. We need to think of a better way to handle its unbounded growth, or at least add telemetry to track it.
    /// </summary>
    internal class BackingContentStoreExpiryCache
    {
        /// <summary>
        /// A singleton cache for ContentHash expiry in backing content store.
        /// </summary>
        public static BackingContentStoreExpiryCache Instance => new BackingContentStoreExpiryCache();

        private readonly ConcurrentDictionary<ContentHash, DateTime> _backingStoreExpiryCacheDictionary
            = new ConcurrentDictionary<ContentHash, DateTime>();

        private BackingContentStoreExpiryCache()
        {
        }

        /// <summary>
        /// Tries to get the expiry from the cache if available.
        /// </summary>
        public bool TryGetExpiry(ContentHash hash, out DateTime dateTime)
        {
            return _backingStoreExpiryCacheDictionary.TryGetValue(hash, out dateTime);
        }

        /// <summary>
        /// Adds an expiry to the cache.
        /// </summary>
        public void AddExpiry(ContentHash hash, DateTime expiryTime)
        {
            _backingStoreExpiryCacheDictionary.AddOrUpdate(hash, expiryTime, (contentHash, existingExpiry) => expiryTime > existingExpiry ? expiryTime : existingExpiry);
        }
    }
}
