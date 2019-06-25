// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591

namespace BuildXL.FrontEnd.Script.Debugger.Tracing
{
    /// <summary>
    /// Debugger messages
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger
    {
        /// <summary>
        /// Factory method that creates instances of this logger.
        /// </summary>
        public static Logger CreateLogger()
        {
            return new LoggerImpl();
        }

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerServerStarted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Debugger,
            Message = "{ShortScriptName} debug server started on port {port} (async)",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerServerStarted(LoggingContext context, int port);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerServerShutDown,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Debugger,
            Message = "{ShortScriptName} debug server shut down",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerServerShutDown(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerClientConnected,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Client debugger connected",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerClientConnected(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerClientDisconnected,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Debug client disconnected.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerClientDisconnected(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerRequestReceived,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Debugger: request received: {json}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerRequestReceived(LoggingContext context, string json);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerMessageSent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Debugger: '{kind}' message sent: {json}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerMessageSent(LoggingContext context, string kind, string json);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerEvaluationThreadSuspended,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Debugger: evaluation thread '{threadId}' suspended.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerEvaluationThreadSuspended(LoggingContext context, int threadId);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerEvaluationThreadResumed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Debugger: evaluation thread '{threadId}' resumed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerEvaluationThreadResumed(LoggingContext context, int threadId);

        // warnings/errors
        public const string DebuggerWarningGeneralSuffix = "BuildXL will continue, but debugging will be disabled.";

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerCannotOpenSocketError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Debugger server failed due to a network error: {message}. " + DebuggerWarningGeneralSuffix,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerCannotOpenSocketError(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerServerGenericError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Debugger server failed due to: {message}. " + DebuggerWarningGeneralSuffix + "{stackTrace}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerServerGenericError(LoggingContext context, string message, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerClientGenericError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Debugger,
            Message = "Communication with a client debugger failed due to: {message}. " + DebuggerWarningGeneralSuffix + "{stackTrace}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerClientGenericError(LoggingContext context, string message, string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.DebuggerRendererFailedToBindCallableMember,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Debugger,
            Message = "{ShortScriptName} DebuggerRenderer couldn't convert CallableMember of type '{callableMemberType}' and bind it to a receiver of type '{receiverType}'. Exception thrown: {message}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ReportDebuggerRendererFailedToBindCallableMember(LoggingContext context, string callableMemberType, string receiverType, string message);
    }
}
