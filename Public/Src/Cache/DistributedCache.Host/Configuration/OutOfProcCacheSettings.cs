// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Host.Configuration
{
#nullable enable

    public class OutOfProcCacheSettings
    {
        /// <summary>
        /// A path to the cache configuration that the launched process will use.
        /// </summary>
        /// <remarks>
        /// This property needs to be set in CloudBuild in order to use 'out-of-proc' cache.
        /// </remarks>
        public string? CacheConfigPath { get; set; }

        /// <summary>
        /// A relative path from the current executing assembly to the stand-alone cache service that will be launched in a separate process.
        /// </summary>
        public string? Executable { get; set; }

        public int? ServiceLifetimePollingIntervalSeconds { get; set; }

        public int? ShutdownTimeoutSeconds { get; set; }
    }
}
