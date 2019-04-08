// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure when trying to start a session that already exists
    /// </summary>
    public class DuplicateSessionIdFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_sessionId;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Failure to create session due to duplicated ID
        /// </summary>
        /// <param name="cacheId">The cache ID</param>
        /// <param name="sessionId">The attempted session ID</param>
        /// <param name="rootCause">The root cause exception (optional)</param>
        public DuplicateSessionIdFailure(string cacheId, string sessionId, Exception rootCause = null)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(sessionId != null);

            m_cacheId = cacheId;
            m_sessionId = sessionId;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            if (m_rootCause != null)
            {
                return string.Format(CultureInfo.InvariantCulture, "Tried to create duplicate session [{0}] in cache [{1}]\nRoot Cause: {2}", m_sessionId, m_cacheId, m_rootCause);
            }

            return string.Format(CultureInfo.InvariantCulture, "Tried to create duplicate session [{0}] in cache [{1}]", m_sessionId, m_cacheId);
        }
    }
}
