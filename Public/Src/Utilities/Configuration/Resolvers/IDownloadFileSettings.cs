// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// The name of the value that points to the downloaded content for other resolvers to consume
        /// </summary>
        /// <remarks>
        /// Defaults to 'download' if not specified. This value will be exposed with type 'File'.
        /// </remarks>
        string DownloadedValueName { get; }

        /// <summary>
        /// The name of the value that points to the extracted content of the downloaded file for other resolvers to consume
        /// </summary>
        /// <remarks>
        /// Defaults to 'extracted' if not specified. This value will be exposed with type 'StaticDirectory'. 
        /// </remarks>
        string ExtractedValueName { get; }

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
