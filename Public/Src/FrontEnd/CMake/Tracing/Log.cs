// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591

namespace BuildXL.FrontEnd.CMake.Tracing
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
            (ushort)LogEventId.CMakeRunnerInternalError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "The CMake to Ninja generator tool run into an internal error. Details: {standardError}")]
        public abstract void CMakeRunnerInternalError(LoggingContext context, Location location, string standardError);


        [GeneratedEvent(
            (ushort)LogEventId.CouldNotDeleteToolArgumentsFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Cannot delete file '{path}' containing the arguments for the CMakeRunner tool. Details: {message}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CouldNotDeleteToolArgumentsFile(LoggingContext context, Location location, string path, string message);


        [GeneratedEvent(
            (ushort)LogEventId.NoSearchLocationsSpecified,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Build parameter 'PATH' is not specified, and no explicit locations were defined in the resolver settings via 'CMakeSearchLocations'.",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void NoSearchLocationsSpecified(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.CannotParseBuildParameterPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Build parameter 'PATH' cannot be interpreted as a collection of paths: {envPath}",
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void CannotParseBuildParameterPath(LoggingContext context, Location location, string envPath);
    }
}
