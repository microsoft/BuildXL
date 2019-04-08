// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure describing that a CasHash could not produce the given file
    /// </summary>
    public sealed class ProduceFileFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly CasHash m_casHash;
        private readonly string m_filename;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Create the failure, including the CasHash and the target filename that failed
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="casHash">The CasHash that failed</param>
        /// <param name="filename">The filename that failed</param>
        /// <param name="rootCause">Optional root cause exception</param>
        public ProduceFileFailure(string cacheId, CasHash casHash, string filename, Exception rootCause = null)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(filename != null);

            m_cacheId = cacheId;
            m_casHash = casHash;
            m_filename = filename;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            if (m_rootCause != null)
            {
                return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] could not produce file [{1}] from the CasHash entry [{2}]\nRoot cause: [{3}]", m_cacheId, m_filename, m_casHash, m_rootCause);
            }

            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] could not produce file [{1}] from the CasHash entry [{2}]", m_cacheId, m_filename, m_casHash);
        }
    }
}
