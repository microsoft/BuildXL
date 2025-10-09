// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
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
    }
}
