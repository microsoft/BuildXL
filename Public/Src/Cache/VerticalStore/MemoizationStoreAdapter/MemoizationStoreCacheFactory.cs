// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Security;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.SQLite;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

[module: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses",
    Scope = "type",
    Target = "BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory+Config",
    Justification = "Tool is confused - it is constructed generically")]
namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// The Cache Factory for an on-disk Cache implementation based on MemoizationStore.
    /// </summary>
    /// <remarks>
    /// Current limitations while we flesh things out:
    /// 1) APIs around tracking named sessions are not implemented
    /// </remarks>
    public class MemoizationStoreCacheFactory : ICacheFactory
    {
        private const bool WaitForLruOnShutdown = true;
        private const string DefaultStreamFolder = "streams";

        /// <summary>
        /// Configuration for <see cref="MemoizationStoreCacheFactory"/>.
        /// </summary>
        /// <remarks>
        /// MemoizationStoreCacheFactory JSON CONFIG DATA
        /// {
        ///     "Assembly":"BuildXL.Cache.MemoizationStoreAdapter",
        ///     "Type":"BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory",
        ///     "CacheId":"{0}",
        ///     "MaxCacheSizeInMB":{1},
        ///     "MaxStrongFingerprints":{2},
        ///     "CacheRootPath":"{3}",
        ///     "CacheLogPath":"{4}",
        ///     "SingleInstanceTimeoutInSeconds":"{5}",
        ///     "ApplyDenyWriteAttributesOnContent":"{6}",
        ///     "UseStreamCAS":"{7}",
        ///     "BackupLKGCache":{8},
        ///     "CheckCacheIntegrityOnStartup":{9}
        ///     "SingleInstanceTimeoutInSeconds":{10}
        ///     "SynchronizationMode":{11},
        ///     "LogFlushIntervalSeconds":{12}
        ///     "StreamCAS": {
        ///          "MaxCacheSizeInMB":{13},
        ///          "MaxStrongFingerprints":{14},
        ///          "CacheRootPath":"{15}",
        ///          "SingleInstanceTimeoutInSeconds":"{16}",
        ///          "ApplyDenyWriteAttributesOnContent":"{17}",
        ///     },
        ///     "EnableContentServer":{18},
        ///     "EmptyFileHashShortcutEnabled":{19},
        ///     "CheckLocalFiles":{20},
        ///     "CacheName":"{21}",
        ///     "GrpcPort":{22},
        ///     "ScenarioName":"{23}",
        ///     "RetryIntervalSeconds":{24},
        ///     "RetryCount":{25},
        /// }
        /// </remarks>
        public sealed class Config : CasConfig
        {
            /// <summary>
            /// The Id of the cache instance
            /// </summary>
            [DefaultValue("FileSystemCache")]
            public string CacheId { get; set; }

            /// <summary>
            /// Max number of CasEntries entries.
            /// </summary>
            public uint MaxStrongFingerprints { get; set; }

            /// <summary>
            /// Path to the log file for the cache.
            /// </summary>
            public string CacheLogPath { get; set; }

            /// <summary>
            /// If true, use a different CAS for streams, specified by <see cref="StreamCAS"/>.
            /// </summary>
            public bool UseStreamCAS { get; set; }

            /// <summary>
            /// Configuration for stream CAS.
            /// </summary>
            [DefaultValue(null)]
            public CasConfig StreamCAS { get; set; }

            /// <summary>
            ///     Create a backup of the last known good cache at startup. This step has a 
            ///     startup cost but allows better recovery in case the cache detects
            ///     corruption at startup.
            /// </summary>
            [DefaultValue(false)]
            public bool BackupLKGCache { get; set; }

            /// <summary>
            ///     The cache will check its integrity on startup. If the integrity check
            ///     fails, the corrupt cache data is thrown out and use a LKG data backup
            ///     is used. If a backup is unavailable the cache starts from scratch.
            /// </summary>
            [DefaultValue(false)]
            public bool CheckCacheIntegrityOnStartup { get; set; }

            /// <summary>
            /// Controls the synchronization mode for writes to the database.
            /// </summary>
            [DefaultValue(null)]
            public string SynchronizationMode { get; set; }

            /// <summary>
            /// Duration to wait for exclusive access to the cache directory before timing out.
            /// </summary>
            [DefaultValue(0)]
            public uint LogFlushIntervalSeconds { get; set; }

            /// <summary>
            /// Whether the shortcuts for streaming, placing, and pinning the empty file are used.
            /// </summary>
            [DefaultValue(false)]
            public bool EmptyFileHashShortcutEnabled { get; set; }

            /// <summary>
            /// Whether to check for file existence before pinning.
            /// </summary>
            [DefaultValue(false)]
            public bool CheckLocalFiles { get; set; }

            /// <summary>
            /// Whether the cache will communicate with a server in a separate process via GRPC.
            /// </summary>
            [DefaultValue(false)]
            public bool EnableContentServer { get; set; }

            /// <summary>
            /// Name of one of the named caches owned by CASaaS.
            /// </summary>
            [DefaultValue(null)]
            public string CacheName { get; set; }

            /// <summary>
            /// The GRPC port to use.
            /// </summary>
            [DefaultValue(0)]
            public int GrpcPort { get; set; }

            /// <summary>
            /// Name of the custom scenario that the CAS connects to.
            /// allows multiple CAS services to coexist in a machine
            /// since this factors into the cache root and the event that
            /// identifies a particular CAS instance.
            /// </summary>
            [DefaultValue(null)]
            public string ScenarioName { get; set; }

            /// <nodoc />
            [DefaultValue(ServiceClientContentStoreConfiguration.DefaultRetryIntervalSeconds)]
            public int RetryIntervalSeconds { get; set; }

            /// <nodoc />
            [DefaultValue(ServiceClientContentStoreConfiguration.DefaultRetryCount)]
            public int RetryCount { get; set; }

            /// <nodoc />
            public Config()
            {
                CacheId = "FileSystemCache";
                MaxStrongFingerprints = 500000;
                CacheLogPath = null;

                MaxCacheSizeInMB = 512000;
                DiskFreePercent = 0;
                CacheRootPath = null;
                SingleInstanceTimeoutInSeconds = ContentStoreConfiguration.DefaultSingleInstanceTimeoutSeconds;
                ApplyDenyWriteAttributesOnContent = FileSystemContentStoreInternal.DefaultApplyDenyWriteAttributesOnContent;
                UseStreamCAS = false;
                StreamCAS = null;
            }
        }

        /// <summary>
        /// CAS configuration.
        /// </summary>
        public class CasConfig
        {
            /// <summary>
            /// Max size of the cache in MB.
            /// </summary>
            public int MaxCacheSizeInMB { get; set; }

            /// <summary>
            /// Percentage of disk free space to maintain - zero/negative to disable this quota.
            /// </summary>
            public uint DiskFreePercent { get; set; }

            /// <summary>
            /// Root path for storing CAS entries.
            /// </summary>
            public string CacheRootPath { get; set; }

            /// <summary>
            /// Duration to wait for exclusive access to the cache directory before timing out.
            /// </summary>
            public uint SingleInstanceTimeoutInSeconds { get; set; }

            /// <summary>
            /// If true, the cache will set Deny-WriteAttributes ACLs on files for which ReadOnly is requested.
            /// </summary>
            public bool ApplyDenyWriteAttributesOnContent { get; set; }

            /// <summary>
            /// If true, enable elastic CAS.
            /// </summary>
            public bool EnableElasticity { get; set; }

            /// <summary>
            /// Initial size for elastic CAS
            /// </summary>
            public uint InitialElasticSizeInMB { get; set; }

            /// <summary>
            /// Size of history buffer.
            /// </summary>
            public uint HistoryBufferSize { get; set; }

            /// <summary>
            /// Size of history window.
            /// </summary>
            public uint HistoryWindowSize { get; set; }

            /// <nodoc />
            public CasConfig()
            {
                // The size 1G is based on data from an Office builds, where pathset and metadata are of size 80M.
                // The size 1G can give 10 builds before start losing the metadata/pathset.
                MaxCacheSizeInMB = 1024;
                DiskFreePercent = 0;

                // Setting it to empty to support conversion by CacheFactory.
                CacheRootPath = string.Empty;
                SingleInstanceTimeoutInSeconds = ContentStoreConfiguration.DefaultSingleInstanceTimeoutSeconds;
                ApplyDenyWriteAttributesOnContent = FileSystemContentStoreInternal.DefaultApplyDenyWriteAttributesOnContent;

                // Elasticity.
                EnableElasticity = false;
                InitialElasticSizeInMB = 1024;
                HistoryBufferSize = 0;
                HistoryWindowSize = 0;
            }
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

            try
            {
                var logPath = new AbsolutePath(cacheConfig.CacheLogPath);
                var logger = new DisposeLogger(() => new EtwFileLog(logPath.Path, cacheConfig.CacheId), cacheConfig.LogFlushIntervalSeconds);

                var localCache = cacheConfig.UseStreamCAS
                    ? CreateLocalCacheWithStreamPathCas(cacheConfig, logger)
                    : CreateLocalCacheWithSingleCas(cacheConfig, logger);

                var statsFilePath = new AbsolutePath(logPath.Path + ".stats");
                var cache = new MemoizationStoreAdapterCache(cacheConfig.CacheId, localCache, logger, statsFilePath);

                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    return startupResult.Failure;
                }

                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(cacheConfig.CacheId, e);
            }
        }

        private static CasConfig GetCasConfig(Config config)
        {
            return new CasConfig
                   {
                       ApplyDenyWriteAttributesOnContent = config.ApplyDenyWriteAttributesOnContent,
                       CacheRootPath = config.CacheRootPath,
                       DiskFreePercent = config.DiskFreePercent,
                       MaxCacheSizeInMB = config.MaxCacheSizeInMB,
                       SingleInstanceTimeoutInSeconds = config.SingleInstanceTimeoutInSeconds,
                       EnableElasticity = config.EnableElasticity,
                       InitialElasticSizeInMB = config.InitialElasticSizeInMB,
                       HistoryBufferSize = config.HistoryBufferSize,
                       HistoryWindowSize = config.HistoryWindowSize,
                   };
        }

        private static ConfigurationModel CreateConfigurationModel(CasConfig casConfig)
        {
            return new ConfigurationModel(
                new ContentStoreConfiguration(
                    new MaxSizeQuota(I($"{casConfig.MaxCacheSizeInMB}MB")),
                    casConfig.DiskFreePercent > 0 ? new DiskFreePercentQuota(casConfig.DiskFreePercent) : null,
                    casConfig.ApplyDenyWriteAttributesOnContent
                        ? DenyWriteAttributesOnContentSetting.Enable
                        : DenyWriteAttributesOnContentSetting.Disable,
                    (int)casConfig.SingleInstanceTimeoutInSeconds,
                    casConfig.EnableElasticity,
                    casConfig.InitialElasticSizeInMB > 0 ? new MaxSizeQuota(I($"{casConfig.InitialElasticSizeInMB}MB")) : null,
                    casConfig.HistoryBufferSize > 0 ? (int?)casConfig.HistoryBufferSize : null,
                    casConfig.HistoryWindowSize > 0 ? (int?)casConfig.HistoryWindowSize : null));
        }

        private static void SetDefaultsForStreamCas(Config config)
        {
            Contract.Requires(config.UseStreamCAS);

            if (config.StreamCAS == null)
            {
                config.StreamCAS = new CasConfig();
            }

            if (string.IsNullOrWhiteSpace(config.StreamCAS.CacheRootPath))
            {
                config.StreamCAS.CacheRootPath = System.IO.Path.Combine(config.CacheRootPath, DefaultStreamFolder);
            }
        }

        private static SQLiteMemoizationStoreConfiguration GetMemoConfig(AbsolutePath cacheRoot, Config config, CasConfig configCore)
        {
            var memoConfig = new SQLiteMemoizationStoreConfiguration(cacheRoot)
            {
                MaxRowCount = config.MaxStrongFingerprints,
                BackupDatabase = config.BackupLKGCache,
                VerifyIntegrityOnStartup = config.CheckCacheIntegrityOnStartup,
                SingleInstanceTimeoutSeconds = (int)configCore.SingleInstanceTimeoutInSeconds,
                WaitForLruOnShutdown = WaitForLruOnShutdown
            };

            if (!string.IsNullOrEmpty(config.SynchronizationMode))
            {
                memoConfig.SyncMode = (SynchronizationMode)Enum.Parse(typeof(SynchronizationMode), config.SynchronizationMode, ignoreCase: true);
            }

            return memoConfig;
        }

        private static LocalCache CreateLocalCacheWithSingleCas(Config config, DisposeLogger logger)
        {
            var configCore = GetCasConfig(config);
            var configurationModel = CreateConfigurationModel(configCore);

            var cacheRoot = new AbsolutePath(config.CacheRootPath);
            var memoConfig = GetMemoConfig(cacheRoot, config, configCore);

            LocalCacheConfiguration localCacheConfiguration;
            if (config.EnableContentServer)
            {
                localCacheConfiguration = LocalCacheConfiguration.CreateServerEnabled(
                    config.GrpcPort,
                    config.CacheName,
                    config.ScenarioName,
                    config.RetryIntervalSeconds,
                    config.RetryCount);
            }
            else
            {
                localCacheConfiguration = LocalCacheConfiguration.CreateServerDisabled();
            }

            return new LocalCache(
                logger,
                cacheRoot,
                memoConfig,
                localCacheConfiguration,
                configurationModel: configurationModel,
                clock: null,
                checkLocalFiles: config.CheckLocalFiles,
                emptyFileHashShortcutEnabled: config.EmptyFileHashShortcutEnabled);
        }

        private static LocalCache CreateLocalCacheWithStreamPathCas(Config config, DisposeLogger logger)
        {
            Contract.Requires(config.UseStreamCAS);

            SetDefaultsForStreamCas(config);

            var configCoreForPath = GetCasConfig(config);
            var configurationModelForPath = CreateConfigurationModel(configCoreForPath);
            var configurationModelForStreams = CreateConfigurationModel(config.StreamCAS);
            
            var memoConfig = GetMemoConfig(new AbsolutePath(config.CacheRootPath), config, configCoreForPath);
            return new LocalCache(
                logger,
                new AbsolutePath(config.StreamCAS.CacheRootPath),
                new AbsolutePath(configCoreForPath.CacheRootPath),
                memoConfig,
                configurationModelForStreams,
                configurationModelForPath);
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheRootPath, nameof(cacheConfig.CacheRootPath));

                if (cacheConfig.UseStreamCAS && string.IsNullOrEmpty(cacheConfig.StreamCAS.CacheRootPath))
                {
                    failures.Add(new IncorrectJsonConfigDataFailure($"If {nameof(cacheConfig.UseStreamCAS)} is enabled, {nameof(cacheConfig.StreamCAS)}.{nameof(cacheConfig.StreamCAS.CacheRootPath)} cannot be null or empty."));
                }

                return failures;
            });
        }
    }
}
