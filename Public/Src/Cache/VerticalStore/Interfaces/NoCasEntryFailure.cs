// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure describing that a CasHash was not available
    /// </summary>
    public sealed class NoCasEntryFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly CasHash m_casHash;

        /// <summary>
        /// Create the failure, including the CasHash that was not found
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="casHash">The CasHash that failed</param>
        public NoCasEntryFailure(string cacheId, CasHash casHash)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
            m_casHash = casHash;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] could not locate the CasHash entry [{1}]", m_cacheId, m_casHash);
        }
    }
}
