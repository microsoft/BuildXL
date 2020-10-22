// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

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
        public LoggingSettings LoggingSettings { get; set; } = null;
    }
}
