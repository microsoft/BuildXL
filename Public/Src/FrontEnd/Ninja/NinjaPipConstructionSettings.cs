// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Groups settings for the NinjaPipConstructor 
    /// </summary>
    internal struct NinjaPipConstructionSettings
    {
        /// <inheritdoc cref="INinjaResolverSettings.RemoveAllDebugFlags" />
        public bool SuppressDebugFlags { get; init; }

        /// <summary>
        /// Environment exposed to the pip
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> UserDefinedEnvironment { get; init; }

        /// <summary>
        /// Passthrough environment variables for the pip
        /// </summary>
        public IEnumerable<string> UserDefinedPassthroughVariables { get; init; }

        /// <inheritdoc cref="IUntrackingSettings" />
        public IUntrackingSettings UntrackingSettings { get; init; }

        /// <inheritdoc cref="IProjectGraphResolverSettings.AdditionalOutputDirectories" />
        public IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> AdditionalOutputDirectories { get; init; }
    }
}
