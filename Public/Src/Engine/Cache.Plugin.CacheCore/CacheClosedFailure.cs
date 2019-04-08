// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Error that is returned when a non-read-only operation is attempted against a cache
    /// session has already been closed (<see cref="ICacheReadOnlySession.IsClosed"/>.
    /// </summary>
    public sealed class CacheClosedFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_cacheSessionId;
        private readonly string m_operationThatRequiresOpenCacheSession;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="cacheId">Cache id where the failure was</param>
        /// <param name="cacheSessionId">Id of the corresponding session.</param>
        /// <param name="operationThatRequiresOpenCacheSession">The operation that requires the cache</param>
        public CacheClosedFailure(string cacheId, string cacheSessionId, string operationThatRequiresOpenCacheSession)
        {
            m_cacheId = cacheId;
            m_cacheSessionId = cacheSessionId;
            m_operationThatRequiresOpenCacheSession = operationThatRequiresOpenCacheSession;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Attemped to execute {0} against a closed cache session [{1}] (with cache [{2}]).",
                m_operationThatRequiresOpenCacheSession,
                m_cacheSessionId,
                m_cacheId);
        }
    }
}
