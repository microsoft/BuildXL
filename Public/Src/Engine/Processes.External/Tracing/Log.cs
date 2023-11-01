// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#nullable enable

namespace BuildXL.Processes.External.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("ProcessesLogger")]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (int)LogEventId.FindAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Find AnyBuild client for process remoting at '{anyBuildInstallDir}'")]
        public abstract void FindAnyBuildClient(LoggingContext context, string anyBuildInstallDir);

        [GeneratedEvent(
            (int)LogEventId.FindOrStartAnyBuildDaemon,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Find or start AnyBuild daemon manager for process remoting with arguments '{args}' (log directory: '{logDir}')")]
        public abstract void FindOrStartAnyBuildDaemon(LoggingContext context, string args, string logDir);

        [GeneratedEvent(
            (int)LogEventId.ExceptionOnFindOrStartAnyBuildDaemon,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Exception on finding or starting AnyBuild daemon: {exception}")]
        public abstract void ExceptionOnFindOrStartAnyBuildDaemon(LoggingContext context, string exception);

        [GeneratedEvent(
            (int)LogEventId.ExceptionOnGetAnyBuildRemoteProcessFactory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Exception on getting AnyBuild remote process factory: {exception}")]
        public abstract void ExceptionOnGetAnyBuildRemoteProcessFactory(LoggingContext context, string exception);

        [GeneratedEvent(
            (int)LogEventId.ExceptionOnFindingAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Exception on finding AnyBuild client: {exception}")]
        public abstract void ExceptionOnFindingAnyBuildClient(LoggingContext context, string exception);

        [GeneratedEvent(
            (int)LogEventId.AnyBuildRepoConfigOverrides,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "AnyBuild repo config overrides: {config}")]
        public abstract void AnyBuildRepoConfigOverrides(LoggingContext context, string config);

        [GeneratedEvent(
            (int)LogEventId.InstallAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Installing AnyBuild client from '{source}' (ring: {ring})")]
        public abstract void InstallAnyBuildClient(LoggingContext context, string source, string ring);

        [GeneratedEvent(
            (int)LogEventId.InstallAnyBuildClientDetails,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Installing AnyBuild client from '{source}' (ring: {ring}): {reason}")]
        public abstract void InstallAnyBuildClientDetails(LoggingContext context, string source, string ring, string reason);

        [GeneratedEvent(
            (int)LogEventId.FailedDownloadingAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Failed downloading AnyBuild client: {message}")]
        public abstract void FailedDownloadingAnyBuildClient(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FailedInstallingAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Failed installing AnyBuild client: {message}")]
        public abstract void FailedInstallingAnyBuildClient(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FinishedInstallAnyBuild,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Finished installing AnyBuild client: {message}")]
        public abstract void FinishedInstallAnyBuild(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.ExecuteAnyBuildBootstrapper,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Execute AnyBuild bootstrapper: {command}")]
        public abstract void ExecuteAnyBuildBootstrapper(LoggingContext context, string command);

        [GeneratedEvent(
            (int)LogEventId.PipProcessExternalExecution,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "External execution: {message}")]
        public abstract void PipProcessExternalExecution(LoggingContext context, long pipSemiStableHash, string pipDescription, string message);
    }
}
