// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.App.Tracing;
using BuildXL.Engine;
using BuildXL.Engine.Distribution;
using BuildXL.Engine.Visualization;
using BuildXL.Ide.Generator;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using SchedulerEventId = BuildXL.Scheduler.Tracing.LogEventId;
using Logger = BuildXL.App.Tracing.Logger;
using ProcessNativeMethods = BuildXL.Native.Processes.ProcessUtilities;
using Strings = bxl.Strings;
using BuildXL.Engine.Recovery;
using BuildXL.FrontEnd.Sdk.FileSystem;
#pragma warning disable SA1649 // File name must match first type name
using BuildXL.Visualization;
using BuildXL.Visualization.Models;
using BuildXL.Utilities.CrashReporting;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif
using static BuildXL.Utilities.FormattableStringEx;


namespace BuildXL
{
    /// <summary>
    /// Host for the BuildXL bxl.exe application. Enables special start/end behaviors such as logging details about the app host
    /// </summary>
    internal interface IAppHost
    {
        /// <summary>
        /// Called when the engine starts a run
        /// </summary>
        void StartRun(LoggingContext loggingContext);

        /// <summary>
        /// Called when the host ends a run
        /// </summary>
        void EndRun(LoggingContext loggingContext);

        /// <summary>
        /// Whether telemetry should be shut down after the run
        /// </summary>
        bool ShutDownTelemetryAfterRun { get; }
    }

    /// <summary>
    /// A single BuildXLApp will execute, and then this process will exit.
    /// </summary>
    internal sealed class SingleInstanceHost : IAppHost
    {
        /// <inheritdoc />
        public void StartRun(LoggingContext loggingContext)
        {
            return;
        }

        /// <inheritdoc />
        public void EndRun(LoggingContext loggingContext)
        {
            return;
        }

        /// <inheritdoc/>
        public bool ShutDownTelemetryAfterRun => true;
    }

    internal readonly struct AppResult
    {
        public readonly ExitKind ExitKind;
        public readonly ExitKind CloudBuildExitKind;
        public readonly EngineState EngineState;
        public readonly string ErrorBucket;
        public readonly string BucketMessage;
        public readonly bool KillServer;

        private AppResult(ExitKind exitKind, ExitKind cloudBuildExitKind, EngineState engineState, string errorBucket, string bucketMessage, bool killServer)
        {
            ExitKind = exitKind;
            CloudBuildExitKind = cloudBuildExitKind;
            EngineState = engineState;
            ErrorBucket = errorBucket;
            BucketMessage = bucketMessage;
            KillServer = killServer;
        }

        public static AppResult Create(ExitKind exitKind, EngineState engineState, string errorBucket, string bucketMessage = "", bool killServer = false)
        {
            return new AppResult(exitKind, exitKind, engineState, errorBucket, bucketMessage, killServer);
        }

        public static AppResult Create(ExitKind exitKind, ExitKind cloudBuildExitKind, EngineState engineState, string errorBucket, string bucketMessage = "", bool killServer = false)
        {
            return new AppResult(exitKind, cloudBuildExitKind, engineState, errorBucket, bucketMessage, killServer);
        }
    }

    /// <summary>
    /// Single instance of the BuildXL app. Corresponds to exactly one command line invocation / build.
    /// </summary>
    internal sealed class BuildXLApp : IDisposable
    {
        private const int FailureCompletionTimeoutMs = 30 * 1000;

        // 24K buffer size means that internally, the StreamWriter will use 48KB for a char[] array, and 73731 bytes for an encoding byte array buffer --- all buffers <85000 bytes, and therefore are not in large object heap
        private const int LogFileBufferSize = 24 * 1024;
        private static Encoding s_utf8NoBom;

        private readonly IAppHost m_appHost;
        private readonly IConsole m_console;

        // This one is only to be used for the initial run of the engine.
        private readonly ICommandLineConfiguration m_initialConfiguration;

        // The following are not readonly because when the engine uses a cached graph these two are recomputed.
        private IConfiguration m_configuration;
        private PathTable m_pathTable;

        private readonly DateTime m_startTimeUtc;
        private readonly IReadOnlyCollection<string> m_commandLineArguments;
        private bool m_hasInfrastructureFailures;

        // If server mode was requested but cannot be started, here is the reason
        private readonly ServerModeStatusAndPerf? m_serverModeStatusAndPerf;
        private static readonly BuildInfo s_buildInfo = BuildInfo.FromRunningApplication();
        private static readonly MachineInfo s_machineInfo = MachineInfo.CreateForCurrentMachine();

        // Cancellation request handling.
        private readonly CancellationTokenSource m_cancellationSource = new CancellationTokenSource();
        private int m_cancellationAlreadyAttempted = 0;
        private LoggingContext m_appLoggingContext;

        private readonly CrashCollectorMacOS m_crashCollector;

        /// <nodoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public BuildXLApp(
            IAppHost host,
            IConsole console,
            ICommandLineConfiguration initialConfig,
            PathTable pathTable,
            IReadOnlyCollection<string> commandLineArguments = null, // Pass null here if you want client to override cmd args.
            DateTime? startTimeUtc = null,
            ServerModeStatusAndPerf? serverModeStatusAndPerf = null)
        {
            Contract.Requires(initialConfig != null, "initialConfig can't be null");
            Contract.Requires(pathTable != null, "pathTable can't be null");
            Contract.Requires(host != null, "host can't be null");

            var mutableConfig = new BuildXL.Utilities.Configuration.Mutable.CommandLineConfiguration(initialConfig);

            string exeLocation = null;

            if (commandLineArguments != null)
            {
                exeLocation = commandLineArguments.FirstOrDefault();
                // The location may come as an absolute or relative path, depending on how bxl was called from command line.
                // Let's make sure we always pass an absolute path
                if (exeLocation != null)
                {
                    if (!Path.IsPathRooted(exeLocation))
                    {
                        // For the relative path case, we interpret it relative to the current directory of the app
                        exeLocation = Path.Combine(Directory.GetCurrentDirectory(), exeLocation);
                    }
                }
            }

            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(mutableConfig, pathTable, exeLocation);

            // If /debuggerBreakOnExit assume /debugScript
            if (mutableConfig.FrontEnd.DebuggerBreakOnExit())
            {
                mutableConfig.FrontEnd.DebugScript = true;
            }

            // Disable graph caching if debugging
            if (mutableConfig.FrontEnd.DebugScript())
            {
                mutableConfig.Cache.CacheGraph = false;
                mutableConfig.Engine.ReuseEngineState = false;
            }

            ConfigureDistributionLogging(pathTable, mutableConfig);
            ConfigureCloudBuildLogging(pathTable, mutableConfig);
            if (mutableConfig.Logging.CacheMissAnalysisOption.Mode != CacheMissMode.Disabled)
            {
                ConfigureCacheMissLogging(pathTable, mutableConfig);
            }

            m_configuration = mutableConfig;
            m_initialConfiguration = mutableConfig;

            if (console == null)
            {
                console = CreateStandardConsole(m_configuration.Logging, pathTable);
            }

            m_appHost = host;
            m_console = console;

            // Process start time isn't helpful if in an app server, so we allow an override.
            m_startTimeUtc = startTimeUtc ?? Process.GetCurrentProcess().StartTime.ToUniversalTime();

            // Allow the client to override the command line that gets logged which will be different from the server
            m_commandLineArguments = commandLineArguments ?? AssemblyHelper.GetCommandLineArgs();
            m_pathTable = pathTable;

            // This app was requested to be launched in server mode, but the server cannot be started
            // We store this to log it once the appropriate listeners are set up
            m_serverModeStatusAndPerf = serverModeStatusAndPerf;

            m_crashCollector = OperatingSystemHelper.IsUnixOS 
                ? new CrashCollectorMacOS(new[] { CrashType.BuildXL, CrashType.Kernel })
                : null;
        }

        private static void ConfigureCacheMissLogging(PathTable pathTable, BuildXL.Utilities.Configuration.Mutable.CommandLineConfiguration mutableConfig)
        {
            mutableConfig.Logging.CustomLog.Add(
                mutableConfig.Logging.CacheMissLog,
                new[]
                {
                    (int)EventId.CacheMissAnalysis,
                    (int)EventId.MissingKeyWhenSavingFingerprintStore,
                    (int)EventId.FingerprintStoreSavingFailed,
                    (int)EventId.FingerprintStoreToCompareTrace,
                    (int)EventId.SuccessLoadFingerprintStoreToCompare
                });
        }

        private static void ConfigureDistributionLogging(PathTable pathTable, BuildXL.Utilities.Configuration.Mutable.CommandLineConfiguration mutableConfig)
        {
            if (mutableConfig.Distribution.BuildRole != DistributedBuildRoles.None)
            {
                mutableConfig.Logging.CustomLog.Add(
                    mutableConfig.Logging.RpcLog, DistributionHelpers.DistributionAllMessages.ToArray());
            }
        }

        private static void ConfigureCloudBuildLogging(PathTable pathTable, BuildXL.Utilities.Configuration.Mutable.CommandLineConfiguration mutableConfig)
        {
            if (mutableConfig.InCloudBuild())
            {
                // Unless explicitly specified, async logging is enabled by default in CloudBuild
                if (!mutableConfig.Logging.EnableAsyncLogging.HasValue)
                {
                    mutableConfig.Logging.EnableAsyncLogging = true;
                }

                var logPath = mutableConfig.Logging.Log;

                // NOTE: We rely on explicit exclusion of pip output messages in CloudBuild rather than turning them off by default.
                mutableConfig.Logging.CustomLog.Add(
                    mutableConfig.Logging.PipOutputLog, new[] { (int)EventId.PipProcessOutput });

                mutableConfig.Logging.CustomLog.Add(
                    mutableConfig.Logging.DevLog, new[]
                    {
                        // Add useful low volume-messages for dev diagnostics here
                        (int)EventId.DominoInvocation,
                        (int)EventId.StartupTimestamp,
                        (int)EventId.StartupCurrentDirectory,
                        (int)EventId.DominoCompletion,
                        (int)EventId.DominoPerformanceSummary,
                        (int)EventId.DominoCatastrophicFailure,
                        (int)EventId.UnexpectedCondition,
                        (int)SchedulerEventId.CriticalPathPipRecord,
                        (int)SchedulerEventId.CriticalPathChain,
                        (int)EventId.HistoricMetadataCacheLoaded,
                        (int)EventId.HistoricMetadataCacheSaved,
                        (int)EventId.RunningTimesLoaded,
                        (int)EventId.RunningTimesSaved,
                        (int)SchedulerEventId.CreateSymlinkFromSymlinkMap,
                        (int)SchedulerEventId.SymlinkFileTraceMessage,
                        (int)EventId.StartEngineRun,
                        (int)Engine.Tracing.LogEventId.StartCheckingForPipGraphReuse,
                        (int)Engine.Tracing.LogEventId.EndCheckingForPipGraphReuse,
                        (int)Engine.Tracing.LogEventId.GraphNotReusedDueToChangedInput,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndInitializeResolversPhaseStart,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndInitializeResolversPhaseComplete,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndBuildWorkspacePhaseStart,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndBuildWorkspacePhaseComplete,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndWorkspaceAnalysisPhaseStart,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndWorkspaceAnalysisPhaseComplete,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndParsePhaseStart,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndParsePhaseComplete,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues,
                        (int)BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndEndEvaluateValues,
                        (int)EventId.StartLoadingRunningTimes,
                        (int)EventId.EndLoadingRunningTimes,
                        (int)Engine.Tracing.LogEventId.StartSerializingPipGraph,
                        (int)Engine.Tracing.LogEventId.EndSerializingPipGraph,
                        (int)EventId.ScrubbingStarted,
                        (int)EventId.ScrubbingFinished,
                        (int)EventId.StartSchedulingPipsWithFilter,
                        (int)EventId.EndSchedulingPipsWithFilter,
                        (int)EventId.StartScanningJournal,
                        (int)EventId.EndScanningJournal,
                        (int)Engine.Tracing.LogEventId.StartExecute,
                        (int)Engine.Tracing.LogEventId.EndExecute,
                        (int)EventId.PipDetailedStats,
                        (int)EventId.ProcessesCacheHitStats,
                        (int)EventId.ProcessesCacheMissStats,
                        (int)EventId.ProcessesSemaphoreQueuedStats,
                        (int)EventId.CacheTransferStats,
                        (int)EventId.OutputFileStats,
                        (int)EventId.SourceFileHashingStats,
                        (int)EventId.OutputFileHashingStats,
                        (int)EventId.BuildSetCalculatorStats,
                        (int)EventId.EndFilterApplyTraversal,
                        (int)EventId.EndAssigningPriorities,
                        (int)Engine.Tracing.LogEventId.DeserializedFile,
                        (int)EventId.PipQueueConcurrency,
                        (int)Engine.Tracing.LogEventId.GrpcSettings
                    });

                // Distribution related messages are disabled in default text log and routed to special log file
                mutableConfig.Logging.NoLog.AddRange(DistributionHelpers.DistributionInfoMessages);
                mutableConfig.Logging.NoLog.AddRange(DistributionHelpers.DistributionWarnings);

                // Need to route events to ETW from custom log since no log is enabled for text log
                // Use special distribution log kind in order to have a tag for only searching distribution
                // log events in Kusto.
                mutableConfig.Logging.CustomLogEtwKinds[mutableConfig.Logging.PipOutputLog] = "pipoutput";
            }
        }

        void IDisposable.Dispose()
        {
            m_cancellationSource.Dispose();
        }

        /// <nodoc />
        public AppResult Run(EngineState engineState = null)
        {
            if (Environment.GetEnvironmentVariable("BuildXLDebugOnStart") == "1" || m_initialConfiguration.LaunchDebugger)
            {
                if (OperatingSystemHelper.IsUnixOS)
                {
                    Console.WriteLine("=== Attach to this process from a debugger, then press ENTER to continue ...");
                    Console.ReadLine();
                }
                else
                {
                    Debugger.Launch();
                }
            }

            // Get rid of any prior engine state if this build is not configured to reuse it
            if (!m_configuration.Engine.ReuseEngineState)
            {
                engineState?.Dispose();
                engineState = null;
            }

            using (var appLoggers = new AppLoggers(m_startTimeUtc, m_console, m_configuration.Logging, m_pathTable,
                   notWorker: m_configuration.Distribution.BuildRole != DistributedBuildRoles.Worker,

                   // TODO: Remove this once we can add timestamps for all logs by default
                   displayWarningErrorTime: m_configuration.InCloudBuild()))
            {
                // Mapping roots. Error is logged here to console because file logging may be set up under
                // the mapped path. In success case, logging of root mappings is done below to ensure it goes
                IReadOnlyDictionary<string, AbsolutePath> rootMappings = m_configuration.Engine.RootMap;
                if (rootMappings != null && rootMappings.Count != 0)
                {
                    if (!(m_appHost is SingleInstanceHost))
                    {
                        return RunWithLoggingScope((pm) => AppResult.Create(ExitKind.InvalidCommandLine, null, string.Empty), sendFinalStatistics: appLoggers.SendFinalStatistics);
                    }

                    if (!ApplyRootMappings(rootMappings))
                    {
                        return RunWithLoggingScope((pm) => AppResult.Create(ExitKind.InternalError, null, "FailedToApplyRootMappings"), sendFinalStatistics: appLoggers.SendFinalStatistics);
                    }
                }

                // Remote telemetry is also disable when a debugger is attached. The motivation is that it messes up
                // the statistics because typically these are error cases and the timings will be off. A second motivation
                // is that telemetry systems typically don't respect the guideline of not throwing exceptions in regular
                // execution path, so a lot of first chance exceptions are encountered from telemetry when debugging.
                var remoteTelemetryEnabled = m_configuration.Logging.RemoteTelemetry != RemoteTelemetry.Disabled && !Debugger.IsAttached;
                Stopwatch stopWatch = null;
                if (remoteTelemetryEnabled)
                {
                    stopWatch = Stopwatch.StartNew();
                    AriaV2StaticState.Enable(AriaTenantToken.Key, m_configuration.Logging.LogsRootDirectory(m_pathTable).ToString(m_pathTable));
                    stopWatch.Stop();
                }
                else
                {
                    AriaV2StaticState.Disable();
                }

                return RunWithLoggingScope(
                    configureLogging: loggingContext =>
                    {
                        appLoggers.ConfigureLogging(loggingContext);
                        if (m_configuration.InCloudBuild())
                        {
                            appLoggers.EnableEtwOutputLogging(loggingContext);
                        }
                    },
                    sendFinalStatistics: () => appLoggers.SendFinalStatistics(),
                    run: (pm) =>
                    {
                        if (!ProcessUtilities.SetupProcessDumps(m_configuration.Logging.LogsDirectory.ToString(m_pathTable), out var coreDumpDirectory))
                        {
                            Logger.Log.DisplayCoreDumpDirectoryNoPermissionsWarning(pm.LoggingContext, coreDumpDirectory);
                        }

                        if (remoteTelemetryEnabled)
                        {
                            if (m_configuration.Logging.RemoteTelemetry == RemoteTelemetry.EnabledAndNotify)
                            {
                                Logger.Log.TelemetryEnabledNotifyUser(pm.LoggingContext, pm.LoggingContext.Session.Id);
                            }
                            else if (m_configuration.Logging.RemoteTelemetry == RemoteTelemetry.EnabledAndHideNotification)
                            {
                                Logger.Log.TelemetryEnabledHideNotification(pm.LoggingContext, pm.LoggingContext.Session.Id);
                            }

                            LoggingHelpers.LogCategorizedStatistic(pm.LoggingContext, Strings.TelemetryInitialization, Strings.DurationMs, (int)stopWatch.ElapsedMilliseconds);
                        }

                        CollectAndUploadCrashReports(pm.LoggingContext, remoteTelemetryEnabled);

                        foreach (var mapping in rootMappings)
                        {
                            Logger.Log.MappedRoot(pm.LoggingContext, mapping.Key, mapping.Value.ToString(m_pathTable));
                        }

                        // Start a cleanup if requested
                        Thread logCleanupThread = null;
                        if (m_configuration.Logging.LogsToRetain > 0)
                        {
                            logCleanupThread = new Thread(() =>
                            {
                                var rootLogsDirectory = m_configuration.Logging.LogsRootDirectory(m_pathTable).ToString(m_pathTable);
                                CleanupLogsDirectory(pm.LoggingContext, rootLogsDirectory, m_configuration.Logging.LogsToRetain);
                            });

                            logCleanupThread.IsBackground = true;   // Kill it if not done by the time we finish the build
                            logCleanupThread.Priority = ThreadPriority.Lowest;
                            logCleanupThread.Start();
                        }

                        var result = RunInternal(pm, m_cancellationSource.Token, appLoggers, engineState);

                        if (logCleanupThread != null && logCleanupThread.IsAlive)
                        {
                            Logger.Log.WaitingCleanupLogDir(pm.LoggingContext);
                            logCleanupThread.Join();
                        }

                        ProcessUtilities.TeardownProcessDumps();

                        return result;
                    });
            }
        }

        /// <nodoc />
        private void CollectAndUploadCrashReports(LoggingContext context, bool remoteTelemetryEnabled)
        {
            if (m_crashCollector != null)
            {
                // Put the state file at the root of the logs directory
                var stateFileDirectory = m_configuration.Logging.LogsRootDirectory(m_pathTable).ToString(m_pathTable);

                CrashCollectorMacOS.Upload upload = (IReadOnlyList<CrashReport> reports, string sessionId) =>
                {
                    if (!remoteTelemetryEnabled)
                    {
                        return false;
                    }

                    foreach (var report in reports)
                    {
                        Logger.Log.DominoMacOSCrashReport(context, sessionId, report.Content, report.Type.ToString(), report.FileName);
                    }

                    return true;
                };

                try
                {
                    m_crashCollector.UploadCrashReportsFromLastSession(context.Session.Id, stateFileDirectory, out var stateFilePath, upload);
                }
                catch (Exception ex)
                {
                    Logger.Log.DisplayCrashReportProcessingFailedWarning(context, stateFileDirectory, ex.GetLogEventMessage());
                }
            }
        }

        /// <nodoc />
        private AppResult RunInternal(PerformanceMeasurement pm, CancellationToken cancellationToken, AppLoggers appLoggers, EngineState engineState)
        {
            EngineState newEngineState = null;
            EngineLiveVisualizationInformation visualizationInformation = null;
            UnhandledExceptionEventHandler unhandledExceptionHandler = null;
            Action<Exception> unexpectedExceptionHandler = null;
            EventHandler<UnobservedTaskExceptionEventArgs> unobservedTaskHandler = null;
            EventHandler<FirstChanceExceptionEventArgs> firstChanceExceptionHandler = null;

            try
            {
                // This has a value if BuildXL was started in server mode
                if (m_serverModeStatusAndPerf.HasValue)
                {
                    ServerModeStatusAndPerf serverModeStatusAndPerf = m_serverModeStatusAndPerf.Value;

                    // There is always an up to date check related to starting server mode
                    Logger.Log.DeploymentUpToDateCheckPerformed(pm.LoggingContext, serverModeStatusAndPerf.UpToDateCheck, serverModeStatusAndPerf.CacheCreated.HasValue, serverModeStatusAndPerf.CacheCreated.HasValue ? serverModeStatusAndPerf.CacheCreated.Value : default(ServerDeploymentCacheCreated));

                    // We maybe created a deployment cache
                    if (serverModeStatusAndPerf.CacheCreated.HasValue)
                    {
                        Logger.Log.DeploymentCacheCreated(pm.LoggingContext, serverModeStatusAndPerf.CacheCreated.Value);
                    }

                    // The server mode maybe didn't start properly
                    if (serverModeStatusAndPerf.ServerModeCannotStart.HasValue)
                    {
                        Logger.Log.CannotStartServer(pm.LoggingContext, serverModeStatusAndPerf.ServerModeCannotStart.Value);
                    }
                }

                unhandledExceptionHandler =
                    (sender, eventArgs) =>
                    {
                        HandleUnhandledFailure(
                            eventArgs.ExceptionObject as Exception,
                            appLoggers,
                            pm);
                    };

                unexpectedExceptionHandler =
                    (exception) => {
                        if (EngineEnvironmentSettings.FailFastOnNullReferenceException && exception is NullReferenceException)
                        {
                            // Detach unhandled exception handler. Failing fast.
                            AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;

                            HandleUnhandledFailure(
                                exception,
                                appLoggers,
                                pm,
                                predefinedRootCause: ExceptionRootCause.FailFast);
                        }
                    };

                firstChanceExceptionHandler = OnFirstChanceException;
                ExceptionUtilities.UnexpectedException += unexpectedExceptionHandler;
                AppDomain.CurrentDomain.UnhandledException += unhandledExceptionHandler;
                AppDomain.CurrentDomain.FirstChanceException += firstChanceExceptionHandler;

                if (EngineEnvironmentSettings.FailFastOnCacheCriticalError)
                {
                    Cache.ContentStore.Interfaces.Tracing.CriticalErrorsObserver.OnCriticalError +=
                        (sender, args) => HandleUnhandledFailure(args.CriticalException, appLoggers, pm);
                }

                unobservedTaskHandler =
                    (sender, eventArgs) =>
                    {
                        HandleUnhandledFailure(
                            eventArgs.Exception,
                            appLoggers,
                            pm);
                    };

                TaskScheduler.UnobservedTaskException += unobservedTaskHandler;

                if (!OperatingSystemHelper.IsUnixOS)
                {
                    // Set the execution state to prevent the machine from going to sleep for the duration of the build
                    NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_SYSTEM_REQUIRED | NativeMethods.EXECUTION_STATE.ES_CONTINUOUS);
                }

                using (PerformanceCollector collector = CreateCounterCollectorIfEnabled(pm.LoggingContext))
                {
                    m_appHost.StartRun(pm.LoggingContext);

                    // Initialize the resources.csv log file if it is enabled.
                    if (m_configuration.Logging.StatusLog.IsValid)
                    {
                        appLoggers.ConfigureStatusLogFile(m_configuration.Logging.StatusLog);
                    }

                    try
                    {
                        Contract.Assume(m_initialConfiguration == m_configuration, "Expect the initial configuration to still match the updatable configuration object.");

                        newEngineState = RunEngineWithDecorators(pm.LoggingContext, cancellationToken, appLoggers, engineState, collector, out visualizationInformation);

                        Contract.Assert(EngineState.CorrectEngineStateTransition(engineState, newEngineState, out var incorrectMessage), incorrectMessage);

                        if (Events.Log.HasEventWriteFailures)
                        {
                            Logger.Log.EventWriteFailuresOccurred(pm.LoggingContext);
                        }

                        appLoggers.LogEventSummary(pm.LoggingContext);

                        if (appLoggers.TrackingEventListener.HasFailures)
                        {
                            WriteErrorToConsoleWithDefaultColor(Strings.App_Main_BuildFailed);

                            LogGeneratedFiles(pm.LoggingContext, appLoggers.TrackingEventListener, translator: appLoggers.PathTranslatorForLogging);

                            var classification = ClassifyFailureFromLoggedEvents(appLoggers.TrackingEventListener);
                            var cbClassification = GetExitKindForCloudBuild(appLoggers.TrackingEventListener);
                            return AppResult.Create(classification.ExitKind, cbClassification, newEngineState, classification.ErrorBucket, bucketMessage: classification.BucketMessage);
                        }

                        WriteToConsole(Strings.App_Main_BuildSucceeded);

                        LogGeneratedFiles(pm.LoggingContext, appLoggers.TrackingEventListener, translator: appLoggers.PathTranslatorForLogging);

                        if (m_configuration.Ide.IsEnabled)
                        {
                            var translator = appLoggers.PathTranslatorForLogging;
                            var configFile = m_initialConfiguration.Startup.ConfigFile;
                            IdeGenerator.WriteCmd(GetExpandedCmdLine(m_commandLineArguments), m_configuration.Ide, configFile, m_pathTable, translator);
                            var solutionFile = IdeGenerator.GetSolutionPath(m_configuration.Ide, m_pathTable).ToString(m_pathTable);
                            if (translator != null)
                            {
                                solutionFile = translator.Translate(solutionFile);
                            }

                            WriteToConsole(Strings.App_Vs_SolutionFile, solutionFile);
                            var vsVersions = IdeGenerator.GetVersionsNotHavingLatestPlugin();
                            if (vsVersions != null)
                            {
                                WriteWarningToConsole(Strings.App_Vs_InstallPlugin, vsVersions, IdeGenerator.LatestPluginVersion);
                            }
                        }

                        return AppResult.Create(ExitKind.BuildSucceeded, newEngineState, string.Empty);
                    }
                    finally
                    {
                        // Allow the app host to perform any shutdown actions before the logging is cleaned up.
                        m_appHost.EndRun(pm.LoggingContext);

                        if (!OperatingSystemHelper.IsUnixOS)
                        {
                            // Reset the ExecutionState
                            NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS);
                        }
                    }
                }
            }
            finally
            {
                if (newEngineState != null)
                {
                    var isTransferred = visualizationInformation?.TransferPipTableOwnership(newEngineState.PipTable);
                    Contract.Assume(!isTransferred.HasValue || isTransferred.Value);
                }

                if (visualizationInformation != null)
                {
                    visualizationInformation.Dispose();
                }

                // Due to some nasty patterns, we hold onto a static collection of hashers. Make sure these are no longer
                // referenced.
                ContentHashingUtilities.DisposeAndResetHasher();

                if (unexpectedExceptionHandler != null)
                {
                    ExceptionUtilities.UnexpectedException -= unexpectedExceptionHandler;
                }

                if (unhandledExceptionHandler != null)
                {
                    AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;
                }

                if (unobservedTaskHandler != null)
                {
                    TaskScheduler.UnobservedTaskException -= unobservedTaskHandler;
                }

                if (firstChanceExceptionHandler != null)
                {
                    AppDomain.CurrentDomain.FirstChanceException -= firstChanceExceptionHandler;
                }
            }
        }

        private void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            // Bug #1209727: Intercept first chance exception for ArgumentNullException for additional logging.
            if (e.Exception is ArgumentNullException argumentNullException &&
                argumentNullException.ParamName == "destination")
            {
                // Log full exception string with stack trace as unexpected condition
                OnUnexpectedCondition(string.Join(Environment.NewLine, "Bug 1209727", e.Exception.ToString()));
            }
        }

        private EngineState RunEngineWithDecorators(
            LoggingContext loggingContext,
            CancellationToken cancellationToken,
            AppLoggers appLoggers,
            EngineState engineState,
            PerformanceCollector collector,
            out EngineLiveVisualizationInformation visualizationInformation)
        {
            var fileSystem = new PassThroughFileSystem(m_pathTable);
            var engineContext = EngineContext.CreateNew(cancellationToken, m_pathTable, fileSystem);

            return RunEngine(
                    engineContext,
                    FrontEndControllerFactory.Create(
                        m_configuration.FrontEnd.FrontEndMode(),
                        loggingContext,
                        m_initialConfiguration,
                        collector),
                    appLoggers.TrackingEventListener,
                    engineState,
                    out visualizationInformation);
        }

        internal static (ExitKind ExitKind, string ErrorBucket, string BucketMessage) ClassifyFailureFromLoggedEvents(TrackingEventListener listener)
        {
            // The loss of connectivity to other machines during a distributed build is generally the true cause of the
            // failure even though it may manifest itself as a different failure first (like failure to materialize)
            if (listener.CountsPerEventId((EventId)BuildXL.Engine.Tracing.LogEventId.DistributionExecutePipFailedNetworkFailure) >= 1)
            {
                return (ExitKind: ExitKind.InfrastructureError, ErrorBucket: BuildXL.Engine.Tracing.LogEventId.DistributionExecutePipFailedNetworkFailure.ToString(), BucketMessage: string.Empty);
            }
            else if (listener.InternalErrorDetails.Count > 0)
            {
                return (ExitKind: ExitKind.InternalError, ErrorBucket: listener.InternalErrorDetails.FirstErrorName, BucketMessage: listener.InternalErrorDetails.FirstErrorMessage);
            }
            else if (listener.InfrastructureErrorDetails.Count > 0)
            {
                return (ExitKind: ExitKind.InfrastructureError, ErrorBucket: listener.InfrastructureErrorDetails.FirstErrorName, BucketMessage: listener.InfrastructureErrorDetails.FirstErrorMessage);
            }
            else
            {
                return (ExitKind: ExitKind.UserError, ErrorBucket: listener.UserErrorDetails.FirstErrorName, BucketMessage: listener.UserErrorDetails.FirstErrorMessage);
            }
        }

        /// <summary>
        /// Computes the legacy ExitKind used for CloudBuild integration. This needs to exist until GBR understands
        /// the simpler InternalError/InfrastructureError/UserError categorization.
        /// </summary>
        private static ExitKind GetExitKindForCloudBuild(TrackingEventListener listener)
        {
            foreach (var item in listener.CountsPerEvent)
            {
                if (item.Value > 0)
                {
                    // Pick the best bucket by the type of events that were logged. First wins.
                    switch (item.Key)
                    {
                        case (int)EventId.FileMonitoringError:
                            return ExitKind.BuildFailedWithFileMonErrors;
                        case (int)EventId.PipProcessExpectedMissingOutputs:
                            return ExitKind.BuildFailedWithMissingOutputErrors;
                        case (int)EventId.InvalidOutputDueToSimpleDoubleWrite:
                            return ExitKind.BuildFailedSpecificationError;
                        case (int)EventId.PipProcessError:
                        case (int)EventId.DistributionWorkerForwardedError:
                            return ExitKind.BuildFailedWithPipErrors;
                        case (int)EventId.CancellationRequested:
                            return ExitKind.BuildCancelled;
                        case (int)EventId.NoPipsMatchedFilter:
                            return ExitKind.NoPipsMatchFilter;
                    }
                }
            }

            return ExitKind.BuildFailedWithGeneralErrors;
        }

        private void LogGeneratedFiles(LoggingContext loggingContext, TrackingEventListener trackingListener, PathTranslator translator)
        {
            if (m_configuration.Logging.LogsDirectory.IsValid)
            {
                // When using the new style logging configuration, just show the path to the logs directory
                WriteToConsole(Strings.App_LogsDirectory, m_configuration.Logging.LogsDirectory.ToString(m_pathTable));
            }
            else
            {
                // Otherwise, show the path(s) of the various logs that could have been created
                Action<string, AbsolutePath> logFunction =
                    (message, file) =>
                    {
                        if (file.IsValid)
                        {
                            string path = file.ToString(m_pathTable);
                            if (translator != null)
                            {
                                path = translator.Translate(path);
                            }

                            bool fileExists = File.Exists(path);

                            if (fileExists)
                            {
                                if (trackingListener.HasFailures)
                                {
                                    WriteToConsole(message, path);
                                }
                                else
                                {
                                    WriteErrorToConsoleWithDefaultColor(message, path);
                                }
                            }
                        }
                    };

                logFunction(Strings.App_Main_Log, m_configuration.Logging.Log);
                logFunction(Strings.App_Main_Log, m_configuration.Logging.ErrorLog);
                logFunction(Strings.App_Main_Log, m_configuration.Logging.WarningLog);

                foreach (var log in m_configuration.Logging.CustomLog)
                {
                    logFunction(Strings.App_Main_Log, log.Key);
                }

                logFunction(Strings.App_Main_Snapshot, m_configuration.Export.SnapshotFile);
            }

            if (trackingListener.HasFailuresOrWarnings)
            {
                Logger.Log.DisplayHelpLink(loggingContext, Strings.DX_Help_Link_Prefix, Strings.DX_Help_Link);
            }
        }

        private AppResult RunWithLoggingScope(Func<PerformanceMeasurement, AppResult> run, Action sendFinalStatistics, Action<LoggingContext> configureLogging = null)
        {
            AppResult result = AppResult.Create(ExitKind.InternalError, null, "FailedBeforeRunAttempted");
            Guid relatedActivityId;
            if (!string.IsNullOrEmpty(m_configuration.Logging.RelatedActivityId))
            {
                var success = Guid.TryParse(m_configuration.Logging.RelatedActivityId, out relatedActivityId);
                Contract.Assume(success, "relatedActivityId guid must have been validated already as part of config validation.");
            }
            else
            {
                relatedActivityId = Guid.NewGuid();
            }

            LoggingContext topLevelContext = new LoggingContext(
                relatedActivityId,
                Branding.ProductExecutableName,
                new LoggingContext.SessionInfo(Guid.NewGuid().ToString(), ComputeEnvironment(m_configuration), relatedActivityId));

            // As the most of filesystem operations are defined as static, we need to reset counters not to add values between server-mode builds.
            FileUtilities.CreateCounters();

            using (PerformanceMeasurement pm = PerformanceMeasurement.StartWithoutStatistic(
                topLevelContext,
                (loggingContext) =>
                {
                    m_appLoggingContext = loggingContext;
                    Events.StaticContext = loggingContext;
                    var utcNow = DateTime.UtcNow;
                    var localNow = utcNow.ToLocalTime();

                    configureLogging?.Invoke(loggingContext);

                    string translatedLogDirectory = string.Empty;
                    if (m_configuration.Logging.LogsDirectory.IsValid)
                    {
                        // Log directory is not valid if BuildXL is invoked with /setupJournal argument.
                        // Otherwise, it is always valid as we call PopulateLoggingAndLayoutConfiguration in the BuildXLApp constructor.
                        // If the user does not provide /logDirectory, then the method call above (PopulateLogging...) will find a default one.
                        var pathTranslator = GetPathTranslator(m_configuration.Logging, m_pathTable);
                        var logDirectory = m_configuration.Logging.LogsDirectory.ToString(m_pathTable);
                        translatedLogDirectory = pathTranslator != null ? pathTranslator.Translate(logDirectory) : logDirectory;
                    }

                    Logger.LogDominoInvocation(
                        loggingContext,
                        GetExpandedCmdLine(m_commandLineArguments),
                        s_buildInfo,
                        s_machineInfo,
                        loggingContext.Session.Id,
                        relatedActivityId.ToString(),
                        m_configuration.Logging.Environment,
                        utcNow.Ticks,
                        translatedLogDirectory,
                        m_configuration.InCloudBuild(),
                        Directory.GetCurrentDirectory(),
                        m_initialConfiguration.Startup.ConfigFile.ToString(m_pathTable));

                    // "o" means it is round-trippable. It happens to be ISO-8601.
                    Logger.Log.StartupTimestamp(
                        loggingContext,
                        utcNow.ToString("o", CultureInfo.InvariantCulture),
                        localNow.ToString("o", CultureInfo.InvariantCulture));
                    Logger.Log.StartupCurrentDirectory(loggingContext, Directory.GetCurrentDirectory());
                },
                (loggingContext) =>
                {
                    var exitKind = result.ExitKind;
                    var utcNow = DateTime.UtcNow;
                    Logger.LogDominoCompletion(
                        loggingContext,
                        ExitCode.FromExitKind(exitKind),
                        exitKind,
                        result.CloudBuildExitKind,
                        result.ErrorBucket,
                        result.BucketMessage,
                        Convert.ToInt32(utcNow.Subtract(m_startTimeUtc).TotalMilliseconds),
                        utcNow.Ticks,
                        m_configuration.InCloudBuild());

                    sendFinalStatistics();

                    m_appLoggingContext = null;

                    // If required, shut down telemetry now that the last telemetry event has been sent.
                    if (m_appHost.ShutDownTelemetryAfterRun && AriaV2StaticState.IsEnabled)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        Exception telemetryShutdownException;

                        var shutdownResult = AriaV2StaticState.TryShutDown(out telemetryShutdownException);
                        switch (shutdownResult)
                        {
                            case AriaV2StaticState.ShutDownResult.Failure:
                                Logger.Log.TelemetryShutDownException(loggingContext, telemetryShutdownException?.Message);
                                break;
                            case AriaV2StaticState.ShutDownResult.Timeout:
                                LogTelemetryShutdownInfo(loggingContext, sw.ElapsedMilliseconds);
                                Logger.Log.TelemetryShutdownTimeout(loggingContext, sw.ElapsedMilliseconds);
                                break;
                            case AriaV2StaticState.ShutDownResult.Success:
                                LogTelemetryShutdownInfo(loggingContext, sw.ElapsedMilliseconds);
                                break;
                        }
                    }
                }))
            {
                var appServer = m_appHost as AppServer;
                if (appServer != null)
                {
                    // Verify the timestamp based hash.
                    // Whether the hash in the file (ServerCacheDeployment.hash) is still same as the one that is passed during server creation.
                    var hashInFile = ServerDeployment.GetDeploymentCacheHash(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory));
                    if (!string.Equals(hashInFile, appServer.TimestampBasedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        // If the hashes do not match, the server will be killed and the client will start its own server deployment again.
                        // The client will not observe any failures, so the user as well.
                        Logger.Log.ServerDeploymentDirectoryHashMismatch(m_appLoggingContext, appServer.TimestampBasedHash, hashInFile);
                        return AppResult.Create(ExitKind.InfrastructureError, null, string.Empty, killServer: true);
                    }
                }

                result = run(pm);
            }

            return result;
        }

        private static void LogTelemetryShutdownInfo(LoggingContext loggingContext, long elapsedMilliseconds)
        {
            Logger.Log.TelemetryShutDown(loggingContext, elapsedMilliseconds);

            // Note we log how long the telemetry shutdown took. It is necessary to shut down telemetry inside
            // RunWithLoggingScope in order to be able to log how long it took.
            BuildXL.Tracing.Logger.Log.StatisticWithoutTelemetry(
                loggingContext,
                "TelemetryShutdown.DurationMs",
                elapsedMilliseconds);
        }

        /// <summary>
        /// Produces a cmd line with expanded response files; starts with mapped BuildXL path.
        /// </summary>
        private static string GetExpandedCmdLine(IReadOnlyCollection<string> rawArgs)
        {
            Contract.Requires(rawArgs != null, "rawArgs must not be null.");
            Contract.Ensures(Contract.Result<string>() != null, "Result of the method can't be null.");

            var cl = new CommandLineUtilities(rawArgs);

            return string.Join(" ", cl.ExpandedArguments);
        }

        /// <summary>
        /// Applies the root drive remappings
        /// </summary>
        private bool ApplyRootMappings(IReadOnlyDictionary<string, AbsolutePath> rootMappings)
        {
            if (rootMappings.Count != 0)
            {
                foreach (var mapping in rootMappings)
                {
                    try
                    {
                        var rootPath = mapping.Value.ToString(m_pathTable);
                        if (!Directory.Exists(rootPath))
                        {
                            Directory.CreateDirectory(rootPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteErrorToConsole(
                            Strings.App_RootMapping_CantCreateDirectory,
                            mapping.Value,
                            mapping.Key,
                            ex.GetLogEventMessage());

                        return false;
                    }
                }

                if (
                    !ProcessNativeMethods.ApplyDriveMappings(
                        rootMappings.Select(kvp => new PathMapping(kvp.Key[0], kvp.Value.ToString(m_pathTable))).ToArray()))
                {
                    WriteErrorToConsole(Strings.App_RootMapping_CantApplyRootMappings);
                    return false;
                }
            }

            return true;
        }

        private static void CleanupLogsDirectory(LoggingContext loggingContext, string rootLogsDirectory, int logsToRetain)
        {
            string[] allLogDirs;

            try
            {
                allLogDirs = Directory.EnumerateDirectories(rootLogsDirectory).ToArray();
                if (allLogDirs.Length <= logsToRetain)
                {
                    return;
                }

                Array.Sort(allLogDirs, (x, y) => Directory.GetCreationTime(x).CompareTo(Directory.GetCreationTime(y)));
            }
            catch (DirectoryNotFoundException)
            {
                return; // Nothing to delete.
            }
            catch (Exception ex)
            {
                // Possible exceptions here are PathTooLongException, SecurityException and UnauthorizedAccessException. We catch all just to be safe.
                Logger.Log.FailedToEnumerateLogDirsForCleanup(loggingContext, rootLogsDirectory, ex.GetLogEventMessage());
                return;
            }

            for (int idx = 0; idx < allLogDirs.Length - logsToRetain; ++idx)
            {
                string dir = allLogDirs[idx];
                try
                {
                    FileUtilities.DeleteDirectoryContents(dir, true);
                }
                catch (BuildXLException ex)
                {
                    // No worries. Will do it next time.
                    Logger.Log.FailedToCleanupLogDir(loggingContext, dir, ex.Message);
                }
            }
        }

        #region EventListenerConfiguration

        /// <summary>
        /// All code generated EventSource implementations
        /// </summary>
        internal static IEnumerable<EventSource> GeneratedEventSources =>
            new EventSource[]
                {
                    global::bxl.ETWLogger.Log,
                    global::BuildXL.Engine.Cache.ETWLogger.Log,
                    global::BuildXL.Engine.ETWLogger.Log,
                    global::BuildXL.Scheduler.ETWLogger.Log,
                    global::BuildXL.Tracing.ETWLogger.Log,
                    global::BuildXL.Native.ETWLogger.Log,
                    global::BuildXL.Storage.ETWLogger.Log,
                    global::BuildXL.Processes.ETWLogger.Log,
                    global::BuildXL.FrontEnd.Sdk.ETWLogger.Log,
                    global::BuildXL.FrontEnd.Core.ETWLogger.Log,
                    global::BuildXL.FrontEnd.Download.ETWLogger.Log,
                    global::BuildXL.FrontEnd.Script.ETWLogger.Log,
                    global::BuildXL.FrontEnd.Script.Debugger.ETWLogger.Log,
                    global::BuildXL.FrontEnd.Nuget.ETWLogger.Log,
                    global::BuildXL.FrontEnd.MsBuild.ETWLogger.Log,
                    global::BuildXL.FrontEnd.Ninja.ETWLogger.Log,
                    global::BuildXL.FrontEnd.CMake.ETWLogger.Log,
               };

        internal static PathTranslator GetPathTranslator(ILoggingConfiguration conf, PathTable pathTable)
        {
            return conf.SubstTarget.IsValid && conf.SubstSource.IsValid && !conf.DisableLoggedPathTranslation
                ? new PathTranslator(conf.SubstTarget.ToString(pathTable), conf.SubstSource.ToString(pathTable))
                : null;
        }

        /// <summary>
        /// Enables per-task diagnostics according to arguments.
        /// </summary>
        private static void EnableTaskDiagnostics(ILoggingConfiguration configuration, BaseEventListener listener)
        {
            for (int i = 1; i <= (int)Tasks.Max; i++)
            {
                if ((configuration.Diagnostic & (DiagnosticLevels)(1 << i)) != 0)
                {
                    listener.EnableTaskDiagnostics((EventTask)i);
                }
            }
        }

        private sealed class AppLoggers : IDisposable
        {
            internal const string DefaultLogKind = "default";
            internal const string StatusLogKind = "status";

            private readonly IConsole m_console;
            private readonly ILoggingConfiguration m_configuration;
            private readonly PathTable m_pathTable;
            private readonly DateTime m_baseTime;

            private readonly object m_lock = new object();
            private readonly List<BaseEventListener> m_listeners = new List<BaseEventListener>();
            private readonly Dictionary<AbsolutePath, TextWriterEventListener> m_listenersByPath = new Dictionary<AbsolutePath, TextWriterEventListener>();
            private bool m_disposed;
            private readonly bool m_displayWarningErrorTime;
            private TextWriterEventListener m_defaultFileListener;
            private TextWriterEventListener m_statusFileListener;

            private readonly WarningManager m_warningManager;
            private readonly EventMask m_noLogMask;

            /// <summary>
            /// Path Translator used for logging. Note this may be disabled (null) even if subst or junctions are in effect.
            /// It should only be used for the sake of logging.
            /// </summary>
            public readonly PathTranslator PathTranslatorForLogging;

            // Note: this is not disposed directly because it is also within m_listeners and it gets disposed with
            // that collection
            private StatisticsEventListener m_statisticsEventListener;

            /// <summary>
            /// The path to the log file
            /// </summary>
            public readonly string LogPath;

            public AppLoggers(
                DateTime startTime,
                IConsole console,
                ILoggingConfiguration configuration,
                PathTable pathTable,
                bool notWorker,
                bool displayWarningErrorTime)
            {
                Contract.Requires(console != null);
                Contract.Requires(configuration != null);

                m_console = console;
                m_baseTime = startTime;
                m_configuration = configuration;
                m_pathTable = pathTable;
                m_displayWarningErrorTime = displayWarningErrorTime;

                LogPath = configuration.Log.ToString(pathTable);

                m_noLogMask = new EventMask(enabledEvents: null, disabledEvents: configuration.NoLog, nonMaskableLevel: EventLevel.Error);
                m_warningManager = CreateWarningManager(configuration);

                PathTranslatorForLogging = GetPathTranslator(configuration, pathTable);

                Events.Log.HasDiagnosticsArgument = configuration.Diagnostic != 0;
                EnsureEventProvidersInitialized();

                // Inialize the console logging early
                if (m_configuration.ConsoleVerbosity != VerbosityLevel.Off)
                {
                    ConfigureConsoleLogging(notWorker);
                }
            }

            private static WarningManager CreateWarningManager(IWarningHandling configuration)
            {
                var warningManager = new WarningManager();
                warningManager.AllWarningsAreErrors = configuration.TreatWarningsAsErrors;

                foreach (var messageNum in configuration.NoWarnings)
                {
                    warningManager.SetState(messageNum, WarningState.Suppressed);
                }

                foreach (var messageNum in configuration.WarningsAsErrors)
                {
                    warningManager.SetState(messageNum, WarningState.AsError);
                }

                foreach (var messageNum in configuration.WarningsNotAsErrors)
                {
                    warningManager.SetState(messageNum, WarningState.AsWarning);
                }

                return warningManager;
            }

            public TrackingEventListener TrackingEventListener { get; private set; }

            private string rootLogDirectory = null;

            public void ConfigureLogging(LoggingContext loggingContext)
            {
                lock (m_lock)
                {
                    Contract.Assume(!m_disposed);

                    rootLogDirectory = m_configuration.LogsDirectory.IsValid ? m_configuration.LogsDirectory.ToString(m_pathTable) : null;
                    ConfigureTrackingListener();

                    if (m_configuration.FileVerbosity != VerbosityLevel.Off && m_configuration.LogsDirectory.IsValid)
                    {
                        ConfigureFileLogging();
                    }

                    if (m_configuration.ErrorLog.IsValid)
                    {
                        ConfigureErrorAndWarningLogging(m_configuration.ErrorLog, true, false, displayTime: m_displayWarningErrorTime);
                    }

                    if (m_configuration.WarningLog.IsValid)
                    {
                        ConfigureErrorAndWarningLogging(m_configuration.WarningLog, false, true, displayTime: m_displayWarningErrorTime);
                    }

                    if (m_configuration.StatsLog.IsValid)
                    {
                        ConfigureStatisticsLogFile(m_configuration.StatsLog, loggingContext);
                    }

                    if (m_configuration.CustomLog != null && m_configuration.CustomLog.Count > 0)
                    {
                        ConfigureAdditionalFileLoggers(m_configuration.CustomLog);
                    }
                }
            }

            /// <summary>
            /// See <see cref="BaseEventListener.SuppressNonCriticalEventsInPreparationForCrash" />
            /// </summary>
            public void SuppressNonCriticalEventsInPreparationForCrash()
            {
                lock (m_lock)
                {
                    Contract.Assume(!m_disposed);

                    foreach (BaseEventListener listener in m_listeners)
                    {
                        listener.SuppressNonCriticalEventsInPreparationForCrash();
                    }
                }
            }

            /// <summary>
            /// Sends the FinalStatistic event to telemetry
            /// </summary>
            public void SendFinalStatistics()
            {
                m_statisticsEventListener.SendFinalStatistics();
            }

            public void Dispose()
            {
                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        return;
                    }

                    foreach (BaseEventListener listener in m_listeners)
                    {
                        listener.Dispose();
                    }

                    m_listeners.Clear();
                    m_listenersByPath.Clear();

                    m_disposed = true;
                }
            }

            private static void OnListenerDisabledDueToDiskWriteFailure(BaseEventListener listener)
            {
                throw new BuildXLException(
                    "Failed to write to an event listener, indicating that a volume is out of available space.",
                    ExceptionRootCause.OutOfDiskSpace);
            }

            private void AddListener(BaseEventListener listener)
            {
                EnableTaskDiagnostics(m_configuration, listener);
                m_listeners.Add(listener);
            }

            [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
            private void ConfigureTrackingListener()
            {
                var trackingEventListener = new TrackingEventListener(Events.Log, m_baseTime, m_warningManager.GetState);
                AddListener(trackingEventListener);
                TrackingEventListener = trackingEventListener;
            }

            [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
            private void ConfigureConsoleLogging(bool notWorker)
            {
                var listener = new ConsoleEventListener(
                    Events.Log,
                    m_console,
                    m_baseTime,
                    m_configuration.UseCustomPipDescriptionOnConsole,
                    m_configuration.LogsDirectory.IsValid ? m_configuration.LogsDirectory.ToString(m_pathTable) : null,
                    notWorker,
                    m_warningManager.GetState,
                    m_configuration.ConsoleVerbosity.ToEventLevel(),
                    m_noLogMask,
                    onDisabledDueToDiskWriteFailure: OnListenerDisabledDueToDiskWriteFailure,
                    maxStatusPips: m_configuration.FancyConsoleMaxStatusPips);

                AddListener(listener);
            }

            [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
                Justification =
                    "It complains that 'logFile' isn't disposed, but it is in fact disposed by virtue of being embedded in a StreamWriter which will get disposed.")]
            private static TextWriter CreateLogFile(string path)
            {
                LazilyCreatedStream s = new LazilyCreatedStream(path);

                // Occasionally we see things logged that aren't valid unicode characters.
                // Emitting gibberish for these peculiar characters isn't a big deal
                s_utf8NoBom = s_utf8NoBom ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                TextWriter writer = new StreamWriter(s, s_utf8NoBom, LogFileBufferSize);
                return writer;
            }

            private void ConfigureStatisticsLogFile(AbsolutePath logFilePath, LoggingContext loggingContext)
            {
                AddFileBasedListener(
                    logFilePath,
                    (writer) =>
                    {
                        m_statisticsEventListener = new StatisticsEventListener(
                            Events.Log,
                            writer,
                            loggingContext,
                            onDisabledDueToDiskWriteFailure: OnListenerDisabledDueToDiskWriteFailure);
                        m_statisticsEventListener.EnableTaskDiagnostics(Tasks.CommonInfrastructure);
                        return m_statisticsEventListener;
                    });
            }

            public void ConfigureStatusLogFile(AbsolutePath logFilePath)
            {
                m_statusFileListener = AddFileBasedListener(
                    logFilePath,
                    (writer) =>
                    {
                        var listener = new StatusEventListener(
                            Events.Log,
                            writer,
                            m_baseTime,
                            onDisabledDueToDiskWriteFailure: OnListenerDisabledDueToDiskWriteFailure);
                        listener.EnableTaskDiagnostics(Tasks.CommonInfrastructure);
                        return listener;
                    });
            }

            [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
                Justification =
                    "It complains that 'writer' isn't disposed, but it is in fact disposed by virtue of being embedded in a TextWriterEventListener.")]
            private TextWriterEventListener AddFileBasedListener(AbsolutePath logFilePath, Func<TextWriter, TextWriterEventListener> listenerCreator)
            {
                string logDir;
                try
                {
                    logDir = logFilePath.GetParent(m_pathTable).ToString(m_pathTable);
                }
                catch (Exception ex)
                {
                    WriteErrorToConsole(
                        Strings.Program_ConfigureFileLogging_Can_t_get_directory_name,
                        logFilePath,
                        ex.GetLogEventMessage());
                    return null;
                }

                try
                {
                    Directory.CreateDirectory(logDir);
                }
                catch (Exception ex)
                {
                    WriteErrorToConsole(
                        Strings.App_ConfigureFileLogging_CantCreateDirectory,
                        logDir,
                        ex.GetLogEventMessage());
                }

                TextWriter writer;
                try
                {
                    writer = CreateLogFile(logFilePath.ToString(m_pathTable));
                }
                catch (Exception ex)
                {
                    WriteErrorToConsole(
                        Strings.App_ConfigureFileLogging_CantOpenLogFile,
                        logFilePath,
                        ex.GetLogEventMessage());
                    return null;
                }

                var listener = listenerCreator(writer);
                m_listenersByPath[logFilePath] = listener;
                AddListener(listener);
                return listener;
            }

            private void ConfigureFileLogging()
            {
                m_defaultFileListener = AddFileBasedListener(
                    m_configuration.Log,
                    (writer) =>
                    {
                        var eventMask = new EventMask(enabledEvents: null, disabledEvents: m_configuration.NoLog, nonMaskableLevel: EventLevel.Error);
                        return new TextWriterEventListener(
                            Events.Log,
                            writer,
                            m_baseTime,
                            m_warningManager.GetState,
                            m_configuration.FileVerbosity.ToEventLevel(),
                            TimeDisplay.Milliseconds,
                            eventMask,
                            onDisabledDueToDiskWriteFailure: OnListenerDisabledDueToDiskWriteFailure,
                            pathTranslator: PathTranslatorForLogging);
                    });
            }

            private void ConfigureAdditionalFileLoggers(IReadOnlyDictionary<AbsolutePath, IReadOnlyList<int>> additionalLoggers)
            {
                foreach (var additionalLogger in additionalLoggers)
                {
                    AddFileBasedListener(
                        additionalLogger.Key,
                        (writer) =>
                        {
                            var eventMask = new EventMask(enabledEvents: additionalLogger.Value, disabledEvents: null, nonMaskableLevel: EventLevel.Error);
                            return new TextWriterEventListener(
                                Events.Log,
                                writer,
                                m_baseTime,
                                m_warningManager.GetState,
                                m_configuration.FileVerbosity.ToEventLevel(),
                                TimeDisplay.Milliseconds,
                                eventMask,
                                onDisabledDueToDiskWriteFailure: OnListenerDisabledDueToDiskWriteFailure,
                                pathTranslator: PathTranslatorForLogging);
                        });
                }
            }

            private void ConfigureErrorAndWarningLogging(AbsolutePath logFilePath, bool logErrors, bool logWarnings, bool displayTime)
            {
                AddFileBasedListener(
                    logFilePath,
                    (writer) =>
                    {
                        return new ErrorAndWarningEventListener(
                            Events.Log,
                            writer,
                            m_baseTime,
                            logErrors,
                            logWarnings,
                            m_warningManager.GetState,
                            PathTranslatorForLogging,
                            timeDisplay: displayTime ? TimeDisplay.Milliseconds : TimeDisplay.None);
                    });
            }

            /// <summary>
            /// Registers generated event sources as merged event sources
            ///
            /// This is a workaround for static initialization issues. We need to access and use each static event-source instance
            /// before creating any listeners, or the listeners will not appropriately poke them on construction (there is a static list of initialized event sources,
            /// and the event sources seem to be incorrectly lazy about initializing some internal state).
            /// </summary>
            public static void EnsureEventProvidersInitialized()
            {
                using (var dummy = new TrackingEventListener(Events.Log))
                {
                    foreach (var eventSource in GeneratedEventSources)
                    {
                        Events.Log.RegisterMergedEventSource(eventSource);
                    }
                }
            }

            /// <summary>
            /// Logs a count of the number of times each event was encountered. This only goes to telemetry
            /// </summary>
            public void LogEventSummary(LoggingContext loggingContext)
            {
                Logger.Log.EventCounts(loggingContext, TrackingEventListener.ToEventCountDictionary());
            }

            private void WriteErrorToConsole(string format, params object[] args)
            {
                m_console.WriteOutputLine(MessageLevel.Error, string.Format(CultureInfo.InvariantCulture, format, args));
            }

            internal void EnableEtwOutputLogging(LoggingContext loggingContext)
            {
                EtwOnlyTextLogger.EnableGlobalEtwLogging(loggingContext);
                m_defaultFileListener?.EnableEtwOutputLogging(new EtwOnlyTextLogger(loggingContext, DefaultLogKind));
                m_statusFileListener?.EnableEtwOutputLogging(new EtwOnlyTextLogger(loggingContext, StatusLogKind));

                foreach (var logEntry in m_configuration.CustomLogEtwKinds)
                {
                    TextWriterEventListener listener;
                    if (!string.IsNullOrEmpty(logEntry.Value) && m_listenersByPath.TryGetValue(logEntry.Key, out listener))
                    {
                        listener.EnableEtwOutputLogging(new EtwOnlyTextLogger(loggingContext, logEntry.Value));
                    }
                }
            }
        }

        #endregion

        private static string ComputeEnvironment(IConfiguration configuration)
        {
            using (var builderPool = Pools.StringBuilderPool.GetInstance())
            {
                StringBuilder sb = builderPool.Instance;
                sb.Append(configuration.Logging.Environment);
                sb.Append("Script");

                foreach (KeyValuePair<string, string> traceInfo in configuration.Logging.TraceInfo.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sb.Append(';');
                    sb.Append(traceInfo.Key);
                    sb.Append('=');
                    sb.Append(traceInfo.Value);
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Creates a counter collector if counter collection is enabled
        /// </summary>
        /// <returns>
        /// FxCop complains the IDisposable may not be disposed when it is created with a ternary operator in the using
        /// statement. Use this helper to avoid suppressing the rule
        /// </returns>
        private PerformanceCollector CreateCounterCollectorIfEnabled(LoggingContext loggingContext)
        {
            if (m_configuration.Logging.LogMemory)
            {
                Logger.Log.MemoryLoggingEnabled(loggingContext);
            }

            if (m_configuration.Logging.LogCounters)
            {
                return new PerformanceCollector(TimeSpan.FromSeconds(1), m_configuration.Logging.LogMemory);
            }

            return null;
        }

        internal void OnUnexpectedCondition(string condition)
        {
            if (m_appLoggingContext != null)
            {
                BuildXL.Tracing.Logger.Log.UnexpectedCondition(m_appLoggingContext, condition);
            }
        }

        /// <summary>
        /// Handle CTRL+C
        /// </summary>
        /// <returns>True if we want the program to die immediately; otherwise false</returns>
        internal bool OnConsoleCancelEvent(bool isTermination)
        {
            LoggingContext loggingContext = m_appLoggingContext;
            if (loggingContext == null)
            {
                // Event happened too early or too late --> ignore (let the program terminate).
                // That should be ok because:
                //   - if it is too early: it's as if it happened before this handler was set;
                //   - if it is too late: BuildXL is terminating anyway.
                return true;
            }

            // Exit hard & immediately in case of termination
            if (isTermination)
            {
                return true;
            }
            else
            {
                // Only log on the first cancel
                if (Interlocked.CompareExchange(ref m_cancellationAlreadyAttempted, 1, 0) == 0)
                {
                    // Log an event and set a cancellation signal but allow execution to continue.
                    // NOTE: it is important to log an error message before calling Cancel(),
                    //       because all the clients expect an error to be logged first.
                    Logger.Log.CancellationRequested(loggingContext);
                    m_cancellationSource.Cancel();
                }

                return false;
            }
        }

        private int m_unhandledFailureInProgress = 0;

        private void HandleUnhandledFailure(
            Exception exception,
            AppLoggers loggers,
            PerformanceMeasurement pm,
            ExceptionRootCause? predefinedRootCause = null)
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                // If there's an unhandled exception and debugger is attached then
                // before doing anything else we want to break so that issue can
                // be immediately debugged. The sample user scenario is having cdb/ntsd
                // being attached during BuildXL builds.
                NativeMethods.LaunchDebuggerIfAttached();
            }

            // Given some conditions like running out of disk space, many threads may start crashing at once.
            // We will give the first thread a chance to emit some telemetry and inform the user, but with multiple
            // threads blocked here (with arbitrary stacks below), we are possibly deadlocked. So, all threads but
            // the first are hijacked as watchdog timers: worst case, the process will exit after a generous delay.
            // TODO: Note that a single thread crashing could still deadlock if it tries to acquire locks held on its own stack.
            if (Interlocked.CompareExchange(ref m_unhandledFailureInProgress, 1, comparand: 0) != 0)
            {
                Thread.Sleep(FailureCompletionTimeoutMs);
                ExceptionUtilities.FailFast("Second-chance exception handler has not completed in the allowed time.", new InvalidOperationException());
                return;
            }

            ExitKind effectiveExitKind = ExitKind.InternalError;

            try
            {
                ExceptionRootCause rootCause =
                    predefinedRootCause ??
                    (exception == null
                        ? ExceptionRootCause.Unknown
                        : ExceptionUtilities.AnalyzeExceptionRootCause(exception));

                string failureMessage = exception?.ToStringDemystified() ?? Strings.App_HandleUnhandledFailure_UnknownException;

                // We want the crash-related critical events below to be visible (not hidden by incidental events from other threads).
                loggers.SuppressNonCriticalEventsInPreparationForCrash();

                // All remaining work may allocate disk space (flushing logs, creating dumps, etc.)
                // This means that with ExceptionRootCause.OutOfDiskSpace, it is fairly likely that
                // one of the following things will fail: think of them all as best-effort, and so
                // choose order carefully.
                // Writing to anything derived from BaseEventListener should be fairly safe though, due to special
                // handling in which disk-space failures disable the listener and separately ensure that that failure
                // is reported (we should end up here); see onDisabledDueToDiskWriteFailure in BaseEventListener.
                switch (rootCause)
                {
                    case ExceptionRootCause.OutOfDiskSpace:
                        // Note that we hide failureMessage from the user-facing logs for OutOfDiskSpace
                        // Full info including stacks / dumps may make it to telemetry still.
                        Logger.Log.CatastrophicFailureCausedByDiskSpaceExhaustion(pm.LoggingContext);
                        effectiveExitKind = ExitKind.InfrastructureError;
                        break;
                    case ExceptionRootCause.DataErrorDriveFailure:
                        // Note that we hide failureMessage from the user-facing logs for DataErrorDriveFailure
                        // Full info including stacks / dumps may make it to telemetry still.
                        Logger.Log.StorageCatastrophicFailureCausedByDriveError(pm.LoggingContext);
                        effectiveExitKind = ExitKind.InfrastructureError;
                        break;
                    case ExceptionRootCause.MissingRuntimeDependency:
                        if (exception is FileLoadException && failureMessage.Contains(global::BuildXL.Utilities.Strings.ExceptionUtilities_AccessDeniedPattern))
                        {
                            if (FileUtilities.TryFindOpenHandlesToFile((exception as FileLoadException).FileName, out string diagnosticInfo))
                            {
                                failureMessage += Environment.NewLine + diagnosticInfo;
                            }
                        }

                        Logger.Log.CatastrophicFailureMissingRuntimeDependency(pm.LoggingContext, failureMessage);
                        effectiveExitKind = ExitKind.InfrastructureError;
                        break;
                    case ExceptionRootCause.CorruptedCache:
                        // Failure message should have been logged at the detection sites.
                        Logger.Log.CatastrophicFailureCausedByCorruptedCache(pm.LoggingContext);
                        effectiveExitKind = ExitKind.InfrastructureError;
                        break;
                    case ExceptionRootCause.ConsoleNotConnected:
                        // Not a BuildXL error. Do not log or send a telemetry event.
                        // TODO: Maybe log on in the file? Definitely avoid the console to prevent a stack overflow.
                        effectiveExitKind = ExitKind.Aborted;
                        break;
                    default:
                        Logger.Log.CatastrophicFailure(pm.LoggingContext, failureMessage, s_buildInfo?.CommitId ?? string.Empty, s_buildInfo?.Build ?? string.Empty);
                        break;
                }

                // Mark failure for future recovery.
                var recovery = FailureRecoveryFactory.Create(pm.LoggingContext, m_pathTable, m_configuration);
                Analysis.IgnoreResult(recovery.TryMarkFailure(exception, rootCause));

                // Send a catastrophic failure telemetry event
                AppServer hostServer = m_appHost as AppServer;
                Logger.Log.DominoCatastrophicFailure(pm.LoggingContext, failureMessage, s_buildInfo, rootCause,
                    wasServer: hostServer != null,
                    firstUserError: loggers.TrackingEventListener.UserErrorDetails.FirstErrorName,
                    lastUserError: loggers.TrackingEventListener.UserErrorDetails.LastErrorName,
                    firstInsfrastructureError: loggers.TrackingEventListener.InfrastructureErrorDetails.FirstErrorName,
                    lastInfrastructureError: loggers.TrackingEventListener.InfrastructureErrorDetails.LastErrorName,
                    firstInternalError: loggers.TrackingEventListener.InternalErrorDetails.FirstErrorName,
                    lastInternalError: loggers.TrackingEventListener.InternalErrorDetails.LastErrorName);

                loggers.LogEventSummary(pm.LoggingContext);

                loggers.Dispose();

                pm.Dispose();

                if (rootCause == ExceptionRootCause.Unknown)
                {
                    string[] filesToAttach = m_configuration != null
                        ? new[]
                          {
                              m_configuration.Logging.Log.ToString(m_pathTable),
                              m_configuration.Logging.WarningLog.ToString(m_pathTable),
                              m_configuration.Logging.ErrorLog.ToString(m_pathTable),
                          }
                        : new string[0];

                    // Sometimes the crash dumps don't actually get attached to the WER report. Stick a full heap dump
                    // next to the log file for good measure.
                    try
                    {
                        string logDir = Path.GetDirectoryName(loggers.LogPath);
                        string logPrefix = Path.GetFileNameWithoutExtension(loggers.LogPath);
                        string dumpDir = Path.Combine(logDir, logPrefix, "dumps");
                        Directory.CreateDirectory(dumpDir);
                        Exception dumpException;
                        Analysis.IgnoreResult(BuildXL.Processes.ProcessDumper.TryDumpProcess(Process.GetCurrentProcess(), Path.Combine(dumpDir, "UnhandledFailure.zip"), out dumpException, compress: true));
                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    catch
                    {
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                    if (!OperatingSystemHelper.IsUnixOS)
                    {
                        WindowsErrorReporting.CreateDump(exception, s_buildInfo, filesToAttach, Events.StaticContext?.Session?.Id);
                    }
                }

                Exception telemetryShutdownException;

                // Use a relatively long shutdown timeout since it is important to capture data from crashes
                if (AriaV2StaticState.TryShutDown(TimeSpan.FromMinutes(1), out telemetryShutdownException) == AriaV2StaticState.ShutDownResult.Failure)
                {
                    effectiveExitKind = ExitKind.InfrastructureError;
                }

                // If this is a server mode we should try to write the exit code to the pipe so it makes it back to
                // the client. If the client doesn't get the exit message, it will exit with a error that it couldn't
                // communicate with the server.
                hostServer?.WriteExitCodeToClient(effectiveExitKind);

                if (rootCause == ExceptionRootCause.FailFast)
                {
                    Environment.FailFast("Configured to fail fast.", exception);
                }

                Environment.Exit(ExitCode.FromExitKind(effectiveExitKind));
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch (Exception)
            {
                // Oh my, this isn't going very well.
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            finally
            {
                if (predefinedRootCause == ExceptionRootCause.FailFast)
                {
                    Environment.FailFast("Configured to fail fast.", exception);
                }

                Environment.Exit(ExitCode.FromExitKind(ExitKind.InternalError));
            }
        }

        private EngineState RunEngine(
            EngineContext engineContext,
            FrontEndControllerFactory factory,
            TrackingEventListener trackingEventListener,
            EngineState engineState,
            out EngineLiveVisualizationInformation visualizationInformation)
        {
            visualizationInformation = null;

            var configuration = factory.Configuration;
            var loggingContext = factory.LoggingContext;

            var appInitializationDurationMs = (int)(DateTime.UtcNow - m_startTimeUtc).TotalMilliseconds;

            BuildXL.Tracing.Logger.Log.Statistic(
                loggingContext,
                new Statistic()
                {
                    Name = Statistics.AppHostInitializationDurationMs,
                    Value = appInitializationDurationMs,
                });

            var engine = BuildXLEngine.Create(
                loggingContext,
                engineContext,
                configuration,
                factory,
                factory.Collector,
                m_startTimeUtc,
                trackingEventListener,
                rememberAllChangedTrackedInputs: true,
                commitId: s_buildInfo?.IsDeveloperBuild == false ? s_buildInfo.CommitId : null,
                buildVersion:  s_buildInfo?.IsDeveloperBuild == false ? s_buildInfo.Build : null);

            if (engine == null)
            {
                return engineState;
            }

            if (configuration.Viewer != ViewerMode.Disable)
            {
                // Create the live visualization information object, hook it to the engine and make it available on the EngineModel
                visualizationInformation = new EngineLiveVisualizationInformation();
                engine.SetVisualizationInformation(visualizationInformation);
                EngineModel.VisualizationInformation = visualizationInformation;
            }

            if (configuration.Export.SnapshotFile.IsValid && configuration.Export.SnapshotMode != SnapshotMode.None)
            {
                engine.SetSnapshotCollector(
                    new SnapshotCollector(
                        loggingContext,
                        configuration.Export.SnapshotFile,
                        configuration.Export.SnapshotMode,
                        m_commandLineArguments));
            }


            var loggingQueue = m_configuration.Logging.EnableAsyncLogging.GetValueOrDefault() ? new LoggingQueue() : null;
            var asyncLoggingContext = new LoggingContext(loggingContext.ActivityId, loggingContext.LoggerComponentInfo, loggingContext.Session, loggingContext, loggingQueue);

            BuildXLEngineResult result = null;
            // All async logging needs to complete before code that checks the state of logging contexts or tracking event listeners.
            // The interactions with app loggers (specifically with the TrackingEventListener) presume all
            // logged events have been flushed. If async logging were still active the state may not be correct
            // with respect to the Engine's return value.
            using (loggingQueue?.EnterAsyncLoggingScope(asyncLoggingContext))
            {
                result = engine.Run(asyncLoggingContext, engineState);
            }

            Contract.Assert(result != null, "Running the engine should return a valid engine result.");

            // Graph caching complicates some things. we'll have to reload state which invalidates the pathtable and everything that holds
            // a pathtable like configuration.
            m_pathTable = engine.Context.PathTable;
            m_configuration = engine.Configuration;

            if (!result.IsSuccess)
            {
                // if this returns false, we better have seen some errors being logged
                Contract.Assert(
                    trackingEventListener.HasFailures && loggingContext.ErrorWasLogged,
                    I($"The build has failed but the logging infrastructure has not encountered an error. TrackingEventListener has errors:[{trackingEventListener.HasFailures}.] LoggingContext has errors:[{string.Join(", ", loggingContext.ErrorsLoggedById.ToArray())}]"));

                // Remember if we have network problems.
                m_hasInfrastructureFailures = engine.HasInfrastructureFailures;
            }

            var engineRunDuration = (int)(DateTime.UtcNow - m_startTimeUtc).TotalMilliseconds;

            AppPerformanceInfo appPerfInfo = new AppPerformanceInfo
            {
                AppInitializationDurationMs = appInitializationDurationMs,
                EnginePerformanceInfo = result.EnginePerformanceInfo,
                ServerModeUsed = m_appHost is AppServer,
                ServerModeEnabled = m_initialConfiguration.Server == ServerMode.Enabled,
                EngineRunDurationMs = engineRunDuration,
            };

            if (m_configuration.Engine.LogStatistics)
            {
                BuildXL.Tracing.Logger.Log.Statistic(
                    loggingContext,
                    new Statistic()
                    {
                        Name = Statistics.WarningWasLogged,
                        Value = loggingContext.WarningWasLogged ? 1 : 0,
                    });
                BuildXL.Tracing.Logger.Log.Statistic(
                    loggingContext,
                    new Statistic()
                    {
                        Name = Statistics.ErrorWasLogged,
                        Value = loggingContext.ErrorWasLogged ? 1 : 0,
                    });
                BuildXL.Tracing.Logger.Log.Statistic(
                    loggingContext,
                    new Statistic()
                    {
                        Name = Statistics.TimeToEngineRunCompleteMs,
                        Value = engineRunDuration,
                    });

                Logger.Log.AnalyzeAndLogPerformanceSummary(loggingContext, configuration, appPerfInfo);
            }

            return result.EngineState;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Caller is responsible for disposing these objects.")]
        internal static IConsole CreateStandardConsole(LightConfig lightConfig)
        {
            PathTranslator translator;
            PathTranslator.CreateIfEnabled(lightConfig.SubstTarget, lightConfig.SubstSource, out translator);

            return new StandardConsole(lightConfig.Color, lightConfig.AnimateTaskbar, lightConfig.FancyConsole, translator);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Caller is responsible for disposing these objects.")]
        internal static IConsole CreateStandardConsole(ILoggingConfiguration loggingConfiguration, PathTable pathTable)
        {
            PathTranslator translator = null;
            if (!loggingConfiguration.DisableLoggedPathTranslation)
            {
                PathTranslator.CreateIfEnabled(loggingConfiguration.SubstTarget, loggingConfiguration.SubstSource, pathTable, out translator);
            }

            return new StandardConsole(loggingConfiguration.Color, loggingConfiguration.AnimateTaskbar, loggingConfiguration.FancyConsole, translator);
        }

        private void WriteToConsole(string format, params object[] args)
        {
            m_console.WriteOutputLine(MessageLevel.Info, string.Format(CultureInfo.InvariantCulture, format, args));
        }

        private void WriteWarningToConsole(string format, params object[] args)
        {
            m_console.WriteOutputLine(MessageLevel.Warning, string.Format(CultureInfo.InvariantCulture, format, args));
        }

        private void WriteErrorToConsole(string format, params object[] args)
        {
            m_console.WriteOutputLine(MessageLevel.Error, string.Format(CultureInfo.InvariantCulture, format, args));
        }

        private void WriteErrorToConsoleWithDefaultColor(string format, params object[] args)
        {
            m_console.WriteOutputLine(MessageLevel.ErrorNoColor, string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }

    /// <nodoc />
    internal static class VerbosityLevelExtensions
    {
        /// <nodoc />
        public static EventLevel ToEventLevel(this VerbosityLevel level)
        {
            switch (level)
            {
                case VerbosityLevel.Error:
                    return EventLevel.Error;
                case VerbosityLevel.Warning:
                    return EventLevel.Warning;
                case VerbosityLevel.Informational:
                    return EventLevel.Informational;
                case VerbosityLevel.Verbose:
                    return EventLevel.Verbose;

                case VerbosityLevel.Off:
                    Contract.Assert(false, "Should not need conversion if it is disabled.");
                    return EventLevel.Informational;

                default:
                    Contract.Assert(false, "Unsupported verbosity level");
                    return EventLevel.Informational;
            }
        }
    }

    /// <summary>
    /// Performance related information about the BuildXLApp run
    /// </summary>
    public sealed class AppPerformanceInfo
    {
        /// <nodoc/>
        public long AppInitializationDurationMs;

        /// <nodoc/>
        public bool ServerModeEnabled;

        /// <nodoc/>
        public bool ServerModeUsed;

        /// <nodoc/>
        public EnginePerformanceInfo EnginePerformanceInfo;

        /// <nodoc/>
        public long EngineRunDurationMs;
    }
}
