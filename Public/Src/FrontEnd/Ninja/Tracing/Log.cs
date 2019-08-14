// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591

namespace BuildXL.FrontEnd.Ninja.Tracing
{
    /// <summary>
    /// Logging for the Ninja frontend and resolvers
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
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
            (ushort)LogEventId.ProjectRootDirectoryDoesNotExist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The ProjectRoot (resolved to {path} from the resolver settings) should exist.")]
        public abstract void ProjectRootDirectoryDoesNotExist(LoggingContext context, Location location, string path);

        [GeneratedEvent(
            (ushort)LogEventId.NinjaSpecFileDoesNotExist,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The Ninja spec file (resolved to {path} from the resolver settings) should exist.")]
        public abstract void NinjaSpecFileDoesNotExist(LoggingContext context, Location location, string path);

        [GeneratedEvent(
            (ushort)LogEventId.GraphConstructionInternalError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "An internal error occurred when computing the Ninja graph. Tool standard error: '{toolStandardError}'",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void GraphConstructionInternalError(LoggingContext context, Location location, string toolStandardError);

        [GeneratedEvent(
            (ushort)LogEventId.GraphConstructionFinishedSuccessfullyButWithWarnings,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Message = EventConstants.LabeledProvenancePrefix + "Graph construction process finished successfully, but some warnings occurred: {message}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser)]
        public abstract void GraphConstructionFinishedSuccessfullyButWithWarnings(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.InvalidExecutablePath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Message = EventConstants.LabeledProvenancePrefix + "Could not resolve an executable absolute path from the command line [{command}]",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser)]
        public abstract void InvalidExecutablePath(LoggingContext context, Location location, string command);

        [GeneratedEvent(
            (ushort)LogEventId.PipSchedulingFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Message = EventConstants.LabeledProvenancePrefix + "An error ocurred scheduling a pip",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser)]
        public abstract void PipSchedulingFailed(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.UnexpectedPipConstructorException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Message = EventConstants.LabeledProvenancePrefix + "An unexpected exception occurred while constructing the pip graph. Message: {message}. Stack trace: {stackTrace}",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser)]
        public abstract void UnexpectedPipConstructorException(
            LoggingContext context,
            Location location,
            string message,
            string stackTrace);

        [GeneratedEvent(
            (ushort)LogEventId.CouldNotDeleteToolArgumentsFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot delete file '{path}' containing the arguments for the Ninja graph construction tool. Details: {message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CouldNotDeleteToolArgumentsFile(LoggingContext context, Location location, string path, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CouldNotComputeRelativePathToSpec,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Could not compute a relative path from project root {projectRoot} to the specification {pathToSpec}. Consider using a different project structure.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CouldNotComputeRelativePathToSpec(LoggingContext context, Location location, string projectRoot, string pathToSpec);

        [GeneratedEvent(
            (ushort)LogEventId.LeftGraphToolOutputAt,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The serialized Ninja graph that this resolver used was left at {output}.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void LeftGraphToolOutputAt(LoggingContext context, Location location, string output);
        
    }
}
