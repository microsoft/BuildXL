// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure describing that a CasHash could not produce a stream
    /// </summary>
    public sealed class ProduceStreamFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly CasHash m_casHash;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Create the failure, including the CasHash that failed
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="casHash">The CasHash that failed</param>
        /// <param name="rootCause">Optional root cause exception</param>
        public ProduceStreamFailure(string cacheId, CasHash casHash, Exception rootCause = null)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
            m_casHash = casHash;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            if (m_rootCause != null)
            {
                return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] could not produce stream from the CasHash entry [{1}]\nRoot cause: [{2}]", m_cacheId, m_casHash, m_rootCause);
            }

            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] could not produce stream from the CasHash entry [{1}]", m_cacheId, m_casHash);
        }
    }
}
