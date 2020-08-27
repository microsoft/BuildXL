// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public class LocalCasSettings
    {
        public const string DefaultScenario = "ContentAddressableStore";

        public LocalCasSettings()
        {    
        }

        public static readonly AbsolutePath DefaultCacheDrive = OperatingSystemHelper.IsUnixOS ? new AbsolutePath("/") : new AbsolutePath("D:\\");

        public LocalCasSettings(
            string cacheSizeQuotaString,
            long defaultSingleInstanceTimeoutSec,
            string cacheRootPath,
            string cacheName = LocalCasServiceSettings.DefaultCacheName,
            bool useCasService = false,
            uint connectionsPerSession = LocalCasClientSettings.DefaultConnectionsPerSession,
            uint gracefulShutdownSeconds = LocalCasServiceSettings.DefaultGracefulShutdownSeconds,
            uint maxPipeListeners = LocalCasServiceSettings.DefaultMaxPipeListeners,
            uint retryIntervalSecondsOnFailServiceCalls = LocalCasClientSettings.DefaultRetryCountOnFailServiceCalls,
            uint retryCountOnFailServiceCalls = LocalCasClientSettings.DefaultRetryCountOnFailServiceCalls,
            bool supportsSensitiveSessions = false,
            string scenarioName = null,
            uint grpcPort = 0,
            string grpcPortFileName = null,
            bool supportsProactiveReplication = true,
            int? bufferSizeForGrpcCopies = null,
            int? gzipBarrierSizeForGrpcCopies = null,
            int? grpcThreadPoolSize = null)
        {
            CasClientSettings = new LocalCasClientSettings(useCasService, cacheName, connectionsPerSession, retryIntervalSecondsOnFailServiceCalls, retryCountOnFailServiceCalls);

            ServiceSettings = new LocalCasServiceSettings(
                defaultSingleInstanceTimeoutSec,
                gracefulShutdownSeconds: gracefulShutdownSeconds,
                maxPipeListeners: maxPipeListeners,
                scenarioName: scenarioName,
                grpcPort: grpcPort,
                grpcPortFileName: grpcPortFileName,
                bufferSizeForGrpcCopies: bufferSizeForGrpcCopies,
                gzipBarrierSizeForGrpcCopies: gzipBarrierSizeForGrpcCopies,
                grpcThreadPoolSize: grpcThreadPoolSize);

            AddNamedCache(cacheName, new NamedCacheSettings(
                cacheRootPath, cacheSizeQuotaString, supportsSensitiveSessions, supportsProactiveReplication, requiredCapabilites: null));
        }

        public static LocalCasSettings Default(int maxSizeQuotaMB = 1024, string cacheRootPath = null, string cacheName = "CacheName", uint grpcPort = 7096, string grpcPortFileName = LocalCasServiceSettings.DefaultFileName) =>
            new LocalCasSettings(
                cacheSizeQuotaString: $"{maxSizeQuotaMB}MB",
                defaultSingleInstanceTimeoutSec: 0,
                cacheRootPath: cacheRootPath ?? (DefaultCacheDrive / "Cache" / "cacheRoot").Path,
                cacheName: cacheName,
                useCasService: false,
                connectionsPerSession: 4,
                gracefulShutdownSeconds: 15,
                maxPipeListeners: 128,
                retryIntervalSecondsOnFailServiceCalls: 12,
                retryCountOnFailServiceCalls: 12,
                supportsSensitiveSessions: false,
                scenarioName: null,
                grpcPort: grpcPort,
                grpcPortFileName: grpcPortFileName,
                supportsProactiveReplication: false,
                grpcThreadPoolSize: null);

        /// <summary>
        /// For unit test use only.
        /// </summary>
        public LocalCasSettings(IDictionary<string, NamedCacheSettings> nameCacheSettings)
        {
            foreach(KeyValuePair<string, NamedCacheSettings> kvp in nameCacheSettings)
            {
                AddNamedCache(kvp.Key, kvp.Value);
            }
        }

        [DataMember]
        public LocalCasClientSettings CasClientSettings { get; set; }

        [DataMember]
        public LocalCasServiceSettings ServiceSettings { get; set; }

        [DataMember]
        public Dictionary<string, NamedCacheSettings> CacheSettings { get; set; }

        /// <summary>
        /// Deprecated - Recognized in config, but deprecated in favor of <see cref="DrivePreferenceOrder"/>.
        /// Will be removed once all configs are updated.
        /// TODO: Remove DataMemberAttribute and setter.
        /// </summary>
        [DataMember]
        public string PreferredCacheDrive
        {
            get
            {
                if(DrivePreferenceOrder == null || DrivePreferenceOrder.Count == 0)
                {
                    return null;
                }
                return DrivePreferenceOrder[0];
            }
            set
            {
                if (value == null)
                {
                    if (DrivePreferenceOrder != null)
                    {
                        DrivePreferenceOrder.Clear();
                    }
                }
                else
                {
                    if (DrivePreferenceOrder == null)
                    {
                        DrivePreferenceOrder = new List<string>();
                    }

                    DrivePreferenceOrder.Clear();
                    DrivePreferenceOrder.Add(value);
                }
            }
        }

        /// <summary>
        /// Order of drive preference when multiple cache sessions enabled.  
        /// At runtime, the first element of this list is used for streamed content.
        /// Configuration allows multiple drives, since some may not be availble on some machines, 
        /// and be removed from config based on capability checks.
        /// </summary>
        [DataMember]
        public List<string> DrivePreferenceOrder { get; set; } = new List<string> { DefaultCacheDrive.Path };

        /// <summary>
        /// Indicates whether CAS instances should be separated by Scenario
        /// </summary>
        public bool UseScenarioIsolation { get; set; } = true;

        /// <summary>
        /// The resolved scenario name
        /// </summary>
        public string ResolvedScenario => ServiceSettings?.ScenarioName ?? DefaultScenario;

        public AbsolutePath GetCacheRootPathWithScenario(string cacheName)
        {
            return new AbsolutePath(GetCacheRootPath(cacheName, ResolvedScenario));
        }

        public Dictionary<string, NamedCacheSettings> CacheSettingsByCacheName
        {
            get { return CacheSettings?.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase); }
            set
            {
                CacheSettings = value.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            }
        }

        public string GetCacheRootPath(string cacheName, string intent)
        {
            if (string.IsNullOrEmpty(cacheName))
            {
                throw new ArgumentException($"Could not get cache root path due to null or empty {nameof(cacheName)}");
            }

            var settings = GetCacheSettings(cacheName);
            if (UseScenarioIsolation)
            {
                if (string.IsNullOrEmpty(intent))
                {
                    throw new ArgumentException($"Could not get cache root path due to null or empty {nameof(intent)}");
                }

                return Path.Combine(settings.CacheRootPath, intent);
            }
            else
            {
                return settings.CacheRootPath;
            }
        }

        public NamedCacheSettings GetCacheSettings(string cacheName)
        {
            NamedCacheSettings settings;
            if (!CacheSettingsByCacheName.TryGetValue(cacheName, out settings))
            {
                throw new ArgumentException($"{nameof(cacheName)} does not exist in named cache settings.");
            }

            return settings;
        }

        private void AddNamedCache(string cacheName, NamedCacheSettings settings)
        {
            if (CacheSettings == null)
            {
                CacheSettings = new Dictionary<string, NamedCacheSettings>(StringComparer.OrdinalIgnoreCase);
            }

            CacheSettings.Add(cacheName, settings);
        }
    }
}
