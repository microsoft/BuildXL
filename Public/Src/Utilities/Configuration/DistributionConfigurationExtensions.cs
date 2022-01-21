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
        /// Whether this build materializes output files on all workers
        /// </summary>
        public static bool ReplicateOutputsToWorkers(this IDistributionConfiguration configuration)
        {
            return configuration.ReplicateOutputsToWorkers ?? false;
        }

        /// <summary>
        /// Whether workers should send results of materializeoutput step to the orchestrator.
        /// </summary>
        public static bool FireForgetMaterializeOutput(this IDistributionConfiguration configuration)
        {
            return configuration.FireForgetMaterializeOutput ?? false;
        }
    }
}
