// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.IO;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#nullable enable

namespace BuildXL.App.Tracing
{
    /// <summary>
    /// Logging for bxl.exe.
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("AppLogger", InstanceBasedLogging = true)]
    public abstract partial class LoggerDeclaration
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>

        [SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private const string AppInvocationMessage = "{ShortProductName} Startup Command Line Arguments: '{commandLine}' \r\n{ShortProductName} version:{buildInfo.CommitId}, Build: {buildInfo.Build}, Engine configuration version: {buildInfo.EngineConfigurationVersion}, Session ID:{sessionIdentifier}, Related Session:{relatedSessionIdentifier}, MachineInfo: CPU count: {machineInfo.ProcessorCount}, Physical Memory: {machineInfo.InstalledMemoryMB}MB, Available Physical Memory: {machineInfo.AvailableMemoryMB}MB, Current Drive seek penalty: {machineInfo.CurrentDriveHasSeekPenalty}, OS: {machineInfo.OsVersion}, .NETFramework: {machineInfo.DotNetFrameworkVersion}, Processor:{machineInfo.ProcessorIdentifier} - {machineInfo.ProcessorName}, CLR Version: {machineInfo.EnvironmentVersion}, Runtime Framework: '{machineInfo.RuntimeFrameworkName}', Starup directory: {startupDirectory}, Main configuration file: {mainConfig}";

        /// <summary>
        /// CAUTION!!
        ///
        /// WDG has Asimov telemetry listening to this event. Any change to an existing field will require a breaking change announcement
        ///
        /// This event is only used for ETW and telemetry. The commandLine must be scrubbed so it doesn't overflow
        /// </summary>
        [GeneratedEvent(
            (ushort)SharedLogEventId.DominoInvocation,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventOpcode = (byte)EventOpcode.Start,
            // Prevent this from going to the log. It is only for ETW and telemetry. DominoInvocationForLocalLog is for the log.
            Keywords = (int)Keywords.SelectivelyEnabled,
            Message = AppInvocationMessage)]
        public abstract void DominoInvocation(LoggingContext context, string commandLine, BuildInfo buildInfo, MachineInfo machineInfo, string sessionIdentifier, string relatedSessionIdentifier, string startupDirectory, string mainConfig);

        /// <summary>
        /// This is the event that populates the local log file. It differs from DominoInvocation in that it contains the raw commandline without any truncation
        /// </summary>
        [GeneratedEvent(
            (ushort)SharedLogEventId.DominoInvocationForLocalLog,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventOpcode = (byte)EventOpcode.Start,
            Message = AppInvocationMessage)]
        public abstract void DominoInvocationForLocalLog(LoggingContext context, string commandLine, BuildInfo buildInfo, MachineInfo machineInfo, string sessionIdentifier, string relatedSessionIdentifier, string startupDirectory, string mainConfig);

        [GeneratedEvent(
            (ushort)LogEventId.StartupTimestamp,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "{ShortProductName} Startup began at: '{0}' UTC time, '{1}' Local time")]
        public abstract void StartupTimestamp(LoggingContext context, string timestamp, string localTimestamp);

        [GeneratedEvent(
            (ushort)LogEventId.StartupCurrentDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "{ShortProductName} Startup Current Directory: '{0}'")]
        public abstract void StartupCurrentDirectory(LoggingContext context, string startupDirectory);

        /// <summary>
        /// CAUTION!!
        ///
        /// WDG has Asimov telemetry listening to this event. Any change to an existing field will require a breaking change announcement
        /// </summary>
        [GeneratedEvent(
            (ushort)LogEventId.DominoCompletion,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = "{ShortProductName} process exited with: ExitCode:'{0}', ExitType:{1}, ErrorBucket:{errorBucket}")]
        public abstract void DominoCompletion(LoggingContext context, int exitCode, string exitKind, string errorBucket, string bucketMessage, int processRunningTime);

        [GeneratedEvent(
            (ushort)LogEventId.DominoPerformanceSummary,

            // All data that goes into this is already sent to telemetry in other places. This event is just here for
            // sake of creating a pretty summary. Hence it is LocalOnly
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,

            Message = "Performance Summary:\r\n" +
                        "Time breakdown:\r\n" +
                        "    Application Initialization:            {appInitializationPercent}\r\n" +
                        "    Graph Construction:                    {graphConstructionPercent}\r\n" +
                        "        Checking for pip graph reuse:          {checkingForPipGraphReusePercent}\r\n" +
                        "        Reloading pip graph:                   {reloadingPipGraphPercent}\r\n" +
                        "        Create graph:                          {createGraphPercent}\r\n" +
                        "        Other:                                 {graphConstructionOtherPercent}%\r\n" +
                        "    Scrubbing:                             {scrubbingPercent}\r\n" +
                        "    Scheduler Initialization:              {schedulerInitPercent}\r\n" +
                        "    Execute Phase:                         {executePhasePercent}\r\n" +
                        "        Executing processes                    {processExecutePercent}%\r\n" +
                        "{telemetryTagsPercent}" +
                        "        Process running overhead:              {processRunningPercent}%\r\n" +
                        "            Hashing inputs:                        {hashingInputs}%\r\n" +
                        "            Checking for cache hits:               {checkingForCacheHit}%\r\n" +
                        "            Processing outputs:                    {processOutputs}%\r\n" +
                        "            Replay outputs from cache:             {replayFromCache}%\r\n" +
                        "            Prepare process sandbox:               {prepareSandbox}%\r\n" +
                        "            Non-process pips:                      {nonProcessPips}%\r\n" +
                        "            Other:                                 {processOverheadOther}%\r\n" +
                        "    Other:                                 {highLevelOtherPercent}%\r\n\r\n" +
                        "Process pip cache hits: {cacheHitRate}% ({processPipsCacheHit}/{totalProcessPips})\r\n" +
                        "Incremental scheduling up to date rate: {incrementalSchedulingPruneRate}% ({incrementalSchedulingPrunedPips}/{totalProcessPips})\r\n" +
                        "Server mode used: {serverUsed}\r\n" +
                        "Execute phase utilization: Avg CPU:{averageCpu}% Min Available Ram MB:{minAvailableMemoryMb} Avg Disk Active:{diskUsage}\r\n" +
                        "Factors limiting concurrency by build time: CPU:{limitingResourcePercentages.CPU}%, Graph shape:{limitingResourcePercentages.GraphShape}%, Disk:{limitingResourcePercentages.Disk}%, Memory:{limitingResourcePercentages.Memory}%, ProjectedMemory:{limitingResourcePercentages.ProjectedMemory}%, Semaphore:{limitingResourcePercentages.Semaphore}%, Concurrency limit:{limitingResourcePercentages.ConcurrencyLimit}%, Other:{limitingResourcePercentages.Other}%")]
        public abstract void DominoPerformanceSummary(LoggingContext context, int processPipsCacheHit, int cacheHitRate, int incrementalSchedulingPrunedPips, int incrementalSchedulingPruneRate, int totalProcessPips, bool serverUsed,
            string appInitializationPercent, string graphConstructionPercent, string scrubbingPercent, string schedulerInitPercent, string executePhasePercent, int highLevelOtherPercent,
            string checkingForPipGraphReusePercent, string reloadingPipGraphPercent, string createGraphPercent, int graphConstructionOtherPercent,
            int processExecutePercent, string telemetryTagsPercent, int processRunningPercent, int hashingInputs, int checkingForCacheHit, int processOutputs, int replayFromCache, int prepareSandbox, int processOverheadOther, int nonProcessPips,
            int averageCpu, int minAvailableMemoryMb, string diskUsage,
            LimitingResourcePercentages limitingResourcePercentages);

        [GeneratedEvent(
            (ushort)LogEventId.DominoCatastrophicFailure,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Critical,
            Message = "Telemetry Only")]
        public abstract void DominoCatastrophicFailure(LoggingContext context,
            string exception,
            BuildInfo buildInfo,
            ExceptionRootCause rootCause,
            bool wasServer,
            string firstUserError,
            string lastUserError,
            string firstInsfrastructureError,
            string lastInfrastructureError,
            string firstInternalError,
            string lastInternalError);

        [GeneratedEvent(
            (ushort)LogEventId.DominoMacOSCrashReport,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Critical,
            Message = "Telemetry Only")]
        public abstract void DominoMacOSCrashReport(LoggingContext context, string crashSessionId, string content, string type, string filename);

        [GeneratedEvent(
            (ushort)LogEventId.UsingExistingServer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            Message = EventConstants.PhasePrefix + "Running from existing {ShortProductName} server process.")]
        public abstract void UsingExistingServer(LoggingContext context, ServerModeBuildStarted serverModeBuildStarted);

        [GeneratedEvent(
            (ushort)LogEventId.AppServerBuildStart,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Message = "Starting build from server process: UniqueServerName:{uniqueAppServerName}, PID: {serverModeBuildStarted.PID}, Times process reused: {serverModeBuildStarted.TimesPreviouslyUsed}, ThreadCount:{serverModeBuildStarted.StartPerformance.ThreadCount}, HandleCount:{serverModeBuildStarted.StartPerformance.HandleCount}")]
        public abstract void ServerModeBuildStarted(LoggingContext context, ServerModeBuildStarted serverModeBuildStarted, string uniqueAppServerName);

        [GeneratedEvent(
            (ushort)LogEventId.AppServerBuildFinish,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "Finished build from server process: ThreadCount:{serverModeBuildCompleted.EndPerformance.ThreadCount} HandleCount:{serverModeBuildCompleted.EndPerformance.HandleCount}")]
        public abstract void ServerModeBuildCompleted(LoggingContext context, ServerModeBuildCompleted serverModeBuildCompleted);

        [GeneratedEvent(
            (ushort)LogEventId.StartingNewServer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            Message = EventConstants.PhasePrefix + "Starting new {ShortProductName} server process.")]
        public abstract void StartingNewServer(LoggingContext context, ServerModeBuildStarted serverModeBuildStarted);

        [GeneratedEvent(
            (ushort)LogEventId.CannotStartServer,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            Message = "Server mode was requested but cannot be started. {serverModeCannotStart.Reason}.")]
        public abstract void CannotStartServer(LoggingContext context, ServerModeCannotStart serverModeCannotStart);

        [GeneratedEvent(
            (ushort)LogEventId.DeploymentUpToDateCheckPerformed,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            Message = "{ShortProductName} binary deployment up-to-date check performed in {deploymentUpToDateCheck.TimeToUpToDateCheckMilliseconds}ms. Deployment cache created:{deploymentCacheCreated}, deployment duration:{serverDeploymentCacheCreated.TimeToCreateServerCacheMilliseconds}ms.")]
        public abstract void DeploymentUpToDateCheckPerformed(LoggingContext context, ServerDeploymentUpToDateCheck deploymentUpToDateCheck, bool deploymentCacheCreated, ServerDeploymentCacheCreated serverDeploymentCacheCreated);

        [GeneratedEvent(
            (ushort)LogEventId.DeploymentCacheCreated,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.UserMessage | Keywords.Progress),
            EventTask = (ushort)Tasks.HostApplication,
            Message = "{ShortProductName} deployment cache was created. This means this is the first time {ShortProductName} is requested to run in server mode or {ShortProductName} binaries changed. Duration: {serverDeploymentCacheCreated.TimeToCreateServerCacheMilliseconds}ms.")]
        public abstract void DeploymentCacheCreated(LoggingContext context, ServerDeploymentCacheCreated serverDeploymentCacheCreated);

        [GeneratedEvent(
            (ushort)LogEventId.TelemetryEnabledHideNotification,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = EventConstants.PhasePrefix + "Telemetry is enabled. SessionId: {sessionId}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TelemetryEnabledHideNotification(LoggingContext context, string sessionId);

        [GeneratedEvent(
            (ushort)LogEventId.MemoryLoggingEnabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Message = "Memory logging is enabled (/logmemory). This has a negative performance impact and should only be used for performing memory analysis.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void MemoryLoggingEnabled(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.TelemetryEnabledNotifyUser,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "Telemetry is enabled. SessionId: {sessionId}",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void TelemetryEnabledNotifyUser(LoggingContext context, string sessionId);

        [GeneratedEvent(
            (ushort)LogEventId.MappedRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Mapped root drive '{rootDrive}' to directory '{directory}'")]
        public abstract void MappedRoot(LoggingContext context, string rootDrive, string directory);

        [GeneratedEvent(
            (ushort)LogEventId.CatastrophicFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Catastrophic {ShortProductName} Failure.\nBuild:{build}{commitId}.\nException:{message}")]
        public abstract void CatastrophicFailure(LoggingContext context, string message, string commitId, string build);

        [GeneratedEvent(
            (ushort)LogEventId.CatastrophicFailureCausedByDiskSpaceExhaustion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "One or more I/O operations have failed since a volume is out of space. Ensure that the volumes containing build outputs, logs, or the build cache have sufficient free space, and try again.")]
        public abstract void CatastrophicFailureCausedByDiskSpaceExhaustion(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.StorageCatastrophicFailureDriveError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "One or more I/O operations have failed due to a disk error. Check your disk drives for errors.")]
        public abstract void StorageCatastrophicFailureCausedByDriveError(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.CatastrophicFailureMissingRuntimeDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "A runtime dependency was not found. This usually indicates one or more assemblies were not correctly copied with the {MainExecutableName} deployment. Details: {message}")]
        public abstract void CatastrophicFailureMissingRuntimeDependency(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.CatastrophicFailureCausedByCorruptedCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "{ShortProductName} cache is potentially corrupted. Please restart the build. {ShortProductName} will try to recover from this corruption in the next run. If this issue persists, please email domdev@microsoft.com")]
        public abstract void CatastrophicFailureCausedByCorruptedCache(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.Channel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Engine,
            Message = "Listen channel is '{channelName}'")]
        public abstract void Channel(LoggingContext context, string channelName);

        [GeneratedEvent(
            (ushort)LogEventId.CancellationRequested,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (ushort)Tasks.HostApplication,
            EventOpcode = (byte)EventOpcode.Info,
            Message = "Graceful cancellation requested.\r\n" + "Use ctrl-break for immediate termination. CAUTION! This may slow down the next build.")]
        public abstract void CancellationRequested(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.TelemetryShutDown,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Telemetry shut down completed in {0}ms")]
        public abstract void TelemetryShutDown(LoggingContext context, long telemetryShutdownDurationMs);

        [GeneratedEvent(
            (ushort)LogEventId.TelemetryShutDownException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Telemetry shut down results in an exception: {0}")]
        public abstract void TelemetryShutDownException(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)LogEventId.TelemetryShutdownTimeout,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Telemetry timed out after {0} milliseconds. This session will have incomplete telemetry data")]
        public abstract void TelemetryShutdownTimeout(LoggingContext context, long milliseconds);

        [GeneratedEvent(
            (ushort)LogEventId.EventCount,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Message = "N/A")]
        public abstract void EventCounts(LoggingContext context, IDictionary<string, int> entryMatches);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToEnumerateLogDirsForCleanup,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Failed to enumerate log directories for cleanup '{logsRoot}': {message}",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.HostApplication,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void FailedToEnumerateLogDirsForCleanup(LoggingContext context, string logsRoot, string message);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToCleanupLogDir,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Failed to delete log directory '{logDirectory}': {message}",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.HostApplication,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void FailedToCleanupLogDir(LoggingContext context, string logDirectory, string message);

        [GeneratedEvent(
            (ushort)LogEventId.WaitingCleanupLogDir,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Waiting for the log cleanup thread to finish...",
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.HostApplication,
            Keywords = (int)Keywords.UserMessage)]
        public abstract void WaitingCleanupLogDir(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.EventWriteFailuresOccurred,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.HostApplication,
            Message = "One or more event-write failures occurred. ETW trace sessions (including produced trace files) may be incomplete.")]
        public abstract void EventWriteFailuresOccurred(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.CoreDumpNoPermissions,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.HostApplication,
            Message = "Setting up core dump creation for abnormal program exits has failed. Make sure you have permissions to read and write the core dump directory at '{directory}'.")]
        public abstract void DisplayCoreDumpDirectoryNoPermissionsWarning(LoggingContext context, string directory);

        [GeneratedEvent(
            (int)LogEventId.CrashReportProcessing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.HostApplication,
            Message = "Crash reports could not be processed and uploaded, make sure the state file '{stateFilePath}' is not malformed and accessible. Error: {message}.")]
        public abstract void DisplayCrashReportProcessingFailedWarning(LoggingContext context, string stateFilePath, string message);

        [GeneratedEvent(
            (ushort)LogEventId.ChangeJournalServiceReady,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = EventConstants.PhasePrefix + "{ShortProductName} JournalService is properly set up and you are ready to use {ShortProductName} with graph-caching enabled.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ChangeJournalServiceReady(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.BuildHasPerfSmells,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "---------- PERFORMANCE SMELLS ----------",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void BuildHasPerfSmells(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.ProcessPipsUncacheable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Uncacheable Pips: This build had {count} pips that are not cacheable and will be unconditionally run. See related DX0269 messages earlier in the log for details.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ProcessPipsUncacheable(LoggingContext context, long count);

        [GeneratedEvent(
            (ushort)LogEventId.NoCriticalPathTableHits,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "No critical path info: This build could not optimize the critical path based on previous runtime information. Either this was the first build on a machine or the engine cache directory was deleted.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void NoCriticalPathTableHits(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.NoSourceFilesUnchanged,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "No FileContentTable: This build had to rehash all files instead of leveraging the USN journal to skip hashing of previously seen files. Either this was the first build on a machine or the engine cache directory was deleted.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void NoSourceFilesUnchanged(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.ServerModeDisabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Server mode disabled: This build disabled server mode. Unless this is a lab build, server mode should be enabled to speed up back to back builds.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void ServerModeDisabled(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.GraphCacheCheckJournalDisabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Change journal scan disabled: This build didn't utilize the change journal scan when checking for pip graph reuse. This significantly degrades I/O performance on spinning disk drives. The journal requires running as admin or installation of the change journal service. Check the warning log for details.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void GraphCacheCheckJournalDisabled(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.SlowCacheInitialization,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Cache initialization took {cacheInitializationDurationMs}ms. This long of an initialization may mean that cache metadata needed to be reconstructed because {ShortProductName} was not shut down cleanly in the previous build. Make sure to allow {ShortProductName} to shut down cleanly (single ctrl-c).",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void SlowCacheInitialization(LoggingContext context, long cacheInitializationDurationMs);

        [GeneratedEvent(
            (ushort)LogEventId.LogProcessesEnabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "The /logprocesses option is enabled which causes {ShortProductName} to capture data about all child processes and all file accesses. This is helpful for diagnosing problems, but slows down builds and should be selectively be enabled only when that data is needed.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void LogProcessesEnabled(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FrontendIOSlow,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.HostApplication,
            Message = "Reading build specifications was {0:N1}x more expensive as evaluating them. This is generally a sign that IO performance is degraded. This could be due to GVFS needing to materialize remote files.",
            Keywords = (int)Keywords.UserMessage)]
        public abstract void FrontendIOSlow(LoggingContext context, double factor);
    }

    /// <summary>
    /// Start of a build connecting to a server process
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct ServerModeBuildStarted
    {
        /// <summary>
        /// The server's process ID
        /// </summary>
        public int PID { get; set; }

        /// <summary>
        /// Number of times the server process was used in previous builds
        /// </summary>
        public int TimesPreviouslyUsed { get; set; }

        /// <summary>
        /// Time the server mode has been idle since its previous use
        /// </summary>
        public int TimeIdleSeconds { get; set; }

        /// <summary>
        /// Performance info from when the build started
        /// </summary>
        public PerformanceSnapshot StartPerformance { get; set; }
    }

    /// <summary>
    /// Snapshot of process
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct PerformanceSnapshot
    {
        /// <summary>
        /// Process ID
        /// </summary>
        public int PID { get; set; }

        /// <summary>
        /// Number of active threads
        /// </summary>
        public int ThreadCount { get; set; }

        /// <summary>
        /// Handles in current process
        /// </summary>
        public int HandleCount { get; set; }

        /// <summary>
        /// Gets the performance info from the current process
        /// </summary>
        public static PerformanceSnapshot CreateFromCurrentProcess()
        {
            Process me = Process.GetCurrentProcess();
            return new PerformanceSnapshot()
            {
                PID = me.Id,
                HandleCount = me.HandleCount,
                ThreadCount = me.Threads.Count,
            };
        }

        /// <summary>
        /// Compares two performance info objects by subtracting each field in the first from each field in the second
        /// </summary>
        public static PerformanceSnapshot Compare(PerformanceSnapshot first, PerformanceSnapshot second)
        {
            return new PerformanceSnapshot()
            {
                HandleCount = second.HandleCount - first.HandleCount,
                ThreadCount = second.ThreadCount - first.ThreadCount,
            };
        }
    }

    /// <summary>
    /// Data for when the server mode build is completed
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct ServerModeBuildCompleted
    {
        /// <summary>
        /// Performance info from when the build started
        /// </summary>
        public PerformanceSnapshot EndPerformance { get; set; }

        /// <summary>
        /// Difference between start and finish
        /// </summary>
        public PerformanceSnapshot PerformanceDifference { get; set; }
    }

    /// <summary>
    /// End of BuildXL invocation
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
    public struct DominoInvocationEnd
    {
        /// <summary>
        /// The exit code returned on the command line
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Details of how the process exited
        /// </summary>
        /// <remarks>
        /// This is separate from ExitCode because we may decide to expand exit codes to ex
        /// </remarks>
        public ExitKind ExitKind { get; set; }

        /// <summary>
        /// Total duration the process was open
        /// </summary>
        public int ProcessRunningTime { get; set; }
    }

    /// <summary>
    /// Reasons for not being able to start server mode
    /// </summary>
    [Serializable]
    public enum ServerCannotStartKind
    {
        /// <summary>
        /// Connecting to the server timed out
        /// </summary>
        Timeout,

        /// <summary>
        /// An exception when trying to create the server deployment
        /// </summary>
        Exception,

        /// <summary>
        /// The server process was started, but could not be used (startup crash?)
        /// </summary>
        ServerFailedToStart,

        /// <summary>
        /// The server process failed to start
        /// </summary>
        ServerProcessCreationFailed,
    }

    /// <summary>
    /// Server mode cannot be started
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    [Serializable]
    public struct ServerModeCannotStart
    {
        /// <summary>
        /// Kind
        /// </summary>
        public ServerCannotStartKind Kind { get; set; }

        /// <summary>
        /// Reason
        /// </summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// Up to date check of BuildXL deployment binaries
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    [Serializable]
    // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
    public struct ServerDeploymentUpToDateCheck
    {
        /// <summary>
        /// Time it takes to compute the deployment up to date hash
        /// </summary>
        public long TimeToUpToDateCheckMilliseconds { get; set; }
    }

    /// <summary>
    /// Deployment cache created
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    [Serializable]
    public struct ServerDeploymentCacheCreated
    {
        /// <summary>
        /// Time it takes to create BuildXL deployment cache
        /// </summary>
        public long TimeToCreateServerCacheMilliseconds { get; set; }
    }

    /// <summary>
    /// Summarizes status and perf data when BuildXL is started in server mode
    /// There is always an up to date check involved when server mode is requested
    /// There may be a cache creation involved and the server mode may not be able to start properly
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    [Serializable]
    public struct ServerModeStatusAndPerf
    {
        /// <summary>
        /// Up to date check for getting a hash of the deployed binaries
        /// </summary>
        public ServerDeploymentUpToDateCheck UpToDateCheck { get; set; }

        // TODO:for an owner: why the type is serializable? We're not using BinaryFormatter here.
#pragma warning disable CA2235 // Mark all non-serializable fields
        /// <summary>
        /// Server mode couldn't be started
        /// </summary>
        public ServerModeCannotStart? ServerModeCannotStart { get; set; }
#pragma warning restore CA2235 // Mark all non-serializable fields

        // TODO:for an owner: why the type is serializable? We're not using BinaryFormatter here.
#pragma warning disable CA2235 // Mark all non-serializable fields
        /// <summary>
        /// The server cache was created
        /// </summary>
        public ServerDeploymentCacheCreated? CacheCreated { get; set; }
#pragma warning restore CA2235 // Mark all non-serializable fields

        public void Write(BinaryWriter writer)
        {
            writer.Write(UpToDateCheck.TimeToUpToDateCheckMilliseconds);
            writer.Write(ServerModeCannotStart.HasValue);
            if (ServerModeCannotStart.HasValue)
            {
                writer.Write((int)ServerModeCannotStart.Value.Kind);
                writer.Write(ServerModeCannotStart.Value.Reason);
            }

            writer.Write(CacheCreated.HasValue);
            if (CacheCreated.HasValue)
            {
                writer.Write(CacheCreated.Value.TimeToCreateServerCacheMilliseconds);
            }
        }

        public static ServerModeStatusAndPerf Read(BinaryReader reader)
        {
            ServerModeStatusAndPerf ret = default(ServerModeStatusAndPerf);
            ret.UpToDateCheck = new ServerDeploymentUpToDateCheck()
            {
                TimeToUpToDateCheckMilliseconds = reader.ReadInt64(),
            };
            if (reader.ReadBoolean())
            {
                ret.ServerModeCannotStart = new ServerModeCannotStart()
                {
                    Kind = (ServerCannotStartKind)reader.ReadInt32(),
                    Reason = reader.ReadString(),
                };
            }
            else
            {
                ret.ServerModeCannotStart = null;
            }

            if (reader.ReadBoolean())
            {
                ret.CacheCreated = new ServerDeploymentCacheCreated()
                {
                    TimeToCreateServerCacheMilliseconds = reader.ReadInt64(),
                };
            }
            else
            {
                ret.CacheCreated = null;
            }

            return ret;
        }
    }
}
