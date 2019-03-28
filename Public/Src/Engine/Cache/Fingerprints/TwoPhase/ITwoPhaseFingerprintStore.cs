// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Fingerprints.TwoPhase
{
    /// <summary>
    /// Represents a store of 'fingerprint' entries, each referencing content items by hash. Looking up a fingerprint is a two-phase process:
    /// - First, an approximate (or perfect!) fingerprint is computed using whatever is known ahead of time. This is a <see cref="WeakContentFingerprint"/>.
    ///   A two-phase fingerprint store can provide a list of strong (perfect) candidates via <see cref="ListPublishedEntriesByWeakFingerprint"/>.
    /// - Then, a candidate is selected (if possible), and its <see cref="StrongContentFingerprint"/> is used to lookup an entry (via <see cref="TryGetCacheEntryAsync"/>).
    /// A candidate is a (hash component, strong fingerprint) pair. The 'hash component' (plus any outside information, such as file content available in a build)
    /// can be used to derive a strong fingerprint; and the candidate is a match if it has that same strong fingerprint. Note that candidates could be simply a hash component;
    /// but that would induce a lookup per hash-component instead of a local comparison.
    /// </summary>
    public interface ITwoPhaseFingerprintStore
    {
        /// <summary>
        /// Lists (hash component, strong fingerprint) candidate pairs for a weak fingerprint. A sequence of <see cref="PublishedEntryRef"/> batches
        /// are returned (each batch containing zero or more entries). Note that the granularity of asynchronicity failure is a batch; an implementation
        /// may chooe to return all records from one place in a single batch (it may aggregate multiple such places). If retrieval of *any* batch fails, then
        /// results are incomplete (the consumer is still free to use any matching candidate that was returned).
        /// Consumers are permitted to await multiple batch-tasks in parallel.
        /// </summary>
        IEnumerable<Task<Possible<PublishedEntryRef, Failure>>> ListPublishedEntriesByWeakFingerprint(WeakContentFingerprint weak);

        /// <summary>
        /// Attempts to find a cache entry for a strong fingerprint, which *may* have been discovered via <see cref="ListPublishedEntriesByWeakFingerprint"/>.
        /// (consumers may instead invent strong fingerprints, if needed). If the query proceeds without finding an entry, returns <c>null</c> (this is distinct
        /// from a failure, which indicates that the query did not complete correctly).
        /// </summary>
        Task<Possible<CacheEntry?, Failure>> TryGetCacheEntryAsync(
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint);

        /// <summary>
        /// Attempts to add a new entry. As with querying for entries, this has two phases:
        /// - Attach a (hash component, strong fingerprint) pair to the <paramref name="weakFingerprint"/> (such that it is returned by <see cref="ListPublishedEntriesByWeakFingerprint"/>).
        /// - Attach the <paramref name="entry"/> to the <paramref name="strongFingerprint"/>.
        /// An implementation may choose to reject a proposed entry for any reason, so long as it can provide an *alternative* (already-published) entry to use instead.
        /// We call this a 'conflict', manifesting as <see cref="CacheEntryPublishStatus.RejectedDueToConflictingEntry"/>.
        /// </summary>
        Task<Possible<CacheEntryPublishResult, Failure>> TryPublishCacheEntryAsync(
            WeakContentFingerprint weakFingerprint,
            ContentHash pathSetHash,
            StrongContentFingerprint strongFingerprint,
            CacheEntry entry,
            CacheEntryPublishMode mode = CacheEntryPublishMode.CreateNew);
    }

    /// <summary>
    /// Replacement mode for <see cref="ITwoPhaseFingerprintStore.TryPublishCacheEntryAsync"/>
    /// </summary>
    public enum CacheEntryPublishMode
    {
        /// <summary>
        /// This is the typical mode in which existing entries are not replaced, and instead
        /// we expect a 'conflict' result (<see cref="CacheEntryPublishStatus.RejectedDueToConflictingEntry"/>).
        /// </summary>
        CreateNew,

        /// <summary>
        /// This mode requests that the underlying cache should unconditionally publish an entry ('last writer wins').
        /// Note that this is advisory; cache implementations may choose to not honor this request under
        /// various circumstances.
        /// TODO: We only have this mode to support <see cref="SinglePhase.SinglePhaseFingerprintStoreAdapter"/>.
        /// </summary>
        CreateNewOrReplaceExisting,
    }

    /// <summary>
    /// Status of a <see cref="CacheEntryPublishResult"/> as returned by <see cref="ITwoPhaseFingerprintStore.TryPublishCacheEntryAsync"/>.
    /// </summary>
    public enum CacheEntryPublishStatus
    {
        /// <summary>
        /// Publish succeeded. The entry is now stored.
        /// </summary>
        Published,

        /// <summary>
        /// Publish failed. The store has provided an existing, conflicitng entry to use instead.
        /// </summary>
        RejectedDueToConflictingEntry,
    }

    /// <summary>
    /// Result of attempting to publish a <see cref="CacheEntry"/> via <see cref="ITwoPhaseFingerprintStore.TryPublishCacheEntryAsync"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct CacheEntryPublishResult
    {
        private readonly CacheEntryBox m_conflictingEntryBox;

        private CacheEntryPublishResult(CacheEntryBox box)
        {
            m_conflictingEntryBox = box;
        }

        /// <summary>
        /// Creates a result with <see cref="CacheEntryPublishStatus.Published"/>.
        /// </summary>
        public static CacheEntryPublishResult CreatePublishedResult()
        {
            return default(CacheEntryPublishResult);
        }

        /// <summary>
        /// Creates a conflict result (<see cref="CacheEntryPublishStatus.RejectedDueToConflictingEntry"/>).
        /// </summary>
        public static CacheEntryPublishResult CreateConflictResult(in CacheEntry conflictingEntry)
        {
            return new CacheEntryPublishResult(new CacheEntryBox { Entry = conflictingEntry });
        }

        /// <nodoc />
        public CacheEntryPublishStatus Status => m_conflictingEntryBox == null ? CacheEntryPublishStatus.Published : CacheEntryPublishStatus.RejectedDueToConflictingEntry;

        /// <summary>
        /// Conflicting entry causing rejection (the entry to use instead).
        /// This property is only available with a <see cref="Status"/> of <see cref="CacheEntryPublishStatus.RejectedDueToConflictingEntry"/>.
        /// </summary>
        public CacheEntry ConflictingEntry
        {
            get
            {
                Contract.Requires(Status == CacheEntryPublishStatus.RejectedDueToConflictingEntry);
                return m_conflictingEntryBox.Entry;
            }
        }

        private sealed class CacheEntryBox
        {
            public CacheEntry Entry;
        }
    }

    /// <summary>
    /// Indicates the qualitative close-ness of a ref (local vs. remote hit).
    /// </summary>
    public enum PublishedEntryRefLocality
    {
        /// <summary>
        /// Indicates that an originating cache for a <see cref="PublishedEntryRef"/> is considered local.
        /// </summary>
        Local,

        /// <summary>
        /// Indicates that an originating cache for a <see cref="PublishedEntryRef"/> is considered remote (e.g. a shared cache).
        /// </summary>
        Remote,

        /// <summary>
        /// Indicates the value was taken from the cache due to convergence. This is broken out because it isn't
        /// necessarily known to be Local vs. Remote at the time convergence happens.
        /// </summary>
        Converged,
    }

    /// <summary>
    /// Single candidate as indicated by <see cref="ITwoPhaseFingerprintStore.ListPublishedEntriesByWeakFingerprint"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct PublishedEntryRef
    {
        private readonly ContentHash m_pathSetHash;
        private readonly StrongContentFingerprint m_strongFingerprint;
        private readonly string m_originatingCache;
        private readonly PublishedEntryRefLocality m_locality;

        /// <summary>
        /// Use instead of default(PublishedEntryRef)
        /// </summary>
        public static readonly PublishedEntryRef Default = new PublishedEntryRef(ContentHashingUtilities.ZeroHash, StrongContentFingerprint.Zero);

        /// <nodoc />
        public static readonly PublishedEntryRef Ignore = new PublishedEntryRef(true);

        /// <summary>
        /// Hash component which a consumer can use to generate a <see cref="StrongContentFingerprint"/>.
        /// </summary>
        public ContentHash PathSetHash
        {
            get
            {
                AssertNotIgnoring();
                return m_pathSetHash;
            }
        }

        /// <summary>
        /// A strong fingerprint previously derived from <see cref="PathSetHash"/>. This is a hint that
        /// a lookup of the strong fingerprint with <see cref="ITwoPhaseFingerprintStore.TryGetCacheEntryAsync"/>
        /// would succeed.
        /// </summary>
        public StrongContentFingerprint StrongFingerprint
        {
            get
            {
                AssertNotIgnoring();
                return m_strongFingerprint;
            }
        }

        /// <summary>
        /// The cache where the entry ref was published to
        /// </summary>
        public string OriginatingCache
        {
            get
            {
                AssertNotIgnoring();
                return m_originatingCache;
            }
        }

        /// <summary>
        /// Indicates the qualitative close-ness of this ref (local vs. remote hit).
        /// </summary>
        public PublishedEntryRefLocality Locality
        {
            get
            {
                AssertNotIgnoring();
                return m_locality;
            }
        }

        /// <nodoc />
        public readonly bool IgnoreEntry;

        /// <nodoc />
        private PublishedEntryRef(bool ignoreEntry)
        {
            m_pathSetHash = ContentHashingUtilities.ZeroHash;
            m_strongFingerprint = StrongContentFingerprint.Zero;
            m_originatingCache = null;
            m_locality = PublishedEntryRefLocality.Local;
            IgnoreEntry = ignoreEntry;
        }

        /// <nodoc />
        public PublishedEntryRef(ContentHash pathSetHash, StrongContentFingerprint strongFingerprint, string oringinatingCache, PublishedEntryRefLocality locality)
        {
            m_pathSetHash = pathSetHash;
            m_strongFingerprint = strongFingerprint;
            m_originatingCache = oringinatingCache;
            m_locality = locality;
            IgnoreEntry = false;
        }
        
        /// <nodoc />
        public PublishedEntryRef(ContentHash pathSetHash, StrongContentFingerprint strongFingerprint)
        : this()
        {
            m_pathSetHash = pathSetHash;
            m_strongFingerprint = strongFingerprint;
        }

        private void AssertNotIgnoring()
        {
            Contract.Assert(!IgnoreEntry);
        }
    }
}
