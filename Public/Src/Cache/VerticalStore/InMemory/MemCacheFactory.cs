// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

[module: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses",
    Scope = "type",
    Target = "BuildXL.Cache.InMemory.MemCacheFactory+Config",
    Justification = "Tool is confused - it is constructed generically")]
namespace BuildXL.Cache.InMemory
{
    /// <summary>
    /// The Cache Factory for an in-memory Cache implementation
    /// that is mainly used for testing of other elements.
    /// This implementation stores all metadata and CAS data
    /// in memory only.  (Not saving to disk)
    /// </summary>
    public class MemCacheFactory : ICacheFactory
    {
        // MemCacheFactory JSON CONFIG DATA
        // {
        //     "Assembly":"BuildXL.Cache.InMemory",
        //     "Type":"BuildXL.Cache.InMemory.MemCacheFactory",
        //     "CacheId":"{0}",
        //     "StrictMetadataCasCoupling":{1}
        // }
        private sealed class Config
        {
            /// <summary>
            /// The Id of the cache instance
            /// </summary>
            [DefaultValue("MemCache")]
            public string CacheId { get; set; }

            /// <summary>
            /// Flag signaling that strict metadata coupling should be used
            /// </summary>
            [DefaultValue(true)]
            public bool StrictMetadataCasCoupling { get; set; }

            /// <summary>
            /// Whether or not the memcache is acting an authoritative or non-authoritative cache.
            /// </summary>
            [DefaultValue(false)]
            public bool IsAuthoritative { get; set; }
        }

        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<Config>();
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            Config cacheConfig = possibleCacheConfig.Result;

            // instantiate new MemCache
            return await Task.FromResult(new MemCache(cacheConfig.CacheId, cacheConfig.StrictMetadataCasCoupling, cacheConfig.IsAuthoritative));
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData) => CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig => new Failure[] { });
    }
}
