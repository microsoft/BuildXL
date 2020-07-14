// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Describes drops/files which should be deployed
    /// </summary>
    public class DeploymentConfiguration
    {
        /// <summary>
        /// List of drops/files with target paths. Drops overlay with each other and later declarations can overwrite files from
        /// prior declarations if files overlap
        /// </summary>
        public List<DropDeploymentConfiguration> Drops { get; set; } = new List<DropDeploymentConfiguration>();

        /// <summary>
        /// Configuration for launching tool inside deployment
        /// </summary>
        public LaunchConfiguration Tool { get; set; }

        /// <summary>
        /// Time to live for SAS urls returned by deployment service
        /// </summary>
        public int SasUrlTimeToLiveMinutes { get; set; }

        /// <summary>
        /// The name of the secret used to communicate to storage account
        /// </summary>
        public string AzureStorageSecretName { get; set; }
    }

    public class DropDeploymentConfiguration
    {
        /// <summary>
        /// The url used to download the drop
        /// This can point to a drop service drop url or source file/directory relative to source root:
        /// https://{accountName}.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/{dropName}?root=release/win-x64
        /// 
        /// file://MyEnvironment/CacheConfiguration.json
        /// file://MyEnvironment/
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Defines target folder under which deployment files should be placed.
        /// Optional. Defaults to root folder.
        /// </summary>
        public string TargetRelativePath { get; set; }
    }

    /// <summary>
    /// Describes parameters used to launch tool inside a deployment
    /// </summary>
    public class LaunchConfiguration
    {
        /// <summary>
        /// Path to the executable used when launching the tool relative to the layout root
        /// </summary>
        public string Executable { get; set; }

        /// <summary>
        /// Arguments used when launching the tool
        /// </summary>
        public string[] Arguments { get; set; }

        /// <summary>
        /// Environment variables used when launching the tool
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();
    }
}
