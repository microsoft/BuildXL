// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING

#else
using System.Diagnostics.Tracing;
#endif

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field
#pragma warning disable SA1600 // Element must be documented

namespace BuildXL.Ide.LanguageServer.Tracing
{
    /// <summary>
    /// Logging facilities for the language server.
    /// </summary>
    /// <remarks>
    /// All the tracing methods in this class should strart with LanguageServer prefix to differentiate them from other BuildXL telemetry events.
    /// </remarks>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger
    {
        // Internal logger will prevent public users from creating an instance of the logger
        internal Logger()
        { }

        /// <summary>
        /// Factory method that creates instances of the logger.
        /// </summary>
        public static Logger CreateLogger()
        {
            return new LoggerImpl();
        }

        [GeneratedEvent(
            (ushort)LogEventId.LanguageServerStarted,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "Language server is started.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerStarted(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.LanguageServerStoped,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "Language server is stopped.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerStopped(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.LanguageServerClientType,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "Language server client type is '{clientType}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerClientType(LoggingContext context, string clientType);

        [GeneratedEvent(
            (ushort)LogEventId.LogFileLocation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "For more detailed information see the log file at '{logFile}' or use 'Open {ShortScriptName} log file' command.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerLogFileLocation(LoggingContext context, string logFile);

        [GeneratedEvent(
            (ushort)LogEventId.OperationIsTooLong,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "Operation {operation} took longer ({durationInMs}ms) than expected.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerOperationIsTooLong(LoggingContext context, string operation, int durationInMs);

        [GeneratedEvent(
            (ushort)LogEventId.ConfigurationChanged,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "The configuration was changed. New configuration is '{configuration}'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ReportConfigurationChanged(LoggingContext context, string configuration);

        [GeneratedEvent(
            (ushort)LogEventId.CanNotFindSourceFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "The file '{uri}' is not part of the workspace.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerCanNotFindSourceFile(LoggingContext context, string uri);

        [GeneratedEvent(
            (ushort)LogEventId.NonCriticalInternalIssue,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "Non-critical internal issue occurred: {error}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerNonCriticalInternalIssue(LoggingContext context, string error);

        [GeneratedEvent(
            (ushort)LogEventId.UnhandledInternalError,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "Unhandled error: {error}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerUnhandledInternalError(LoggingContext context, string error);

        [GeneratedEvent(
            (ushort)LogEventId.NewFileWasAdded,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "New file '{path}' was added to the workspace that forces a full workspace recomputation.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerNewFileWasAdded(LoggingContext context, string path);

        [GeneratedEvent(
            (ushort)LogEventId.FileWasRemoved,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.LanguageServer,
            Message = EventConstants.PhasePrefix + "The file '{path}' was removed from the workspace that forces a full workspace recomputation.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LanguageServerFileWasRemoved(LoggingContext context, string path);
    }
}
