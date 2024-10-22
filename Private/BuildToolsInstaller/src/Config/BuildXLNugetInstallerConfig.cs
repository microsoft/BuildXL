// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller.Config
{
    /// <summary>
    /// Specific configuration for the BuildXL installer
    /// Consumed from a JSON file
    /// </summary>
    public class BuildXLNugetInstallerConfig
    {
        /// <summary>
        /// A specific version to install
        /// TODO: Support 'Latest' here
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Use this feed to download BuildXL from instead of inferring one from the ADO environment
        /// </summary>
        public string? FeedOverride { get; set; }

        /// <summary>
        /// Set the endpoint where the global config is downloaded from
        /// This is here for development purposes, should be removed later
        /// when the download location stabilizes
        /// </summary>
        public string? Internal_GlobalConfigOverride { get; set; }
    }
}
