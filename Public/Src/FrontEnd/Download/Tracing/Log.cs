// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591
#nullable enable

namespace BuildXL.FrontEnd.Download.Tracing
{
    /// <summary>
    /// Logging for the Download frontend and resolvers
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("DownloadLogger")]
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
            (ushort)LogEventId.NameContainsInvalidCharacters,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage),
            EventTask = (ushort)Tasks.Parser,
            Message = ResolverSettingsPrefix + "The string '{name}' specified in '{fieldName}' is not a valid identifier.")]
        public abstract void NameContainsInvalidCharacters(LoggingContext context, string fieldName, string name);

        [GeneratedEvent(
            (ushort)LogEventId.ContextStatistics,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = "[Download.{0}] contexts: {1} trees, {2} contexts.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ContextStatistics(LoggingContext context, string name, long contextTrees, long contexts);

        [GeneratedEvent(
            (ushort)LogEventId.BulkStatistic,
            EventGenerators = Generators.Statistics,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.CommonInfrastructure,
            Message = "N/A",
            Keywords = (int)Keywords.Diagnostics)]
        public abstract void BulkStatistic(LoggingContext context, IDictionary<string, long> statistics);
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
