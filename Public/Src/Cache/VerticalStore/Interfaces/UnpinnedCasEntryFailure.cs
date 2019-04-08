// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Error that is returned when an attempt to access a CasHash
    /// value is made that had not been pinned.
    /// </summary>
    public class UnpinnedCasEntryFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly CasHash m_casHash;

        /// <summary>
        /// Attempted access to unpinned CasHash entry
        /// </summary>
        /// <param name="cacheId">Cache id where the failure was</param>
        /// <param name="casHash">The CasHash that was being accessed</param>
        public UnpinnedCasEntryFailure(string cacheId, CasHash casHash)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
            m_casHash = casHash;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Attemped to get unpinned CasHash [{0}] from cache [{1}]", m_casHash, m_cacheId);
        }
    }
}
