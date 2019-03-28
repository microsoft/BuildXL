// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Error that is returned when an attempt to access a CasHash
    /// value is made that had not been pinned.
    /// </summary>
    public class UnknownSessionFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_sessionId;
        private readonly Exception m_exception;

        /// <summary>
        /// Attempted access to unpinned CasHash entry
        /// </summary>
        /// <param name="cacheId">Cache id where the failure was</param>
        /// <param name="sessionId">The session that was requested</param>
        /// <param name="e">Exception causing the failure</param>
        public UnknownSessionFailure(string cacheId, string sessionId, Exception e = null)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
            m_sessionId = sessionId;
            m_exception = e;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Attemped to get unknown session [{0}] from cache [{1}]{2}", m_sessionId, m_cacheId, m_exception?.ToString());
        }
    }
}
