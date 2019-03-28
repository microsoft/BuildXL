// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Download.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11800 .. 11900 for Download front-end
        DownloadFrontendMissingUrl = 11800,
        DownloadFrontendMissingModuleId = 11801,
        DownloadFrontendDuplicateModuleId = 11802,
        DownloadFrontendInvalidUrl = 11803,
        DownloadFrontendHashValueNotValidContentHash = 11804,
        DownloadMismatchedHash = 11805,
        StartDownload = 11806,
        DownloadFailed = 11807,
        ErrorPreppingForDownload = 11808,
        ErrorCheckingIncrementality = 11809,
        ErrorStoringIncrementality = 11810,
        ErrorExtractingArchive = 11811,
        ErrorNothingExtracted = 11812,
        ErrorValidatingPackage = 11813,
        ErrorListingPackageContents = 11814,
        DownloadManifestDoesNotMatch = 11815,
        ExtractManifestDoesNotMatch = 11816,
        Downloaded = 11817,
    }
}
