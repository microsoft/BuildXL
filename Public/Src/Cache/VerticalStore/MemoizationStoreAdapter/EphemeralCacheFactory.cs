// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter;

/// <summary>
/// Cache factory for a ephemeral cache.
/// </summary>
public class EphemeralCacheFactory : ICacheFactory
{
    /// <summary>
    /// Configuration for <see cref="MemoizationStoreCacheFactory"/>.
    /// </summary>
    public sealed class FactoryConfiguration : BlobCacheFactory.BlobCacheConfig
    {
        /// <summary>
        /// The Id of the cache instance
        /// </summary>
        [DefaultValue(typeof(CacheId))]
        public CacheId CacheId { get; set; }

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
        if (string.IsNullOrEmpty(configuration.Universe))
        {
            configuration.Universe = "default";
        }

        if (string.IsNullOrEmpty(configuration.Namespace))
        {
            configuration.Namespace = "default";
        }

        try
        {
            var logPath = new AbsolutePath(configuration.CacheLogPath);

            var failures = new List<Failure>();

            var logger = new DisposeLogger(() => new EtwFileLog(logPath.Path, configuration.CacheId), configuration.LogFlushIntervalSeconds);
            var cache = new MemoizationStoreAdapterCache(
                cacheId: configuration.CacheId,
                innerCache: await CreateEphemeralCache(logger, configuration),
                logger: logger,
                statsFile: new AbsolutePath(logPath.Path + ".stats"),
                implicitPin: ImplicitPin.None,
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

        var machineLocation = MachineLocation.Create(Environment.MachineName, GrpcConstants.DefaultEphemeralGrpcPort);
        var leaderLocation = MachineLocation.Create(configuration.LeaderMachineName, GrpcConstants.DefaultEphemeralGrpcPort);
        var rootPath = new AbsolutePath(configuration.CacheRootPath);
        context.TracingContext.Info($"Creating ephemeral cache. Root=[{rootPath}] Machine=[{machineLocation}] Leader=[{leaderLocation}] Universe=[{configuration.Universe}] Namespace=[{configuration.Namespace}] RetentionPolicyInDays=[{configuration.RetentionPolicyInDays}]", nameof(EphemeralCacheFactory));

        if (configuration.DatacenterWide)
        {
            var accounts = BlobCacheFactory.LoadAzureCredentials(configuration);
            var sorted = ShardingScheme.SortAccounts(accounts.Keys.ToList());
            var credentials = accounts[sorted.First()];

            factoryConfiguration = new ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.DatacenterWideCacheConfiguration()
            {
                Location = machineLocation,
                Leader = leaderLocation,
                RootPath = rootPath,
                StorageCredentials = credentials,
                MaxCacheSizeMb = configuration.CacheSizeMb,
                Universe = configuration.Universe,
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

        var persistentCache = BlobCacheFactory.CreateCache(logger, configuration);
        return await ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.CreateAsync(context, factoryConfiguration, persistentCache);
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
