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
            (ushort)LogEventId.CannotFindGraphBuilderTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot find the graph builder tool, which is required to compute the project graph. {details}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CannotFindGraphBuilderTool(LoggingContext context, Location location, string details);

        [GeneratedEvent(
            (ushort)LogEventId.SpecifiedPackageForExportDoesNotExist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "The specified content for export symbol '{exportSymbol}' does not contain any valid package. Selector: '{selector}'.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void SpecifiedPackageForExportDoesNotExist(LoggingContext context, Location location, string exportSymbol, string selector);

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

        [GeneratedEvent(
            (ushort)LogEventId.ConstructingGraphScript,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "The JavaScript project graph tool execution is: {script}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void ConstructingGraphScript(LoggingContext context, string script);

        [GeneratedEvent(
            (ushort)LogEventId.JavaScriptCommandGroupCanOnlyContainRegularCommands,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "JavaScript command '{command}' specified in group '{commandGroup}' can only be a regular command, " +
            "but it is also being defined as a command group.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void JavaScriptCommandGroupCanOnlyContainRegularCommands(LoggingContext context, Location location, string commandGroup, string command);

        [GeneratedEvent(
            (ushort)LogEventId.CustomScriptsFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "Failure at computing custom scripts for package '{packageName}'. {failure}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CustomScriptsFailure(LoggingContext context, Location location, string packageName, string failure);

        [GeneratedEvent(
            (ushort)LogEventId.CannotLoadScriptsFromJsonFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "Failure reading package scripts from '{pathToJson}'. {failure}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CannotLoadScriptsFromJsonFile(LoggingContext context, Location location, string pathToJson, string failure);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidRegexInProjectSelector,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Message = EventConstants.LabeledProvenancePrefix + "Invalid regular expression in project selector: {selector}. Failure: {failure}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void InvalidRegexInProjectSelector(LoggingContext context, Location location, string selector, string failure);

        [GeneratedEvent(
            (ushort)LogEventId.IgnoredDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.LabeledProvenancePrefix + "Dependency '{dependency}' for project '{project}' was ignored since it is not defined.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void IgnoredDependency(LoggingContext context, Location location, string dependency, string project);
    }
}
