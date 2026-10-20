// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Text;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Configuration.Resolvers;
using BuildXL.Utilities.Configuration.Resolvers.Mutable;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Base settings for a resolver that can build a an environment-dependent project graph by exploring the filesystem from a given root
    /// </summary>
    /// <remarks>
    /// Not intended to use directly in a configuration file, just grouping common fields for other more specific resolver settings to use
    /// </remarks>
    public interface IProjectGraphResolverSettings : IResolverSettings, IUntrackingSettings
    {
        /// <summary>
        /// The enlistment root. This may not be the location where parsing starts and a root traversal
        /// can override that behavior.
        /// </summary>
        AbsolutePath Root { get; }

        /// <summary>
        /// The name of the module exposed to other DScript projects that will include all Rush projects found under
        /// the enlistment
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// The environment that is exposed to the resolver. If not specified, the process environment is used.
        /// </summary>
        IReadOnlyDictionary<string, EnvironmentData> Environment { get; }

        /// <summary>
        /// Collection of additional output directories pips may write to
        /// </summary>
        /// <remarks>
        /// If a relative path is provided, it will be interpreted relative to every project root
        /// </remarks>
        IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> AdditionalOutputDirectories { get; }

        /// <summary>
        /// For debugging purposes. If this field is true, the JSON representation of the project graph file is not deleted
        /// </summary>
        bool? KeepProjectGraphFile { get; }

        /// <summary>
        /// Turns on file access reporting for the project graph construction phase
        /// </summary>
        bool? EnableProjectGraphVerboseLogging { get; }
    }

    /// <nodoc/>
    public static class ProjectGraphResolverSettingsSettingsExtensions
    {
        /// <summary>
        /// Process <see cref="IProjectGraphResolverSettings.Environment"/> and split the specified environment variables that need to be exposed and tracked from the passthrough environment variables
        /// </summary>
        /// <remarks>
        /// When <see cref="IProjectGraphResolverSettings.Environment"/> is null, the current environment is defined as the tracked environment, with no passthroughs
        /// </remarks>
        public static void ComputeEnvironment(this IProjectGraphResolverSettings resolverSettings, PathTable pathTable, out IDictionary<string, string> trackedEnv, out ICollection<string> passthroughEnv, out bool processEnvironmentUsed)
        {
            ResolverSettings.ComputeEnvironment(resolverSettings.Environment, pathTable, out trackedEnv, out passthroughEnv, out processEnvironmentUsed);
        }
    }
}
