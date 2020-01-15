// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591

namespace BuildXL.FrontEnd.Factory.Tracing
{
    /// <summary>
    /// Logging for bxl.exe.
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (ushort)LogEventId.MaterializingProfilerReport,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Tasks.HostApplication,
            Message = EventConstants.PhasePrefix + "Writing profiler report to '{destination}'.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void MaterializingProfilerReport(LoggingContext context, string destination);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorMaterializingProfilerReport,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Tasks.HostApplication,
            Message = EventConstants.PhasePrefix + "Profiler report could not be written. Error code {errorCode:X8}: {message}.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ErrorMaterializingProfilerReport(LoggingContext context, int errorCode, string message);

        [GeneratedEvent(
            (ushort)LogEventId.WaitingClientDebugger,
            EventGenerators = EventGenerators.LocalOnly,
            Message = @"Waiting for a debugger to connect (blocking). Configure VSCode by adding \""debugServer\"": {port} to your '.vscode/launch.json' and choose \""Attach to running {ShortScriptName}\"".",
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Tasks.HostApplication,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void WaitingForClientDebuggerToConnect(LoggingContext context, int port);
    }
}
