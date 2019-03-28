// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.VerticalAggregator
{
    /// <summary>
    /// Failure to create vertical aggregator due to local cache being read-only
    /// </summary>
    public class VerticalCacheAggregatorNeedsWriteableLocalFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;

        /// <summary>
        /// Failure to create vertical aggregator due to local cache being read-only
        /// </summary>
        /// <param name="cacheId">The cache ID</param>
        public VerticalCacheAggregatorNeedsWriteableLocalFailure(string cacheId)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Failed to create VerticalAggregator due to read-only local cache [{0}]", m_cacheId);
        }
    }
}
