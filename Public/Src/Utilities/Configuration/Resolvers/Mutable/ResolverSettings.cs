// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Text;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Resolvers;
using BuildXL.Utilities.Configuration.Resolvers.Mutable;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class ResolverSettings : IResolverSettings
    {
        /// <nodoc />
        public ResolverSettings()
        {
        }

        /// <nodoc />
        public ResolverSettings(IResolverSettings template, PathRemapper pathRemapper)
        {
            Contract.Requires(template != null);
            Contract.Requires(pathRemapper != null);

            Kind = template.Kind;
            Name = template.Name;
            File = pathRemapper.Remap(template.File);
            Location = template.Location;
            AllowWritableSourceDirectory = template.AllowWritableSourceDirectory;
            RequestFullReparsePointResolving = template.RequestFullReparsePointResolving;
        }

        /// <inheritdoc />
        public string Kind { get; set; }

        /// <inheritdoc />
        public string Name { get; set; }

        /// <inheritdoc />
        public LineInfo Location { get; set; }

        /// <inheritdoc />
        public AbsolutePath File { get; set; }

        /// <inheritdoc />
        public bool AllowWritableSourceDirectory { get; set; }

        /// <inheritdoc />
        public bool RequestFullReparsePointResolving { get; set; }

        /// <summary>
        /// Polymorphic instantiation from template function
        /// </summary>
        /// <remarks>
        /// Each time we add a resolver, we'll have to add the instance here.
        /// </remarks>
        public static ResolverSettings CreateFromTemplate(IResolverSettings resolverSettings, PathRemapper pathRemapper)
        {
            Contract.Requires(resolverSettings != null);
            Contract.Requires(pathRemapper != null);

            switch (resolverSettings)
            {
                case IDScriptResolverSettings sourceResolver:
                    return new SourceResolverSettings(sourceResolver, pathRemapper);
                case IDefaultSourceResolverSettings defaultResolver:
                    return new DefaultSourceResolverSettings(defaultResolver, pathRemapper);
                case INugetResolverSettings nugetResolver:
                    return new NugetResolverSettings(nugetResolver, pathRemapper);
                case IDownloadResolverSettings downloadResolver:
                    return new DownloadResolverSettings(downloadResolver, pathRemapper);
                case IMsBuildResolverSettings msBuildResolver:
                    return new MsBuildResolverSettings(msBuildResolver, pathRemapper);
                case INinjaResolverSettings ninjaResolver:
                    return new NinjaResolverSettings(ninjaResolver, pathRemapper);
                case IRushResolverSettings rushResolver:
                    return new RushResolverSettings(rushResolver, pathRemapper);
                case IYarnResolverSettings yarnResolver:
                    return new YarnResolverSettings(yarnResolver, pathRemapper);
                case ICustomJavaScriptResolverSettings customYarnResolver:
                    return new CustomJavaScriptResolverSettings(customYarnResolver, pathRemapper);
                case ILageResolverSettings lageResolver:
                    return new LageResolverSettings(lageResolver, pathRemapper);
                case INxResolverSettings nxResolver:
                    return new NxResolverSettings(nxResolver, pathRemapper);
                case IDirectGraphResolverSettings directGraphResolver:
                    return new DirectGraphResolverSettings(directGraphResolver, pathRemapper);
                default:
                    Contract.Assume(false, "Unexpected type of resolver settings.");
                    return null;
            }
        }

        /// <inheritdoc />
        public void SetName(string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(string.IsNullOrEmpty(Name), "Expected name to only be set once if not set by default");
            Name = name;
        }

        /// <summary>
        /// Process the specified environment variables and split them into tracked environment variables and passthrough environment variables
        /// </summary>
        /// <remarks>
        /// When <paramref name="environment"/> is null, the current environment is defined as the tracked environment, with no passthroughs
        /// </remarks>
        public static void ComputeEnvironment(IReadOnlyDictionary<string, EnvironmentData> environment, PathTable pathTable, out IDictionary<string, string> trackedEnv, out ICollection<string> passthroughEnv, out bool processEnvironmentUsed)
        {
            // Do not use CollectionUtilities.EmptyArray<string>() because the caller can modify the collection.
            passthroughEnv = new List<string>();

            if (environment == null)
            {
                var allEnvironmentVariables = Environment.GetEnvironmentVariables();
                processEnvironmentUsed = true;
                trackedEnv = new Dictionary<string, string>(allEnvironmentVariables.Count, OperatingSystemHelper.EnvVarComparer);
                foreach (var envVar in allEnvironmentVariables.Keys)
                {
                    object value = allEnvironmentVariables[envVar];
                    trackedEnv[envVar.ToString()] = value.ToString();
                }

                return;
            }

            processEnvironmentUsed = false;
            trackedEnv = new Dictionary<string, string>(OperatingSystemHelper.EnvVarComparer);
            var builder = new StringBuilder();

            foreach (var kvp in environment)
            {
                builder.Clear();
                var valueOrPassthrough = kvp.Value?.GetValue();
                if (valueOrPassthrough == null || valueOrPassthrough is not UnitValue)
                {
                    trackedEnv.Add(kvp.Key, ProcessEnvironmentData(kvp.Value, pathTable, builder));
                }
                else
                {
                    passthroughEnv.Add(kvp.Key);
                }
            }
        }

        private static string ProcessEnvironmentData([MaybeNull] EnvironmentData environmentData, PathTable pathTable, StringBuilder s)
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
