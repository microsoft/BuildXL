// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Cache.Host.Configuration
{
    public class DeploymentManifest
    {
        /// <summary>
        /// Map from drop url to drop's <see cref="LayoutSpec"/>
        /// </summary>
        public Dictionary<string, LayoutSpec> Drops { get; set; } = new Dictionary<string, LayoutSpec>();

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
        }

        /// <summary>
        /// Map from file relative path in drop to content hash
        /// </summary>
        public class LayoutSpec : Dictionary<string, FileSpec>
        {
        }
    }
}
