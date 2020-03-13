// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

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

        /// <summary>
        /// Collection of additional output directories pips may write to
        /// </summary>
        /// <remarks>
        /// If a relative path is provided, it will be interpreted relative to every project root
        /// </remarks>
        IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> AdditionalOutputDirectories { get; }
    }
}
