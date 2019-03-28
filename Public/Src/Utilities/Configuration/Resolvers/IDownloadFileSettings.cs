// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Nuget package definition
    /// </summary>
    public partial interface IDownloadFileSettings
    {
        /// <summary>
        /// The id of the package
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// Url of the download
        /// </summary>
        string Url { get; }

        /// <summary>
        /// Optional filename. By default the filename for the download is determined from the URL, but can be overridden when the url is obscure.
        /// </summary>
        string FileName { get; }

        /// <summary>
        /// Optional declaration of the archive type that allows us 
        /// </summary>
        DownloadArchiveType ArchiveType { get; }

        /// <summary>
        /// Optional hash of the downloaded file to ensure safe robust builds and correctness. When specified the download is validated against this hash.
        /// </summary>
        string Hash { get; }
    }
}
