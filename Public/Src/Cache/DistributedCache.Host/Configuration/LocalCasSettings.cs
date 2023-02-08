// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

#nullable disable

namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public class LocalCasSettings
    {
        public const string DefaultScenario = "ContentAddressableStore";

        public LocalCasSettings()
        {    
        }

        public LocalCasSettings(
            string cacheSizeQuotaString,
            long defaultSingleInstanceTimeoutSec,
            string cacheRootPath,
            string cacheName = LocalCasServiceSettings.DefaultCacheName,
            bool useCasService = false,
            uint gracefulShutdownSeconds = LocalCasServiceSettings.DefaultGracefulShutdownSeconds,
            uint retryIntervalSecondsOnFailServiceCalls = LocalCasClientSettings.DefaultRetryCountOnFailServiceCalls,
            uint retryCountOnFailServiceCalls = LocalCasClientSettings.DefaultRetryCountOnFailServiceCalls,
            string scenarioName = null,
            uint grpcPort = 0,
            string grpcPortFileName = null,
            int? bufferSizeForGrpcCopies = null)
        {
            CasClientSettings = new LocalCasClientSettings(useCasService, cacheName, retryIntervalSecondsOnFailServiceCalls, retryCountOnFailServiceCalls);

            ServiceSettings = new LocalCasServiceSettings(
                defaultSingleInstanceTimeoutSec,
                gracefulShutdownSeconds: gracefulShutdownSeconds,
                scenarioName: scenarioName,
                grpcPort: grpcPort,
                grpcPortFileName: grpcPortFileName,
                bufferSizeForGrpcCopies: bufferSizeForGrpcCopies);

            AddNamedCache(cacheName, new NamedCacheSettings() { CacheRootPath = cacheRootPath, CacheSizeQuotaString = cacheSizeQuotaString, });
        }

        public static LocalCasSettings Default(string cacheRootPath, int maxSizeQuotaMB = 1024, string cacheName = "CacheName", uint grpcPort = 7096, string grpcPortFileName = LocalCasServiceSettings.DefaultFileName) =>
            new LocalCasSettings(
                cacheSizeQuotaString: $"{maxSizeQuotaMB}MB",
                defaultSingleInstanceTimeoutSec: 0,
                cacheRootPath: cacheRootPath,
                cacheName: cacheName,
                useCasService: false,
                gracefulShutdownSeconds: 15,
                retryIntervalSecondsOnFailServiceCalls: 12,
                retryCountOnFailServiceCalls: 12,
                scenarioName: null,
                grpcPort: grpcPort,
                grpcPortFileName: grpcPortFileName);

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
        /// Order of drive preference when multiple cache sessions enabled.  
        /// At runtime, the first element of this list is used for streamed content.
        /// Configuration allows multiple drives, since some may not be availble on some machines, 
        /// and be removed from config based on capability checks.
        /// </summary>
        [DataMember]
        public List<string> DrivePreferenceOrder { get; set; } = new();

        /// <summary>
        /// The resolved scenario name
        /// </summary>
        public string ResolvedScenario => ServiceSettings?.ScenarioName ?? DefaultScenario;

        public AbsolutePath DefaultRootPath => GetCacheRootPathWithScenario(CasClientSettings.DefaultCacheName);

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

        public string GetCacheRootPath(string name, string scenario)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException($"Could not get cache root path due to null or empty {nameof(name)}");
            }

            if (string.IsNullOrEmpty(scenario))
            {
                throw new ArgumentException($"Could not get cache root path due to null or empty {nameof(scenario)}");
            }

            var settings = GetCacheSettings(name);
            return Path.Combine(settings.CacheRootPath, scenario);
        }

        private NamedCacheSettings GetCacheSettings(string cacheName)
        {
            NamedCacheSettings settings;
            if (!CacheSettingsByCacheName.TryGetValue(cacheName, out settings))
            {
                throw new ArgumentException($"{nameof(cacheName)} does not exist in named cache settings.");
            }

            return settings;
        }

        public void AddNamedCache(string cacheName, string cacheRoot)
        {
            AddNamedCache(cacheName, new NamedCacheSettings()
            {
                CacheRootPath = cacheRoot,
            });
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
