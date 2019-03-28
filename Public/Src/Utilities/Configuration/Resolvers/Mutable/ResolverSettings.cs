// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

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
                case ICMakeResolverSettings cmakeResolver:
                    return new CMakeResolverSettings(cmakeResolver, pathRemapper);
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
