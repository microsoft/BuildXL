// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

// Suppress missing XML comments on publicly visible types or members
#pragma warning disable 1591
#nullable enable

namespace BuildXL.FrontEnd.Lage.Tracing
{
    /// <summary>
    /// Logging for the Lage frontend and resolvers
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("LageLogger")]
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
            (ushort)LogEventId.UsingNpmAt,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.LabeledProvenancePrefix + "Using Npm at '{basePath}'.",
            EventTask = (ushort)Tasks.Engine,
            EventOpcode = (byte)Tasks.Parser,
            Keywords = (int)(Keywords.UserMessage | Keywords.Diagnostics))]
        public abstract void UsingNpmAt(LoggingContext context, Location location, string basePath);
    }
}
