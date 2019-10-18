// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field

namespace BuildXL.PipGraphFragmentGenerator.Tracing
{
    /// <summary>
    /// Logging
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
            (ushort)LogEventId.ErrorParsingFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Error at position {position} of command line pip filter {filter}. {message} {positionMarker}",
            Keywords = (int)(Keywords.UserMessage| Keywords.UserError))]
        public abstract void ErrorParsingFilter(LoggingContext context, string filter, int position, string message, string positionMarker);

        [GeneratedEvent(
            (ushort)LogEventId.GraphFragmentExceptionOnSerializingFragment,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "An exception occured when the GraphFragment analyzer serialized the graph fragment to '{file}': {exceptionMessage}",
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError))]
        public abstract void GraphFragmentExceptionOnSerializingFragment(LoggingContext context, string file, string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.GraphFragmentSerializationStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Serialization stats of graph fragment '{fragmentDescription}': {stats}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void GraphFragmentSerializationStats(LoggingContext context, string fragmentDescription, string stats);
    }
}

#pragma warning restore CA1823 // Unused field
