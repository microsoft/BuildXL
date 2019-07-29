// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
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
    public class ReadOnlyRocksDbMemoizationSession : StartupShutdownBase, IReadOnlyMemoizationSessionWithLevelSelectors
    {
        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        protected override Tracer Tracer { get; }

        /// <nodoc />
        protected readonly RocksDbMemoizationStore MemoizationStore;

        /// <nodoc />
        public ReadOnlyRocksDbMemoizationSession(string name, RocksDbMemoizationStore memoizationStore)
        {
            Contract.Requires(name != null);
            Contract.Requires(memoizationStore != null);

            Tracer = new Tracer(name);
            Name = name;
            MemoizationStore = memoizationStore;
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return MemoizationStore.GetContentHashListAsync(context, strongFingerprint, cts);
        }

        /// <inheritdoc />
        public async Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            return LevelSelectors.Single(await MemoizationStore.GetSelectorsCoreAsync(context, weakFingerprint));
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }
    }
}
