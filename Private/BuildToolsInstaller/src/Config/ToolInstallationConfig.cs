// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace BuildToolsInstaller.Config
{
    /// <summary>
    /// Data object for the configuration file, which contains an array of tools to install
    /// </summary>
    public class ToolsToInstall
    {
        /// <summary>
        /// The list of tools to install
        /// </summary>
        public required IReadOnlyList<ToolInstallationConfig> Tools { get; init; }
    }

    /// <summary>
    /// Specifies which version of a tool to install, optionally giving some options to the tool-specific installer
    /// </summary>
    public class ToolInstallationConfig
    {
        /// <summary>
        /// Because the version specification is optional, and every tool might have a different one for
        /// the default case, we use this moniker across the board for the default installation path, so the 
        /// external consumers can always rely on it to access the installed tool without knowing the 
        /// default specification for each particular tool.
        /// </summary>
        public const string DefaultVersionMoniker = "default";

        /// <summary>
        /// The tool to install
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BuildTool Tool { get; init; }

        /// <summary>
        /// A version 'specification', from which the version to install will be resolved.
        /// This is typically a ring name, but some tools might allow explicit versions to be specified.
        /// </summary>
        public string? Version { get; init; }

        /// <summary>
        /// A key from which to select the package name from the configuration.
        /// If not specified, it defaults to the OS name (namely: "Linux", "Windows")
        /// </summary>
        public required string PackageSelector { get; init; }

        /// <summary>
        /// The name of the environment variable that will hold the path to the installed tool
        /// </summary>
        public required string OutputVariable { get; init; }

        /// <summary>
        /// Each tool is responsible for interpreting this value, which might for instance point to an additional
        /// configuration file, or hold some serialized arguments to be used for the installer.
        /// </summary>
        public string? AdditionalConfiguration { get; init; }

        /// <summary>
        /// If true, the tool will be installed even if it is already present in the cache
        /// </summary>
        public bool IgnoreCache { get; init; }
    }
}
