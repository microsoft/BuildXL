// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// A ICacheSession instance represents a single build session within the cache.
    /// </summary>
    /// <remarks>
    /// There may be multiple instances of this interface live within the
    /// cache at any time (due to multiple concurrent builds going on).
    /// The session nature holds onto the pinning of CasHash entries
    /// and the set of used strong fingerprints for the whole cache.
    /// </remarks>
    public interface ICacheSession : ICacheReadOnlySession
    {
        /// <summary>
        /// Add the given file to the local CAS environment
        /// </summary>
        /// <param name="filename">Path to the file</param>
        /// <param name="fileState">Provides information on what state the build engine requires the file be in after it is added.</param>
        /// <param name="hash">The contenthash for the data</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>The CAS Hash for this file</returns>
        /// <remarks>
        /// Adding to the CAS is how the CASHash is obtained for a file.
        /// </remarks>
        Task<Possible<CasHash, Failure>> AddToCasAsync(
            [NotNull]string filename,
            FileState fileState,
            CasHash? hash = null,
            UrgencyHint urgencyHint = UrgencyHint.Nominal,
            Guid activityId = default(Guid));

        /// <summary>
        /// Add the given file stream to the CAS environment
        /// </summary>
        /// <param name="filestream">Read-only stream for the file data</param>
        /// <param name="hash">The contenthash for the data</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>The CAS Hash for this file</returns>
        /// <remarks>
        /// Adding to the CAS is how the CASHash is obtained for the data.
        /// </remarks>
        Task<Possible<CasHash, Failure>> AddToCasAsync(
            [NotNull]Stream filestream,
            CasHash? hash = null,
            UrgencyHint urgencyHint = UrgencyHint.Nominal,
            Guid activityId = default(Guid));

        /// <summary>
        /// Add or Get the given cache data
        /// </summary>
        /// <param name="weak">The weak fingerprint element of the strong fingerprint</param>
        /// <param name="casElement">The CAS Hash element of the strong fingerprint</param>
        /// <param name="hashElement">The hash element of the strong fingerprint</param>
        /// <param name="hashes">The ordered array of CAS Hashes for this entry</param>
        /// <param name="urgencyHint">Optional hint as to how urgent this request is</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// Returns null if the cache entry was encached or otherwise accepted.
        /// Returns a FullCacheRecord if the cache entry was not accepted and there
        /// was a replacement record from elsewhere.
        /// </returns>
        /// <remarks>
        /// Note that all CAS Hash items must already exist in the local CAS due to
        /// a successful call to either AddToCas or Pin.
        ///
        /// A cache may pick if it does determinism recovery.  It may just replace
        /// content in the cache if you add to it.  If the content is exactly the same
        /// it should be seen as the same and not as a replacement.
        ///
        /// Note that the internal details as to how it ensures the correctness is
        /// up to the cache implementation.  The content should not be assumed valid
        /// until some result is returned.
        ///
        /// Normally, one would default to saying that the content is non-deterministic
        /// but if the content is known to be deterministic by some measure, then
        /// adding ToolDeterminism will allow the cache to simplify some of the
        /// internal operations.
        ///
        /// Replacement policy is based on the following:
        /// New content does not replace existing content except when
        /// a) New content is "more deterministic" than existing content
        /// b) Old content is "incomplete"
        ///
        /// The definition of "more deterministic" is in the CacheDeterminism class.
        /// </remarks>
        Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default(Guid));

        /// <summary>
        /// Add the given FullCacheRecords to the cache records of this session
        /// </summary>
        /// <param name="strongFingerprints">An enumeration of strong fingerprints for the session</param>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>Number of cache records incorporated</returns>
        /// <remarks>
        /// This API is mainly for supporting multi-level caches where, at close time,
        /// the L2 may not have seen all of the cache records that were used by the
        /// session due to L1 cache hits.  In order for the L2 to know that these
        /// strong fingerprints were used, the strong fingerprints from the L1 are
        /// made available by the aggregator before closing the L2.  The L2 should add
        /// them as needed to its own session.
        ///
        /// This also provides a "bulk" way to update LRU records if a cache wishes
        /// to operate in such a manner.
        ///
        /// The return value should be the number of records that were added that the
        /// L2 did not already know for this session.  However, this is only for telemetry
        /// and potential internal testing of the cache.
        ///
        /// A failure would be returned if records can not be read.
        /// </remarks>
        Task<Possible<int, Failure>> IncorporateRecordsAsync([NotNull]IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId = default(Guid));

        /// <summary>
        /// Returns the set of strong fingerprints that belong to this cache
        /// session.
        /// </summary>
        /// <param name="activityId">Guid that identifies the parent of this call for tracing.</param>
        /// <returns>
        /// The enumeration of strong fingerprints that were consumed and/or produced
        /// in the build.
        /// </returns>
        /// <remarks>
        /// Can only be called on a closed session.  Will return no fingerprints
        /// for anonymous sessions.
        /// </remarks>
        IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId = default(Guid));
    }
}
