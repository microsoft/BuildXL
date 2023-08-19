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
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.MemoizationStoreAdapter;

/// <summary>
/// Cache factory for a ephemeral cache.
/// </summary>
public class EphemeralCacheFactory : ICacheFactory
{
    /// <summary>
    /// Configuration for <see cref="MemoizationStoreCacheFactory"/>.
    /// </summary>
    public sealed class FactoryConfiguration
    {
        /// <summary>
        /// The Id of the cache instance
        /// </summary>
        [DefaultValue(typeof(CacheId))]
        public CacheId CacheId { get; set; }

        /// <nodoc />
        [DefaultValue("EphemeralCacheConnectionString")]
        public string ManagementConnectionStringEnvironmentVariableName { get; set; }

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

        /// <summary>
        /// The configured number of days the storage account will retain blobs before deleting (or soft deleting) them based
        /// on last access time. If content and metadata have different retention policies, the shortest retention period is expected here.
        /// </summary>
        /// <remarks>
        /// By setting this value to reflect the storage account life management configuration policy, pin operations can be optimized.
        /// If unset, pin operations will likely be costlier. If the value is set to a number larger than the storage account policy, that
        /// can lead to build failures.
        /// 
        /// When enabled (a non-zero value), every time that a content hash list is stored, a last upload time is associated to it and stored as well.
        /// This last upload time is deemed very close to the one used for storing all the corresponding content for that content hash list
        /// (since typically that's the immediate step prior to storing the fingerprint). Whenever a content hash list is retrieved and has a last upload
        /// time associated to it, the metadata store notifies the cache of it. The cache then uses that information to determine whether the content
        /// associated to that fingerprint can be elided, based on the provided configured blob retention policy of the blob storage account.
        /// </remarks>
        [DefaultValue(0)]
        public int RetentionPolicyInDays { get; set; }

        /// <nodoc />
        public string CacheRootPath { get; set; }

        /// <nodoc />
        public string LeaderMachineName { get; set; }

        /// <summary>
        /// The replication domain of the ephemeral cache. When the cache is build-wide (flag is false), workers can
        /// get cache hits from other workers in the same build. When it's datacenter-wide, workers can get cache hits
        /// from any other machine in the "datacenter".
        ///
        /// The datacenter mode requires a storage account that all machines can access, as well as the ability to
        /// perform P2P copies between machines even across different builds.
        /// </summary>
        public bool DatacenterWide { get; set; } = false;

        /// <nodoc />
        public uint CacheSizeMb { get; set; }

        /// <nodoc />
        public FactoryConfiguration()
        {
            CacheId = new CacheId("EphemeralCache");
        }
    }

    /// <inheritdoc />
    public async Task<Possible<ICache, Failure>> InitializeCacheAsync(
        ICacheConfigData cacheData,
        Guid activityId,
        ICacheConfiguration cacheConfiguration = null)
    {
        Contract.Requires(cacheData != null);

        var possibleCacheConfig = cacheData.Create<FactoryConfiguration>();
        if (!possibleCacheConfig.Succeeded)
        {
            return possibleCacheConfig.Failure;
        }

        return await InitializeCacheAsync(possibleCacheConfig.Result);
    }

    /// <summary>
    /// Create cache using configuration
    /// </summary>
    public async Task<Possible<ICache, Failure>> InitializeCacheAsync(FactoryConfiguration configuration)
    {
        Contract.Requires(configuration != null);

        try
        {
            var logPath = new AbsolutePath(configuration.CacheLogPath);

            // If the retention period is not set, this is not a blocker for constructing the cache, but performance can be degraded. Report it.
            var failures = new List<Failure>();
            if (configuration.RetentionPolicyInDays == 0)
            {
                failures.Add(new RetentionDaysNotSetFailure(configuration.CacheId));
            }

            var logger = new DisposeLogger(() => new EtwFileLog(logPath.Path, configuration.CacheId), configuration.LogFlushIntervalSeconds);
            var cache = new MemoizationStoreAdapterCache(
                cacheId: configuration.CacheId,
                innerCache: await CreateEphemeralCache(logger, configuration),
                logger: logger,
                statsFile: new AbsolutePath(logPath.Path + ".stats"),
                precedingStateDegradationFailures: failures);

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

    private static async Task<MemoizationStore.Interfaces.Caches.ICache> CreateEphemeralCache(ILogger logger, FactoryConfiguration configuration)
    {
        var tracingContext = new Context(logger);
        var context = new OperationContext(tracingContext);

        ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.Configuration factoryConfiguration;

        var machineLocation = MachineLocation.Create(Environment.MachineName, GrpcConstants.DefaultGrpcPort);
        var leaderLocation = MachineLocation.Create(configuration.LeaderMachineName, GrpcConstants.DefaultGrpcPort);
        var rootPath = new AbsolutePath(configuration.CacheRootPath);
        context.TracingContext.Info($"Creating ephemeral cache. Root=[{rootPath}] Machine=[{machineLocation}] Leader=[{leaderLocation}]", nameof(EphemeralCacheFactory));

        if (configuration.DatacenterWide)
        {
            var connectionString = Environment.GetEnvironmentVariable(configuration.ManagementConnectionStringEnvironmentVariableName);
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException($"Can't find a connection string in environment variable '{configuration.ManagementConnectionStringEnvironmentVariableName}'.");
            }
            var credentials = new SecretBasedAzureStorageCredentials(connectionString);

            factoryConfiguration = new ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.DatacenterWideCacheConfiguration()
            {
                Location = machineLocation,
                Leader = leaderLocation,
                RootPath = rootPath,
                StorageCredentials = credentials,
                MaxCacheSizeMb = configuration.CacheSizeMb,
            };
        }
        else
        {
            factoryConfiguration = new ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.BuildWideCacheConfiguration()
            {
                Location = machineLocation,
                Leader = leaderLocation,
                RootPath = rootPath,
                MaxCacheSizeMb = configuration.CacheSizeMb,
            };
        }

        var persistentCache = CreateBlobCache(configuration);
        return await Cache.ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.CreateAsync(context, factoryConfiguration, persistentCache);
    }

    private static MemoizationStore.Interfaces.Caches.IFullCache CreateBlobCache(FactoryConfiguration configuration)
    {
        var connectionString = Environment.GetEnvironmentVariable(configuration.ConnectionStringEnvironmentVariableName);
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException($"Can't find a connection string in environment variable '{configuration.ConnectionStringEnvironmentVariableName}'.");
        }

        var credentials = new SecretBasedAzureStorageCredentials(connectionString);
        var accountName = BlobCacheStorageAccountName.Parse(credentials.GetAccountName());

        var factoryConfiguration = new AzureBlobStorageCacheFactory.Configuration(
            ShardingScheme: new ShardingScheme(ShardingAlgorithm.SingleShard, new List<BlobCacheStorageAccountName> { accountName }),
            Universe: configuration.Universe,
            Namespace: configuration.Namespace,
            RetentionPolicyInDays: configuration.RetentionPolicyInDays);

        return AzureBlobStorageCacheFactory.Create(factoryConfiguration, new StaticBlobCacheSecretsProvider(credentials));
    }

    /// <inheritdoc />
    public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
    {
        return CacheConfigDataValidator.ValidateConfiguration<FactoryConfiguration>(
            cacheData,
            cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNullOrWhitespace(
                    cacheConfig.ConnectionStringEnvironmentVariableName,
                    nameof(cacheConfig.ConnectionStringEnvironmentVariableName));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.Universe, nameof(cacheConfig.Universe));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.Namespace, nameof(cacheConfig.Namespace));

                if (!string.IsNullOrEmpty(cacheConfig.ConnectionStringEnvironmentVariableName))
                {
                    failures.AddFailureIfNullOrWhitespace(
                        Environment.GetEnvironmentVariable(cacheConfig.ConnectionStringEnvironmentVariableName),
                        $"GetEnvironmentVariable('{cacheConfig.ConnectionStringEnvironmentVariableName}')");
                }

                return failures;
            });
    }
}
