// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        // was DownloadMismatchedHash = 11805,
        // was StartDownload = 11806,
        // was DownloadFailed = 11807,
        // was ErrorPreppingForDownload = 11808,
        // was ErrorCheckingIncrementality = 11809,
        // was ErrorStoringIncrementality = 11810,
        // was ErrorExtractingArchive = 11811,
        // was ErrorNothingExtracted = 11812,
        // was ErrorValidatingPackage = 11813,
        // was ErrorListingPackageContents = 11814,
        // was DownloadManifestDoesNotMatch = 11815,
        // was ExtractManifestDoesNotMatch = 11816,
        // was Downloaded = 11817,
        NameContainsInvalidCharacters = 11818,
        // was AuthenticationViaCredentialProviderFailed = 11819,
        // was AuthenticationViaIWAFailed = 11820,
        ContextStatistics = 11821,
        BulkStatistic = 11822,
    }
}
