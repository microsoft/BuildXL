// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591

namespace BuildXL.Engine.DirectGraph.Tracing
{
    /// <summary>
    /// Logging for DirectGraph graph construction.
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("DirectGraphLogger")]
    public abstract partial class Logger
    {
        /// <summary>
        /// Logger instance.
        /// </summary>
        public static Logger Log { get; } = new LoggerImpl();

        internal Logger()
        {
        }

        [GeneratedEvent(
            (ushort)LogEventId.GraphOperationReceived,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)Keywords.Diagnostics,
            Message = "DirectGraph operation received: {operation}")]
        public abstract void GraphOperationReceived(LoggingContext context, string operation);

        [GeneratedEvent(
            (ushort)LogEventId.GraphOperationResponse,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)Keywords.Diagnostics,
            Message = "DirectGraph operation response: {response}")]
        public abstract void GraphOperationResponse(LoggingContext context, string response);

        [GeneratedEvent(
            (ushort)LogEventId.GraphBuilderToolFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            Message = "DirectGraph tool '{tool}' failed with exit code {exitCode}: {error}")]
        public abstract void GraphBuilderToolFailed(LoggingContext context, string tool, int exitCode, string error);

        [GeneratedEvent(
            (ushort)LogEventId.GraphBuilderToolExitedWithoutCompletion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            Message = "DirectGraph tool '{tool}' exited before BuildXL handled a Complete operation. The tool has to send Complete " +
                      "and keep its call open until it has read the response to it.")]
        public abstract void GraphBuilderToolExitedWithoutCompletion(LoggingContext context, string tool);

        [GeneratedEvent(
            (ushort)LogEventId.GraphBuilderToolWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)Keywords.UserMessage,
            Message = "DirectGraph tool '{tool}' wrote to its standard error: {warning}")]
        public abstract void GraphBuilderToolWarning(LoggingContext context, string tool, string warning);

        [GeneratedEvent(
            (ushort)LogEventId.GraphOperationRejected,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            Message = "DirectGraph tool '{tool}' sent operation {operationId}, which BuildXL rejected: {error}")]
        public abstract void GraphOperationRejected(LoggingContext context, string tool, ulong operationId, string error);

        [GeneratedEvent(
            (ushort)LogEventId.GraphBuilderToolTimedOut,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            Message = "DirectGraph tool '{tool}' sent no operation for {minutes} minute(s) and was terminated. " +
                      "If it needs longer between operations, raise 'idleTimeoutMin' in the resolver settings.")]
        public abstract void GraphBuilderToolTimedOut(LoggingContext context, string tool, int minutes);

        [GeneratedEvent(
            (ushort)LogEventId.GraphBuilderToolNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Engine,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            Message = "DirectGraph tool '{tool}' does not exist.")]
        public abstract void GraphBuilderToolNotFound(LoggingContext context, string tool);
    }
}
