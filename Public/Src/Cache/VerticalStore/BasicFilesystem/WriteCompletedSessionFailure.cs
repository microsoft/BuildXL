// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.BasicFilesystem
{
    /// <summary>
    /// Failure describing that the completed session could not be written
    /// </summary>
    public sealed class WriteCompletedSessionFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_sessionFileName;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Create the failure, including the session file name that failed
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="sessionFileName">The session file name that failed</param>
        /// <param name="rootCause">Optional root cause exception</param>
        public WriteCompletedSessionFailure(string cacheId, string sessionFileName, Exception rootCause)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(sessionFileName != null);
            Contract.Requires(rootCause != null);

            m_cacheId = cacheId;
            m_sessionFileName = sessionFileName;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] failed to write session file [{1}]\nRoot cause: [{2}]", m_cacheId, m_sessionFileName, m_rootCause);
        }
    }
}
