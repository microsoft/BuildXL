// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591

namespace BuildXL.FrontEnd.DirectGraph.Tracing
{
    /// <summary>
    /// Logging for the DirectGraph front-end.
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("DirectGraphFrontEndLogger")]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance.
        /// </summary>
        public static Logger Log { get; } = new LoggerImpl();

        internal Logger()
        {
        }

        [GeneratedEvent(
            (ushort)LogEventId.ResolverMissingModuleNames,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Invalid DirectGraph resolver settings. At least one module name must be specified.")]
        public abstract void ReportResolverMissingModuleNames(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.ResolverEmptyModuleName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Invalid DirectGraph resolver settings. All provided module names must be non-empty.")]
        public abstract void ReportResolverEmptyModuleName(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.ResolverDuplicateModuleNames,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Invalid DirectGraph resolver settings. Duplicate module names found: {duplicatedModuleNames}. Names that differ only in case are duplicates.")]
        public abstract void ReportResolverDuplicateModuleNames(LoggingContext context, string duplicatedModuleNames);

        [GeneratedEvent(
            (ushort)LogEventId.ResolverMissingTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Invalid DirectGraph resolver settings. A tool must be specified.")]
        public abstract void ReportResolverMissingTool(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.ResolverInvalidIdleTimeout,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = "Invalid DirectGraph resolver settings. 'idleTimeoutMin' is {idleTimeoutMin}, but it must be at least {minimum} minute(s).")]
        public abstract void ReportResolverInvalidIdleTimeout(LoggingContext context, int idleTimeoutMin, int minimum);
    }
}
