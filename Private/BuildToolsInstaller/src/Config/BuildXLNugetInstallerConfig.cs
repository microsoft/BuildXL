// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace BuildToolsInstaller.Config
{
    /// <summary>
    /// On distributed builds, an 'Orchestrator' agent resolves the version to use,
    /// and a 'Worker' agent retrieves the resolved version from the build properties
    /// </summary>
    public enum DistributedRole
    {
        Orchestrator,
        Worker
    }

    /// <summary>
    /// Specific configuration for the BuildXL installer
    /// Consumed from a JSON file
    /// </summary>
    public class BuildXLNugetInstallerConfig
    {
        /// <summary>
        /// A specific version to install
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Use this feed to download BuildXL from instead of inferring one from the ADO environment
        /// </summary>
        public string? FeedOverride { get; set; }

        /// <summary>
        /// If worker mode is true, the installer will poll the build properties to resolve
        /// the BuildXL version. If it's false, the installer will push the resolved version
        /// to the build properties
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DistributedRole? DistributedRole {  get; set; }

        /// <summary>
        /// Used as a prefix for all build property keys. Used to disambiguate jobs within the same ADO build 
        /// </summary>
        public string? InvocationKey { get; set; }

        /// <summary>
        /// Time to wait for the orchestrator information. Only applies in Worker distributed mode.
        /// </summary>
        public int? WorkerTimeoutMin {  get; set; }

        /// <summary>
        /// Set the endpoint where the global config is downloaded from
        /// This is here for development purposes, should be removed later
        /// when the download location stabilizes
        /// </summary>
        public string? Internal_GlobalConfigOverride { get; set; }
    }
}
