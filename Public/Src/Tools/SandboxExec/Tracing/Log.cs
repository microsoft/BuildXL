// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#nullable enable

namespace BuildXL.SandboxExec.Tracing
{
    /// <summary>
    /// Logging for SandboxExec.
    /// There are no log files, so messages for events with <see cref="EventGenerators.LocalOnly"/> will be lost.
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("SandboxExecLogger")]

    public abstract partial class Logger : LoggerBase
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (ushort)LogEventId.SandboxExecMacOSCrashReport,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Critical,
            Message = "Telemetry Only")]
        public abstract void SandboxExecCrashReport(LoggingContext context, string crashSessionId, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DominoMacOSCrashReport,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Critical,
            Message = "Telemetry Only")]
        public abstract void DominoMacOSCrashReport(LoggingContext context, string crashSessionId, string content, string type, string filename);
    }
}
