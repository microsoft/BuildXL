// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Failure describing that <see cref="BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory.BlobCacheConfig.RetentionPolicyInDays"/> was not set
    /// </summary>
    internal class RetentionDaysNotSetFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;

        /// <nodoc/>
        public RetentionDaysNotSetFailure(string cacheId)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
        }

        /// <nodoc/>
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "'RetentionPolicyInDays' was not set for cache [{0}]. This can introduce a degradation in performance when pinning content. " +
                "Add an entry with this value in the cache configuration file to reflect the number of days the storage account will retain blobs before deleting (or soft deleting) them based on last access time.", m_cacheId);
        }
    }
}
