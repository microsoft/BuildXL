// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Container related configuration
    /// </summary>
    /// <remarks>
    /// These values can be configured on a per-pip basis, but the ones defined here define the defaults
    /// </remarks>
    public interface ISandboxContainerConfiguration
    {
        /// <summary>
        /// Whether pips should run in an isolated container.
        /// </summary>
        bool? RunInContainer { get; }

        /// <summary>
        /// The level of isolation for a given process when running in a container
        /// </summary>
        /// <remarks>
        /// If the process is not configured to run in a container, this value is just ignored
        /// </remarks>
        ContainerIsolationLevel? ContainerIsolationLevel { get; }
    }
}
