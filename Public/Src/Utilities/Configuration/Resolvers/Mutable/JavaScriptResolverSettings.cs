// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

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
            Environment = resolverSettings.Environment;
            KeepProjectGraphFile = resolverSettings.KeepProjectGraphFile;
            NodeExeLocation = resolverSettings.NodeExeLocation;
            AdditionalOutputDirectories = resolverSettings.AdditionalOutputDirectories;
            Execute = resolverSettings.Execute;
            CustomCommands = resolverSettings.CustomCommands;
            Exports = resolverSettings.Exports;
        }

        /// <inheritdoc/>
        public AbsolutePath Root { get; set; }

        /// <inheritdoc/>
        public string ModuleName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DirectoryArtifact> UntrackedDirectoryScopes { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<FileArtifact> UntrackedFiles { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DirectoryArtifact> UntrackedDirectories { get; set; }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, DiscriminatingUnion<string, UnitValue>> Environment { get; set; }

        /// <inheritdoc/>
        public bool? KeepProjectGraphFile { get; set; }

        /// <inheritdoc/>
        public FileArtifact? NodeExeLocation { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> AdditionalOutputDirectories { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptCommand>> Execute { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IExtraArgumentsJavaScript> CustomCommands { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IJavaScriptExport> Exports { get; set; }
    }
}
