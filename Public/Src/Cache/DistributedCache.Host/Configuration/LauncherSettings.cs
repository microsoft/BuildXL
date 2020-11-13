// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

#nullable disable

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Describes settings used by launcher when downloading deployments
    /// </summary>
    public class LauncherSettings
    {
        /// <summary>
        /// Configuration specifying the tool to launch
        /// </summary>
        public DeploymentParameters DeploymentParameters { get; set; }

        /// <summary>
        /// The url for the deployment service
        /// </summary>
        public string ServiceUrl { get; set; }

        /// <summary>
        /// The interval in which to query for deployment updates
        /// </summary>
        public double QueryIntervalSeconds { get; set; } = 300;

        /// <summary>
        /// The polling interval of the ServiceLifetimeManager
        /// </summary>
        public double ServiceLifetimePollingIntervalSeconds { get; set; } = 1;

        /// <summary>
        /// Gets or sets the location of the target directory for deployment downloads
        /// </summary>
        public string TargetDirectory { get; set; }

        /// <summary>
        /// Forces all downloads of deployments to be deployed to the given location
        /// </summary>
        public string OverrideServiceDeploymentLocation { get; set; }

        /// <summary>
        /// The size of retained content in download cache
        /// </summary>
        public int RetentionSizeGb { get; set; }

        /// <summary>
        /// Gets the concurrency of file deployment downloads
        /// </summary>
        public int DownloadConcurrency { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Gets whether the launcher should queue a background task to launch services on startup as opposed
        /// to requiring an explicit call to RunAsync.
        /// </summary>
        public bool RunInBackgroundOnStartup { get; set; } = true;

        /// <summary>
        /// Indicates whether a job object is created with terminate on close to prevent orphaned child processes.
        /// TODO: Non-windows platforms?
        /// </summary>
        public bool CreateJobObject { get; set; } = true;

        /// <summary>
        /// Indicates how long deployment should allow for downloads and placement before timing out the entire operation
        /// </summary>
        public TimeSpan DeployTimeout { get; set; } = TimeSpan.FromMinutes(3);

        /// <summary>
        /// The service id used for service lifetime of the launcher
        /// </summary>
        public string LauncherServiceId { get; set; } = "Launcher";
    }
}
