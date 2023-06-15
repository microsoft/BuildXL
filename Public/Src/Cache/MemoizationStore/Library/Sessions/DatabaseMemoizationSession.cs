// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <nodoc />
    public class DatabaseMemoizationSession : StartupShutdownBase, IMemoizationSession, IMemoizationSessionWithLevelSelectors
    {
        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        protected override Tracer Tracer { get; }

        /// <nodoc />
        protected readonly DatabaseMemoizationStore MemoizationStore;

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            bool preferShared = urgencyHint == UrgencyHint.PreferShared;
            return MemoizationStore.GetContentHashListAsync(context, strongFingerprint, cts, _contentSession, preferShared);
        }

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            return MemoizationStore.GetLevelSelectorsAsync(context, weakFingerprint, cts, level);
        }

        /// <inheritdoc />
        public System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }

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
        {
            Contract.Requires(name != null);
            Contract.Requires(memoizationStore != null);

            Tracer = new Tracer(nameof(DatabaseMemoizationSession));
            Name = name;
            MemoizationStore = memoizationStore;
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
                context,
                strongFingerprint,
                contentHashListWithDeterminism,
                _contentSession,
                cts);
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(
            Context context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return MemoizationStore.IncorporateStrongFingerprintsAsync(context, strongFingerprints, cts);
        }
    }
}
