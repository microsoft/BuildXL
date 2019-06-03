// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// A ICacheReadOnlySession instance represents a single read-only session within the cache.
    /// </summary>
    /// <remarks>
    /// There may be multiple instances of this interface live within the
    /// cache at any time (due to multiple concurrent builds going on).
    /// The session nature holds onto the pinning of CasHash entries.
    /// </remarks>
    public interface ICacheReadOnlySession
    {
        /// <summary>
        /// Returns the CacheId of the cache this session is connected to
        /// </summary>
        [NotNull]
        string CacheId { get; }

        /// <summary>
        /// The name unique name of this session or null if anonymous
        /// </summary>
        /// <remarks>
        /// This can be null for anonymous sessions
        /// </remarks>
        string CacheSessionId { get; }

        /// <summary>
        /// Returns true if the session has been closed
        /// </summary>
        /// <remarks>
        /// Needed to enable the contracts that prevent calling most
        /// methods after the session is closed.
        /// </remarks>
        bool IsClosed { get; }

        /// <summary>
        /// Returns true if this cache is configured for strict metadata to CAS coupling
        /// </summary>
        /// <remarks>
        /// A cache that has strict metadata to CAS coupling means that adding a
        /// metadata record requires that the CAS content referenced in that metadata
        /// record is also available via that cache.  This requires that the CAS
        /// content be made available before the metadata that references it is
        /// added via the AddOrGet() operation.
        /// Not having this strict coupling is usually only useful for caches that
        /// are operating as a lower-level cache with another higher level providing
        /// the strong coupling required.
        /// </remarks>
        bool StrictMetadataCasCoupling { get; }

        /// <summary>
        /// Shutdown the Cache subsystem.  After this call, calls to
        /// the Cache session will all fail/throw that the cache has
        /// been shut down.  The Cache may take a while to shut down
        /// as it may have to ensure content has completed any
        /// storing/transmission/etc requirements.
        /// </summary>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>The cache session ID on successful close</returns>
        /// <remarks>
        /// Closing a session that has a session ID but that had no
        /// cache records generated will abandon that session ID.
        /// </remarks>
        Task<Possible<string, Failure>> CloseAsync(Guid activityId = default(Guid));

        /// <summary>
        /// Returns an enumeration of the cached strong fingerprints that
        /// match the given weak fingerprint.
        /// </summary>
        /// <param name="weak">The weak fingerprint</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// An enumeration of Tasks that contain the potential cache Strong Fingerprints
        /// </returns>
        /// <remarks>
        /// It is expected that the client break out of the enumeration
        /// when further entries are not needed (when there is a cache
        /// hit) - this would allow the cache to stop the enumeration
        /// process and thus save time/resources.
        ///
        /// A marker StrongFingerprint may be used to identify when the
        /// enumeration hits the end of a given "effort amount" such
        /// as when finishing up with the last of the L1 data and the
        /// next request for more in the enumeration may be to obtain L2
        /// data.
        /// </remarks>
        IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints([CanBeNull]WeakFingerprintHash weak, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default(Guid));

        /// <summary>
        /// Get the unique cache entry that matches the Strong Fingerprint
        /// </summary>
        /// <param name="strong">The strong fingerprint</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// A list of CasHash that is in the order that was
        /// originally submitted - order is significant.
        /// </returns>
        /// <remarks>
        /// This may fail for various reasons but likely due to cache misses or
        /// cache connectivity problems
        /// </remarks>
        Task<Possible<CasEntries, Failure>> GetCacheEntryAsync([NotNull]StrongFingerprint strong, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default(Guid));

        /// <summary>
        /// Given a CAS Hash, ensure that the entry is available and kept available
        /// for the duration of this cache session
        /// </summary>
        /// <param name="hash">CAS Hash entry</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// If success, the CAS Hash entry is now available and the CacheIdentifier
        /// may be used to log explains where it came from.
        /// </returns>
        Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default(Guid));

        /// <summary>
        /// Given an array of CAS Hash, ensure that the entries are available and kept
        /// available for the duration of this cache session.
        /// </summary>
        /// <param name="hashes">Array of CAS Hashes</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// If success, the CAS Hash entries are now available locally and the CacheIdentifiers
        /// explains where they came from.  (Including already local content returning
        /// the CacheIdentifier.Local
        /// </returns>
        /// <remarks>
        /// If success, the CAS Hash entry is now available and the CacheIdentifier
        /// may be used to log explains where it came from.
        ///
        /// It is unclear if this should really be done as a single task or if the
        /// result should be an array of tasks which is just a helper over the single
        /// CasHash PinToCas() call above.  I do like this because it would allow
        /// the result of a GetCacheEntry() to then choose to ensure all are available.
        /// </remarks>
        Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries hashes, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default(Guid));

        /// <summary>
        /// Given a CAS Hash that is local, map it to the given filename
        /// </summary>
        /// <param name="hash">The CAS hash of the file</param>
        /// <param name="filename">Filename of the file to produce</param>
        /// <param name="fileState">Provides information on what state the build engine requires the files is in when produced.</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <param name="fileReplacementMode">File replacement mode.</param>
        /// <returns>The filename or a failure</returns>
        /// <remarks>
        /// This will fail for any CAS entry that is not available locally.
        /// </remarks>
        Task<Possible<string, Failure>> ProduceFileAsync(
            CasHash hash,
            [NotNull]string filename,
            FileState fileState,
            UrgencyHint urgencyHint = UrgencyHint.Nominal,
            Guid activityId = default(Guid),
            FileReplacementMode fileReplacementMode = FileReplacementMode.FailIfExists);

        /// <summary>
        /// Open a read-only stream on the given CasHash
        /// </summary>
        /// <param name="hash">The CAS Hash entry</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// A read-only stream of the contents in the CAS Hash entry
        /// </returns>
        Task<Possible<Stream, Failure>> GetStreamAsync(CasHash hash, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default(Guid));

        /// <summary>
        /// Get a dictionary of name/value pairs of cache session activity statistics
        /// </summary>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// The dictionary of cache key/value pairs
        /// </returns>
        /// <remarks>
        /// This should always return a dictionary but the actual statistics for
        /// a cache are not set in stone by the API.  There are some recommended
        /// ones but the key may be augmented by the aggregators.
        /// This is marked async in case the statistics require some activity to
        /// obtain.
        /// It is unclear what kind of failures are possible.
        /// </remarks>
        Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId = default(Guid));

        /// <summary>
        /// Tell the cache about a CasHash that is suspected to be invalid
        /// </summary>
        /// <param name="hash">The CasHash that is suspect</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// The ValidateContentStatus for this CasHash
        /// </returns>
        /// <remarks>
        /// This method tells the cache about a CasHash that had been gotten from
        /// the cache and seemed to hash to a different value.  The method is
        /// really intended to allow something like the Vertical Aggregator to
        /// notify the remote cache about a suspected cache inconsistency, but
        /// any user of the cache may do this.
        ///
        /// The operation may not be not low cost as it requires the cache to
        /// hash the contents of the file and then, if incorrect, potentially
        /// remediate the problem (usually involves deleting the entry or finding
        /// another source for the entry)
        ///
        /// This does not require pinning and may trigger operations on the cache
        /// including undoing a pinning operation for the session.  If the cache
        /// is read-only, it may not be able to remediate the situation.  A cache
        /// may try to but since it is read-only it may be limited in what can be
        /// done.
        ///
        /// It is expected that this API is used very infrequently - as in almost
        /// never.  However, when it is needed, it really is needed.
        /// </remarks>
        Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default(Guid));
    }
}
