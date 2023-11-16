// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field
#nullable enable

namespace BuildXL.ConsoleRedirector.Tracing
{
    /// <summary>
    /// Special logging to send standard log messages to the console. Not intended for bxl components to use directly.
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("ConsoleRedirector")]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;


        /// <summary>
        /// A <see cref="ConsoleRedirectorEventListener"/> will selectively log standard log events via this method
        /// so the ConsoleEventListener can pick them up
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.LogToConsole,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Unclassified,
            Message = "{text}")]
        public abstract void LogToConsole(LoggingContext context, string text);
    }
}

#pragma warning restore CA1823 // Unused field