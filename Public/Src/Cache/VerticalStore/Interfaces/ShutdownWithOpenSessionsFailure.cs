// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure returned when a cache gets shutdown with open sessions
    /// </summary>
    public class ShutdownWithOpenSessionsFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_sessionIds;

        /// <summary>
        /// Shutdown was called with active named sessions
        /// </summary>
        /// <param name="cacheId">CacheId</param>
        /// <param name="sessionIds">The session IDs that are active</param>
        public ShutdownWithOpenSessionsFailure(string cacheId, IEnumerable<object> sessionIds)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(sessionIds != null);

            m_cacheId = cacheId;
            m_sessionIds = string.Join(", ", sessionIds);
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache: {0} - Shutdown with active sessions: {1}", m_cacheId, m_sessionIds);
        }
    }
}
