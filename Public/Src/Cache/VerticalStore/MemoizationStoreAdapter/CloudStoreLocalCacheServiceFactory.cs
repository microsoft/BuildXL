// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blobs;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Cache Factory for BuildXL as a CASaaS client in CloudBuild
    /// </summary>
    /// <remarks>
    /// This is the class responsible for creating the BuildXL to Cache adapter in the CloudBuild environment. It
    /// configures the cache client for the BuildXL process.
    /// </remarks>
    public class CloudStoreLocalCacheServiceFactory : ICacheFactory
    {
        private sealed class Config
        {
            /// <summary>
            /// The Id of the cache instance.
            /// </summary>
            [DefaultValue(typeof(CacheId))]
            public CacheId CacheId { get; set; }

            /// <summary>
            /// Path to the log file for the cache.
            /// </summary>
            public string MetadataLogPath { get; set; }

            /// <summary>
            /// Name of one of the named caches owned by CASaaS.
            /// </summary>
            public string CacheName { get; set; }

            /// <summary>
            /// How many seconds each call should wait for a CASaaS connection before retrying.
            /// </summary>
            [DefaultValue(5)]
            public uint ConnectionRetryIntervalSeconds { get; set; }

            /// <summary>
            /// How many times each call should retry connecting to CASaaS before timing out.
            /// </summary>
            [DefaultValue(12)]
            public uint ConnectionRetryCount { get; set; }

            /// <summary>
            /// A custom scenario to connect to for the CAS service.
            /// </summary>
            [DefaultValue(null)]
            public string ScenarioName { get; set; }

            /// <summary>
            /// A custom scenario to connect to for the CAS service. If set, overrides GrpcPortFileName.
            /// </summary>
            [DefaultValue(0)]
            public uint GrpcPort { get; set; }

            /// <summary>
            /// Custom name of the memory-mapped file to read from to find the GRPC port used for the CAS service.
            /// </summary>
            [DefaultValue(null)]
            public string GrpcPortFileName { get; set; }

            /// <nodoc />
            [DefaultValue(false)]
            public bool GrpcTraceOperationStarted { get; set; }

            /// <summary>
            /// Duration to wait for exclusive access to the cache directory before timing out.
            /// </summary>
            [DefaultValue(0)]
            public uint LogFlushIntervalSeconds { get; set; }

            [DefaultValue(false)]
            public bool ReplaceExistingOnPlaceFile { get; set; }

            /// <summary>
            /// Path to the root of VFS cas
            /// </summary>
            [DefaultValue(null)]
            public string VfsCasRoot { get; set; }

            /// <summary>
            /// Indicates whether symlinks should be used to specify VFS files
            /// </summary>
            [DefaultValue(true)]
            public bool VfsUseSymlinks { get; set; } = true;

            /// <nodoc />
            [DefaultValue(null)]
            public GrpcEnvironmentOptions GrpcEnvironmentOptions { get; set; }

            /// <nodoc />
            [DefaultValue(null)]
            public GrpcCoreClientOptions GrpcCoreClientOptions { get; set; }

            [DefaultValue(false)]
            public bool EnableBlobL3Publishing { get; set; }

            [DefaultValue("BlobCacheFactoryConnectionString")]
            public string BlobL3PublishingPatEnvironmentVariable { get; set; }
        }

        /// <inheritdoc />
        public async Task<Possible<Interfaces.ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId, ICacheConfiguration cacheConfiguration = null)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<Config>();
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            Config cacheConfig = possibleCacheConfig.Result;

            try
            {
                Contract.Assert(cacheConfig.CacheName != null);
                var logPath = new AbsolutePath(cacheConfig.MetadataLogPath);
                var logger = new DisposeLogger(() => new EtwFileLog(logPath.Path, cacheConfig.CacheId), cacheConfig.LogFlushIntervalSeconds);

                logger.Debug($"Creating CASaaS backed LocalCache using cache name: {cacheConfig.CacheName}");

                var cache = CreateCacheAdapter(cacheConfig, logPath, logger);

                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    logger.Error($"Error while initializing the cache [{cacheConfig.CacheId}]. Failure: {startupResult.Failure}");
                    cache.Dispose();

                    return startupResult.Failure;
                }

                logger.Debug("Successfully started CloudStoreLocalCacheService client.");
                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(cacheConfig.CacheId, e);
            }
        }

        private static MemoizationStoreAdapterCache CreateCacheAdapter(Config configuration, AbsolutePath logPath, ILogger logger)
        {
            var clientConfiguration = CreateClientConfiguration(configuration, logger);

            MemoizationStore.Interfaces.Caches.ICache cache;
            if (configuration.EnableBlobL3Publishing)
            {
                PublishingCacheConfiguration publishingConfiguration = new AzureBlobStoragePublishingCacheConfiguration();
                string personalAccessToken = Environment.GetEnvironmentVariable(configuration.BlobL3PublishingPatEnvironmentVariable);
                if (string.IsNullOrEmpty(personalAccessToken))
                {
                    logger.Error("Attempt to use L3 cache without a personal access token. Moving forward anyways...");
                }

                cache = LocalCache.CreatePublishingRpcCache(
                    logger,
                    clientConfiguration,
                    publishingConfiguration,
                    personalAccessToken);
            }
            else
            {
                cache = LocalCache.CreateRpcCache(logger, clientConfiguration);
            }

            var statisticsFilePath = new AbsolutePath(logPath.Path + ".stats");

            return new MemoizationStoreAdapterCache(configuration.CacheId, cache, logger, statisticsFilePath, configuration.ReplaceExistingOnPlaceFile);
        }

        private static ServiceClientContentStoreConfiguration CreateClientConfiguration(Config configuration, ILogger logger)
        {
            var grpcPort = (int)configuration.GrpcPort;
            if (grpcPort <= 0)
            {
                var factory = new MemoryMappedFileGrpcPortSharingFactory(logger, configuration.GrpcPortFileName);
                var portReader = factory.GetPortReader();
                grpcPort = portReader.ReadPort();
            }

            var rpcConfiguration = new ServiceClientRpcConfiguration()
            {
                GrpcPort = grpcPort,
                GrpcCoreClientOptions = configuration.GrpcCoreClientOptions,
            };

            return new ServiceClientContentStoreConfiguration(configuration.CacheName, rpcConfiguration, configuration.ScenarioName)
            {
                RetryCount = configuration.ConnectionRetryCount,
                RetryIntervalSeconds = configuration.ConnectionRetryIntervalSeconds,
                TraceOperationStarted = configuration.GrpcTraceOperationStarted,
                GrpcEnvironmentOptions = configuration.GrpcEnvironmentOptions,
            };
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheName, nameof(cacheConfig.CacheName));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.MetadataLogPath, nameof(cacheConfig.MetadataLogPath));
                return failures;
            });
        }
    }
}
