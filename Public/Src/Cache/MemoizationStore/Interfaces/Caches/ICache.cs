// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Caches
{
    /// <summary>
    ///     Standard interface for caches.
    /// </summary>
    public interface ICache : IStartupShutdown
    {
        /// <summary>
        ///     Gets the unique GUID for the given cache.
        /// </summary>
        /// <remarks>
        ///     It will be used also for storing who provided
        ///     the ViaCache determinism for memoization data.
        /// </remarks>
        Guid Id { get; }

        /// <summary>
        ///     Create a new session that can only read.
        /// </summary>
        CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin);

        /// <summary>
        ///     Create a new session that can change the cache.
        /// </summary>
        CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin);

        /// <summary>
        ///     Gets a current stats snapshot.
        /// </summary>
        Task<GetStatsResult> GetStatsAsync(Context context);

        /// <summary>
        ///     Asynchronously enumerates the known strong fingerprints.
        /// </summary>
        Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context);
    }
}
