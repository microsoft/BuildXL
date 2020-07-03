// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class SandboxContainerConfiguration : ISandboxContainerConfiguration
    {
        /// <nodoc />
        public SandboxContainerConfiguration()
        {
        }

        /// <nodoc />
        public SandboxContainerConfiguration(ISandboxContainerConfiguration template)
        {
            RunInContainer = template.RunInContainer;
            ContainerIsolationLevel = template.ContainerIsolationLevel;
        }

        /// <inheritdoc/>
        public bool? RunInContainer { get; set; }

        /// <inheritdoc/>
        public ContainerIsolationLevel? ContainerIsolationLevel { get; set; }
    }
}
