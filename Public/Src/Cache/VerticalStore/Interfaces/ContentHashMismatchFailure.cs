// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure describing that content was found in the CAS but its hash did not match the expected value.
    /// </summary>
    public sealed class ContentHashMismatchFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly CasHash m_casHash;

        /// <summary>
        /// Create the failure, including the CasHash that was expected
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="casHash">The CasHash that was expected but did not match the downloaded content</param>
        public ContentHashMismatchFailure(string cacheId, CasHash casHash)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
            m_casHash = casHash;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] downloaded content for CasHash [{1}] but the hash of the downloaded bytes did not match", m_cacheId, m_casHash);
        }
    }
}
