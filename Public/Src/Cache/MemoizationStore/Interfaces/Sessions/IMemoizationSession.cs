// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     A related set of accesses to a cache.
    /// </summary>
    public interface IMemoizationSession : IReadOnlyMemoizationSession
    {
        /// <summary>
        ///     Store a ContentHashList
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="strongFingerprint">
        ///     Full key for ContentHashList value.
        /// </param>
        /// <param name="contentHashListWithDeterminism">
        ///     The value, and associated determinism guarantee, to store.
        /// </param>
        /// <param name="cts">
        ///     A token that can signal this call should return as soon as possible.
        /// </param>
        /// <param name="urgencyHint">
        ///     Hint as to how urgent this request is.
        /// </param>
        /// <returns>
        ///     Result providing the call's completion status.
        /// </returns>
        Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        ///     Add the given StrongFingerprints to the set of StrongFingerprints that were touched by this session
        /// </summary>
        /// <remarks>
        ///     (Paraphrased from BuildXL)
        ///     This API is mainly for supporting multi-level caches where, at shutdown time,
        ///     the Remote may not have seen all of the strong fingerprints that were used by the
        ///     session due to Local cache hits.  In order for the Remote to know that these
        ///     strong fingerprints were used, the strong fingerprints from the Local are
        ///     made available by the aggregator before closing the Remote.  The Remote should add
        ///     them as needed to inform its own retention.
        /// </remarks>
        Task<BoolResult> IncorporateStrongFingerprintsAsync(
            Context context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);
    }
}
