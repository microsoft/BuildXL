// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    /// <summary>
    ///     An IReadOnlyMemoizationSession implemented in RocksDb
    /// </summary>
    public class ReadOnlyDatabaseMemoizationSession : StartupShutdownBase, IReadOnlyMemoizationSessionWithLevelSelectors
    {
        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        protected override Tracer Tracer { get; }

        /// <nodoc />
        protected readonly DatabaseMemoizationStore MemoizationStore;

        /// <nodoc />
        public ReadOnlyDatabaseMemoizationSession(string name, DatabaseMemoizationStore memoizationStore)
        {
            Contract.Requires(name != null);
            Contract.Requires(memoizationStore != null);

            Tracer = new Tracer(nameof(ReadOnlyDatabaseMemoizationSession));
            Name = name;
            MemoizationStore = memoizationStore;
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return MemoizationStore.GetContentHashListAsync(context, strongFingerprint, cts);
        }

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            return MemoizationStore.GetLevelSelectorsAsync(context, weakFingerprint, cts, level);
        }

        /// <inheritdoc />
        public System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }
    }
}
