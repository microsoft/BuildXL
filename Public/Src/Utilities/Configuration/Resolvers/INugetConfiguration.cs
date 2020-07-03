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
        /// Optional credential helper to use for NuGet
        /// </summary>
        IReadOnlyList<IArtifactLocation> CredentialProviders { get; }
    }
}
