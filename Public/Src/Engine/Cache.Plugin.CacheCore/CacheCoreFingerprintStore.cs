// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.Interfaces;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Implementation of <see cref="ITwoPhaseFingerprintStore"/> via a Cache.Core <see cref="ICacheSession"/>.
    /// </summary>
    public sealed class CacheCoreFingerprintStore : ITwoPhaseFingerprintStore
    {
        /// <summary>
        /// Timeout for some fingerprint store operations.
        /// </summary>
        public static int TimeoutDurationMin => EngineEnvironmentSettings.FingerprintStoreOperationTimeout.Value ?? 60 * 6;

        private readonly ICacheSession m_cache;

        /// <nodoc />
        public CacheCoreFingerprintStore(ICacheSession cache)
        {
            Contract.Requires(cache != null);
            m_cache = cache;
        }

        /// <inheritdoc />
        public IEnumerable<Task<Possible<PublishedEntryRef, Failure>>> ListPublishedEntriesByWeakFingerprint(WeakContentFingerprint weak, OperationHints hints = default)
        {
            WeakFingerprintHash cacheCoreWeakFingerprint = new WeakFingerprintHash(new Hash(weak.Hash));

            // Default assumption: everything before the first sentinel is local.
            PublishedEntryRefLocality currentLocality = PublishedEntryRefLocality.Local;

            foreach (Task<Possible<StrongFingerprint, Failure>> entryPromise in m_cache.EnumerateStrongFingerprints(cacheCoreWeakFingerprint, hints))
            {
                // We must resolve each entry synchronously here, before yielding the next one: a sentinel can appear
                // at any position in the stream and must update currentLocality so that any following entries are
                // attributed correctly. We can't defer this work into the yielded Task continuation, because the
                // interface contract (see ITwoPhaseFingerprintStore.ListPublishedEntriesByWeakFingerprint) allows
                // consumers to await batch-tasks in parallel — sharing a mutable currentLocality across those
                // continuations would race. The unwrap-then-rewrap-as-Task.FromResult round trip is a consequence of
                // needing a synchronous inspection point in an IEnumerable<Task<>> shape.
                //
                // In practice the synchronous wait here is cheap: the closest implementation we exercise
                // (MemoizationStoreAdapter) materializes the full GetSelectors result inside its first yielded task
                // and emits all subsequent entries as already-completed Task.FromResult instances. So only the first
                // entry actually blocks; for everything after that, the wait is a no-op. Code here does not rely on
                // that — any implementation is correctly handled — but it explains why this is not a perf concern in
                // current usage.
                Possible<StrongFingerprint, Failure> maybeFingerprint =
                    PerformFingerprintCacheOperationAsync(() => entryPromise, nameof(ListPublishedEntriesByWeakFingerprint)).GetAwaiter().GetResult();

                if (maybeFingerprint.Succeeded && maybeFingerprint.Result is StrongFingerprintSentinel sentinel)
                {
                    // The sentinel tells us the locality of entries that follow.
                    currentLocality = sentinel.FollowingEntriesAreRemote
                        ? PublishedEntryRefLocality.Remote
                        : PublishedEntryRefLocality.Local;

                    continue;
                }

                yield return Task.FromResult(ToPublishedEntryRef(maybeFingerprint, currentLocality));
            }
        }

        private static Possible<PublishedEntryRef, Failure> ToPublishedEntryRef(Possible<StrongFingerprint, Failure> maybeFingerprint, PublishedEntryRefLocality locality)
        {
            if (maybeFingerprint.Succeeded)
            {
                StrongFingerprint fingerprint = maybeFingerprint.Result;

                return new PublishedEntryRef(
                                pathSetHash: ContentHashingUtilities.CreateFrom(fingerprint.CasElement.ToArray()),
                                strongFingerprint: new StrongContentFingerprint(FingerprintUtilities.CreateFrom(fingerprint.HashElement.ToArray())),
                                oringinatingCache: fingerprint.CacheId,
                                locality: locality);
            }

            return maybeFingerprint.Failure;
        }

        /// <inheritdoc />
        public async Task<Possible<CacheEntry?, Failure>> TryGetCacheEntryAsync(
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint, 
            OperationHints hints = default)
        {
            // TODO: We need a different side channel for prefetching etc. other than strong fingerprint subclasses.
            //              - Given aggregation of multiple *closely aligned* stores as the common case,
            //                one has the problem of optimizing which instance you hand to which store (and if you get it wrong, maybe things are just slower).
            //              - For it to work, we have to be rather disingenuous about equality (see examples below).
            //              - Usage this way is extremely error prone (with a perf pit if you get it wrong:
            //
            //                  foreach (StrongFingerprint candidate in Enumerate(...)) {
            //                      StrongFingerprint satisifiable = ComputeAvailable(LoadPathSet(candidate.CasElement));
            //                      if (satisfiable == candidate) { // TODO: We have to lie about equality!
            //                          GetCacheEntryAndRunPipFromCache(satisfiable); // TODO: Oops. Accidentally slower?
            //                      }
            //                  }
            //
            //              - By itself, lying about equality (for sake of the side-channel) probably has unintended consequences:
            //                      FancyStrongFingerprintCache.TryGetValue(pathSetHash, out strongFingerprint); // TODO: Oops. Side channel is broken again.
            //
            //              - The implementations have to figure out which StrongFingerprints have side channel info for them. This requires
            //                reflecting at least; but implementations must be vigilant to StrongFingerprints from *other instances*. This can break invariants,
            //                (e.g. what if someone had a dictionary of 'fingerprints I totally returned' to some other data, and indexed blindly), and means we
            //                have more to worry about w.r.t. cohabitation of implementations / instances under aggregation.
            //
            //  Given those, and that any implementation *must* accept valid StrongFingerprints from 'thin air' anyway (or aggregation won't work),
            //  I'm choosing here to neuter the sidechannel altogether (this has the added housekeeping benefit of Cache.Core types not appearing on BuildXL.Engine.Cache interfaces at all).
            StrongFingerprint reconstructedStrongFingerprint = new StrongFingerprint(
                weak: new WeakFingerprintHash(new Hash(weakFingerprint.Hash)),
                casElement: new CasHash(new global::BuildXL.Cache.Interfaces.Hash(pathSetHash)),
                hashElement: new global::BuildXL.Cache.Interfaces.Hash(strongFingerprint.Hash),
                cacheId: "Thin Air");

            if (m_cache.IsClosed)
            {
                return (CacheEntry?)null;
            }

            Possible<CasEntries, Failure> maybeEntry = await PerformFingerprintCacheOperationAsync(() => m_cache.GetCacheEntryAsync(reconstructedStrongFingerprint, hints), nameof(TryGetCacheEntryAsync));

            if (maybeEntry.Succeeded)
            {
                Contract.Assume(maybeEntry.Result != null, "Miss is supposed to be indicated with NoMatchingFingerprintFailure");
                return TryConvertCasEntriesToCacheEntry(maybeEntry.Result, null).Then<CacheEntry?>(e => e);
            }
            else
            {
                // TODO: This API will fail just because an entry isn't available, and that case isn't distinguishable (at least looking only at the interface).
                //              We can determine that case by reflecting here, in order to fit the requirements of the BuildXL-side interface; but that is quite fragile
                //              and requires some not-yet-prescribed co-operation from the implementations as to failure type. Instead, for a successful query (with no results),
                //              define a result type that indicates found / not-found (if only we had easy discriminated unions in C#!)
                if (maybeEntry.Failure is NoMatchingFingerprintFailure)
                {
                    return (CacheEntry?)null;
                }

                return maybeEntry.Failure;
            }
        }

        /// <inheritdoc />
        public async Task<Possible<CacheEntryPublishResult, Failure>> TryPublishCacheEntryAsync(
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint,
            CacheEntry entry,
            CacheEntryPublishMode publishMode = CacheEntryPublishMode.CreateNew,
            PublishCacheEntryOptions options = default)
        {
            // We can request semantics appropriate for CreateNewOrReplaceExisting via CacheDeterminism.SinglePhaseNonDeterministic
            // Note that conflict-rejections / failures may still occur.
            CacheDeterminism determinism = publishMode == CacheEntryPublishMode.CreateNewOrReplaceExisting
                ? CacheDeterminism.SinglePhaseNonDeterministic
                : default(CacheDeterminism);

            // N.B. this includes the metadata hash.
            CasEntries adaptedHashes = new CasEntries(
                entry.ToArray(h => new CasHash(new global::BuildXL.Cache.Interfaces.Hash(h))),
                determinism);

            Possible<FullCacheRecordWithDeterminism, Failure> maybePublished = await PerformFingerprintCacheOperationAsync(
                () => m_cache.AddOrGetAsync(
                    weak: new WeakFingerprintHash(new Hash(weakFingerprint.Hash)),
                    casElement: new CasHash(new Hash(pathSetHash)),
                    hashElement: new Hash(strongFingerprint.Hash),
                    hashes: adaptedHashes,
                    urgencyHint: options.ShouldPublishAssociatedContent ? UrgencyHint.RegisterAssociatedContent : UrgencyHint.SkipRegisterContent), 
                nameof(TryPublishCacheEntryAsync));

            if (maybePublished.Succeeded)
            {
                if (maybePublished.Result.Record == null)
                {
                    // Happy path: Entry accepted without an alternative.
                    return CacheEntryPublishResult.CreatePublishedResult();
                }
                else
                {
                    // Less happy path: The underlying store has an alternative entry that we need to use instead.
                    Possible<CacheEntry, Failure> maybeConvertedConflictingEntry = TryConvertCasEntriesToCacheEntry(maybePublished.Result.Record.CasEntries, maybePublished.Result.Record.CacheId);
                    if (maybeConvertedConflictingEntry.Succeeded)
                    {
                        return CacheEntryPublishResult.CreateConflictResult(maybeConvertedConflictingEntry.Result);
                    }
                    else
                    {
                        return maybeConvertedConflictingEntry.Failure.Annotate(
                            "The cache returned a conflicting entry (rejecting the proposed entry), but the conflicting entry is invalid.");
                    }
                }
            }
            else
            {
                return maybePublished.Failure;
            }
        }

        private static Possible<CacheEntry, Failure> TryConvertCasEntriesToCacheEntry(in CasEntries entry, string originatingCache)
        {
            Contract.Requires(entry != null);

            if (entry.Count > 0)
            {
                ContentHash[] hashes = new ContentHash[entry.Count];
                for (int i = 0; i < hashes.Length; i++)
                {
                    hashes[i] = entry[i].ToContentHash();
                }

                return CacheEntry.FromArray(ReadOnlyArray<ContentHash>.FromWithoutCopy(hashes), originatingCache);
            }
            else
            {
                return new Failure<string>("Cache entry is invalid; missing metadata reference");
            }
        }

        private static Task<Possible<TResult, Failure>> PerformFingerprintCacheOperationAsync<TResult>(Func<Task<Possible<TResult, Failure>>> func, string operationName)
        {
            return Utilities.PerformCacheOperationAsync(func, operationName, TimeSpan.FromMinutes(TimeoutDurationMin));
        }
    }
}
