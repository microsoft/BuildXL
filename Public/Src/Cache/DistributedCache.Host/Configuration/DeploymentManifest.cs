// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

#nullable disable

namespace BuildXL.Cache.Host.Configuration
{
    public class DeploymentManifest
    {
        /// <summary>
        /// Map from drop url to drop's <see cref="LayoutSpec"/>
        /// </summary>
        public Dictionary<string, LayoutSpec> Drops { get; set; } = new Dictionary<string, LayoutSpec>();

        /// <summary>
        /// The content id of the deployment manifest.
        /// This is not persisted but is instead computed based on the hash of deployment manifest.
        /// This value should be set on <see cref="LauncherManifest.DeploymentManifestChangeId"/>
        /// </summary>
        public string ChangeId;

        public class FileSpec
        {
            /// <summary>
            /// The hash of the file
            /// </summary>
            public string Hash { get; set; }

            /// <summary>
            /// The size of the file
            /// </summary>
            public long Size { get; set; }

            /// <summary>
            /// Url to use to download the file.
            /// NOTE: This may be missing if file is not yet uploaded
            /// </summary>
            public string DownloadUrl { get; set; }
        }

        /// <summary>
        /// Map from file relative path in drop to content hash
        /// </summary>
        public class LayoutSpec : Dictionary<string, FileSpec>
        {
        }
    }
}
