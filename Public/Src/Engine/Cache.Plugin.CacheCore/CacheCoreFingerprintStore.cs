// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Implementation of <see cref="ITwoPhaseFingerprintStore"/> via a Cache.Core <see cref="ICacheSession"/>.
    /// </summary>
    public sealed class CacheCoreFingerprintStore : ITwoPhaseFingerprintStore
    {
        private readonly ICacheSession m_cache;

        /// <nodoc />
        public CacheCoreFingerprintStore(ICacheSession cache)
        {
            Contract.Requires(cache != null);
            m_cache = cache;
        }

        /// <inheritdoc />
        public IEnumerable<Task<Possible<PublishedEntryRef, Failure>>> ListPublishedEntriesByWeakFingerprint(WeakContentFingerprint weak)
        {
            WeakFingerprintHash cacheCoreWeakFingerprint = new WeakFingerprintHash(new Hash(weak.Hash));

            // TODO: We assume that everything up until the first sentinel is local. This is fine for a simple 'vertical' aggregator
            //       of a local and remote cache, but isn't general.
            PublishedEntryRefLocality currentLocality = PublishedEntryRefLocality.Local;

            foreach (Task<Possible<StrongFingerprint, Failure>> entryPromise in m_cache.EnumerateStrongFingerprints(cacheCoreWeakFingerprint))
            {
                if (entryPromise.IsCompleted && entryPromise.Result.Succeeded && entryPromise.Result.Result is StrongFingerprintSentinel)
                {
                    currentLocality = PublishedEntryRefLocality.Remote;
                    continue;
                }

                yield return AdaptPublishedEntry(entryPromise, currentLocality);
            }
        }

        private static async Task<Possible<PublishedEntryRef, Failure>> AdaptPublishedEntry(Task<Possible<StrongFingerprint, Failure>> cacheCoreEntryPromise, PublishedEntryRefLocality locality)
        {
            Possible<StrongFingerprint, Failure> maybeFingerprint = await cacheCoreEntryPromise;
            if (maybeFingerprint.Succeeded)
            {
                StrongFingerprint fingerprint = maybeFingerprint.Result;

                if (fingerprint is StrongFingerprintSentinel)
                {
                    return PublishedEntryRef.Ignore;
                }

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
            StrongContentFingerprint strongFingerprint)
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

            Possible<CasEntries, Failure> maybeEntry = await m_cache.GetCacheEntryAsync(reconstructedStrongFingerprint);
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
            CacheEntryPublishMode publishMode = CacheEntryPublishMode.CreateNew)
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

            Possible<FullCacheRecordWithDeterminism, Failure> maybePublished = await m_cache.AddOrGetAsync(
                weak: new WeakFingerprintHash(new Hash(weakFingerprint.Hash)),
                casElement: new CasHash(new Hash(pathSetHash)),
                hashElement: new Hash(strongFingerprint.Hash),
                hashes: adaptedHashes);

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
    }
}
