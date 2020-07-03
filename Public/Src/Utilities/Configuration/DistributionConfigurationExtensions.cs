// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Set of extension methods for <see cref="IDistributionConfiguration"/>.
    /// </summary>
    public static class DistributionConfigurationExtensions
    {
        /// <summary>
        /// Whether this build is running in CloudBuild
        /// </summary>
        public static bool ReplicateOutputsToWorkers(this IDistributionConfiguration configuration)
        {
            return configuration.ReplicateOutputsToWorkers ?? false;
        }
    }
}
