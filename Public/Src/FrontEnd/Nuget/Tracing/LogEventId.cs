// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        LaunchingNugetExe = 11300,
        CredentialProviderRequiresToolUrl,
        NugetDownloadInvalidHash,
        NugetFailedToCleanTargetFolder,
        NugetFailedWithNonZeroExitCode,
        NugetFailedWithNonZeroExitCodeDetailed,
        NugetFailedToListPackageContents,
        NugetFailedToWriteConfigurationFile,
        NugetFailedToWriteConfigurationFileForPackage,
        NugetFailedToWriteSpecFileForPackage,
        NugetFailedToReadNuSpecFile,
        NugetFailedWithInvalidNuSpecXml,
        NugetPackageVersionIsInvalid,
        NugetLaunchFailed,
        NugetStatistics,
        NugetPackagesRestoredCount,
        NugetPackagesAreRestored,
        NugetDependencyVersionWasNotSpecifiedButConfigOneWasChosen,
        NugetDependencyVersionWasPickedWithinRange,
        NugetDependencyVersionDoesNotMatch,
        NugetUnknownFramework,
        NugetPackageDownloadDetails,
        NugetFailedNuSpecFileNotFound,
        NugetFailedDueToSomeWellKnownIssue,
        NugetRegenerateNugetSpecs,
        NugetRegeneratingNugetSpecs,
        NugetUnhandledError,
        ForcePopulateTheCacheOptionWasSpecified,
        NugetConcurrencyLevel,
        UsePackagesFromDisOptionWasSpecified,
        NugetFailedDownloadPackagesAndGenerateSpecs,
        NugetFailedDownloadPackage,
        NugetFailedGenerationResultFromDownloadedPackage,
        NugetFailedToWriteGeneratedSpecStateFile,
    }
}
