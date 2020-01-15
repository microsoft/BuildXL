// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure describing that a CasHash was asked to replace an entry with mixed SinglePhaseNonDeterministic content
    /// </summary>
    public sealed class SinglePhaseMixingFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;

        /// <summary>
        /// Create the failure, including the CasHash that failed
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        public SinglePhaseMixingFailure(string cacheId)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}]: Can not mix SinglePhaseNonDeterministic with other CacheDeterminism forms", m_cacheId);
        }
    }
}
