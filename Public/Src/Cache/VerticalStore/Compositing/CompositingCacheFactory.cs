// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

[module: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses",
    Scope = "type",
    Target = "BuildXL.Cache.Compositing.CompositingCacheFactory+Config",
    Justification = "Tool is confused - it is constructed generically")]

namespace BuildXL.Cache.Compositing
{
    /// <summary>
    /// The Cache Factory for an compositing cache that will take seperate CAS and Metadata caches
    /// and blend them into a single cache facade.
    /// </summary>
    public sealed class CompositingCacheFactory : ICacheFactory
    {
        // CompositingCacheFactory JSON CONFIG DATA
        // {
        //     "Assembly":"Cache.Compositing",
        //     "Type":"BuildXL.Cache.InMemory.CompositingCacheFactory",
        //     "CacheId":"{0}",
        //     "MetadataCache":{1}
        //     "CasCache":{2}
        // }
        private sealed class Config
        {
            /// <summary>
            /// The Id of the cache instance
            /// </summary>
            [DefaultValue("CompositingCache")]
            public string CacheId { get; set; }

            /// <summary>
            /// The Metadata Cache configuration
            /// </summary>
            public ICacheConfigData MetadataCache { get; set; }

            /// <summary>
            /// The CAS Cache configuration
            /// </summary>
            public ICacheConfigData CasCache { get; set; }

            /// <summary>
            /// Flag signaling that strict metadata coupling should be used
            /// </summary>
            [DefaultValue(true)]
            public bool StrictMetadataCasCoupling { get; set; }
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

            Config compositingConfig = possibleCacheConfig.Result;

            // initialize local cache
            var maybeCache = await CacheFactory.InitializeCacheAsync(compositingConfig.MetadataCache, activityId);
            if (!maybeCache.Succeeded)
            {
                return maybeCache.Failure;
            }

            ICache metadata = maybeCache.Result;

            if (metadata.StrictMetadataCasCoupling)
            {
                Analysis.IgnoreResult(await metadata.ShutdownAsync(), justification: "Okay to ignore shutdown status");
                return new InconsistentCacheStateFailure("Must specify a non-strict metadata cache when compositing caches.");
            }

            maybeCache = await CacheFactory.InitializeCacheAsync(compositingConfig.CasCache, activityId);
            if (!maybeCache.Succeeded)
            {
                Analysis.IgnoreResult(await metadata.ShutdownAsync(), justification: "Okay to ignore shutdown status");
                return maybeCache.Failure;
            }

            ICache cas = maybeCache.Result;

            try
            {
                // instantiate new cache
                return new CompositingCache(
                    metadata,
                    cas,
                    compositingConfig.CacheId,
                    compositingConfig.StrictMetadataCasCoupling);
            }
            catch
            {
                Analysis.IgnoreResult(await metadata.ShutdownAsync(), justification: "Okay to ignore shutdown status");
                Analysis.IgnoreResult(await cas.ShutdownAsync(), justification: "Okay to ignore shutdown status");
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, compositingConfig =>
            {
                var failures = new List<Failure>();

                failures.AddRange(
                    CacheFactory.ValidateConfig(compositingConfig.MetadataCache)
                        .Select(failure => new Failure<string>($"{nameof(compositingConfig.MetadataCache)} validation failed", failure)));

                failures.AddRange(
                    CacheFactory.ValidateConfig(compositingConfig.CasCache)
                        .Select(failure => new Failure<string>($"{nameof(compositingConfig.CasCache)} validation failed", failure)));

                return failures;
            });
        }
    }
}
