// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    ///     Simple wrapper around <see cref="ICacheSession"/> which ensures that the session is
    ///     not closed (<see cref="ICacheReadOnlySession.IsClosed"/>) before every access.
    /// </summary>
    public sealed class PossiblyOpenCacheSession
    {
        private readonly ICacheSession m_cache;

        /// <nodoc />
        public PossiblyOpenCacheSession(ICacheSession cache)
        {
            Contract.Requires(cache != null);

            m_cache = cache;
        }

        /// <summary>
        ///     Returns the wrapped cache session only if it is not closed; otherwise returns
        ///     <see cref="CacheClosedFailure"/>.
        /// </summary>
        /// <param name="callerName">Optional name of the caller (used for error message only)</param>
        public Possible<ICacheSession, Failure> Get(string callerName = null)
        {
            return m_cache.IsClosed
                ? new CacheClosedFailure(m_cache.CacheId, m_cache.CacheSessionId, callerName ?? "<unknown>")
                : Possible.Create(m_cache);
        }
    }
}
