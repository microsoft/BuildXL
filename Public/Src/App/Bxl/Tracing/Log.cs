// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Scheduler;
using BuildXL.Tracing;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

using static BuildXL.Scheduler.ExecutionSampler;

#pragma warning disable 1591

namespace BuildXL.App.Tracing
{
    /// <summary>
    /// Logging for bxl.exe.
    /// </summary>
    [EventKeywordsType(typeof(Events.Keywords))]
    [EventTasksType(typeof(Events.Tasks))]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private const string AppInvocationMessage = "{ShortProductName} Startup Command Line Arguments: '{commandLine}' \r\n{ShortProductName} version:{buildInfo.CommitId}, Build: {buildInfo.Build}, Session ID:{sessionIdentifier}, Related Session:{relatedSessionIdentifier}, MachineInfo: CPU count: {machineInfo.ProcessorCount}, Physical Memory: {machineInfo.InstalledMemoryMB}MB, Current Drive seek penalty: {machineInfo.CurrentDriveHasSeekPenalty}, OS: {machineInfo.OsVersion}, .NETFramework: {machineInfo.DotNetFrameworkVersion}, Processor:{machineInfo.ProcessorIdentifier} - {machineInfo.ProcessorName}, CLR Version: {machineInfo.EnvironmentVersion}, Starup directory: {startupDirectory}, Main configuration file: {mainConfig}";

        /// <summary>
        /// CAUTION!!
        ///
        /// WDG has Asimov telemetry listening to this event. Any change will require a breaking change announcement.
        /// 
        /// This event is only used for ETW and telemetry. The commandLine must be scrubbed so it doesn't overflow
        /// </summary>
        [GeneratedEvent(
            (ushort)EventId.DominoInvocation,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventOpcode = (byte)EventOpcode.Start,
            // Prevent this from going to the log. It is only for ETW and telemetry. DominoInvocationForLocalLog is for the log. 
            Keywords = (int)Events.Keywords.SelectivelyEnabled,
            Message = AppInvocationMessage)]
        public abstract void DominoInvocation(LoggingContext context, string commandLine, BuildInfo buildInfo, MachineInfo machineInfo, string sessionIdentifier, string relatedSessionIdentifier, string startupDirectory, string mainConfig);

        /// <summary>
        /// This is the event that populates the local log file. It differs from DominoInvocation in that it contains the raw commandline without any truncation
        /// </summary>
        [GeneratedEvent(
            (ushort)EventId.DominoInvocationForLocalLog,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventOpcode = (byte)EventOpcode.Start,
            Message = AppInvocationMessage)]
        public abstract void DominoInvocationForLocalLog(LoggingContext context, string commandLine, BuildInfo buildInfo, MachineInfo machineInfo, string sessionIdentifier, string relatedSessionIdentifier, string startupDirectory, string mainConfig);

        [GeneratedEvent(
            (ushort)EventId.StartupTimestamp,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "{ShortProductName} Startup began at: '{0}' UTC time, '{1}' Local time")]
        public abstract void StartupTimestamp(LoggingContext context, string timestamp, string localTimestamp);

        [GeneratedEvent(
            (ushort)EventId.StartupCurrentDirectory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "{ShortProductName} Startup Current Directory: '{0}'")]
        public abstract void StartupCurrentDirectory(LoggingContext context, string startupDirectory);

        /// <summary>
        /// CAUTION!!
        ///
        /// WDG has Asimov telemetry listening to this event. Any change will require a breaking change announcement.
        /// </summary>
        [GeneratedEvent(
            (ushort)EventId.DominoCompletion,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            EventOpcode = (byte)EventOpcode.Stop,
            Message = "{ShortProductName} process exited with: ExitCode:'{0}', ExitType:{1}, ErrorBucket:{errorBucket}")]
        public abstract void DominoCompletion(LoggingContext context, int exitCode, string exitKind, string errorBucket, int processRunningTime);

        [GeneratedEvent(
            (ushort)EventId.DominoPerformanceSummary,

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

        /// <summary>
        /// Analyzes and logs a performance summary. All of the data logged in this message is available in various places in
        /// the standard and stats log. This just consolidates it together in a human consumable format
        /// </summary>
        public void AnalyzeAndLogPerformanceSummary(LoggingContext context, ICommandLineConfiguration config, AppPerformanceInfo perfInfo)
        {
            // Don't bother logging this stuff if we don't actually have the data
            if (perfInfo == null || perfInfo.EnginePerformanceInfo == null || perfInfo.EnginePerformanceInfo.SchedulerPerformanceInfo == null)
            {
                return;
            }

            // Various heuristics for what caused the build to be slow.
            using (var sbPool = Pools.GetStringBuilder())
            {
                SchedulerPerformanceInfo schedulerInfo = perfInfo.EnginePerformanceInfo.SchedulerPerformanceInfo;

                long loadOrConstructGraph = perfInfo.EnginePerformanceInfo.GraphCacheCheckDurationMs +
                    perfInfo.EnginePerformanceInfo.GraphReloadDurationMs +
                    perfInfo.EnginePerformanceInfo.GraphConstructionDurationMs;

                // Log the summary

                // High level
                var graphConstruction = ComputeTimePercentage(loadOrConstructGraph, perfInfo.EngineRunDurationMs);
                var appInitialization = ComputeTimePercentage(perfInfo.AppInitializationDurationMs, perfInfo.EngineRunDurationMs);
                var scrubbing = ComputeTimePercentage(perfInfo.EnginePerformanceInfo.ScrubbingDurationMs, perfInfo.EngineRunDurationMs);
                var schedulerInit = ComputeTimePercentage(perfInfo.EnginePerformanceInfo.SchedulerInitDurationMs, perfInfo.EngineRunDurationMs);
                var executePhase = ComputeTimePercentage(perfInfo.EnginePerformanceInfo.ExecutePhaseDurationMs, perfInfo.EngineRunDurationMs);
                var highLevelOther = Math.Max(0, 100 - appInitialization.Item1 - graphConstruction.Item1 - scrubbing.Item1 - schedulerInit .Item1 - executePhase.Item1);

                // Graph construction
                var checkingForPipGraphReuse = ComputeTimePercentage(perfInfo.EnginePerformanceInfo.GraphCacheCheckDurationMs, loadOrConstructGraph);
                var reloadingPipGraph = ComputeTimePercentage(perfInfo.EnginePerformanceInfo.GraphReloadDurationMs, loadOrConstructGraph);
                var createGraph = ComputeTimePercentage(perfInfo.EnginePerformanceInfo.GraphConstructionDurationMs, loadOrConstructGraph);
                var graphConstructionOtherPercent = Math.Max(0, 100 - checkingForPipGraphReuse.Item1 - reloadingPipGraph.Item1 - createGraph.Item1);

                // Process Overhead
                long allStepsDuration = 0;
                foreach (PipExecutionStep step in Enum.GetValues(typeof(PipExecutionStep)))
                {
                    allStepsDuration += (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(step).TotalMilliseconds;
                }

                // ExecuteProcessDuration comes from the PipExecutionCounters and is a bit tighter around the actual external
                // process invocation than the corresponding counter in PipExecutionStepCounters;
                long allStepsMinusPipExecution = allStepsDuration - schedulerInfo.ExecuteProcessDurationMs;

                var hashingInputs = ComputeTimePercentage(
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.Start).TotalMilliseconds,
                    allStepsMinusPipExecution);
                var checkingForCacheHit = ComputeTimePercentage(
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.CacheLookup).TotalMilliseconds +
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.CheckIncrementalSkip).TotalMilliseconds,
                    allStepsMinusPipExecution);
                var processOutputs = ComputeTimePercentage(
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.PostProcess).TotalMilliseconds,
                    allStepsMinusPipExecution);
                var replayFromCache = ComputeTimePercentage(
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.RunFromCache).TotalMilliseconds +
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.MaterializeOutputs).TotalMilliseconds +
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.MaterializeInputs).TotalMilliseconds,
                    allStepsMinusPipExecution);
                var prepareSandbox = ComputeTimePercentage(
                    schedulerInfo.SandboxedProcessPrepDurationMs,
                    allStepsMinusPipExecution);
                var nonProcessPips = ComputeTimePercentage(
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.ExecuteNonProcessPip).TotalMilliseconds,
                    allStepsMinusPipExecution);
                var processOverheadOther = Math.Max(0, 100 - hashingInputs.Item1 - checkingForCacheHit.Item1 - processOutputs.Item1 - replayFromCache.Item1 - prepareSandbox.Item1 - nonProcessPips.Item1);

                StringBuilder sb = new StringBuilder();
                if (schedulerInfo.DiskStatistics != null)
                {
                    foreach (var item in schedulerInfo.DiskStatistics)
                    {
                        sb.AppendFormat("{0}:{1}% ", item.Drive, item.CalculateActiveTime(lastOnly: false));
                    }
                }

                // The performance summary looks at counters that don't get aggregated and sent back to the master from
                // all workers. So it only applies to single machine builds.
                if (config.Distribution.BuildWorkers == null || config.Distribution.BuildWorkers.Count == 0)
                {
                    DominoPerformanceSummary(
                        context,
                        processPipsCacheHit: (int)schedulerInfo.ProcessPipCacheHits,
                        cacheHitRate: ComputeTimePercentage(schedulerInfo.ProcessPipCacheHits, schedulerInfo.TotalProcessPips).Item1,
                        incrementalSchedulingPrunedPips: (int)schedulerInfo.ProcessPipIncrementalSchedulingPruned,
                        incrementalSchedulingPruneRate: ComputeTimePercentage(schedulerInfo.ProcessPipIncrementalSchedulingPruned, schedulerInfo.TotalProcessPips).Item1,
                        totalProcessPips: (int)schedulerInfo.TotalProcessPips,
                        serverUsed: perfInfo.ServerModeUsed,
                        graphConstructionPercent: graphConstruction.Item3,
                        appInitializationPercent: appInitialization.Item3,
                        scrubbingPercent: scrubbing.Item3,
                        schedulerInitPercent: schedulerInit.Item3,
                        executePhasePercent: executePhase.Item3,
                        highLevelOtherPercent: highLevelOther,
                        checkingForPipGraphReusePercent: checkingForPipGraphReuse.Item3,
                        reloadingPipGraphPercent: reloadingPipGraph.Item3,
                        createGraphPercent: createGraph.Item3,
                        graphConstructionOtherPercent: graphConstructionOtherPercent,
                        processExecutePercent: ComputeTimePercentage(schedulerInfo.ExecuteProcessDurationMs, allStepsDuration).Item1,
                        telemetryTagsPercent: ComputeTelemetryTagsPerformanceSummary(schedulerInfo),
                        processRunningPercent: ComputeTimePercentage(allStepsMinusPipExecution, allStepsDuration).Item1,
                        hashingInputs: hashingInputs.Item1,
                        checkingForCacheHit: checkingForCacheHit.Item1,
                        processOutputs: processOutputs.Item1,
                        replayFromCache: replayFromCache.Item1,
                        prepareSandbox: prepareSandbox.Item1,
                        processOverheadOther: processOverheadOther,
                        nonProcessPips: nonProcessPips.Item1,
                        averageCpu: schedulerInfo.AverageMachineCPU,
                        minAvailableMemoryMb: (int)schedulerInfo?.MachineMinimumAvailablePhysicalMB,
                        diskUsage: sb.ToString(),
                        limitingResourcePercentages: perfInfo.EnginePerformanceInfo.LimitingResourcePercentages ?? new LimitingResourcePercentages());
                }

                if (schedulerInfo.ProcessPipsUncacheable > 0)
                {
                    LogPerfSmell(context, () => ProcessPipsUncacheable(context, schedulerInfo.ProcessPipsUncacheable));
                }

                // Make sure there were some misses since a complete noop with incremental scheduling shouldn't cause this to trigger
                if (schedulerInfo.CriticalPathTableHits == 0 && schedulerInfo.CriticalPathTableMisses != 0)
                {
                    LogPerfSmell(context, () => NoCriticalPathTableHits(context));
                }

                // Make sure some source files were hashed since a complete noop build with incremental scheduling shouldn't cause this to trigger
                if (schedulerInfo.FileContentStats.SourceFilesUnchanged == 0 && schedulerInfo.FileContentStats.SourceFilesHashed != 0)
                {
                    LogPerfSmell(context, () => NoSourceFilesUnchanged(context));
                }

                if (!perfInfo.ServerModeEnabled)
                {
                    LogPerfSmell(context, () => ServerModeDisabled(context));
                }

                if (!perfInfo.EnginePerformanceInfo.GraphCacheCheckJournalEnabled)
                {
                    LogPerfSmell(context, () => GraphCacheCheckJournalDisabled(context));
                }

                if (perfInfo.EnginePerformanceInfo.CacheInitializationDurationMs > 5000)
                {
                    LogPerfSmell(context, () => SlowCacheInitialization(context, perfInfo.EnginePerformanceInfo.CacheInitializationDurationMs));
                }

                if (perfInfo.EnginePerformanceInfo.SchedulerPerformanceInfo.HitLowMemorySmell)
                {
                    LogPerfSmell(context, () => Scheduler.Tracing.Logger.Log.LowMemory(context, perfInfo.EnginePerformanceInfo.SchedulerPerformanceInfo.MachineMinimumAvailablePhysicalMB));
                }

                if (config.Sandbox.LogProcesses)
                {
                    LogPerfSmell(context, () => LogProcessesEnabled(context));
                }

                if (perfInfo.EnginePerformanceInfo.FrontEndIOWeight > 5)
                {
                    LogPerfSmell(context, () => FrontendIOSlow(context, perfInfo.EnginePerformanceInfo.FrontEndIOWeight));
                }
            }
        }

        private static string ComputeTelemetryTagsPerformanceSummary(SchedulerPerformanceInfo schedulerInfo)
        {
            string telemetryTagPerformanceSummary = string.Empty;
            if (schedulerInfo != null && schedulerInfo.ProcessPipCountersByTelemetryTag != null && schedulerInfo.ExecuteProcessDurationMs > 0)
            {
                var elapsedTimesByTelemetryTag = schedulerInfo.ProcessPipCountersByTelemetryTag.GetElapsedTimes(PipCountersByGroup.ExecuteProcessDuration);
                telemetryTagPerformanceSummary = string.Join(Environment.NewLine, elapsedTimesByTelemetryTag.Select(elapedTime => string.Format("{0,-12}{1,-39}{2}%", string.Empty, elapedTime.Key, ComputeTimePercentage((long)elapedTime.Value.TotalMilliseconds, schedulerInfo.ExecuteProcessDurationMs).Item1)));
                if (elapsedTimesByTelemetryTag.Count > 0)
                {
                    var otherTime = ComputeTimePercentage(Math.Max(0, schedulerInfo.ExecuteProcessDurationMs - elapsedTimesByTelemetryTag.Sum(elapedTime => (long)elapedTime.Value.TotalMilliseconds)), schedulerInfo.ExecuteProcessDurationMs);
                    telemetryTagPerformanceSummary += string.Format("{0}{1,-12}{2,-39}{3}%{0}", Environment.NewLine, string.Empty, "Other:", otherTime.Item1);
                }
            }

            return telemetryTagPerformanceSummary;
        }

        private void LogPerfSmell(LoggingContext context, Action action)
        {
            if (m_firstSmell)
            {
                BuildHasPerfSmells(context);
                m_firstSmell = false;
            }

            action();
        }

        private bool m_firstSmell = true;

        private static Tuple<int, string, string> ComputeTimePercentage(long numerator, long denominator)
        {
            int percent = 0;
            if (denominator > 0)
            {
                percent = (int)(100.0 * numerator / denominator);
            }

            string time = (int)TimeSpan.FromMilliseconds(numerator).TotalSeconds + "sec";
            string combined = percent + "% (" + time + ")";
            return new Tuple<int, string, string>(percent, time, combined);
        }

        [GeneratedEvent(
            (ushort)EventId.DominoCatastrophicFailure,
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
            (ushort)EventId.DominoMacOSCrashReport,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Critical,
            Message = "Telemetry Only")]
        public abstract void DominoMacOSCrashReport(LoggingContext context, string crashSessionId, string content, string type, string filename);

        [GeneratedEvent(
            (ushort)EventId.UsingExistingServer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = Events.PhasePrefix + "Running from existing {ShortProductName} server process.")]
        public abstract void UsingExistingServer(LoggingContext context, ServerModeBuildStarted serverModeBuildStarted);

        [GeneratedEvent(
            (ushort)EventId.AppServerBuildStart,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Message = "Starting build from server process: UniqueServerName:{uniqueAppServerName}, PID: {serverModeBuildStarted.PID}, Times process reused: {serverModeBuildStarted.TimesPreviouslyUsed}, ThreadCount:{serverModeBuildStarted.StartPerformance.ThreadCount}, HandleCount:{serverModeBuildStarted.StartPerformance.HandleCount}")]
        public abstract void ServerModeBuildStarted(LoggingContext context, ServerModeBuildStarted serverModeBuildStarted, string uniqueAppServerName);

        [GeneratedEvent(
            (ushort)EventId.AppServerBuildFinish,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "Finished build from server process: ThreadCount:{serverModeBuildCompleted.EndPerformance.ThreadCount} HandleCount:{serverModeBuildCompleted.EndPerformance.HandleCount}")]
        public abstract void ServerModeBuildCompleted(LoggingContext context, ServerModeBuildCompleted serverModeBuildCompleted);

        [GeneratedEvent(
            (ushort)EventId.StartingNewServer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = Events.PhasePrefix + "Starting new {ShortProductName} server process.")]
        public abstract void StartingNewServer(LoggingContext context, ServerModeBuildStarted serverModeBuildStarted);

        [GeneratedEvent(
            (ushort)EventId.CannotStartServer,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Progress),
            Message = "Server mode was requested but cannot be started. {serverModeCannotStart.Reason}.")]
        public abstract void CannotStartServer(LoggingContext context, ServerModeCannotStart serverModeCannotStart);

        [GeneratedEvent(
            (ushort)EventId.DeploymentUpToDateCheckPerformed,
            EventGenerators = EventGenerators.LocalAndTelemetry,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Progress),
            Message = "{ShortProductName} binary deployment up-to-date check performed in {deploymentUpToDateCheck.TimeToUpToDateCheckMilliseconds}ms. Deployment cache created:{deploymentCacheCreated}, deployment duration:{serverDeploymentCacheCreated.TimeToCreateServerCacheMilliseconds}ms.")]
        public abstract void DeploymentUpToDateCheckPerformed(LoggingContext context, ServerDeploymentUpToDateCheck deploymentUpToDateCheck, bool deploymentCacheCreated, ServerDeploymentCacheCreated serverDeploymentCacheCreated);

        [GeneratedEvent(
            (ushort)EventId.DeploymentCacheCreated,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Progress),
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "{ShortProductName} deployment cache was created. This means this is the first time {ShortProductName} is requested to run in server mode or {ShortProductName} binaries changed. Duration: {serverDeploymentCacheCreated.TimeToCreateServerCacheMilliseconds}ms.")]
        public abstract void DeploymentCacheCreated(LoggingContext context, ServerDeploymentCacheCreated serverDeploymentCacheCreated);

        [GeneratedEvent(
            (ushort)EventId.TelemetryEnabledHideNotification,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = Events.PhasePrefix + "Telemetry is enabled. SessionId: {sessionId}",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void TelemetryEnabledHideNotification(LoggingContext context, string sessionId);

        [GeneratedEvent(
            (ushort)EventId.MemoryLoggingEnabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Message = "Memory logging is enabled (/logmemory). This has a negative performance impact and should only be used for performing memory analysis.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void MemoryLoggingEnabled(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.TelemetryEnabledNotifyUser,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = Events.PhasePrefix + "Telemetry is enabled. SessionId: {sessionId}",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void TelemetryEnabledNotifyUser(LoggingContext context, string sessionId);

        [GeneratedEvent(
            (ushort)EventId.MappedRoot,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Mapped root drive '{rootDrive}' to directory '{directory}'")]
        public abstract void MappedRoot(LoggingContext context, string rootDrive, string directory);

        [GeneratedEvent(
            (ushort)EventId.CatastrophicFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Catastrophic {ShortProductName} Failure.\nBuild:{build}{commitId}.\nException:{message}")]
        public abstract void CatastrophicFailure(LoggingContext context, string message, string commitId, string build);

        [GeneratedEvent(
            (ushort)EventId.CatastrophicFailureCausedByDiskSpaceExhaustion,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "One or more I/O operations have failed since a volume is out of space. Ensure that the volumes containing build outputs, logs, or the build cache have sufficient free space, and try again.")]
        public abstract void CatastrophicFailureCausedByDiskSpaceExhaustion(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.StorageCatastrophicFailureDriveError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "One or more I/O operations have failed due to a disk error. Check your disk drives for errors.")]
        public abstract void StorageCatastrophicFailureCausedByDriveError(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.CatastrophicFailureMissingRuntimeDependency,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "A runtime dependency was not found. This usually indicates one or more assemblies were not correctly copied with the {MainExecutableName} deployment. Details: {message}")]
        public abstract void CatastrophicFailureMissingRuntimeDependency(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)EventId.CatastrophicFailureCausedByCorruptedCache,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Critical,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "{ShortProductName} cache is potentially corrupted. Please restart the build. {ShortProductName} will try to recover from this corruption in the next run. If this issue persists, please email domdev@microsoft.com")]
        public abstract void CatastrophicFailureCausedByCorruptedCache(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.Channel,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Engine,
            Message = "Listen channel is '{channelName}'")]
        public abstract void Channel(LoggingContext context, string channelName);

        [GeneratedEvent(
            (ushort)EventId.CancellationRequested,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (ushort)Events.Tasks.HostApplication,
            EventOpcode = (byte)EventOpcode.Info,
            Message = "Graceful cancellation requested.\r\n" + "Use ctrl-break for immediate termination. CAUTION! This may slow down the next build.")]
        public abstract void CancellationRequested(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.TelemetryShutDown,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Telemetry shut down completed in {0}ms")]
        public abstract void TelemetryShutDown(LoggingContext context, long telemetryShutdownDurationMs);

        [GeneratedEvent(
            (ushort)EventId.TelemetryShutDownException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Telemetry shut down results in an exception: {0}")]
        public abstract void TelemetryShutDownException(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)EventId.TelemetryShutdownTimeout,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Telemetry timed out after {0} milliseconds. This session will have incomplete telemetry data")]
        public abstract void TelemetryShutdownTimeout(LoggingContext context, long milliseconds);
        
        [GeneratedEvent(
            (ushort)EventId.ServerDeploymentDirectoryHashMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "ServerDeploymentDirectory hash mismatch: {ShortProductName} AppServer hash, {0} != ServerDeploymentDirectory hash, {1}. Re-running {ShortProductName} will fix the issue.")]
        public abstract void ServerDeploymentDirectoryHashMismatch(LoggingContext context, string hashInMemory, string hashInFile);

        [GeneratedEvent(
            (ushort)EventId.EventCount,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Message = "N/A")]
        public abstract void EventCounts(LoggingContext context, IDictionary<string, int> entryMatches);

        [GeneratedEvent(
            (ushort)EventId.FailedToEnumerateLogDirsForCleanup,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Failed to enumerate log directories for cleanup '{logsRoot}': {message}",
            EventLevel = Level.Informational,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void FailedToEnumerateLogDirsForCleanup(LoggingContext context, string logsRoot, string message);

        [GeneratedEvent(
            (ushort)EventId.FailedToCleanupLogDir,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Failed to delete log directory '{logDirectory}': {message}",
            EventLevel = Level.Informational,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void FailedToCleanupLogDir(LoggingContext context, string logDirectory, string message);

        [GeneratedEvent(
            (ushort)EventId.WaitingCleanupLogDir,
            EventGenerators = EventGenerators.LocalOnly,
            Message = "Waiting for the log cleanup thread to finish...",
            EventLevel = Level.Informational,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void WaitingCleanupLogDir(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.WaitingClientDebugger,
            EventGenerators = EventGenerators.LocalOnly,
            Message = @"Waiting for a debugger to connect (blocking). Configure VSCode by adding \""debugServer\"": {port} to your '.vscode/launch.json' and choose \""Attach to running {ShortScriptName}\"".",
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void WaitingForClientDebuggerToConnect(LoggingContext context, int port);

        [GeneratedEvent(
            (int)EventId.EventWriteFailuresOccurred,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.HostApplication,
            Message = "One or more event-write failures occurred. ETW trace sessions (including produced trace files) may be incomplete.")]
        public abstract void EventWriteFailuresOccurred(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.DisplayHelpLink,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.HostApplication,
            Message = "{helpLinkPrefix} {helpLink}")]
        public abstract void DisplayHelpLink(LoggingContext context, string helpLinkPrefix, string helpLink);

        [GeneratedEvent(
            (int)EventId.CoreDumpNoPermissions,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.HostApplication,
            Message = "Setting up core dump creation for abnormal program exits has failed. Make sure you have permissions to read and write the core dump directory at '{directory}'.")]
        public abstract void DisplayCoreDumpDirectoryNoPermissionsWarning(LoggingContext context, string directory);

        [GeneratedEvent(
            (int)EventId.CrashReportProcessing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.HostApplication,
            Message = "Crash reports could not be processed and uploaded, make sure the state file '{stateFilePath}' is not malformed and accessible. Error: {message}.")]
        public abstract void DisplayCrashReportProcessingFailedWarning(LoggingContext context, string stateFilePath, string message);

        [GeneratedEvent(
            (ushort)EventId.ChangeJournalServiceReady,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Message = Events.PhasePrefix + "{ShortProductName} JournalService is properly set up and you are ready to use {ShortProductName} with graph-caching enabled.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void ChangeJournalServiceReady(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.MaterializingProfilerReport,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = Events.PhasePrefix + "Writing profiler report to '{destination}'.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void MaterializingProfilerReport(LoggingContext context, string destination);

        [GeneratedEvent(
            (ushort)EventId.ErrorMaterializingProfilerReport,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.LogAlways,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = Events.PhasePrefix + "Profiler report could not be written. Error code {errorCode:X8}: {message}.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void ErrorMaterializingProfilerReport(LoggingContext context, int errorCode, string message);

        [GeneratedEvent(
            (ushort)EventId.BuildHasPerfSmells,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "---------- PERFORMANCE SMELLS ----------",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void BuildHasPerfSmells(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ProcessPipsUncacheable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Uncacheable Pips: This build had {count} pips that are not cacheable and will be unconditionally run. See related DX0269 messages earlier in the log for details.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void ProcessPipsUncacheable(LoggingContext context, long count);

        [GeneratedEvent(
            (ushort)EventId.NoCriticalPathTableHits,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "No critical path info: This build could not optimize the critical path based on previous runtime information. Either this was the first build on a machine or the engine cache directory was deleted.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void NoCriticalPathTableHits(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.NoSourceFilesUnchanged,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "No FileContentTable: This build had to rehash all files instead of leveraging the USN journal to skip hashing of previously seen files. Either this was the first build on a machine or the engine cache directory was deleted.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void NoSourceFilesUnchanged(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.ServerModeDisabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Server mode disabled: This build disabled server mode. Unless this is a lab build, server mode should be enabled to speed up back to back builds.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void ServerModeDisabled(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.GraphCacheCheckJournalDisabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Change journal scan disabled: This build didn't utilize the change journal scan when checking for pip graph reuse. This significantly degrades I/O performance on spinning disk drives. The journal requires running as admin or installation of the change journal service. Check the warning log for details.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void GraphCacheCheckJournalDisabled(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.SlowCacheInitialization,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Cache initialization took {cacheInitializationDurationMs}ms. This long of an initialization may mean that cache metadata needed to be reconstructed because {ShortProductName} was not shut down cleanly in the previous build. Make sure to allow {ShortProductName} to shut down cleanly (single ctrl-c).",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void SlowCacheInitialization(LoggingContext context, long cacheInitializationDurationMs);

        [GeneratedEvent(
            (ushort)EventId.LogProcessesEnabled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "The /logprocesses option is enabled which causes {ShortProductName} to capture data about all child processes and all file accesses. This is helpful for diagnosing problems, but slows down builds and should be selectively be enabled only when that data is needed.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void LogProcessesEnabled(LoggingContext context);

        [GeneratedEvent(
            (ushort)EventId.FrontendIOSlow,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.HostApplication,
            Message = "Reading build specifications was {0:N1}x more expensive as evaluating them. This is generally a sign that IO performance is degraded. This could be due to GVFS needing to materialize remote files.",
            Keywords = (int)Events.Keywords.UserMessage)]
        public abstract void FrontendIOSlow(LoggingContext context, double factor);

        /// <summary>
        /// Logging DominoCompletion with an extra CloudBuild event
        /// </summary>
        public static void LogDominoCompletion(LoggingContext context, int exitCode, ExitKind exitKind, ExitKind cloudBuildExitKind, string errorBucket, int processRunningTime, long utcTicks, bool inCloudBuild)
        {
            Log.DominoCompletion(context, exitCode, exitKind.ToString(), errorBucket, processRunningTime);

            // Sending a different event to CloudBuild ETW listener.
            if (inCloudBuild)
            {
                BuildXL.Tracing.CloudBuildEventSource.Log.DominoCompletedEvent(new DominoCompletedEvent
                {
                    ExitCode = exitCode,
                    UtcTicks = utcTicks,
                    ExitKind = cloudBuildExitKind,
                });
            }
        }

        /// <summary>
        /// Logging DominoInvocation with an extra CloudBuild event
        /// </summary>
        public static void LogDominoInvocation(
            LoggingContext context,
            string commandLine,
            BuildInfo buildInfo,
            MachineInfo machineInfo,
            string sessionIdentifier,
            string relatedSessionIdentifier,
            ExecutionEnvironment environment,
            long utcTicks,
            string logDirectory,
            bool inCloudBuild,
            string startupDirectory,
            string mainConfigurationFile)
        {
            Log.DominoInvocation(context, ScrubCommandLine(commandLine, 100000, 100000), buildInfo, machineInfo, sessionIdentifier, relatedSessionIdentifier, startupDirectory, mainConfigurationFile);
            Log.DominoInvocationForLocalLog(context, commandLine, buildInfo, machineInfo, sessionIdentifier, relatedSessionIdentifier, startupDirectory, mainConfigurationFile);

            if (inCloudBuild)
            {
                // Sending a different event to CloudBuild ETW listener.
                BuildXL.Tracing.CloudBuildEventSource.Log.DominoInvocationEvent(new DominoInvocationEvent
                {
                    UtcTicks = utcTicks,

                    // Truncate the command line that gets to the CB event to avoid exceeding the max event payload of 64kb
                    CommandLineArgs = commandLine?.Substring(0, Math.Min(commandLine.Length, 4 * 1024)),
                    DominoVersion = buildInfo.CommitId,
                    Environment = environment,
                    LogDirectory = logDirectory,
                });
            }
        }

        /// <summary>
        /// Scrubs the command line to make it amenable (short enough) to be sent to telemetry
        /// </summary>
        internal static string ScrubCommandLine(string rawCommandLine, int leadingChars, int trailingChars)
        {
            Contract.Requires(rawCommandLine != null);
            Contract.Requires(leadingChars >= 0);
            Contract.Requires(trailingChars >= 0);

            if (rawCommandLine.Length < leadingChars + trailingChars)
            {
                return rawCommandLine;
            }

            int indexOfBreakWithinPrefix = rawCommandLine.LastIndexOf(' ', leadingChars);
            string prefix = indexOfBreakWithinPrefix == -1 ? rawCommandLine.Substring(0, leadingChars) : rawCommandLine.Substring(0, indexOfBreakWithinPrefix);
            string breakMarker = indexOfBreakWithinPrefix == -1 ? "[...]" : " [...]";

            int indexOfBreakWithinSuffix = rawCommandLine.IndexOf(' ', rawCommandLine.Length - trailingChars);
            string suffix = indexOfBreakWithinSuffix == -1 ? rawCommandLine.Substring(rawCommandLine.Length - trailingChars) : rawCommandLine.Substring(indexOfBreakWithinSuffix, rawCommandLine.Length - indexOfBreakWithinSuffix);

            return prefix + breakMarker + suffix;
        }
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
