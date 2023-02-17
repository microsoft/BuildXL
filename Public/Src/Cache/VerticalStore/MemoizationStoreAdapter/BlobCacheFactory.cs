// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Cache Factory for BuildXL as a cache user for dev machines using Azure blob
    /// </summary>
    /// <remarks>
    /// This is the class responsible for creating the BuildXL to Cache adapter in the Selfhost builds. It configures
    /// the cache on the BuildXL process.
    ///
    /// Current limitations while we flesh things out:
    /// 1) APIs around tracking named sessions are not implemented
    /// </remarks>
    public partial class BlobCacheFactory : ICacheFactory
    {
        /// <summary>
        /// Configuration for <see cref="MemoizationStoreCacheFactory"/>.
        /// </summary>
        public sealed class Config
        {
            /// <summary>
            /// The Id of the cache instance
            /// </summary>
            [DefaultValue(typeof(CacheId))]
            public CacheId CacheId { get; set; }

            /// <nodoc />
            [DefaultValue("BlobCacheFactoryConnectionString")]
            public string ConnectionStringEnvironmentVariableName { get; set; }

            /// <nodoc />
            [DefaultValue("default")]
            public string Universe { get; set; }

            /// <nodoc />
            [DefaultValue("default")]
            public string Namespace { get; set; }

            /// <summary>
            /// Path to the log file for the cache.
            /// </summary>
            [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
            public string CacheLogPath { get; set; }

            /// <summary>
            /// Duration to wait for exclusive access to the cache directory before timing out.
            /// </summary>
            [DefaultValue(0)]
            public uint LogFlushIntervalSeconds { get; set; }

            /// <nodoc />
            public Config()
            {
                CacheId = new CacheId("BlobCache");
            }
        }

        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(
            ICacheConfigData cacheData,
            Guid activityId,
            ICacheConfiguration cacheConfiguration = null)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<Config>();
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            return await InitializeCacheAsync(possibleCacheConfig.Result);
        }

        /// <summary>
        /// Create cache using configuration
        /// </summary>
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(Config configuration)
        {
            Contract.Requires(configuration != null);

            try
            {
                var logPath = new AbsolutePath(configuration.CacheLogPath);

                var cache = new MemoizationStoreAdapterCache(
                    cacheId: configuration.CacheId,
                    innerCache: CreateCache(configuration),
                    logger: new DisposeLogger(() => new EtwFileLog(logPath.Path, configuration.CacheId), configuration.LogFlushIntervalSeconds),
                    statsFile: new AbsolutePath(logPath.Path + ".stats"));

                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    return startupResult.Failure;
                }

                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(configuration.CacheId, e);
            }
        }

        private static MemoizationStore.Interfaces.Caches.ICache CreateCache(Config configuration)
        {
            var connectionString = Environment.GetEnvironmentVariable(configuration.ConnectionStringEnvironmentVariableName);
            Contract.Assert(!string.IsNullOrEmpty(connectionString), $"Can't find a connection string in environment variable '{configuration.ConnectionStringEnvironmentVariableName}'.");
            var factoryConfiguration = new AzureBlobStorageCacheFactory.Configuration(
                Credentials: new ContentStore.Interfaces.Secrets.AzureBlobStorageCredentials(connectionString),
                Universe: configuration.Universe,
                Namespace: configuration.Namespace,
                StorageInteractionTimeout: TimeSpan.FromHours(1),
                MetadataPinElisionDuration: TimeSpan.FromDays(1));

            return AzureBlobStorageCacheFactory.Create(factoryConfiguration);
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.ConnectionStringEnvironmentVariableName, nameof(cacheConfig.ConnectionStringEnvironmentVariableName));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.Universe, nameof(cacheConfig.Universe));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.Namespace, nameof(cacheConfig.Namespace));

                if (!string.IsNullOrEmpty(cacheConfig.ConnectionStringEnvironmentVariableName))
                {
                    failures.AddFailureIfNullOrWhitespace(Environment.GetEnvironmentVariable(cacheConfig.ConnectionStringEnvironmentVariableName), $"GetEnvironmentVariable('{cacheConfig.ConnectionStringEnvironmentVariableName}')");
                }

                return failures;
            });
        }
    }
}
