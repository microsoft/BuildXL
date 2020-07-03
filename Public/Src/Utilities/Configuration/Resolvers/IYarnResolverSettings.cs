// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        FileArtifact? YarnLocation { get; }
    }
}
