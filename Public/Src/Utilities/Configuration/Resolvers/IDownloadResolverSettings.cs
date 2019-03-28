// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
