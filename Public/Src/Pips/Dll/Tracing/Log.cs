// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field

namespace BuildXL.Pips.Tracing
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
            (ushort)LogEventId.DeserializationStatsPipGraphFragment,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Deserialization stats of graph fragment '{fragmentDescription}': {stats}")]
        public abstract void DeserializationStatsPipGraphFragment(LoggingContext context, string fragmentDescription, string stats);

        [GeneratedEvent(
            (ushort)LogEventId.ExceptionOnDeserializingPipGraphFragment,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "An exception occured during deserialization of pip graph fragment '{path}': {exceptionMessage}")]
        public abstract void ExceptionOnDeserializingPipGraphFragment(LoggingContext context, string path, string exceptionMessage);


        [GeneratedEvent(
            (ushort)LogEventId.FailedToAddFragmentPipToGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Engine,
            Message = "[{pipDescription}] Unable to add the pip from fragment '{fragmentName}'.")]
        public abstract void FailedToAddFragmentPipToGraph(LoggingContext context, string fragmentName, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.ExceptionOnAddingFragmentPipToGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "[{pipDescription}] An exception occured when adding the pip from fragment '{fragmentName}': {exceptionMessage}")]
        public abstract void ExceptionOnAddingFragmentPipToGraph(LoggingContext context, string fragmentName, string pipDescription, string exceptionMessage);
    }
}
#pragma warning restore CA1823 // Unused field
