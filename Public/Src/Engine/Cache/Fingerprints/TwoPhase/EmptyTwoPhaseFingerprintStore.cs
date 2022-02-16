// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Fingerprints.TwoPhase
{
    /// <summary>
    /// Trivial <see cref="ITwoPhaseFingerprintStore"/> which is always semantically empty.
    /// Note that this store does *not* provide the usual property that stored entries can be later retrieved.
    /// </summary>
    public sealed class EmptyTwoPhaseFingerprintStore : ITwoPhaseFingerprintStore
    {
        /// <inheritdoc />
        public IEnumerable<Task<Possible<PublishedEntryRef, Failure>>> ListPublishedEntriesByWeakFingerprint(WeakContentFingerprint weak)
        {
            return Enumerable.Empty<Task<Possible<PublishedEntryRef, Failure>>>();
        }

        /// <inheritdoc />
        public Task<Possible<CacheEntry?, Failure>> TryGetCacheEntryAsync(WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, StrongContentFingerprint strongFingerprint)
        {
            return Task.FromResult(new Possible<CacheEntry?, Failure>(result: null));
        }

        /// <inheritdoc />
        public Task<Possible<CacheEntryPublishResult, Failure>> TryPublishCacheEntryAsync(
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint,
            CacheEntry entry,
            CacheEntryPublishMode mode = CacheEntryPublishMode.CreateNew,
            PublishCacheEntryOptions options = default)
        {
            return Task.FromResult(new Possible<CacheEntryPublishResult, Failure>(CacheEntryPublishResult.CreatePublishedResult()));
        }
    }
}
