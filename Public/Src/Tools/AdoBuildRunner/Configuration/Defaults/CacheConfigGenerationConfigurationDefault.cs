// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using System.IO;

namespace BuildXL.AdoBuildRunner
{
    /// <nodoc/>
    public static class CacheConfigGenerationConfigurationDefaults
    {
        /// <summary>
        /// Predefined location, based on the contract between BuildXL and 1ESHP for the hosted pool cache config file in Windows.
        /// Where a pool with a set a cache resources associated to it is guaranteed to put this file on every agent at this location.
        /// </summary>
        private const string HostedPoolBuildCacheConfigurationFileForWindows = $@"C:\buildcacheconfig.json";

        /// <summary>
        /// Predefined location, based on the contract between BuildXL and 1ESHP for the hosted pool cache config file in Linux.
        /// Where a pool with a set a cache resources associated to it is guaranteed to put this file on every agent at this location.
        /// </summary>
        private const string HostedPoolBuildCacheConfigurationFileForLinux = $@"/buildcacheconfig.json";

        /// <summary>
        /// 6 days proved to be a good default for the retention policy (and avoids the weekend lump issue)
        /// </summary>
        public const int DefaultRetentionPolicyInDays = 6;

        /// <nodoc/>
        public const string DefaultUniverse = "default";

        /// <summary>
        /// For now this is a regular blob cache. Ephemeral might become the default in the future as it gains more mileage
        /// </summary>
        public const CacheType DefaultCacheType = AdoBuildRunner.CacheType.Blob;

        /// <summary>
        /// The default assumes stateless agents and therefore no limit on the cache size.
        /// TODO: change this by null whenever that's supported by the cache configuration, as a true indicator that we want no limit
        /// </summary>
        public const int DefaultCacheSizeMb = int.MaxValue;

        /// <summary>
        /// By default we do not send the generated configuration to console to avoid clutter
        /// </summary>
        public const bool DefaultLogGeneratedConfiguration = false;

        /// <nodoc/>
        public static int RetentionPolicyInDays(this ICacheConfigGenerationConfiguration config) => config.RetentionPolicyInDays ?? DefaultRetentionPolicyInDays;

        /// <nodoc/>
        public static string Universe(this ICacheConfigGenerationConfiguration config) => config.Universe ?? DefaultUniverse;

        /// <nodoc/>
        public static string CacheId(this ICacheConfigGenerationConfiguration config) => config.CacheId ?? config.CacheType.ToString();

        /// <nodoc/>
        public static CacheType CacheType(this ICacheConfigGenerationConfiguration config) => config.CacheType ?? DefaultCacheType;

        /// <nodoc/>
        public static int CacheSizeInMB(this ICacheConfigGenerationConfiguration config) => config.CacheSizeInMB ?? DefaultCacheSizeMb;

        /// <nodoc/>
        public static bool LogGeneratedConfiguration(this ICacheConfigGenerationConfiguration config) => config.LogGeneratedConfiguration ?? DefaultLogGeneratedConfiguration;

        /// <nodoc/>
        public static string HostedPoolActiveBuildCacheName(this ICacheConfigGenerationConfiguration config) => config.HostedPoolActiveBuildCacheName ?? string.Empty;

        /// <summary>
        /// Gets the path of the cache configuration file produced by Hosted Pool
        /// </summary>
        /// <returns>
        /// Absolute path of the cache configuration file if found, otherwise null
        /// </returns>
        public static string GetHostedPoolBuildCacheConfigurationFile()
        {
            var filePath = (OperatingSystemHelper.IsWindowsOS) ? HostedPoolBuildCacheConfigurationFileForWindows : HostedPoolBuildCacheConfigurationFileForLinux;
            return File.Exists(filePath) ? filePath : null;
        }
    }
}