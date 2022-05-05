// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#pragma warning disable SA1600 // Element must be documented
#nullable enable

namespace BuildXL.FrontEnd.Nuget.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("NugetLogger")]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log { get; } = new LoggerImpl();

        [GeneratedEvent(
            (ushort)LogEventId.NugetFailedToCleanTargetFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package nuget://{id}/{version} could not be downloaded because folder '{targetLocation}' could not be cleaned: {message}")]
        public abstract void NugetFailedToCleanTargetFolder(LoggingContext context, string id, string version, string targetLocation, string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetInspectionInitialization,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (ushort)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Nuget inspection info: {message}")]
        public abstract void NuGetInspectionInitializationInfo(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetFailedToWriteSpecFileForPackage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Package nuget://{id}/{version} could not be processed because we could not write BuildXL file to '{buildxlFile}': {message}")]
        public abstract void NugetFailedToWriteSpecFileForPackage(
            LoggingContext context,
            string id,
            string version,
            string buildxlFile,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetFailedToReadNuSpecFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Package nuget://{id}/{version} could not be processed because we could not process the nuspec file at: '{nuspecFile}': {message}")]
        public abstract void NugetFailedToReadNuSpecFile(
            LoggingContext context,
            string id,
            string version,
            string nuspecFile,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetFailedNuSpecFileNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Package nuget://{id}/{version} could not be processed because the nuspec file cannot be found at '{expectedPath}'.")]
        public abstract void NugetFailedNuSpecFileNotFound(LoggingContext context, string id, string version, string expectedPath);

        [GeneratedEvent(
            (ushort)LogEventId.NugetPackageVersionIsInvalid,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.Engine,
            Message = "Invalid nuget version '{version}' found in config.dsc for package '{packageName}'. Expected version format is 'A.B.C.D'.")]
        public abstract void ConfigNugetPackageVersionIsInvalid(LoggingContext context, string version, string packageName);

        [GeneratedEvent(
            (ushort)LogEventId.NugetStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "Nuget statistics: from disk: {packagesFromDisk}, from cache: {packagesFromCache}, from the remote: {packagesDownloaded}, " +
                      "failed: {packagesFailed}. Total elapsed milliseconds: {totalMilliseconds}. Degree of parallelism: {dop}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void NugetStatistics(LoggingContext context, long packagesFromDisk, long packagesFromCache, long packagesDownloaded, long packagesFailed, long totalMilliseconds, int dop);

        [GeneratedEvent(
            (ushort)LogEventId.NugetPackagesRestoredCount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message = "Inspecting NuGet packages ({packagesDownloadedCount} of {totalPackagesCount} done).",
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Overwritable)]
        public abstract void NugetPackageDownloadedCount(LoggingContext context, long packagesDownloadedCount, long totalPackagesCount);

        [GeneratedEvent(
            (ushort)LogEventId.NugetPackagesAreRestored,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message = "Inspected {totalPackagesCount} NuGet packages in {totalMilliseconds}ms.",
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Overwritable)]
        public abstract void NugetPackagesAreRestored(LoggingContext context, long totalPackagesCount, long totalMilliseconds);

        [GeneratedEvent(
            (ushort)LogEventId.NugetRegeneratingNugetSpecs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message = "Generating NuGet package specs.",
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Overwritable)]
        public abstract void NugetRegeneratingNugetSpecs(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.NugetRegenerateNugetSpecs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message = "Generated {totalPackagesCount} NuGet package specs in {totalMilliseconds}ms.",
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Overwritable)]
        public abstract void NugetRegenerateNugetSpecs(LoggingContext context, long totalPackagesCount, long totalMilliseconds);

        [GeneratedEvent(
            (ushort)LogEventId.NugetUnhandledError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "Package nuget://{id}/{version} could not be inspected because of an unhandled error '{error}'.",
            Keywords = (int)Keywords.UserMessage | (int)Keywords.Overwritable)]
        public abstract void NugetUnhandledError(LoggingContext context, string id, string version, string error);

        [GeneratedEvent(
            (ushort)LogEventId.NugetPackageDownloadDetails,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Parser,
            Message =
                "Inspecting NuGet packages ({packagesDownloadedCount} of {totalPackagesCount} done). Remaining {packagesToDownloadDetail}",
            Keywords = (int)Keywords.UserMessage | (int)Keywords.OverwritableOnly)]
        public abstract void NugetPackageDownloadedCountWithDetails(LoggingContext context, long packagesDownloadedCount,
            long totalPackagesCount, string packagesToDownloadDetail);

        [GeneratedEvent(
            (ushort)LogEventId.NugetDependencyVersionWasNotSpecifiedButConfigOneWasChosen,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "NuGet dependency '{packageId}' do not specify a version, but '{pickedVersion}' was selected based on configuration settings.",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void NugetDependencyVersionWasNotSpecifiedButConfigOneWasChosen(LoggingContext context, string packageId, string pickedVersion);

        [GeneratedEvent(
            (ushort)LogEventId.NugetDependencyVersionWasPickedWithinRange,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "NuGet dependency '{packageId}' was requested with version range '{versionRange}' and '{pickedVersion}' was selected based on configuration settings.",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void NugetDependencyVersionWasPickedWithinRange(LoggingContext context, string packageId, string pickedVersion, string versionRange);

        [GeneratedEvent(
            (ushort)LogEventId.NugetDependencyVersionDoesNotMatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "NuGet package 'nuget://{packageRequestorId}/{packageRequestorVersion}' requested dependency 'nuget://{packageDependencyId}/{requestedVersion}', but found version '{pickedVersion}' specified in the configuration file. The found version was selected anyway.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void NugetDependencyVersionDoesNotMatch(LoggingContext context, string packageRequestorId, string packageRequestorVersion, string packageDependencyId, string pickedVersion, string requestedVersion);

        [GeneratedEvent(
            (ushort)LogEventId.NugetUnknownFramework,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "Unknown target framework '{targetFramework}' when processing package '{packageId}' for artifact '{relativePathToArtifact}'.",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void NugetUnknownFramework(LoggingContext context, string packageId, string targetFramework, string relativePathToArtifact);

        [GeneratedEvent(
            (ushort)LogEventId.ForcePopulateTheCacheOptionWasSpecified,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "All the nuget packages will be re-downloaded because '/forcePopulatePackageCache' option was specified.")]
        public abstract void ForcePopulateTheCacheOptionWasSpecified(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.UsePackagesFromDisOptionWasSpecified,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "All the nuget packages will be used from the file system because '/usePackagesFromDisk' option was specified or a target platform does not support nuget propertly.")]
        public abstract void UsePackagesFromDisOptionWasSpecified(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.NugetFailedDownloadPackagesAndGenerateSpecs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Nuget resolver failed to download packages and generate specs: {message}")]
        public abstract void NugetFailedDownloadPackagesAndGenerateSpecs(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetFailedDownloadPackage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Nuget failed to download package '{package}': {message}")]
        public abstract void NugetFailedDownloadPackage(LoggingContext context, string package, string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetFailedGenerationResultFromDownloadedPackage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Nuget failed to to generate result from downloaded package '{package}': {message}")]
        public abstract void NugetFailedGenerationResultFromDownloadedPackage(LoggingContext context, string package, string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetFailedToWriteGeneratedSpecStateFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "Nuget resolver failed to write generated spec state file. Spec will be regenerated on future invocations: {message}")]
        public abstract void NugetFailedToWriteGeneratedSpecStateFile(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetConcurrencyLevel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Parser,
            Message = "{message}")]
        public abstract void NugetConcurrencyLevel(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.NugetCannotReuseSpecOnDisk,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "NuGet resolver cannot reuse existing spec on disk for package '{package}' because spec's fingerprint may have changed or /forceGenerateNuGetSpecs is on",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void NugetCannotReuseSpecOnDisk(LoggingContext context, string package);

    }
}
#pragma warning restore SA1600 // Element must be documented
