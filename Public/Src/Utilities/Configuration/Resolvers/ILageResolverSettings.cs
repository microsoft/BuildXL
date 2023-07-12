// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Lage resolver
    /// </summary>
    public interface ILageResolverSettings : IJavaScriptResolverSettings
    {
        /// <summary>
        /// The location of NPM.  If not provided, BuildXL will try to look for it under PATH.
        /// </summary>
        /// <remarks>
        /// Npm is used to get Lage during graph construction
        /// </remarks>
        FileArtifact? NpmLocation { get; }
    }
}
