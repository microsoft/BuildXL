// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Set of extension methods for <see cref="IConfiguration"/>.
    /// </summary>
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Whether this build is running in CloudBuild
        /// </summary>
        public static bool InCloudBuild(this IConfiguration configuration)
        {
            return configuration.InCloudBuild ?? false;
        }

        /// <summary>
        /// Disable the default source resolver (as the last considered resolver) when set to true.
        /// </summary>
        public static bool DisableDefaultSourceResolver(this IConfiguration configuration)
        {
            return configuration.DisableDefaultSourceResolver ?? false;
        }

        /// <summary>
        /// Whether this build should store fingerprints
        /// </summary>
        public static bool FingerprintStoreEnabled(this IConfiguration configuration)
        {
            // Distributed workers send their execution events back to master,
            // to reduce storage needed on workers, workers do not need a fingerprint store

            return configuration.Logging.StoreFingerprints.HasValue
                && configuration.Logging.StoreFingerprints.Value
                && configuration.Distribution.BuildRole != DistributedBuildRoles.Worker
                && configuration.Layout.FingerprintStoreDirectory.IsValid 
                && configuration.Engine.Phase.HasFlag(EnginePhases.Execute);
        }
    }
}
