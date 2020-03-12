// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Rush resolver
    /// </summary>
    public interface IRushResolverSettings : IProjectGraphResolverSettings
    {
        /// <summary>
        /// The path to node.exe to use for discovering the Rush graph
        /// </summary>
        /// <remarks>
        /// If not provided, node.exe will be looked in PATH
        /// </remarks>
        FileArtifact? NodeExeLocation { get; }
    }
}
