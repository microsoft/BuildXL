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
        /// The identifier for the ring
        /// </summary>
        public required string Name { get; init; }
        
        /// <summary>
        /// An optional description
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Tools available for this ring
        /// </summary>
        public required IReadOnlyDictionary<BuildTool, ToolDeployment> Tools { get; init; }
    }

    /// <summary>
    /// An override to the default version of a tool that would be resolved for a build based on its selected ring
    /// </summary>
    public class DeploymentOverride
    {
        /// <summary>
        /// Optional comment describing the override
        /// </summary>
        public string? Comment { get; init; }

        /// <summary>
        /// The exception is applied to builds running under the repository with this name
        /// </summary>
        public required string Repository { get; init; }

        /// <summary>
        /// If this is defined, the exception applies only to the specified pipelines
        /// </summary>
        public IReadOnlyList<int>? PipelineIds { get; init; }

        /// <summary>
        /// The overrides for this exception
        /// </summary>
        public required IReadOnlyDictionary<BuildTool, ToolDeployment> Tools { get; init; }
    }

    /// <summary>
    /// The main configuration object
    /// </summary>
    public class DeploymentConfiguration
    {
        /// <nodoc />
        public required IReadOnlyList<RingDefinition> Rings { get; init; }

        /// <nodoc />
        public IReadOnlyList<DeploymentOverride>? Overrides { get; init; }
    }
}
