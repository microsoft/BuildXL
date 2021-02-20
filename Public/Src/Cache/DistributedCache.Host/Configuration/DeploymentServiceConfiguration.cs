// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Describes settings used by deployment service
    /// </summary>
    public class DeploymentServiceConfiguration
    {
        /// <summary>
        /// Configure the logging behavior for the service
        /// </summary>
        public LoggingSettings? LoggingSettings { get; set; } = null;
    }
}
