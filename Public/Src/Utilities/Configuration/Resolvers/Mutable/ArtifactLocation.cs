// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
