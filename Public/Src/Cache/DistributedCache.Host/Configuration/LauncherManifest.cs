// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

#nullable disable

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Describes drops and files (with download urls) present in a deployment along with tools to launch
    /// </summary>
    public class LauncherManifest
    {
        /// <summary>
        /// Identifier used for comparing launch manifests
        /// </summary>
        public string ContentId { get; set; }

        /// <summary>
        /// Identifier used for determining if service should be requeried
        /// NOTE: This serves essentially the same purpose as <see cref="ContentId"/> but tracks more coarse data
        /// which requires less computation (namely the hash of the global deployment manifest). It changes somewhat more often
        /// than <see cref="ContentId"/> since unrelated deployment changes will trigger this to change.
        /// </summary>
        public string DeploymentManifestChangeId { get; set; }

        /// <summary>
        /// Gets where deployment content is fully available
        /// </summary>
        public bool IsComplete { get; set; }

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
        public ServiceLaunchConfiguration Tool { get; set; }
    }
}
