// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Nx resolver
    /// </summary>
    public interface INxResolverSettings : IJavaScriptResolverSettings
    {
        /// <summary>
        /// The location of Nx libraries
        /// </summary>
        DirectoryArtifact NxLibLocation { get; }
    }

    /// <nodoc/>
    public static class NxResolverSettingsExtensions
    {
        /// <summary>
        /// Gets the location of Nx internal folder '.nx', located at the root of the workspace
        /// </summary>
        public static AbsolutePath GetNxInternalFolder(this INxResolverSettings settings, PathTable pathTable) =>
            settings.Root.IsValid
                ? settings.Root.Combine(pathTable, ".nx")
                : AbsolutePath.Invalid;
    }
}
