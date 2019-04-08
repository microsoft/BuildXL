// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.Cache.Fingerprints.SinglePhase
{
    /// <summary>
    /// Adapter to provide an single-phase fingerprint store from an <see cref="ITwoPhaseFingerprintStore"/> (and an
    /// <see cref="IArtifactContentCache"/> that provides access to referenced content).
    /// This is straightforward in principle:
    /// - <see cref="TryGetFingerprintEntryAsync"/> constructs a <see cref="StrongContentFingerprint"/>, finds a <see cref="CacheEntry"/>,
    ///   and deserializes the content refered to by <see cref="CacheEntry.MetadataHash"/>.
    /// - <see cref="TryStoreFingerprintEntryAsync"/> does the inverse (though note that <see cref="PipFingerprintEntry.OutputContentHashes"/>
    ///   is used to additionally populate the remaining referenced hashes, since the store implementation may use this for e.g. GC)
    /// - <see cref="GetFingerprintEntryReader"/> returns a dummy reader that can return only one result.
    ///
    /// Entries are stored with 'last writer wins' semantics since they are often unusable due to assertions; this is an unusual use of a two-phase store,
    /// since normally entries are usable so long as referenced content is available (and could be removed entirely if this adapter is no longer needed).
    /// </summary>
    /// <remarks>
    /// TODO: Single-phase lookup should be considered deprecated. Therefore, invest in moving to two-phase lookup rather than improving the bad 'replace' semantics in this shim.
    /// </remarks>
    public sealed class SinglePhaseFingerprintStoreAdapter
    {
        private static readonly ContentHash s_dummyPathSetHash = ContentHashingUtilities.ZeroHash;

        private readonly LoggingContext m_loggingContext;
        private readonly PathTable m_pathTable;
        private readonly IArtifactContentCache m_contentCache;
        private readonly ITwoPhaseFingerprintStore m_twoPhaseStore;

        /// <nodoc />
        public SinglePhaseFingerprintStoreAdapter(
            LoggingContext loggingContext,
            PipExecutionContext context,
            ITwoPhaseFingerprintStore twoPhaseStore,
            IArtifactContentCache contentCache)
            : this(loggingContext, context.PathTable, twoPhaseStore, contentCache)
        {
            Contract.Requires(context != null);
        }

        /// <nodoc />
        public SinglePhaseFingerprintStoreAdapter(LoggingContext loggingContext, PathTable pathTable, ITwoPhaseFingerprintStore twoPhaseStore, IArtifactContentCache contentCache)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(twoPhaseStore != null);
            Contract.Requires(contentCache != null);

            m_loggingContext = loggingContext;
            m_twoPhaseStore = twoPhaseStore;
            m_contentCache = contentCache;
            m_pathTable = pathTable;
        }

        /// <summary>
        /// Attempts to retrieve a fingerprint entry.
        /// If the query is successful, a <see cref="PipFingerprintEntry"/> is returned (or null, if there is no current entry for the fingerprint).
        /// </summary>
        public async Task<Possible<PipFingerprintEntry, Failure>> TryGetFingerprintEntryAsync(ContentFingerprint fingerprint, CacheQueryData cacheQueryData = null)
        {
            StrongContentFingerprint dummyFingerprint = ComputeDummyStrongFingerprint(
                m_pathTable,
                fingerprint);

            var weakFingerprint = new WeakContentFingerprint(fingerprint.Hash);

            Possible<CacheEntry?, Failure> maybeEntry = await m_twoPhaseStore.TryGetCacheEntryAsync(
                weakFingerprint: weakFingerprint,
                pathSetHash: s_dummyPathSetHash,
                strongFingerprint: dummyFingerprint);

            if (!maybeEntry.Succeeded)
            {
                return maybeEntry.Failure;
            }

            if (!maybeEntry.Result.HasValue)
            {
                // Real miss.
                return (PipFingerprintEntry)null;
            }

            if (cacheQueryData != null)
            {
                cacheQueryData.WeakContentFingerprint = weakFingerprint;
                cacheQueryData.PathSetHash = s_dummyPathSetHash;
                cacheQueryData.StrongContentFingerprint = dummyFingerprint;
                cacheQueryData.MetadataHash = maybeEntry.Result.Value.MetadataHash;
                cacheQueryData.ContentCache = m_contentCache;
            }

            return await TryLoadAndDeserializeContent(maybeEntry.Result.Value.MetadataHash);
        }

        /// <nodoc />
        public async Task<Possible<PipFingerprintEntry, Failure>> TryLoadAndDeserializeContent(ContentHash contentHash)
        {
            Possible<PipFingerprintEntry, Failure> maybeDeserializedEntry =
                   await m_contentCache.TryLoadAndDeserializeContentWithRetry<PipFingerprintEntry>(
                       m_loggingContext,
                       contentHash,
                       shouldRetry: possibleResult => !possibleResult.Succeeded || (possibleResult.Result != null && possibleResult.Result.IsCorrupted),
                       maxRetry: PipFingerprintEntry.LoadingAndDeserializingRetries);

            if (!maybeDeserializedEntry.Succeeded)
            {
                return maybeDeserializedEntry.Failure.Annotate("Failed to deserialize cache-entry metadata from the CAS");
            }

            return maybeDeserializedEntry.Result;
        }

        /// <summary>
        /// Attempts to store a fingerprint entry; this is a compare-exchange operation, in which <paramref name="previousEntry"/>
        /// must match what is currently stored (or must be <c>null</c> if no entry is currently stored).
        /// This operation will fail if the previous entry doesn't match, or if the store could not normally proceed.
        /// </summary>
        public async Task<Possible<CacheEntryPublishResult, Failure>> TryStoreFingerprintEntryAsync(
            ContentFingerprint fingerprint,
            PipFingerprintEntry entry,
            PipFingerprintEntry previousEntry = null,
            bool replaceExisting = true,
            CacheQueryData cacheQueryData = null)
        {
            Contract.Assume(entry != null);
            Analysis.IgnoreArgument(previousEntry); // See class remarks; replace semantics are broken.

            // We have in hand a PipFingerprintEntry which the underlyign m_twoPhaseStore does not understand.
            // We will serialize it and store it to the CAS, and that CAS hash will be the stored entry's MetadataHash.
            // See symmetric deserialization in TryGetFingerprintEntryAsync.
            Possible<ContentHash, Failure> maybeStored = await m_contentCache.TrySerializeAndStoreContent(entry);
            if (!maybeStored.Succeeded)
            {
                return maybeStored.Failure.Annotate("Failed to store cache-entry metadata to the CAS");
            }

            ContentHash metadataHash = maybeStored.Result;

            // The metadata (single-phase entry) is stored, so now we can construct an entry that references it.
            // From now on, 'twoPhaseEntry' will mean 'the entry we are actually storing in the two-phase store'.
            // Meanwhile, like any implementation, we assume that the referenced content (e.g. output files)
            // were stored by the caller already.
            ContentHash[] twoPhaseReferencedHashes = entry.OutputContentHashes.Select(b => b.ToContentHash()).Where(hash => !hash.IsSpecialValue()).ToArray();
            CacheEntry twoPhaseEntry = new CacheEntry(metadataHash, null, ArrayView<ContentHash>.FromArray(twoPhaseReferencedHashes));

            StrongContentFingerprint dummyFingerprint = ComputeDummyStrongFingerprint(
                m_pathTable,
                fingerprint);

            var weakFingerprint = new WeakContentFingerprint(fingerprint.Hash);

            Possible<CacheEntryPublishResult, Failure> maybePublished = await m_twoPhaseStore.TryPublishCacheEntryAsync(
                weakFingerprint: weakFingerprint,
                pathSetHash: s_dummyPathSetHash,
                strongFingerprint: dummyFingerprint,
                entry: twoPhaseEntry,
                mode: replaceExisting ? CacheEntryPublishMode.CreateNewOrReplaceExisting : CacheEntryPublishMode.CreateNew);

            if (cacheQueryData != null)
            {
                cacheQueryData.WeakContentFingerprint = weakFingerprint;
                cacheQueryData.PathSetHash = s_dummyPathSetHash;
                cacheQueryData.StrongContentFingerprint = dummyFingerprint;
                cacheQueryData.ContentCache = m_contentCache;
            }

            if (maybePublished.Succeeded)
            {
                if (maybePublished.Result.Status == CacheEntryPublishStatus.Published ||
                    (!replaceExisting && maybePublished.Result.Status == CacheEntryPublishStatus.RejectedDueToConflictingEntry))
                {
                    return maybePublished.Result;
                }
                else
                {
                    // ISinglePhaseFingerprintStore represents conflicts as failures.
                    return new Failure<string>(
                        "Failed to publish a cache entry; the underlying two-phase store indicated an entry conflict (maybe it does not allow replacement of existing entries).");
                }
            }
            else
            {
                return maybePublished.Failure;
            }
        }

        /// <summary>
        /// Returns a reader which can visit multiple fingerprint entries for a fingerprint (due to multiple cache levels, etc.)
        /// </summary>
        public IFingerprintEntryReader GetFingerprintEntryReader(ContentFingerprint fingerprint)
        {
            return new EntryReader(this, fingerprint);
        }

        private static StrongContentFingerprint ComputeDummyStrongFingerprint(PathTable pathTable, ContentFingerprint weakFingerprint)
        {
            using (var hasher = StrongContentFingerprint.CreateHashingHelper(
                pathTable,
                recordFingerprintString: false))
            {
                return new StrongContentFingerprint(hasher.GenerateHash());
            }
        }

        /// <summary>
        /// Trivial reader which just wraps <see cref="SinglePhaseFingerprintStoreAdapter.TryGetFingerprintEntryAsync"/>.
        /// </summary>
        private sealed class EntryReader : IFingerprintEntryReader
        {
            private readonly SinglePhaseFingerprintStoreAdapter m_store;
            private readonly ContentFingerprint m_fingerprint;
            private bool m_returnedEntry = false;

            public EntryReader(SinglePhaseFingerprintStoreAdapter store, ContentFingerprint fingerprint)
            {
                Contract.Requires(store != null);
                m_store = store;
                m_fingerprint = fingerprint;
            }

            public async Task<bool> ReadBatch(Action<Possible<IAcceptablePipFingerprintEntry, Failure>> inspect)
            {
                if (!m_returnedEntry)
                {
                    m_returnedEntry = true;

                    Possible<PipFingerprintEntry, Failure> maybeEntry = await m_store.TryGetFingerprintEntryAsync(m_fingerprint);

                    if (maybeEntry.Succeeded)
                    {
                        if (maybeEntry.Result == null)
                        {
                            // Miss
                            inspect(
                                new Possible<IAcceptablePipFingerprintEntry, Failure>(
                                    (IAcceptablePipFingerprintEntry)null));
                        }
                        else
                        {
                            // Hit
                            inspect(new AcceptableEntry(maybeEntry.Result));
                        }
                    }
                    else
                    {
                        // Failure
                        inspect(maybeEntry.Failure);
                    }
                }

                // No additional batches available (we always have exactly one).
                return false;
            }

            private sealed class AcceptableEntry : IAcceptablePipFingerprintEntry
            {
                public AcceptableEntry(PipFingerprintEntry entry)
                {
                    Contract.Requires(entry != null);
                    Entry = entry;
                }

                public PipFingerprintEntry Entry { get; }

                public void Accept()
                {
                }
            }
        }
    }

    /// <summary>
    /// Interface to accept which entry provided by an <see cref="IFingerprintEntryReader"/> was usable.
    /// <see cref="Entry"/> is non-null.
    /// </summary>
    public interface IAcceptablePipFingerprintEntry
    {
        /// <nodoc />
        PipFingerprintEntry Entry { get; }

        /// <nodoc />
        void Accept();
    }

    /// <summary>
    /// Reader which can visit multiple fingerprint entries for a fingerprint (due to multiple cache levels, etc.)
    /// </summary>
    public interface IFingerprintEntryReader
    {
        /// <summary>
        /// Calls the <paramref name="inspect"/> callback zero or more times, once per level.
        /// If a level does not have an entry, the parameter is a 'possible' wrapping <c>null</c> (though the next level may have an entry).
        /// Returns 'true' if another call may return more results, or 'false' if it definitely will not.
        /// </summary>
        Task<bool> ReadBatch(Action<Possible<IAcceptablePipFingerprintEntry, Failure>> inspect);
    }
}
