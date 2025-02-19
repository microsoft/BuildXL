// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller.Config
{
    /// <summary>
    /// An override to the default version of a tool that would be resolved for a build based on its selected ring
    /// This is only used by the installers that support overrides
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

    public class OverrideConfiguration
    {
        /// <summary>
        /// A list of overrides to the default configuration
        /// </summary>
        public required IReadOnlyList<DeploymentOverride> Overrides { get; init; }
    }
}
