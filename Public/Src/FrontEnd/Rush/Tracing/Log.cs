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
            (ushort)LogEventId.InvalidRushResolverSettings,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (ushort)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.Parser,
            Message = EventConstants.LabeledProvenancePrefix + "Invalid resolver settings. {reason}")]
        public abstract void InvalidRushResolverSettings(LoggingContext context, Location location, string reason);

        [GeneratedEvent(
            (ushort)LogEventId.UsingRushLibBaseAt,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.LabeledProvenancePrefix + "Using '{basePath}' as the base path for resolving @microsoft/rush-lib.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void UsingRushLibBaseAt(LoggingContext context, Location location, string basePath);
    }
}
