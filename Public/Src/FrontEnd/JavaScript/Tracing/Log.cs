// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591
#nullable enable

namespace BuildXL.FrontEnd.JavaScript.Tracing
{
    /// <summary>
    /// Logging for the JavaScript frontend and resolvers
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("JavaScriptLogger")]
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
            Message = EventConstants.LabeledProvenancePrefix + "An error occurred while constructing the project graph: {message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ProjectGraphConstructionError(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CannotDeleteSerializedGraphFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot delete file '{file}' containing the serialized graph. Details: {message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CannotDeleteSerializedGraphFile(LoggingContext context, Location location, string file, string message);

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
            EventLevel = Level.Warning,
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

        [GeneratedEvent(
            (ushort)LogEventId.DependencyIsIgnoredScriptIsMissing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.LabeledProvenancePrefix + "Project '{projectName}' with script command '{scriptCommandName}' declares a dependency on '{dependency}' with script command '{dependencyScriptCommandName}', but the requested script is not defined on " +
            "the target dependency. The dependency is ignored.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void DependencyIsIgnoredScriptIsMissing(LoggingContext context, Location location, string projectName, string scriptCommandName, string dependency, string dependencyScriptCommandName);

        [GeneratedEvent(
            (ushort)LogEventId.JavaScriptCommandIsEmpty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "JavaScript command is empty. Only non-empty command names are allowed. E.g. 'build'.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void JavaScriptCommandIsEmpty(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.JavaScriptCommandIsDuplicated,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "JavaScript command '{command}' is specified more than once.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void JavaScriptCommandIsDuplicated(LoggingContext context, Location location, string command);

        [GeneratedEvent(
            (ushort)LogEventId.CycleInJavaScriptCommands,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "There is a cyclic dependency in the specified JavaScript commands '{cycle}'.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CycleInJavaScriptCommands(LoggingContext context, Location location, string cycle);

        [GeneratedEvent(
            (ushort)LogEventId.ProjectIsIgnoredScriptIsMissing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.LabeledProvenancePrefix + "Script command '{scriptCommandName}' is requested to be scheduled for project '{projectName}', but the command is not defined. " +
            "The invocation is ignored.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ProjectIsIgnoredScriptIsMissing(LoggingContext context, Location location, string projectName, string scriptCommandName);

        [GeneratedEvent(
            (ushort)LogEventId.CannotFindGraphBuilderTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot find the graph builder tool, which is required to compute the project graph. {details}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CannotFindGraphBuilderTool(LoggingContext context, Location location, string details);

        [GeneratedEvent(
            (ushort)LogEventId.SpecifiedCommandForExportDoesNotExist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "The specified content for export symbol '{exportSymbol}' includes package '{packageName}' with a " +
            "script command '{commandName}'. However, that command is not defined in the corresponding package.json. Available script commands for that package: {availableCommands}.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void SpecifiedCommandForExportDoesNotExist(LoggingContext context, Location location, string exportSymbol, string packageName, string commandName, string availableCommands);

        [GeneratedEvent(
            (ushort)LogEventId.SpecifiedPackageForExportDoesNotExist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "The specified content for export symbol '{exportSymbol}' specifies a non-existent package '{packageName}'.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void SpecifiedPackageForExportDoesNotExist(LoggingContext context, Location location, string exportSymbol, string packageName);

        [GeneratedEvent(
            (ushort)LogEventId.RequestedExportIsNotPresent,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.LabeledProvenancePrefix + "Export symbol '{symbol}' specifies project '{projectName}' with command '{scriptCommandName}' as part of its content, " +
            "but the project has not been scheduled.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void RequestedExportIsNotPresent(LoggingContext context, Location location, string symbol, string projectName, string scriptCommandName);

        [GeneratedEvent(
            (ushort)LogEventId.SpecifiedExportIsAReservedName,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "The specified symbol '{symbol}' is a reserved name.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void SpecifiedExportIsAReservedName(LoggingContext context, Location location, string symbol);
    }
}
