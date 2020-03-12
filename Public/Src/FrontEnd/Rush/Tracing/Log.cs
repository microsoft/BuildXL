// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591
#nullable enable

namespace BuildXL.FrontEnd.Rush.Tracing
{
    /// <summary>
    /// Logging for the Rush frontend and resolvers
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("RushLogger")]
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
            (ushort)LogEventId.InvalidResolverSettings,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Invalid resolver settings. {reason}")]
        public abstract void InvalidResolverSettings(LoggingContext context, Location location, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.ProjectGraphConstructionError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "An error occurred while parsing Rush spec files: {message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ProjectGraphConstructionError(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.GraphConstructionInternalError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "An internal error occurred when computing the Rush graph. This shouldn't have happened! Tool standard error: '{toolStandardError}'",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void GraphConstructionInternalError(LoggingContext context, Location location, string toolStandardError);

        [GeneratedEvent(
            (ushort)LogEventId.CannotDeleteSerializedGraphFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot delete file '{file}' containing the serialized Rush graph. Details: {message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CannotDeleteSerializedGraphFile(LoggingContext context, Location location, string file, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CycleInBuildTargets,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = "A cycle was detected in build target dependencies, evaluation cannot proceed. Cycle: {cycleDescription}")]
        public abstract void CycleInBuildTargets(LoggingContext context, string cycleDescription);

        [GeneratedEvent(
            (ushort)LogEventId.SchedulingPipFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics),
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "A failure occurred while scheduling a pip. Reason: {detailedFailure}.")]
        public abstract void SchedulingPipFailure(LoggingContext context, Location location, string detailedFailure);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedPipBuilderException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics),
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "An unexpected exception occurred while constructing the pip graph. Message: {message}. Stack: {stack}")]
        public abstract void UnexpectedPipBuilderException(LoggingContext context, Location location, string message, string stack);

        [GeneratedEvent(
            (ushort)LogEventId.GraphConstructionFinishedSuccessfullyButWithWarnings,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.LabeledProvenancePrefix + "Graph construction process finished successfully, but some warnings occurred: {message}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void GraphConstructionFinishedSuccessfullyButWithWarnings(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.GraphBuilderFilesAreNotRemoved,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.LogAlways,
            Message = "Regular deletion of graph-related files is skipped. Graph file produced under '{graphFile}'.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void GraphBuilderFilesAreNotRemoved(LoggingContext context, string graphFile);
    }
}
