// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Nuget.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        // reserved 11300 .. 11400 for nuget
        // Reserved LaunchingNugetExe = 11300,
        // Reserved CredentialProviderRequiresToolUrl = 11301,
        // Reserved NugetDownloadInvalidHash = 11302,
        NugetFailedToCleanTargetFolder = 11303,
        // Reserved NugetFailedWithNonZeroExitCode = 11304,
        // Reserved NugetFailedWithNonZeroExitCodeDetailed = 11305,
        // Reserved NugetFailedToListPackageContents = 11306,
        // Reserved NugetFailedToWriteConfigurationFile = 11307,
        // Reserved NugetFailedToWriteConfigurationFileForPackage = 11308,
        NugetFailedToWriteSpecFileForPackage = 11309,
        NugetFailedToReadNuSpecFile = 11310,
        // Reserved NugetFailedWithInvalidNuSpecXml = 11311,
        NugetPackageVersionIsInvalid = 11312,
        // Reserved NugetLaunchFailed = 11313,
        NugetStatistics = 11314,
        NugetPackagesRestoredCount = 11315,
        NugetPackagesAreRestored = 11316,
        NugetDependencyVersionWasNotSpecifiedButConfigOneWasChosen = 11317,
        NugetDependencyVersionWasPickedWithinRange = 11318,
        NugetDependencyVersionDoesNotMatch = 11319,
        NugetUnknownFramework = 11320,
        NugetPackageDownloadDetails = 11321,
        NugetFailedNuSpecFileNotFound = 11322,
        // Reserved NugetFailedDueToSomeWellKnownIssue = 11323,
        NugetRegenerateNugetSpecs = 11324,
        NugetRegeneratingNugetSpecs = 11325,
        NugetUnhandledError = 11326,
        ForcePopulateTheCacheOptionWasSpecified = 11327,
        NugetConcurrencyLevel = 11328,
        UsePackagesFromDisOptionWasSpecified = 11329,
        NugetFailedDownloadPackagesAndGenerateSpecs = 11330,
        NugetFailedDownloadPackage = 11331,
        NugetFailedGenerationResultFromDownloadedPackage = 11332,
        NugetFailedToWriteGeneratedSpecStateFile = 11333,
        NugetCannotReuseSpecOnDisk = 11334,
        NugetInspectionInitialization = 11335,
    }
}
