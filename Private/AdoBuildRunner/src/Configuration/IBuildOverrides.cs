// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace AdoBuildRunner.Configuration
{
    /// <summary>
    /// Configuration used by the build runner to apply overrides to the build 
    /// </summary>
    public interface IBuildOverrides
    {
        /// <summary>
        /// Additional command line arguments for the build runner, appended to the build command line after any customer arguments
        /// </summary>
        string? AdditionalBuildRunnerArguments { get; }

        /// <summary>
        /// Additional command line arguments, appended to the build command line after any customer arguments
        /// </summary>
        string? AdditionalCommandLineArguments { get; }
    }
}
