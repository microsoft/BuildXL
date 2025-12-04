// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace AdoBuildRunner.Configuration.Mutable
{
    // Data objects for deserializing build overrides configuration

    /// <inheritdoc />
    public class BuildOverrides : IBuildOverrides
    {
        /// <inheritdoc />
        public string? AdditionalBuildRunnerArguments { get; init; }

        /// <inheritdoc />
        public string? AdditionalCommandLineArguments { get; init; }
    }

    /// <nodoc />
    public class BuildOverridesRule
    {
        /// <summary>
        /// Optional comment describing the rule's purpose. This documents the rule in the configuration repo, and will also be logged when the rule is applied.
        /// </summary>
        public string? Comment { get; init; }

        /// <summary>
        /// The rule is applied to builds running under the repository with this name
        /// </summary>
        public required string Repository { get; init; }

        /// <summary>
        /// If this is defined, the rule applies only to the specified pipelines
        /// </summary>
        public IReadOnlyList<int>? PipelineIds { get; init; }

        /// <summary>
        /// If this is defined, the rule applies only to the builds with the specified invocation key
        /// This can be used if there are multiple builds in the same pipeline and we only want to target a specific one.
        /// The invocation key for a build can be retrieved from the argument passed to the build runner.
        /// 
        /// !! NOTE !! This value is generated based on the Job and Stage names, so it could change unexpectedly if these are modified (this should be rare).
        /// It should only be used when absolutely necessary to target a specific build within a pipeline.
        /// </summary>
        public IReadOnlyList<string>? InvocationKeys { get; init; }

        /// <summary>
        /// Overrides to apply if the build matches this rule
        /// </summary>
        public required BuildOverrides Overrides { get; init; }
    }

    /// <nodoc />
    public class OverridesConfiguration
    {
        /// <summary>
        /// A list of overrides to the default configuration
        /// </summary>
        public required IReadOnlyList<BuildOverridesRule> Rules { get; init; }
    }
}
