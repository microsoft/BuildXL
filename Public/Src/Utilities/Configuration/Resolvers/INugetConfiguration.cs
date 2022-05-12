// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for resolver nuget.exe
    /// </summary>
    public partial interface INugetConfiguration : IArtifactLocation
    {
        /// <summary>
        /// The download timeout, in minutes, for each NuGet download pip
        /// </summary>
        int? DownloadTimeoutMin { get; }
    }
}
