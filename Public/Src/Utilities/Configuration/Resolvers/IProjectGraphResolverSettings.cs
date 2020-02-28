// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Resolvers;

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
        /// The enlistment root. This may not be the location where parsing starts
        /// <see cref="RootTraversal"/> can override that behavior.
        /// </summary>
        AbsolutePath Root { get; }

        /// <summary>
        /// The directory where the resolver starts parsing the enlistment
        /// (including all sub-directories recursively). Not necessarily the
        /// same as <see cref="Root"/> for cases where the codebase to process
        /// starts in a subdirectory of the enlistment.
        /// </summary>
        /// <remarks>
        /// If this is not specified, it will default to <see cref="Root"/>
        /// </remarks>
        AbsolutePath RootTraversal { get; }

        /// <summary>
        /// The name of the module exposed to other DScript projects that will include all Rush projects found under
        /// the enlistment
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// The environment that is exposed to the resolver. If not specified, the process environment is used.
        /// </summary>
        IReadOnlyDictionary<string, DiscriminatingUnion<string, UnitValue>> Environment { get; }

        /// <summary>
        /// For debugging purposes. If this field is true, the JSON representation of the project graph file is not deleted
        /// </summary>
        bool? KeepProjectGraphFile { get; }
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
        public static void ComputeEnvironment(this IProjectGraphResolverSettings rushResolverSettings, out IDictionary<string, string> trackedEnv, out ICollection<string> passthroughEnv, out bool processEnvironmentUsed)
        {
            if (rushResolverSettings.Environment == null)
            {
                var allEnvironmentVariables = Environment.GetEnvironmentVariables();
                processEnvironmentUsed = true;
                trackedEnv = new Dictionary<string, string>(allEnvironmentVariables.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var envVar in allEnvironmentVariables.Keys)
                {
                    object value = allEnvironmentVariables[envVar];
                    trackedEnv[envVar.ToString()] = value.ToString();
                }

                passthroughEnv = CollectionUtilities.EmptyArray<string>();
                return;
            }

            processEnvironmentUsed = false;
            var trackedList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var passthroughList = new List<string>();

            foreach (var kvp in rushResolverSettings.Environment)
            {
                var valueOrPassthrough = kvp.Value?.GetValue();
                if (valueOrPassthrough == null || valueOrPassthrough is string)
                {
                    trackedList.Add(kvp.Key, (string)valueOrPassthrough);
                }
                else
                {
                    passthroughList.Add(kvp.Key);
                }
            }

            trackedEnv = trackedList;
            passthroughEnv = passthroughList;
        }
    }
}
