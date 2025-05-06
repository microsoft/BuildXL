// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// The type of cache to generate a configuration file for
    /// </summary>
    public enum CacheType : byte
    {
        /// <nodoc/>
        Blob = 0,

        /// <nodoc/>
        EphemeralBuildWide = 1,

        /// <nodoc/>
        EphemeralDatacenterWide = 2
    }

    /// <nodoc/>
    public interface ICacheConfigGenerationConfiguration
    {
        /// <nodoc/>
        public Uri? StorageAccountEndpoint { get; }

        /// <nodoc/>
        public Guid ManagedIdentityId { get; }

        /// <nodoc/>
        public int? RetentionPolicyInDays { get; }

        /// <nodoc/>
        public string? Universe { get; }

        /// <nodoc/>
        public int? CacheSizeInMB { get; }

        /// <nodoc />
        public int? RemoteConstructionTimeoutSeconds { get; }

        /// <nodoc/>
        public string? CacheId { get; }

        /// <nodoc/>
        public CacheType? CacheType { get; }

        /// <nodoc/>
        public bool? LogGeneratedConfiguration { get; }

        /// <nodoc/>
        public string? HostedPoolActiveBuildCacheName { get; }

        /// <nodoc/>
        public string? HostedPoolBuildCacheConfigurationFile { get; }
    }

    /// <nodoc/>
    public static class CacheConfigGenerationConfigurationExtensions
    {
        /// <summary>
        /// Throws an <see cref="ArgumentException"/> on error
        /// </summary>
        public static void ValidateConfiguration(this ICacheConfigGenerationConfiguration cacheConfigGenerationConfiguration)
        {
            if (cacheConfigGenerationConfiguration.StorageAccountEndpoint?.IsAbsoluteUri == false)
            {
                throw new ArgumentException("StorageAccountEndpoint requires an absolute URI.");
            }

            if (cacheConfigGenerationConfiguration.StorageAccountEndpoint != null && cacheConfigGenerationConfiguration.ManagedIdentityId == default)
            {
                throw new ArgumentException("ManagedIdentityId requires a valid GUID.");
            }

            if (cacheConfigGenerationConfiguration.RetentionPolicyInDays <= 0)
            {
                throw new ArgumentException("RetentionPolicyInDays must be a positive integer.");
            }

            var universeRegex = new Regex("^([a-z0-9])+$");
            if (cacheConfigGenerationConfiguration.Universe != null && !universeRegex.Match(cacheConfigGenerationConfiguration.Universe).Success)
            {
                throw new ArgumentException("Universe must be a non-empty string containing only lowercase letters or numbers.");
            }

            if (cacheConfigGenerationConfiguration.CacheSizeInMB <= 0)
            {
                throw new ArgumentException("CacheSizeInMB must be a positive integer.");
            }

            if (cacheConfigGenerationConfiguration.HonorBuildCacheConfigurationFile() && !string.IsNullOrEmpty(cacheConfigGenerationConfiguration.Universe))
            {
                throw new ArgumentException($"Setting a specific 'Universe' is not supported by 1ES hosted pool cache.");
            }
        }

        /// <summary>
        /// If the storage account endpoint was provided, that is taken as an indicator that the cache config needs to be generated.
        /// Likewise, if we find a hosted pool build cache configuration file, we'll generate a cache config that honors it.
        /// False otherwise
        /// </summary>
        public static bool ShouldGenerateCacheConfigurationFile(this ICacheConfigGenerationConfiguration cacheConfigGenerationConfiguration) =>
            cacheConfigGenerationConfiguration.StorageAccountEndpoint != null || cacheConfigGenerationConfiguration.HostedPoolBuildCacheConfigurationFile != null;

        /// <summary>
        /// We honor the build cache configuration file when it is present and the storage account endpoint is not provided
        /// </summary>
        /// <remarks>
        /// Providing an explicit storage account endpoint is left as a servicing option to override a configured build cache resource and allow a build
        /// to point to a specific blob account directly. It is also left as a back-compat mechanism.
        /// </remarks>
        public static bool HonorBuildCacheConfigurationFile(this ICacheConfigGenerationConfiguration cacheConfigGenerationConfiguration) =>
            cacheConfigGenerationConfiguration.HostedPoolBuildCacheConfigurationFile != null && cacheConfigGenerationConfiguration.StorageAccountEndpoint == null;
    }
}
