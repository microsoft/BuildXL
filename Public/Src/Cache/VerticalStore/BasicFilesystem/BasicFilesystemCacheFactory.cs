// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ImplementationSupport;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

[module: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses",
    Scope = "type",
    Target = "BuildXL.Cache.BasicFilesystem.BasicFilesystemCacheFactory+Config",
    Justification = "Tool is confused - it is constructed generically")]

namespace BuildXL.Cache.BasicFilesystem
{
    /// <summary>
    /// The Cache Factory for a in-memory Cache implementation
    /// that is mainly used for testing of other elements.
    /// This implementation stores all metadata and CAS data
    /// in memory only.  (Not saving to disk)
    /// </summary>
    public class BasicFilesystemCacheFactory : ICacheFactory
    {
        // BasicFilesystemCacheFactory JSON CONFIG DATA
        // {
        //     "Assembly":"BuildXL.Cache.BasicFilesystem",
        //     "Type": "BuildXL.Cache.BasicFilesystem.BasicFilesystemCacheFactory",
        //     "CacheId":"{0}",
        //     "CacheRootPath":"{1}",
        //     "ReadOnly":{2},
        //     "StrictMetadataCasCoupling":{3},
        //     "ContentionBackoffMaxMilliseonds":{4}
        // }
        private sealed class Config
        {
            /// <summary>
            /// The Id of the cache instance
            /// </summary>
            [DefaultValue("BasicFilesystemCache")]
            public string CacheId { get; set; }

            /// <summary>
            /// The directory where the cache data is stored
            /// </summary>
            public string CacheRootPath { get; set; }

            /// <summary>
            /// A way to force a cache to be read-only even if it could write
            /// </summary>
            [DefaultValue(false)]
            public bool ReadOnly { get; set; }

            /// <summary>
            /// Flag signaling that strict metadata coupling should be used
            /// </summary>
            [DefaultValue(true)]
            public bool StrictMetadataCasCoupling { get; set; }

            [DefaultValue(true)]
            public bool IsAuthoritative { get; set; }

            /// <summary>
            /// The contention backoff maximum time in milliseconds
            /// </summary>
            /// <remarks>
            /// Contention backoff for our metadata use which should
            /// all be rather fast.  A value of 20000ms (20 seconds)
            /// is really the sum of, at minimum, the powers of 2 up to
            /// 13+, which means that we will have done at least 40 seconds
            /// of waiting to get here.  That is a lot but contention resolution
            /// is nasty when you get to large numbers of customers.  (On
            /// the order of O(n^2) - which is why we need the backoff like this)
            /// With the randomization of the amount by which it grows, if
            /// we assume equal distribution, this means it grows by 50% rather
            /// than 100% each time so in stead of 11 loops we are looking at 22
            /// and, an average 6 seconds of total retry time.
            /// Since access to files in exclusive mode is very short, this
            /// should not be a problem except under extreme stress for
            /// the cache.  Since the backoff starts at 1 millisecond and
            /// grows at most by doubling, most conflicts will be solved within
            /// a small number of milliseconds.
            ///
            /// This is a configuration setting such that we can adjust this
            /// value without changing code but most should never configure this.
            ///
            /// Note that the cache may clamp how low or high this setting may
            /// be.  It is constrained to a short here as a way to prevent it
            /// from being set far too high.
            /// </remarks>
            [DefaultValue(20000)]
            public short ContentionBackoffMaxMilliseonds { get; set; }

            /// <summary>
            /// The default minimum age (in seconds) of an unreferenced fingerprint
            /// before it is deleted.
            /// </summary>
            /// <remarks>
            /// This value is only honored when a cache is first being created.
            /// 
            /// Once a cache is created, a configuration file in the cache will override 
            /// this value.
            /// 
            /// This is set to a value longer than a reasonable full build would take.
            /// That is, the time from the first fingerprint being written in the build
            /// until the completed session record is written, the fingerprints are
            /// logically unreferenced and thus are not held in the cache by a session.
            /// The age needs to be long enough such that the fingerprint can become
            /// referenced even in the really worst case and then some situation.
            /// This trades some delay of actually evicting data from the build cache
            /// for the lack of high cost coordination with all of the build clients.
            /// This allows the GC to runs 100% in parallel with and without handshake
            /// with any/all of the builds that may be concurrently operating.
            ///
            /// In all cases, this value must be at least as long as the time it
            /// takes to run a single GC pass too since anything smaller than that
            /// will have inconsistent views of the state of the roots for its own
            /// processing.
            ///
            /// The default value is very large relative to that above constraint.
            /// It is best to be significantly beyond the normal expected time.
            /// Lowering this time may be reasonable for smaller/simpler build
            /// environments but they usually don't store nearly as much so it does
            /// not matter nearly as much.
            /// </remarks>
            [DefaultValue(2 * 24 * 60)]
            public int DefaultFingerprintMinimumAgeMinutes { get; set; }
        }

        private static Possible<ICache, Failure> InitializeCache(ICacheConfigData cacheData, Guid activityId)
        {
            using (var eventing = new InitializeCacheActivity(BasicFilesystemCache.EventSource, activityId, typeof(BasicFilesystemCache).FullName))
            {
                eventing.Start(cacheData);

                var possibleCacheConfig = cacheData.Create<Config>();
                if (!possibleCacheConfig.Succeeded)
                {
                    return eventing.StopFailure(possibleCacheConfig.Failure);
                }

                Config cacheConfig = possibleCacheConfig.Result;

                // instantiate new BasicFilesystemCache
                try
                {
                    return eventing.Returns(new BasicFilesystemCache(
                        cacheConfig.CacheId,
                        cacheConfig.CacheRootPath,
                        cacheConfig.ReadOnly,
                        cacheConfig.StrictMetadataCasCoupling,
                        cacheConfig.IsAuthoritative,
                        cacheConfig.ContentionBackoffMaxMilliseonds,
                        cacheConfig.DefaultFingerprintMinimumAgeMinutes));
                }
                catch (Exception e)
                {
                    return eventing.StopFailure(new CacheConstructionFailure(cacheConfig.CacheId, e));
                }
            }
        }

        /// <inheritdoc />
        public Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId)
        {
            Contract.Requires(cacheData != null);
            return Task.FromResult(InitializeCache(cacheData, activityId));
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();

                failures.AddFailureIfNull(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNull(cacheConfig.CacheRootPath, nameof(cacheConfig.CacheRootPath));

                if (cacheConfig.IsAuthoritative && !cacheConfig.StrictMetadataCasCoupling)
                {
                    failures.Add(new IncorrectJsonConfigDataFailure($"If {nameof(cacheConfig.IsAuthoritative)} is enabled, {nameof(cacheConfig.StrictMetadataCasCoupling)} must be enabled as well."));
                }

                return failures;
            });
        }
    }
}
