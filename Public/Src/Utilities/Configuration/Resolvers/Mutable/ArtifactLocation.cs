// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class ArtifactLocation : IArtifactLocation
    {
        /// <nodoc />
        public ArtifactLocation()
        {
        }

        /// <nodoc />
        public ArtifactLocation(IArtifactLocation template)
        {
            Version = template.Version;
            ToolUrl = template.ToolUrl;
            Hash = template.Hash;
        }

        /// <inheritdoc />
        public string Version { get; set; }

        /// <inheritdoc />
        public string ToolUrl { get; set; }

        /// <inheritdoc />
        public string Hash { get; set; }
    }
}
