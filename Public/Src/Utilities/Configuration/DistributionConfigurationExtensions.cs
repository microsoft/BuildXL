// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
