// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591
#nullable enable

namespace BuildXL.FrontEnd.Yarn.Tracing
{
    /// <summary>
    /// Logging for the Yarn frontend and resolvers
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("YarnLogger")]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log { get; } = new LoggerImpl();

        // Internal logger will prevent public users from creating an instance of the logger
        internal Logger()
        {
        }

        [GeneratedEvent(
            (ushort)LogEventId.UsingYarnAt,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.LabeledProvenancePrefix + "Using Yarn at '{basePath}'.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void UsingYarnAt(LoggingContext context, Location location, string basePath);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorReadingCustomProjectGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix +  "Error reading custom project graph. {failure}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ErrorReadingCustomProjectGraph(LoggingContext context, Location location, string failure);

        [GeneratedEvent(
            (ushort)LogEventId.CannotSerializeGraphFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot serialize file '{file}' containing the serialized graph. Details: {message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CannotSerializeGraphFile(LoggingContext context, Location location, string file, string message);
    }
}
