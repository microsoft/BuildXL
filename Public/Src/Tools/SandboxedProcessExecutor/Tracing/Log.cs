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

namespace BuildXL.SandboxedProcessExecutor.Tracing
{
    /// <summary>
    /// Logging for executor.
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
            (int)LogEventId.SandboxedProcessExecutorInvoked,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.SandboxedProcessExecutor,
            Message = "Invocation")]
        public abstract void SandboxedProcessExecutorInvoked(LoggingContext context, long runtimeMs, string commandLine);

        [GeneratedEvent(
            (int)LogEventId.SandboxedProcessExecutorCatastrophicFailure,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.SandboxedProcessExecutor,
            Message = "Catastrophic failure")]
        public abstract void SandboxedProcessExecutorCatastrophicFailure(LoggingContext context, string exceptionMessage);
    }
}
