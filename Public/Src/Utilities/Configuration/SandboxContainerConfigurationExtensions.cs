// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Set of extension methods for <see cref="ISandboxContainerConfiguration"/>.
    /// </summary>
    public static class SandboxContainerConfigurationExtensions
    {
        // Defaults

        /// <nodoc/>
        public const bool DefaultRunInContainer = false;

        /// <nodoc/>
        public const ContainerIsolationLevel DefaultContainerIsolationLevel = Configuration.ContainerIsolationLevel.IsolateAllOutputs;

        // Extension methods

        /// <nodoc/>
        public static bool RunInContainer(this ISandboxContainerConfiguration configuration) => 
            configuration.RunInContainer ?? DefaultRunInContainer;

        /// <nodoc/>
        public static ContainerIsolationLevel ContainerIsolationLevel(this ISandboxContainerConfiguration configuration) => 
            configuration.ContainerIsolationLevel ?? DefaultContainerIsolationLevel;

    }
}
