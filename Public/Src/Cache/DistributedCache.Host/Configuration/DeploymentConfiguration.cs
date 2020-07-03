// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public List<DropDeploymentConfiguration> Drops { get; } = new List<DropDeploymentConfiguration>();
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
}
