// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
