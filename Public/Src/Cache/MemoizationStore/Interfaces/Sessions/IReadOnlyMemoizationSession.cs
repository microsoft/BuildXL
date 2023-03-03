// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    /// A related set of read accesses to a cache with support for multi-level GetSelectors.
    /// </summary>
    public interface IMemoizationSessionWithLevelSelectors : IMemoizationSession, ILevelSelectorsProvider
    {
    }

    /// <summary>
    /// A related set of read accesses to a cache with support for multi-level GetSelectors.
    /// </summary>
    public interface ILevelSelectorsProvider
    {
        /// <summary>
        /// Gets known selectors for a given weak fingerprint for a given "level".
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="IMemoizationSession.GetSelectors"/>, this method is RPC friendly.
        /// </remarks>
        Task<Result<LevelSelectors>> GetLevelSelectorsAsync(
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cts,
            int level);
    }
}
