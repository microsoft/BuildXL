// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.AdoBuildRunner
{
    /// <nodoc/>
    public static class CacheConfigGenerationConfigurationDefaults
    {
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
    }
}
