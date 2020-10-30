// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for a JavaScript-based front-end.
    /// </summary>
    public class JavaScriptResolverSettings : ResolverSettings, IJavaScriptResolverSettings
    {
        /// <nodoc/>
        public JavaScriptResolverSettings()
        {
            // We allow the source directory to be writable by default
            AllowWritableSourceDirectory = true;
        }

        /// <nodoc/>
        public JavaScriptResolverSettings(
            IJavaScriptResolverSettings resolverSettings,
            PathRemapper pathRemapper)
            : base(resolverSettings, pathRemapper)
        {
            Root = pathRemapper.Remap(resolverSettings.Root);
            ModuleName = resolverSettings.ModuleName;
            UntrackedDirectoryScopes = resolverSettings.UntrackedDirectoryScopes;
            UntrackedFiles = resolverSettings.UntrackedFiles;
            UntrackedDirectories = resolverSettings.UntrackedDirectories;
            UntrackedGlobalDirectoryScopes = resolverSettings.UntrackedGlobalDirectoryScopes;
            Environment = resolverSettings.Environment;
            KeepProjectGraphFile = resolverSettings.KeepProjectGraphFile;
            NodeExeLocation = resolverSettings.NodeExeLocation;
            AdditionalOutputDirectories = resolverSettings.AdditionalOutputDirectories;
            Execute = resolverSettings.Execute;
            CustomCommands = resolverSettings.CustomCommands;
            Exports = resolverSettings.Exports;
            WritingToStandardErrorFailsExecution = resolverSettings.WritingToStandardErrorFailsExecution;
            DoubleWritePolicy = resolverSettings.DoubleWritePolicy;
        }

        /// <inheritdoc/>
        public AbsolutePath Root { get; set; }

        /// <inheritdoc/>
        public string ModuleName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<DirectoryArtifact, RelativePath>> UntrackedDirectoryScopes { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<FileArtifact> UntrackedFiles { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<DirectoryArtifact, RelativePath>> UntrackedDirectories { get; set; }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, EnvironmentData> Environment { get; set; }

        /// <inheritdoc/>
        public bool? KeepProjectGraphFile { get; set; }

        /// <inheritdoc/>
        public FileArtifact? NodeExeLocation { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> AdditionalOutputDirectories { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptCommand, IJavaScriptCommandGroupWithDependencies, IJavaScriptCommandGroup>> Execute { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IExtraArgumentsJavaScript> CustomCommands { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IJavaScriptExport> Exports { get; set; }

        /// <inheritdoc/>
        public bool? WritingToStandardErrorFailsExecution { get; set;  }

        /// <inheritdoc/>
        public bool? BlockWritesUnderNodeModules { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<RelativePath> UntrackedGlobalDirectoryScopes { get; set; }

        /// <inheritdoc />
        public RewritePolicy? DoubleWritePolicy { get; set; }
    }
}
