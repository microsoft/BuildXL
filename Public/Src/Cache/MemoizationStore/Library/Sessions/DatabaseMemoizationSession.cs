// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    ///     An IMemoizationSession implemented using a database
    /// </summary>
    public class DatabaseMemoizationSession : ReadOnlyDatabaseMemoizationSession, IMemoizationSession
    {
        private readonly IContentSession _contentSession;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DatabaseMemoizationSession" /> class.
        /// </summary>
        /// <remarks>
        ///     Allowing contentSession to be null to allow the creation of uncoupled MemoizationSessions.
        ///     While we might extend this to the actual interface at some point, for now it's just a test hook
        ///     to compare to the previous behavior.  With a null content session, metadata will be automatically
        ///     overwritten because we're unable to check whether or not content is missing.
        /// </remarks>
        public DatabaseMemoizationSession(string name, DatabaseMemoizationStore memoizationStore, IContentSession contentSession = null)
            : base(name, memoizationStore)
        {
            _contentSession = contentSession;
        }

        /// <inheritdoc />
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return MemoizationStore.AddOrGetContentHashListAsync(
                context, strongFingerprint, contentHashListWithDeterminism, _contentSession, cts);
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(
            Context context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return BoolResult.SuccessTask;
        }
    }
}
