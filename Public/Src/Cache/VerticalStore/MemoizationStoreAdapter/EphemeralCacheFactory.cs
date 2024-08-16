// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter;


/// <summary>
/// Cache factory for a ephemeral cache.
/// </summary>
public class EphemeralCacheFactory : BlobCacheFactoryBase<EphemeralCacheConfig>, ICacheFactory
{
    private const string ReadOnlyModeError = "Ephemeral cache factory does not support read-only mode.";

    /// <inheritdoc/>
    public override Task<Possible<ICache, Failure>> InitializeCacheAsync(EphemeralCacheConfig configuration)
    {
        // TODO: The ephemeral factory does not support read-only mode, but this could be added in the future.
        // Ideally we shouldn't have this option in the ephemeral configuration object at all, but today there is a subclass relationship
        // with the BlobCacheConfig (where readonly mode is supported) that it is not easy to break.
        // Observe that ValidateConfiguration method (part of ICacheFactory) is today only called in the context of CloudBuild, so
        // it won't catch this on non-CB builds.
        if (configuration.IsReadOnly)
        {
            return Task.FromResult(new Possible<ICache, Failure>(new Failure<string>(ReadOnlyModeError)));
        }

        return base.InitializeCacheAsync(configuration);
    }

    internal override async Task<MemoizationStore.Interfaces.Caches.ICache> CreateCacheAsync(ILogger logger, EphemeralCacheConfig configuration)
    {
        var tracingContext = new Context(logger);
        var context = new OperationContext(tracingContext);

        ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.Configuration factoryConfiguration;

        var machineLocation = MachineLocation.Create(Environment.MachineName, GrpcConstants.DefaultEphemeralEncryptedGrpcPort);
        var leaderLocation = MachineLocation.Create(configuration.LeaderMachineName, GrpcConstants.DefaultEphemeralEncryptedGrpcPort);

        var rootPath = new AbsolutePath(configuration.CacheRootPath);
        context.TracingContext.Info($"Creating ephemeral cache. DatacenterWide=[{configuration.DatacenterWide}] Root=[{rootPath}] Machine=[{machineLocation}] Leader=[{leaderLocation}] Universe=[{configuration.Universe}] Namespace=[{configuration.Namespace}] RetentionPolicyInDays=[{configuration.RetentionPolicyInDays}] UseContentServer=[{configuration.UseContentServer}] ContentServerPort=[{configuration.GrpcPort}]", nameof(EphemeralCacheFactory));

        var persistentCache = BlobCacheFactory.CreateCache(logger, configuration);

        if (configuration.DatacenterWide)
        {
            factoryConfiguration = new ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.DatacenterWideCacheConfiguration()
            {
                Location = machineLocation,
                Leader = leaderLocation,
                RootPath = rootPath,
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

        factoryConfiguration.DeleteLocalOnShutdown = configuration.DeleteOnClose;

        var backingContentStore = configuration.UseContentServer
            ? CreateGrpcContentStore(configuration, logger)
            : null;

        return (await ContentStore.Distributed.Ephemeral.EphemeralCacheFactory.CreateAsync(context, factoryConfiguration, persistentCache, backingLocalContentStore: backingContentStore)).Cache;
    }

    private static IContentStore CreateGrpcContentStore(EphemeralCacheConfig configuration, ILogger logger)
    {
        var grpcPort = (int)configuration.GrpcPort;
        if (grpcPort <= 0)
        {
            var factory = new MemoryMappedFileGrpcPortSharingFactory(logger, configuration.GrpcPortFileName);
            var portReader = factory.GetPortReader();
            grpcPort = portReader.ReadPort();
        }

        var rpcConfiguration = new ServiceClientRpcConfiguration(grpcPort)
        {
            GrpcCoreClientOptions = configuration.GrpcCoreClientOptions,
            UseLocalOnlyCasOperations = configuration.UseContentServerLocalCas,
        };

        ServiceClientContentStoreConfiguration serviceClientContentStoreConfiguration = new ServiceClientContentStoreConfiguration(configuration.CacheName, rpcConfiguration, configuration.ScenarioName)
        {
            RetryCount = configuration.ConnectionRetryCount,
            RetryIntervalSeconds = configuration.ConnectionRetryIntervalSeconds,
            TraceOperationStarted = configuration.GrpcTraceOperationStarted,
            GrpcEnvironmentOptions = configuration.GrpcEnvironmentOptions,
        };

        return LocalCache.CreateRpcContentStore(logger, serviceClientContentStoreConfiguration);
    }

    /// <inheritdoc />
    public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
    {
        return CacheConfigDataValidator.ValidateConfiguration<EphemeralCacheConfig>(
            cacheData,
            cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNullOrWhitespace(
                    cacheConfig.ConnectionStringEnvironmentVariableName,
                    nameof(cacheConfig.ConnectionStringEnvironmentVariableName));
                failures.AddFailureIfNullOrWhitespace(
                    cacheConfig.ConnectionStringFileEnvironmentVariableName,
                    nameof(cacheConfig.ConnectionStringFileEnvironmentVariableName));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.Universe, nameof(cacheConfig.Universe));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.Namespace, nameof(cacheConfig.Namespace));
                if (cacheConfig.IsReadOnly)
                {
                    failures.Add(new Failure<string>(ReadOnlyModeError));
                }

                return failures;
            });
    }
}
