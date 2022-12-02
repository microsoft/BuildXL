// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Text;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Resolvers;
using BuildXL.Utilities.Configuration.Resolvers.Mutable;
using JetBrains.Annotations;

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
            if (resolverSettings.Environment == null)
            {
                var allEnvironmentVariables = Environment.GetEnvironmentVariables();
                processEnvironmentUsed = true;
                trackedEnv = new Dictionary<string, string>(allEnvironmentVariables.Count, OperatingSystemHelper.EnvVarComparer);
                foreach (var envVar in allEnvironmentVariables.Keys)
                {
                    object value = allEnvironmentVariables[envVar];
                    trackedEnv[envVar.ToString()] = value.ToString();
                }

                passthroughEnv = CollectionUtilities.EmptyArray<string>();
                return;
            }

            processEnvironmentUsed = false;
            var trackedList = new Dictionary<string, string>(OperatingSystemHelper.EnvVarComparer);
            var passthroughList = new List<string>();
            var builder = new StringBuilder();

            foreach (var kvp in resolverSettings.Environment)
            {
                builder.Clear();
                var valueOrPassthrough = kvp.Value?.GetValue();
                if (valueOrPassthrough == null || valueOrPassthrough is not UnitValue)
                {
                    trackedList.Add(kvp.Key, ProcessEnvironmentData(kvp.Value, pathTable, builder));
                }
                else
                {
                    passthroughList.Add(kvp.Key);
                }
            }

            trackedEnv = trackedList;
            passthroughEnv = passthroughList;
        }

        private static string ProcessEnvironmentData([CanBeNull] EnvironmentData environmentData, PathTable pathTable, StringBuilder s)
        {
            if (environmentData == null)
            {
                return null;
            }

            object data = environmentData.GetValue();
            Contract.Assert(data is not UnitValue);

            DoProcessEnvironmentData(data, pathTable, s);

            return s.ToString();
        }

        private static void DoProcessEnvironmentData(object data, PathTable pathTable, StringBuilder s)
        {  
            switch (data)
            {
                case string stringData:
                    s.Append(stringData);
                    break;
                case IImplicitPath pathData:
                    s.Append(pathData.Path.ToString(pathTable));
                    break;
                case PathAtom atom:
                    s.Append(atom.ToString(pathTable.StringTable));
                    break;
                case RelativePath relative:
                    s.Append(relative.ToString(pathTable.StringTable));
                    break;
                case int integer:
                    s.Append(integer.ToString(CultureInfo.InvariantCulture));
                    break;
                case ICompoundEnvironmentData compoundData:
                    var separator = compoundData.Separator ?? CompoundEnvironmentData.DefaultSeparator;
                    var contents = compoundData.Contents ?? CollectionUtilities.EmptyArray<EnvironmentData>();

                    int i = 0;
                    foreach (EnvironmentData member in contents)
                    {
                        DoProcessEnvironmentData(member.GetValue(), pathTable, s);
                        
                        if (i < contents.Count - 1) 
                        {
                            s.Append(separator);
                        }

                        i++;
                    }
                    break;
                default:
                    Contract.Assert(false, $"Unexpected data {data.GetType()}");
                    break;
            }
        }
    }
}
