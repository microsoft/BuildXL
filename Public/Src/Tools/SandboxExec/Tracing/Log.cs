// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#if !FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using System.Diagnostics.Tracing;
#endif

#pragma warning disable 1591

namespace BuildXL.SandboxExec.Tracing
{
    /// <summary>
    /// Logging for SandboxExec.
    /// There are no log files, so messages for events with <see cref="EventGenerators.LocalOnly"/> will be lost.
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
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
