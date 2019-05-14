// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    /// A related set of read accesses to a cache.
    /// </summary>
    public interface IReadOnlyMemoizationSession : IName, IStartupShutdown
    {
        /// <summary>
        /// Gets known selectors for a given weak fingerprint.
        /// </summary>
        Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal
        );

        /// <summary>
        /// Load a ContentHashList.
        /// </summary>
        Task<GetContentHashListResult> GetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);
    }

    /// <summary>
    /// A related set of read accesses to a cache with support for multi-level GetSelectors.
    /// </summary>
    public interface IReadOnlyMemoizationSessionWithLevelSelectors : IReadOnlyMemoizationSession
    {
        /// <summary>
        /// Gets known selectors for a given weak fingerprint for a given "level".
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="IReadOnlyMemoizationSession.GetSelectors"/>, this method is RPC friendly.
        /// </remarks>
        Task<Result<LevelSelectors>> GetLevelSelectorsAsync(
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cts,
            int level);
    }
}
