// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Nuget resolvers
    /// </summary>
    public partial interface IDownloadResolverSettings : IResolverSettings
    {
        /// <summary>
        /// Items to download
        /// </summary>
        IReadOnlyList<IDownloadFileSettings> Downloads { get; }
    }
}
