// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Creates and configures LocalContentServer instances.
    /// </summary>
    public class ContentServerFactory
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly DistributedCacheServiceArguments _arguments;

        public ContentServerFactory(DistributedCacheServiceArguments arguments)
        {
            _arguments = arguments;
            _logger = arguments.Logger;
            _fileSystem = new PassThroughFileSystem(_logger);
        }

        public LocalContentServer Create()
        {
            var cacheConfig = _arguments.Configuration;
            var hostInfo = _arguments.HostInfo;

            var dataRootPath = new AbsolutePath(_arguments.DataRootPath);
            var distributedSettings = cacheConfig.DistributedContentSettings;

            cacheConfig.LocalCasSettings = cacheConfig.LocalCasSettings.FilterUnsupportedNamedCaches(hostInfo.Capabilities, _logger);

            ServiceConfiguration serviceConfiguration;
            if (distributedSettings == null || !distributedSettings.IsDistributedContentEnabled)
            {
                serviceConfiguration = CreateServiceConfiguration(_logger, _fileSystem, cacheConfig.LocalCasSettings, dataRootPath, isDistributed: false);
                var localContentServerConfiguration = CreateLocalContentServerConfiguration(cacheConfig.LocalCasSettings.ServiceSettings, serviceConfiguration);
                return new LocalContentServer(
                    _fileSystem,
                    _logger,
                    cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName,
                    path => ContentStoreFactory.CreateContentStore(_fileSystem, path, evictionAnnouncer: null, distributedEvictionSettings: default, contentStoreSettings: default, trimBulkAsync: null),
                    localContentServerConfiguration);
            }
            else
            {
                _logger.Debug($"Creating on stamp id {hostInfo.StampId} with scenario {cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName ?? string.Empty}");
                RedisContentSecretNames secretNames = distributedSettings.GetRedisConnectionSecretNames(hostInfo.StampId);

                var factory = new DistributedContentStoreFactory(
                    _arguments,
                    secretNames);

                serviceConfiguration = CreateServiceConfiguration(_logger, _fileSystem, cacheConfig.LocalCasSettings, dataRootPath, isDistributed: true);
                var localContentServerConfiguration = CreateLocalContentServerConfiguration(cacheConfig.LocalCasSettings.ServiceSettings, serviceConfiguration);
                return new LocalContentServer(
                    _fileSystem,
                    _logger,
                    cacheConfig.LocalCasSettings.ServiceSettings.ScenarioName,
                    path =>
                    {
                        var cacheSettingsByCacheName = cacheConfig.LocalCasSettings.CacheSettingsByCacheName;
                        var drivesWithContentStore = new Dictionary<string, IContentStore>(StringComparer.OrdinalIgnoreCase);

                        foreach (KeyValuePair<string, NamedCacheSettings> settings in cacheSettingsByCacheName)
                        {
                            _logger.Debug($"Using [{settings.Key}]'s settings: {settings.Value}");

                            var rootPath = cacheConfig.LocalCasSettings.GetCacheRootPathWithScenario(settings.Key);
                            drivesWithContentStore[GetRoot(rootPath)] = factory.CreateContentStore(rootPath, replicationSettings: null);
                        }

                        return new MultiplexedContentStore(drivesWithContentStore, cacheConfig.LocalCasSettings.PreferredCacheDrive);
                    },
                    localContentServerConfiguration);
            }
        }

        private static LocalServerConfiguration CreateLocalContentServerConfiguration(LocalCasServiceSettings localCasServiceSettings, ServiceConfiguration serviceConfiguration)
        {
            serviceConfiguration.GrpcPort = localCasServiceSettings.GrpcPort;
            serviceConfiguration.BufferSizeForGrpcCopies = localCasServiceSettings.BufferSizeForGrpcCopies;
            serviceConfiguration.GzipBarrierSizeForGrpcCopies = localCasServiceSettings.GzipBarrierSizeForGrpcCopies;

            var localContentServerConfiguration = new LocalServerConfiguration(serviceConfiguration);

            if (localCasServiceSettings.UnusedSessionTimeoutMinutes.HasValue)
            {
                localContentServerConfiguration.UnusedSessionTimeout = TimeSpan.FromMinutes(localCasServiceSettings.UnusedSessionTimeoutMinutes.Value);
            }

            if (localCasServiceSettings.UnusedSessionHeartbeatTimeoutMinutes.HasValue)
            {
                localContentServerConfiguration.UnusedSessionHeartbeatTimeout = TimeSpan.FromMinutes(localCasServiceSettings.UnusedSessionHeartbeatTimeoutMinutes.Value);
            }

            return localContentServerConfiguration;
        }

        private static ServiceConfiguration CreateServiceConfiguration(ILogger logger, IAbsFileSystem fileSystem, LocalCasSettings localCasSettings, AbsolutePath dataRootPath, bool isDistributed)
        {
            var namedCacheRoots = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, NamedCacheSettings> settings in localCasSettings.CacheSettingsByCacheName)
            {
                var rootPath = localCasSettings.GetCacheRootPathWithScenario(settings.Key);

                logger.Debug($"Writing content store config file at {rootPath}.");
                WriteContentStoreConfigFile(settings.Value.CacheSizeQuotaString, rootPath, fileSystem);

                if (!isDistributed)
                {
                    namedCacheRoots[settings.Key] = rootPath;
                }
                else
                {
                    // Arbitrary set to match ServiceConfiguration and LocalContentServer pattern
                    namedCacheRoots[localCasSettings.CasClientSettings.DefaultCacheName] = rootPath;
                }
            }

            if (!namedCacheRoots.Keys.Any(name => localCasSettings.CasClientSettings.DefaultCacheName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"Must have the default cache name {localCasSettings.CasClientSettings.DefaultCacheName} as one of the named cache roots.");
            }

            return new ServiceConfiguration(
                namedCacheRoots,
                dataRootPath,
                localCasSettings.ServiceSettings.MaxPipeListeners,
                localCasSettings.ServiceSettings.GracefulShutdownSeconds,
                (int)localCasSettings.ServiceSettings.GrpcPort,
                grpcPortFileName: localCasSettings.ServiceSettings.GrpcPortFileName,
                bufferSizeForGrpcCopies: localCasSettings.ServiceSettings.BufferSizeForGrpcCopies,
                gzipBarrierSizeForGrpcCopies: localCasSettings.ServiceSettings.GzipBarrierSizeForGrpcCopies);
        }

        private static void WriteContentStoreConfigFile(string cacheSizeQuotaString, AbsolutePath rootPath, IAbsFileSystem fileSystem)
        {
            fileSystem.CreateDirectory(rootPath);

            var maxSizeQuota = new MaxSizeQuota(cacheSizeQuotaString);
            var casConfig = new ContentStoreConfiguration(maxSizeQuota);

            casConfig.Write(fileSystem, rootPath).GetAwaiter().GetResult();
        }

        private static string GetRoot(AbsolutePath rootPath)
        {
            return Path.GetPathRoot(rootPath.Path);
        }
    }
}
