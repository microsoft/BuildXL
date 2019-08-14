// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591

namespace BuildXL.FrontEnd.Download.Tracing
{
    /// <summary>
    /// Logging for the Download frontend and resolvers
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger
    {
        private const string ResolverSettingsPrefix = "Error processing download resolver settings: ";

        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log { get; } = new LoggerImpl();

        // Internal logger will prevent public users from creating an instance of the logger
        internal Logger()
        {
        }


        [GeneratedEvent(
            (ushort)LogEventId.DownloadFrontendMissingModuleId,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Missing required field 'id' for download with url: '{url}'.")]
        public abstract void DownloadFrontendMissingModuleId(LoggingContext context, string url);


        [GeneratedEvent(
            (ushort)LogEventId.DownloadFrontendMissingUrl,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Missing required field 'url' for download with id: '{id}'.")]
        public abstract void DownloadFrontendMissingUrl(LoggingContext context, string id);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadFrontendInvalidUrl,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Invalid 'url' specified to download id: '{id}' and url: '{url}'. The url must be an absolute url.")]
        public abstract void DownloadFrontendInvalidUrl(LoggingContext context, string id, string url);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadFrontendDuplicateModuleId,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Duplicate module id '{id}' declared in {kind} resolver named {name}.")]
        public abstract void DownloadFrontendDuplicateModuleId(LoggingContext context, string id, string kind, string name);


        [GeneratedEvent(
            (ushort)LogEventId.DownloadFrontendHashValueNotValidContentHash,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Invalid hash value 'hash' for download id '{id}' with url '{url}'. It must be a valid content hash format i.e. 'VSO0:000000000000000000000000000000000000000000000000000000000000000000'.")]
        public abstract void DownloadFrontendHashValueNotValidContentHash(LoggingContext context, string id, string url, string hash);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadMismatchedHash,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Invalid content for download id '{id}' from url '{url}'. The content hash was expected to be: '{expectedHash}' but the downloaded files hash was '{downloadedHash}'. This means that the data on the server has been altered and is not trusted.")]
        public abstract void DownloadMismatchedHash(LoggingContext context, string id, string url, string expectedHash, string downloadedHash);

        [GeneratedEvent(
            (ushort)LogEventId.StartDownload,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (ushort)(Keywords.UserMessage),
            EventTask = (ushort)Tasks.Parser,
            Message = "Starting download id '{id}' from url '{url}'.")]
        public abstract void StartDownload(LoggingContext context, string id, string url);

        [GeneratedEvent(
            (ushort)LogEventId.Downloaded,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (ushort)(Keywords.UserMessage),
            EventTask = (ushort)Tasks.Parser,
            Message = "Finished download id '{id}' from url '{url}' in {durationMs}ms with {sizeInBytes} bytes.")]
        public abstract void Downloaded(LoggingContext context, string id, string url, long durationMs, long sizeInBytes );

        [GeneratedEvent(
            (ushort)LogEventId.DownloadFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Failed to download id '{id}' from url '{url}': {error}.")]
        public abstract void DownloadFailed(LoggingContext context, string id, string url, string error);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorPreppingForDownload,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Error occured trying to prepare for download id '{id}': {error}.")]
        public abstract void ErrorPreppingForDownload(LoggingContext context, string id, string error);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorCheckingIncrementality,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Error occured trying to check the incremental information of download id '{id}': {error}.")]
        public abstract void ErrorCheckingIncrementality(LoggingContext context, string id, string error);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorStoringIncrementality,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Error occured trying to store incremental information of download id '{id}': {error}.")]
        public abstract void ErrorStoringIncrementality(LoggingContext context, string id, string error);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorExtractingArchive,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Error occured trying to extract archive '{id}' from '{archive}' to '{folder}': {error}.")]
        public abstract void ErrorExtractingArchive(LoggingContext context, string id, string archive, string folder, string error);
        
        [GeneratedEvent(
            (ushort)LogEventId.ErrorNothingExtracted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Error occured trying to extract archive '{id}'. Nothing was extracted from '{archive}' to '{folder}'")]
        public abstract void ErrorNothingExtracted(LoggingContext context, string id, string archive, string folder);
   
        [GeneratedEvent(
            (ushort)LogEventId.ErrorValidatingPackage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Error occured trying to validate extracted archive '{id}' from '{archive}' to '{folder}': {error}.")]
        public abstract void ErrorValidatingPackage(LoggingContext context, string id, string archive, string folder, string error);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorListingPackageContents,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.InfrastructureError),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Error occured trying to enumerate extracted archive '{id}' from '{archive}' to '{folder}': {error}.")]
        public abstract void ErrorListingPackageContents(LoggingContext context, string id, string archive, string folder, string error);

        [GeneratedEvent(
            (ushort)LogEventId.DownloadManifestDoesNotMatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (ushort)(Keywords.UserMessage),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Download manifest indicates a redownload is required because of '{reason}'. Expected: '{expected}' actual: '{actual}'")]
        public abstract void DownloadManifestDoesNotMatch(LoggingContext context, string id, string archive, string reason, string expected, string actual);

        [GeneratedEvent(
            (ushort)LogEventId.ExtractManifestDoesNotMatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (ushort)(Keywords.UserMessage),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "Extraction manifest indicates a re-extraction is required because of '{reason}'. Expected: '{expected}' actual: '{actual}'")]
        public abstract void ExtractManifestDoesNotMatch(LoggingContext context, string id, string archive, string reason, string expected, string actual);
    }


    /// <summary>
    /// Statistics about the parse phase
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct DownloadStatistics : IHasEndTime
    {
        /// <nodoc />
        public int FileSize;

        /// <inheritdoc />
        public int ElapsedMilliseconds { get; set; }
    }
}
