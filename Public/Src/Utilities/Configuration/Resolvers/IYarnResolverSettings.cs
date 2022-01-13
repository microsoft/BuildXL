// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Yarn resolver
    /// </summary>
    public interface IYarnResolverSettings : IJavaScriptResolverSettings
    {
        /// <summary>
        /// The location of yarn.  If not provided, BuildXL will try to look for it under PATH.
        /// </summary>
        /// <remarks>
        ///  A file pointing to yarn can be provided, or alternatively a collection of directories to look for yarn.
        /// </remarks>
        DiscriminatingUnion<FileArtifact, IReadOnlyList<DirectoryArtifact>> YarnLocation { get; }
    }
}
