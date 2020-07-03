// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Host.Configuration
{
    public class DistributedCacheServiceConfiguration
    {
        public DistributedCacheServiceConfiguration(
            LocalCasSettings localCasSettings,
            DistributedContentSettings distributedCacheSettings,
            LoggingSettings loggingSettings = null)
        {
            LocalCasSettings = localCasSettings;
            DistributedContentSettings = distributedCacheSettings;
            LoggingSettings = loggingSettings;
        }

        public DistributedContentSettings DistributedContentSettings { get; private set; }

        /// <summary>
        /// Cache settings for the local cache.
        /// </summary>
        public LocalCasSettings LocalCasSettings { get; set; }

        /// <summary>
        /// Use a per stamp isolation for cache.
        /// </summary>
        public bool UseStampBasedIsolation { get; set; } = true;

        /// <summary>
        /// Configure the logging behavior for the service
        /// </summary>
        public LoggingSettings LoggingSettings { get; private set; }
    }
}
