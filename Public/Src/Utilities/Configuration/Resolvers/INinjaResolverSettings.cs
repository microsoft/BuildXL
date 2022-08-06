// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Ninja resolver
    /// </summary>
    public interface INinjaResolverSettings : IProjectGraphResolverSettings
    {
        /// <summary>
        /// Targets to resolve
        /// </summary>
        IReadOnlyList<string> Targets { get; }

        /// <summary>
        /// The build file, typically build.ninja. If null, "{ProjectRoot}/build.ninja" is used
        /// </summary>
        AbsolutePath SpecFile { get; }
    }
}
