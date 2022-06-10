// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ImplementationSupport;
using BuildXL.Cache.InMemory;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.VerticalAggregator
{
    /// <summary>
    /// Factory for a Cache that aggregates multiple levels and moves content as needed.
    /// </summary>
    public class VerticalCacheAggregatorFactory : ICacheFactory
    {
        internal const string RemoteConstructionFailureWarning = "Remote cache construction failed, falling back to local cache {0} only.";

        // VerticalCacheAggregatorFactory JSON CONFIG DATA
        // {
        //     "Assembly":"BuildXL.Cache.VerticalAggregator",
        //     "Type": "BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory",
        //     "RemoteIsReadOnly":{0},
        //     "PreFetchCasData":{1},
        //     "RemoteCache":{2},
        //     "LocalCache":{3},
        //     "WriteThroughCasData":{4},
        //     "FailIfRemoteFails":{5}
        // }
        private sealed class Config : IVerticalAggregatorConfig
        {
            /// <summary>
            /// The local Cache configuration
            /// </summary>
            public ICacheConfigData LocalCache { get; set; }

            /// <summary>
            /// The remote Cache configuration
            /// </summary>
            public ICacheConfigData RemoteCache { get; set; }

            /// <inheritdoc />
            [DefaultValue(false)]
            public bool RemoteIsReadOnly { get; set; }

            /// <inheritdoc />
            [DefaultValue(false)]
            public bool PreFetchCasData { get; set; }

            /// <inheritdoc />
            [DefaultValue(false)]
            public bool WriteThroughCasData { get; set; }

            /// <inheritdoc />
            [DefaultValue(false)]
            public bool FailIfRemoteFails { get; set; }

            /// <inheritdoc />
            [DefaultValue(false)]
            public bool RemoteContentIsReadOnly { get; set; }

            /// <inheritdoc />
            [DefaultValue(false)]
            public bool UseLocalOnly { get; set; }

            /// <inheritdoc />
            [DefaultValue(Timeout.Infinite)]
            public int RemoteConstructionTimeoutMilliseconds { get; set; }

            /// <inheritdoc />
            [DefaultValue(false)]
            public bool RemoteIsWriteOnly { get; set; }

            /// <inheritdoc />
            [DefaultValue(false)]
            public bool SkipDeterminismRecovery { get; set; }
        }

        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId, ICacheConfiguration cacheConfiguration = null)
        {
            Contract.Requires(cacheData != null);

            using (var eventing = new InitializeCacheActivity(VerticalCacheAggregator.EventSource, activityId, typeof(VerticalCacheAggregator).FullName))
            {
                eventing.Start(cacheData);

                var possibleCacheConfig = cacheData.Create<Config>();

                if (!possibleCacheConfig.Succeeded)
                {
                    return eventing.StopFailure(possibleCacheConfig.Failure);
                }

                Config cacheAggregatorConfig = possibleCacheConfig.Result;

                // temporary
                if (cacheAggregatorConfig.PreFetchCasData == true)
                {
                    throw new NotImplementedException();
                }

                // initialize local cache
                var maybeCache = await CacheFactory.InitializeCacheAsync(cacheAggregatorConfig.LocalCache, activityId, cacheConfiguration);
                if (!maybeCache.Succeeded)
                {
                    return eventing.StopFailure(maybeCache.Failure);
                }

                ICache local = maybeCache.Result;

                if (local.IsReadOnly)
                {
                    Analysis.IgnoreResult(await local.ShutdownAsync(), justification: "Okay to ignore shutdown status");
                    return eventing.StopFailure(new VerticalCacheAggregatorNeedsWriteableLocalFailure(local.CacheId));
                }

                if (cacheAggregatorConfig.UseLocalOnly || cacheConfiguration?.UseLocalOnly == true)
                {
                    return eventing.Returns(Possible.Create(local));
                }

                maybeCache = await ConstructRemoteCacheAsync(activityId, cacheAggregatorConfig, cacheConfiguration);
                if (!maybeCache.Succeeded)
                {
                    eventing.Write(CacheActivity.CriticalDataOptions, new { RemoteCacheFailed = maybeCache.Failure });

                    if (cacheAggregatorConfig.FailIfRemoteFails)
                    {
                        Analysis.IgnoreResult(await local.ShutdownAsync(), justification: "Okay to ignore shutdown status");
                        return eventing.StopFailure(maybeCache.Failure);
                    }

                    // If the remote cache does not construct, we fall back to just the local.
                    // This is basically like a disconnected state only we are starting disconnnected
                    // and thus are just the local cache now.  We can just return the local and
                    // not add the overhead of the aggregator.
                    string failureMessage = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        RemoteConstructionFailureWarning,
                        local.CacheId);

                    // Note: Compiler is confused and needs help converting ICache to Possible here but not a few lines below.
                    return eventing.Returns(new MessageForwardingCache(new Failure[] { maybeCache.Failure.Annotate(failureMessage) }, local));
                }

                ICache remote = maybeCache.Result;
                var remoteReadCache = cacheAggregatorConfig.RemoteIsWriteOnly
                    ? new MemCache(new CacheId("ReadOnlyEmptyRemote"), strictMetadataCasCoupling: true, isauthoritative: false)
                    : null;

                try
                {
                    // instantiate new VerticalCacheAggregator
                    return eventing.Returns(new VerticalCacheAggregator(
                        local,
                        remote,
                        remoteReadCache,
                        cacheAggregatorConfig));
                }
                catch (Exception e)
                {
                    string cacheId = local.CacheId + "_" + remote.CacheId;
                    Analysis.IgnoreResult(await local.ShutdownAsync(), justification: "Okay to ignore shutdown status");
                    Analysis.IgnoreResult(await remote.ShutdownAsync(), justification: "Okay to ignore shutdown status");
                    return eventing.StopFailure(new CacheConstructionFailure(cacheId, e));
                }
            }
        }

        private static async Task<Possible<ICache, Failure>> ConstructRemoteCacheAsync(Guid activityId, Config cacheAggregatorConfig, ICacheConfiguration cacheConfiguration)
        {
            var timeout = TimeSpan.FromMilliseconds(cacheAggregatorConfig.RemoteConstructionTimeoutMilliseconds);

            try
            {
                return await TaskUtilities.WithTimeoutAsync(
                    CacheFactory.InitializeCacheAsync(cacheAggregatorConfig.RemoteCache, activityId, cacheConfiguration),
                    timeout);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (TimeoutException)
            {
                return new Failure<string>($"Remote cache construction timed out after waiting for {timeout}");
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheAggregatorConfig =>
            {
                var failures = new List<Failure>();

                failures.AddRange(
                    CacheFactory.ValidateConfig(cacheAggregatorConfig.LocalCache)
                        .Select(failure => new Failure<string>($"{nameof(cacheAggregatorConfig.LocalCache)} validation failed", failure)));

                failures.AddRange(
                    CacheFactory.ValidateConfig(cacheAggregatorConfig.RemoteCache)
                        .Select(failure => new Failure<string>($"{nameof(cacheAggregatorConfig.RemoteCache)} validation failed", failure)));

                return failures;
            });
        }
    }
}
