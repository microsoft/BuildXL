// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller.Config
{
    /// <summary>
    /// Deployment details for a single tool
    /// </summary>
    /// <remarks>
    /// For now this just encapsulates a version,
    /// but leaving it as an object for forwards extensibility
    /// </remarks>
    public class ToolDeployment
    {
        public required string Version { get; set; }
    }

    public class RingDefinition
    {
        /// <summary>
        /// An optional description
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// The version of the tool that is deployed in this ring
        /// </summary>
        public required string Version { get; init; }

        /// <summary>
        /// When true, the tool will be installed even if it is already present in the cache
        /// </summary>
        public bool IgnoreCache { get; init; }
    }

    /// <summary>
    /// The main configuration object
    /// </summary>
    public class DeploymentConfiguration
    {
        /// <summary>
        /// A mapping of ring names to their RingDefinitions
        /// </summary>
        public required IReadOnlyDictionary<string, RingDefinition> Rings { get; init; }

        /// <summary>
        /// A mapping of arbitrary keys to the Nuget package names that will be retrieved
        /// This is used when the package changes in different environments, e.g., the operating system
        /// the installer is running on.
        /// </summary>
        public required IReadOnlyDictionary<string, string> Packages { get; init; }

        /// <summary>
        /// A name of a ring to fall back if the version descriptor is not specified
        /// </summary>
        public required string Default { get; init; }
    }
}
