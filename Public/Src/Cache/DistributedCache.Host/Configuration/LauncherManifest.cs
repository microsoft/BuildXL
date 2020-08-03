// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Describes drops and files (with download urls) present in a deployment along with tools to launch
    /// </summary>
    public class LauncherManifest
    {
        /// <summary>
        /// Map from layout of files for deployment
        /// </summary>
        public DeploymentManifest.LayoutSpec Deployment { get; set; } = new DeploymentManifest.LayoutSpec();

        /// <summary>
        /// List of drops/files with target paths. Drops overlay with each other and later declarations can overwrite files from
        /// prior declarations if files overlap
        /// </summary>
        public List<DropDeploymentConfiguration> Drops { get; set; } = new List<DropDeploymentConfiguration>();

        /// <summary>
        /// Configuration specifying the tool to launch
        /// </summary>
        public LaunchConfiguration Tool { get; set; }
    }
}
