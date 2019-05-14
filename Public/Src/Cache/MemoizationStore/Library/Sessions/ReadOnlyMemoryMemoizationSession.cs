// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

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
    /// <summary>
    ///     An <see cref="IReadOnlyMemoizationSession"/> implemented in memory
    /// </summary>
    public class ReadOnlyMemoryMemoizationSession : StartupShutdownBase, IReadOnlyMemoizationSessionWithLevelSelectors
    {
        /// <nodoc />
        protected readonly MemoryMemoizationStore MemoizationStore;

        /// <inheritdoc />
        protected override Tracer Tracer { get; }

        /// <nodoc />
        public ReadOnlyMemoryMemoizationSession(string name, MemoryMemoizationStore memoizationStore)
        {
            Contract.Requires(name != null);
            Contract.Requires(memoizationStore != null);

            Name = name;
            Tracer = new Tracer(name);
            MemoizationStore = memoizationStore;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            return Task.FromResult(LevelSelectors.Single(MemoizationStore.GetSelectorsCore(context, weakFingerprint, cts)));
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(
            Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return MemoizationStore.GetContentHashListAsync(context, strongFingerprint, cts);
        }
    }
}
