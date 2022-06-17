// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
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
using BuildXL.Engine.Recovery;
using BuildXL.FrontEnd.Factory;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Ide.Generator;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.ViewModel;
using Logger = BuildXL.App.Tracing.Logger;
using AppLogEventId = BuildXL.App.Tracing.LogEventId;
using SchedulerLogEventId = BuildXL.Scheduler.Tracing.LogEventId;
using EngineLogEventId = BuildXL.Engine.Tracing.LogEventId;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;
using ProcessNativeMethods = BuildXL.Native.Processes.ProcessUtilities;
using TracingLogEventId = BuildXL.Tracing.LogEventId;
using PipsLogEventId = BuildXL.Pips.Tracing.LogEventId;
using PluginLogEventId = BuildXL.Plugin.Tracing.LogEventId;
using StorageLogEventId = BuildXL.Storage.Tracing.LogEventId;

using Strings = bxl.Strings;
#pragma warning disable SA1649 // File name must match first type name
using BuildXL.Utilities.CrashReporting;

using static BuildXL.Utilities.FormattableStringEx;
using System.Runtime.InteropServices;
using BuildXL.Utilities.Tasks;

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

        private AppResult(ExitKind exitKind, ExitKind cloudBuildExitKind, EngineState engineState, string errorBucket, string bucketMessage)
        {
            ExitKind = exitKind;
            CloudBuildExitKind = cloudBuildExitKind;
            EngineState = engineState;
            ErrorBucket = errorBucket;
            BucketMessage = bucketMessage;
        }

        public static AppResult Create(ExitKind exitKind, EngineState engineState, string errorBucket, string bucketMessage = "")
        {
            return new AppResult(exitKind, exitKind, engineState, errorBucket, bucketMessage);
        }

        public static AppResult Create(ExitKind exitKind, ExitKind cloudBuildExitKind, EngineState engineState, string errorBucket, string bucketMessage = "")
        {
            return new AppResult(exitKind, cloudBuildExitKind, engineState, errorBucket, bucketMessage);
        }
    }

    /// <summary>
    /// Single instance of the BuildXL app. Corresponds to exactly one command line invocation / build.
    /// </summary>
    internal sealed class BuildXLApp : IDisposable
    {
        // We give the failure completion logic a generous 300 seconds to complete since in some cases taking a crash dump
        // can take quite a while
        private const int FailureCompletionTimeoutMs = 300 * 1000;

        // 24K buffer size means that internally, the StreamWriter will use 48KB for a char[] array, and 73731 bytes for an encoding byte array buffer --- all buffers <85000 bytes, and therefore are not in large object heap
        private const int LogFileBufferSize = 24 * 1024;
        private static Encoding s_utf8NoBom;

        private LoggingContext m_loggingContextForCrashHandler;

        private readonly IAppHost m_appHost;
        private readonly IConsole m_console;

        // This one is only to be used for the initial run of the engine.
        private readonly ICommandLineConfiguration m_initialConfiguration;

        // The following are not readonly because when the engine uses a cached graph these two are recomputed.
        private IConfiguration m_configuration;
        private PathTable m_pathTable;

        private readonly DateTime m_startTimeUtc;
        private readonly IReadOnlyCollection<string> m_commandLineArguments;

        // If server mode was requested but cannot be started, here is the reason
        private readonly ServerModeStatusAndPerf? m_serverModeStatusAndPerf;
        private static readonly BuildInfo s_buildInfo = BuildInfo.FromRunningApplication();
        private static readonly MachineInfo s_machineInfo = MachineInfo.CreateForCurrentMachine();

        // Cancellation request handling.
        private readonly CancellationTokenSource m_cancellationSource = new CancellationTokenSource();
        private int m_cancellationAlreadyAttempted = 0;
        private const int MinEarlyCbTimeoutMins = 5; // Start Termination 5 mins before CB timeout gets triggered
        private const int MaxEarlyCbTimeoutMins = 30; // The Maximum time allowed for Early timeout
        private const int EarlyCbTimeoutPercentage = 2; // Early Termination set to 2% of remaining time before CB timeout gets triggered
        private LoggingContext m_appLoggingContext;

        private BuildViewModel m_buildViewModel;
        private readonly CrashCollectorMacOS m_crashCollector;

        // Allow a longer Aria telemetry flush time in CloudBuild since we're more willing to wait at the tail of builds there
        private TimeSpan TelemetryFlushTimeout => m_configuration.InCloudBuild() ? TimeSpan.FromMinutes(1) : AriaV2StaticState.DefaultShutdownTimeout;

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
            Contract.RequiresNotNull(initialConfig, "initialConfig can't be null");
            Contract.RequiresNotNull(pathTable, "pathTable can't be null");
            Contract.RequiresNotNull(host, "host can't be null");

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

            ConfigurePluginLogging(pathTable, mutableConfig);

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

            m_crashCollector = OperatingSystemHelper.IsMacOS
                ? new CrashCollectorMacOS(new[] { CrashType.BuildXL, CrashType.Kernel })
                : null;

            m_buildViewModel = new BuildViewModel();
        }

        private static void ConfigureCacheMissLogging(PathTable pathTable, BuildXL.Utilities.Configuration.Mutable.CommandLineConfiguration mutableConfig)
        {
            mutableConfig.Logging.CustomLog.Add(
                mutableConfig.Logging.CacheMissLog,
                (new[]
                {
                    (int)SharedLogEventId.CacheMissAnalysis,
                    (int)SharedLogEventId.CacheMissAnalysisBatchResults,
                    (int)BuildXL.Scheduler.Tracing.LogEventId.MissingKeyWhenSavingFingerprintStore,
                    (int)BuildXL.Scheduler.Tracing.LogEventId.FingerprintStoreSavingFailed,
                    (int)BuildXL.Scheduler.Tracing.LogEventId.FingerprintStoreToCompareTrace,
                    (int)BuildXL.Scheduler.Tracing.LogEventId.SuccessLoadFingerprintStoreToCompare
                },
                null));
        }

        private static void ConfigurePluginLogging(PathTable pathTable, BuildXL.Utilities.Configuration.Mutable.CommandLineConfiguration mutableConfig)
        {
            mutableConfig.Logging.CustomLog.Add(
                mutableConfig.Logging.PluginLog,
                (new[]
                {
                    (int)PluginLogEventId.PluginManagerStarting,
                    (int)PluginLogEventId.PluginManagerLoadingPlugin,
                    (int)PluginLogEventId.PluginManagerLogMessage,
                    (int)PluginLogEventId.PluginManagerLoadingPluginsFinished,
                    (int)PluginLogEventId.PluginManagerSendOperation,
                    (int)PluginLogEventId.PluginManagerResponseReceived,
                    (int)PluginLogEventId.PluginManagerShutDown,
                    (int)PluginLogEventId.PluginManagerForwardedPluginClientMessage,
                    (int)PluginLogEventId.PluginManagerErrorMessage
                },
                null));
        }

        private static void ConfigureDistributionLogging(PathTable pathTable, BuildXL.Utilities.Configuration.Mutable.CommandLineConfiguration mutableConfig)
        {
            if (mutableConfig.Distribution.BuildRole != DistributedBuildRoles.None)
            {
                mutableConfig.Logging.CustomLog.Add(
                    mutableConfig.Logging.RpcLog, (DistributionHelpers.DistributionAllMessages.ToArray(), null));
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

                if (!mutableConfig.Logging.SaveFingerprintStoreToLogs.HasValue)
                {
                    mutableConfig.Logging.SaveFingerprintStoreToLogs = true;
                }

                var logPath = mutableConfig.Logging.Log;

                // NOTE: We rely on explicit exclusion of pip output messages in CloudBuild rather than turning them off by default.
                mutableConfig.Logging.CustomLog.Add(
                    mutableConfig.Logging.PipOutputLog, (new[] { (int)ProcessesLogEventId.PipProcessOutput }, null));

                mutableConfig.Logging.CustomLog.Add(
                    mutableConfig.Logging.DevLog,
                    (new List<int>(FrontEndControllerFactory.DevLogEvents)
                    {
                        // Add useful low volume-messages for dev diagnostics here
                        (int)SharedLogEventId.DominoInvocation,
                        (int)AppLogEventId.StartupTimestamp,
                        (int)AppLogEventId.StartupCurrentDirectory,
                        (int)AppLogEventId.DominoCompletion,
                        (int)AppLogEventId.DominoPerformanceSummary,
                        (int)AppLogEventId.DominoCatastrophicFailure,
                        (int)TracingLogEventId.UnexpectedConditionLocal,
                        (int)TracingLogEventId.UnexpectedConditionTelemetry,
                        (int)SchedulerLogEventId.CriticalPathPipRecord,
                        (int)SchedulerLogEventId.CriticalPathChain,
                        (int)EngineLogEventId.HistoricMetadataCacheLoaded,
                        (int)EngineLogEventId.HistoricMetadataCacheSaved,
                        (int)EngineLogEventId.HistoricPerfDataLoaded,
                        (int)EngineLogEventId.HistoricPerfDataSaved,
                        (int)SharedLogEventId.StartEngineRun,
                        (int)EngineLogEventId.StartCheckingForPipGraphReuse,
                        (int)EngineLogEventId.EndCheckingForPipGraphReuse,
                        (int)EngineLogEventId.GraphNotReusedDueToChangedInput,

                        (int)EngineLogEventId.StartLoadingHistoricPerfData,
                        (int)EngineLogEventId.EndLoadingHistoricPerfData,
                        (int)EngineLogEventId.StartSerializingPipGraph,
                        (int)EngineLogEventId.EndSerializingPipGraph,
                        (int)EngineLogEventId.ScrubbingStarted,
                        (int)EngineLogEventId.ScrubbingFinished,
                        (int)SchedulerLogEventId.StartSchedulingPipsWithFilter,
                        (int)SchedulerLogEventId.EndSchedulingPipsWithFilter,
                        (int)StorageLogEventId.StartScanningJournal,
                        (int)StorageLogEventId.EndScanningJournal,
                        (int)EngineLogEventId.StartExecute,
                        (int)EngineLogEventId.EndExecute,
                        (int)SchedulerLogEventId.PipDetailedStats,
                        (int)SchedulerLogEventId.ProcessesCacheHitStats,
                        (int)SchedulerLogEventId.ProcessesCacheMissStats,
                        (int)SchedulerLogEventId.CacheTransferStats,
                        (int)SchedulerLogEventId.OutputFileStats,
                        (int)SchedulerLogEventId.SourceFileHashingStats,
                        (int)SchedulerLogEventId.OutputFileHashingStats,
                        (int)SchedulerLogEventId.BuildSetCalculatorStats,
                        (int)PipsLogEventId.EndFilterApplyTraversal,
                        (int)SchedulerLogEventId.EndAssigningPriorities,
                        (int)EngineLogEventId.DeserializedFile,
                        (int)SchedulerLogEventId.PipQueueConcurrency,
                        (int)EngineLogEventId.GrpcSettings,
                        (int)EngineLogEventId.ChosenABTesting,
                        (int)EngineLogEventId.SynchronouslyWaitedForCache,
                        (int)Scheduler.Tracing.LogEventId.PipFingerprintData,
                        (int)Scheduler.Tracing.LogEventId.ModuleWorkerMapping,

                        (int)EngineLogEventId.DistributionWorkerChangedState,
                        (int)EngineLogEventId.DistributionConnectedToWorker,
                        (int)EngineLogEventId.DistributionWorkerFinish,
                        (int)Scheduler.Tracing.LogEventId.WorkerReleasedEarly,
                        (int)AppLogEventId.CbTimeoutReached,
                        (int)AppLogEventId.CbTimeoutInfo,
                    },
                    // all errors should be included in a dev log
                    EventLevel.Error));

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
                   buildViewModel: m_buildViewModel,
                   cancellationToken: m_cancellationSource.Token,
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
#if FEATURE_ARIA_TELEMETRY
                bool remoteTelemetryEnabled = m_configuration.Logging.RemoteTelemetry != RemoteTelemetry.Disabled && !Debugger.IsAttached;
#else
                bool remoteTelemetryEnabled = false;
#endif
                Stopwatch stopWatch = null;
                if (remoteTelemetryEnabled)
                {
                    stopWatch = Stopwatch.StartNew();
                    AriaV2StaticState.Enable(
                        AriaTenantToken.Key,
                        m_configuration.Logging.LogsRootDirectory(m_pathTable).ToString(m_pathTable),
                        TelemetryFlushTimeout);
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
                        if (!ProcessNativeMethods.SetupProcessDumps(m_configuration.Logging.LogsDirectory.ToString(m_pathTable), out var coreDumpDirectory))
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

                        ProcessNativeMethods.TeardownProcessDumps();

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

                    if (m_configuration.Logging.TraceLog.IsValid)
                    {
                        appLoggers.ConfigureTraceLogFile(m_configuration.Logging.TraceLog);
                        Tracing.Logger.Log.TracerStartEvent(pm.LoggingContext);
                        Tracing.Logger.Log.TracerSignalEvent(pm.LoggingContext, "Start", timeStamp: DateTime.UtcNow.Ticks);
                    }

                    try
                    {
                        Contract.Assume(m_initialConfiguration == m_configuration, "Expect the initial configuration to still match the updatable configuration object.");

                        newEngineState = RunEngineWithDecorators(pm.LoggingContext, cancellationToken, appLoggers, engineState, collector);

                        Contract.Assert(EngineState.CorrectEngineStateTransition(engineState, newEngineState, out var incorrectMessage), incorrectMessage);

                        if (Events.Log.HasEventWriteFailures)
                        {
                            Logger.Log.EventWriteFailuresOccurred(pm.LoggingContext);
                        }

                        appLoggers.LogEventSummary(pm.LoggingContext);


                        // Log Ado Summary
                        var buildSummary = m_buildViewModel.BuildSummary;
                        if (buildSummary != null)
                        {
                            try
                            {
                                string filePath = buildSummary.RenderMarkdown();
                                WriteToConsole("##vso[task.uploadsummary]" + filePath);
                            }
                            catch (IOException e)
                            {
                                WriteErrorToConsole(Strings.App_Main_FailedToWriteSummary, e.Message);
                                // No need to change exit code, only behavior is lack of log in the extensions page.
                            }
                            catch (UnauthorizedAccessException e)
                            {
                                WriteErrorToConsole(Strings.App_Main_FailedToWriteSummary, e.Message);
                                // No need to change exit code, only behavior is lack of log in the extensions page.
                            }
                        }

                        if (appLoggers.TrackingEventListener.HasFailures)
                        {
                            WriteErrorToConsoleWithDefaultColor(Strings.App_Main_BuildFailed);

                            LogGeneratedFiles(pm.LoggingContext, appLoggers.TrackingEventListener, translator: appLoggers.PathTranslatorForLogging);

                            var classification = ClassifyFailureFromLoggedEvents(pm.LoggingContext, appLoggers.TrackingEventListener);
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

                        if (m_configuration.Logging.TraceLog.IsValid)
                        {
                            Tracing.Logger.Log.TracerSignalEvent(pm.LoggingContext, "Stop", timeStamp: DateTime.UtcNow.Ticks);
                            Tracing.Logger.Log.TracerStopEvent(pm.LoggingContext);
                        }
                    }
                }
            }
            finally
            {
                // Release the build view model so that we can garbage collect any state it maintained.
                m_buildViewModel = null;

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
            PerformanceCollector performanceCollector)
        {
            var fileSystem = new PassThroughFileSystem(m_pathTable);
            var engineContext = EngineContext.CreateNew(cancellationToken, m_pathTable, fileSystem);
            var frontEndControllerFactory = FrontEndControllerFactory.Create(
                    m_configuration.FrontEnd.FrontEndMode(),
                    loggingContext,
                    m_initialConfiguration,
                    performanceCollector);

            return RunEngine(
                loggingContext,
                engineContext,
                m_initialConfiguration,
                performanceCollector,
                frontEndControllerFactory,
                appLoggers.TrackingEventListener,
                engineState);
        }

        internal static (ExitKind ExitKind, string ErrorBucket, string BucketMessage) ClassifyFailureFromLoggedEvents(LoggingContext loggingContext, TrackingEventListener listener)
        {
            // The loss of connectivity to other machines during a distributed build is generally the true cause of the
            // failure even though it may manifest itself as a different failure first (like failure to materialize)
            if (listener.CountsPerEventId((int)EngineLogEventId.DistributionExecutePipFailedNetworkFailure) >= 1)
            {
                return (ExitKind: ExitKind.InfrastructureError, ErrorBucket: EngineLogEventId.DistributionExecutePipFailedNetworkFailure.ToString(), BucketMessage: string.Empty);
            }
            else if (listener.CountsPerEventId((int)SchedulerLogEventId.ProblematicWorkerExit) >= 1 &&
                (listener.InternalErrorDetails.Count > 0 || listener.InfrastructureErrorDetails.Count > 0))
            {
                string errorMessage = listener.InternalErrorDetails.Count > 0 ?
                    listener.InternalErrorDetails.FirstErrorMessage :
                    listener.InfrastructureErrorDetails.FirstErrorMessage;

                string errorName = listener.InternalErrorDetails.Count > 0 ?
                    listener.InternalErrorDetails.FirstErrorName :
                    listener.InfrastructureErrorDetails.FirstErrorName;

                return (ExitKind: ExitKind.InfrastructureError, ErrorBucket: $"{SchedulerLogEventId.ProblematicWorkerExit.ToString()}.{errorName}", BucketMessage: errorMessage);
            }
            // Failure to compute a build manifest hash will manifest as an IPC pip failure
            else if (listener.CountsPerEventId((int)SchedulerLogEventId.ErrorApiServerGetBuildManifestHashFromLocalFileFailed) >= 1
                && listener.InternalErrorDetails.Count > 0
                && listener.InternalErrorDetails.FirstErrorName == SchedulerLogEventId.PipIpcFailed.ToString())
            {
                return (ExitKind: ExitKind.InternalError, ErrorBucket: SchedulerLogEventId.ErrorApiServerGetBuildManifestHashFromLocalFileFailed.ToString(), BucketMessage: string.Empty);
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
                        case (int)BuildXL.Scheduler.Tracing.LogEventId.FileMonitoringError:
                            return ExitKind.BuildFailedWithFileMonErrors;
                        case (int)BuildXL.Processes.Tracing.LogEventId.PipProcessExpectedMissingOutputs:
                            return ExitKind.BuildFailedWithMissingOutputErrors;
                        case (int)BuildXL.Pips.Tracing.LogEventId.InvalidOutputDueToSimpleDoubleWrite:
                            return ExitKind.BuildFailedSpecificationError;
                        case (int)BuildXL.Processes.Tracing.LogEventId.PipProcessError:
                        case (int)SharedLogEventId.DistributionWorkerForwardedError:
                            return ExitKind.BuildFailedWithPipErrors;
                        case (int)AppLogEventId.CancellationRequested:
                            return ExitKind.BuildCancelled;
                        case (int)BuildXL.Pips.Tracing.LogEventId.NoPipsMatchedFilter:
                            return ExitKind.NoPipsMatchFilter;
                        case (int)BuildXL.App.Tracing.LogEventId.CbTimeoutReached:
                            return ExitKind.BuildTimeout;
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

            var sessionId = ComputeSessionId(relatedActivityId);

            LoggingContext topLevelContext = new LoggingContext(
                relatedActivityId,
                Branding.ProductExecutableName,
                new LoggingContext.SessionInfo(sessionId.ToString(), ComputeEnvironment(m_configuration), relatedActivityId));

            using (PerformanceMeasurement pm = PerformanceMeasurement.StartWithoutStatistic(
                topLevelContext,
                (loggingContext) =>
                {
                    m_appLoggingContext = loggingContext;
                    // Note the m_appLoggingContext is cleaned up. In the initial implementation this was a static, but after pr comment it is now an instance.
                    m_loggingContextForCrashHandler = loggingContext;
                    Events.StaticContext = loggingContext;
                    FileUtilitiesStaticLoggingContext.LoggingContext = loggingContext;

                    // As the most of filesystem operations are defined as static, we need to reset counters not to add values between server-mode builds.
                    // We should do so after we have set the proper logging context.
                    FileUtilities.CreateCounters();

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

                    LogDominoInvocation(
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
                    LogDominoCompletion(
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

                        var shutdownResult = AriaV2StaticState.TryShutDown(TelemetryFlushTimeout, out telemetryShutdownException);
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
                result = run(pm);
            }

            return result;
        }

        /// <summary>
        /// Logging DominoCompletion with an extra CloudBuild event
        /// </summary>
        public static void LogDominoCompletion(LoggingContext context, int exitCode, ExitKind exitKind, ExitKind cloudBuildExitKind, string errorBucket, string bucketMessage, int processRunningTime, long utcTicks, bool inCloudBuild)
        {
            Logger.Log.DominoCompletion(context,
                exitCode,
                exitKind.ToString(),
                errorBucket,
                // This isn't a command line but it should still be sanatized for sake of not overflowing in telemetry
                ScrubCommandLine(bucketMessage, 1000, 1000),
                processRunningTime);

            // Sending a different event to CloudBuild ETW listener.
            if (inCloudBuild)
            {
                BuildXL.Tracing.CloudBuildEventSource.Log.DominoCompletedEvent(new  BuildXL.Tracing.CloudBuild.DominoCompletedEvent
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
            Logger.Log.DominoInvocation(context, ScrubCommandLine(commandLine, 100000, 100000), buildInfo, machineInfo, sessionIdentifier, relatedSessionIdentifier, startupDirectory, mainConfigurationFile);
            Logger.Log.DominoInvocationForLocalLog(context, commandLine, buildInfo, machineInfo, sessionIdentifier, relatedSessionIdentifier, startupDirectory, mainConfigurationFile);

            if (inCloudBuild)
            {
                // Sending a different event to CloudBuild ETW listener.
                BuildXL.Tracing.CloudBuildEventSource.Log.DominoInvocationEvent(new BuildXL.Tracing.CloudBuild.DominoInvocationEvent
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
            Contract.RequiresNotNull(rawCommandLine);
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

        /// <summary>
        /// Computes session identifier which allows easier searching in Kusto for
        /// builds based traits: Cloudbuild BuildId (i.e. RelatedActivityId), ExecutionEnvironment, Distributed build role
        ///
        /// Search for orchestrators: '| where sessionId has "0001-FFFF"'
        /// Search for workers: '| where sessionId has "0002-FFFF"'
        /// Search for office metabuild: '| where sessionId has "FFFF-0F"'
        /// </summary>
        private Guid ComputeSessionId(Guid relatedActivityId)
        {
            var bytes = relatedActivityId.ToByteArray();
            var executionEnvironment = m_configuration.Logging.Environment;
            var distributedBuildRole = m_configuration.Distribution.BuildRole;
            var inCloudBuild = m_configuration.InCloudBuild();

            // SessionId:
            // 00-03: 00-03 from random guid
            var randomBytes = Guid.NewGuid().ToByteArray();
            for (int i = 0; i <= 3; i++)
            {
                bytes[i] = randomBytes[i];
            }

            // 04-05: BuildRole
            bytes[4] = 0;
            bytes[5] = (byte)distributedBuildRole;

            // 06-07: InCloudBuild = FFFF, !InCloudBuild = 0000
            var inCloudBuildSpecifier = inCloudBuild ? byte.MaxValue : (byte)0;
            bytes[6] = inCloudBuildSpecifier;
            bytes[7] = inCloudBuildSpecifier;

            // 08-09: executionEnvironment
            bytes[8] = (byte)(((int)executionEnvironment >> 8) & 0xFF);
            bytes[9] = (byte)((int)executionEnvironment & 0xFF);

            // 10-15: 10-15 from relatedActivityId
            // Do nothing byte array is initially seeded from related activity id
            return new Guid(bytes);
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
            Contract.RequiresNotNull(rawArgs, "rawArgs must not be null.");

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
                    global::BuildXL.Pips.ETWLogger.Log,
                    global::BuildXL.Native.ETWLogger.Log,
                    global::BuildXL.Storage.ETWLogger.Log,
                    global::BuildXL.Processes.ETWLogger.Log,
                    global::BuildXL.Plugin.ETWLogger.Log,
               }.Concat(
                FrontEndControllerFactory.GeneratedEventSources
            );

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
            private readonly CancellationToken m_cancellationToken;
            private readonly object m_lock = new object();
            private readonly List<BaseEventListener> m_listeners = new List<BaseEventListener>();
            private readonly Dictionary<AbsolutePath, TextWriterEventListener> m_listenersByPath = new Dictionary<AbsolutePath, TextWriterEventListener>();
            private bool m_disposed;
            private readonly bool m_displayWarningErrorTime;
            private TextWriterEventListener m_defaultFileListener;
            private TextWriterEventListener m_statusFileListener;
            private TextWriterEventListener m_tracerFileListener;

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

            /// <summary>
            /// The path to the log directory
            /// </summary>
            public readonly string RootLogDirectory;

            public AppLoggers(
                DateTime startTime,
                IConsole console,
                ILoggingConfiguration configuration,
                PathTable pathTable,
                bool notWorker,
                BuildViewModel buildViewModel,
                bool displayWarningErrorTime,
                CancellationToken cancellationToken)
            {
                Contract.RequiresNotNull(console);
                Contract.RequiresNotNull(configuration);

                m_console = console;
                m_baseTime = startTime;
                m_configuration = configuration;
                m_pathTable = pathTable;
                m_cancellationToken = cancellationToken;
                m_displayWarningErrorTime = displayWarningErrorTime;

                LogPath = configuration.Log.ToString(pathTable);
                RootLogDirectory = Path.GetDirectoryName(LogPath);

                m_noLogMask = new EventMask(enabledEvents: null, disabledEvents: configuration.NoLog, nonMaskableLevel: EventLevel.Error);
                m_warningManager = CreateWarningManager(configuration);

                PathTranslatorForLogging = GetPathTranslator(configuration, pathTable);

                Events.Log.HasDiagnosticsArgument = configuration.Diagnostic != 0;
                EnsureEventProvidersInitialized();

                // Inialize the console logging early
                if (m_configuration.ConsoleVerbosity != VerbosityLevel.Off)
                {
                    ConfigureConsoleLogging(notWorker, buildViewModel);
                }

                if (notWorker
                    && (m_configuration.OptimizeConsoleOutputForAzureDevOps
                    || m_configuration.OptimizeVsoAnnotationsForAzureDevOps
                    || m_configuration.OptimizeProgressUpdatingForAzureDevOps))
                {
                    ConfigureAzureDevOpsLogging(buildViewModel);
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

            public void ConfigureLogging(LoggingContext loggingContext)
            {
                lock (m_lock)
                {
                    Contract.Assume(!m_disposed);

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
            private void ConfigureConsoleLogging(bool notWorker, BuildViewModel buildViewModel)
            {
                var listener = new ConsoleEventListener(
                    Events.Log,
                    m_console,
                    m_baseTime,
                    m_configuration.UseCustomPipDescriptionOnConsole,
                    m_cancellationToken,
                    m_configuration.LogsDirectory.IsValid ? m_configuration.LogsDirectory.ToString(m_pathTable) : null,
                    notWorker,
                    m_warningManager.GetState,
                    m_configuration.ConsoleVerbosity.ToEventLevel(),
                    m_noLogMask,
                    onDisabledDueToDiskWriteFailure: OnListenerDisabledDueToDiskWriteFailure,
                    maxStatusPips: m_configuration.FancyConsoleMaxStatusPips,
                    optimizeForAzureDevOps: m_configuration.OptimizeConsoleOutputForAzureDevOps || m_configuration.OptimizeVsoAnnotationsForAzureDevOps);

                listener.SetBuildViewModel(buildViewModel);

                AddListener(listener);
            }

            private void ConfigureAzureDevOpsLogging(BuildViewModel buildViewModel)
            {
                var initialFrequency = Scheduler.Scheduler.GetLoggingPeriodInMsForExecution(m_configuration);
                var listener = new AzureDevOpsListener(
                    Events.Log,
                    m_console,
                    m_baseTime,
                    buildViewModel,
                    m_configuration.UseCustomPipDescriptionOnConsole,
                    m_warningManager.GetState,
                    initialFrequency,
                    m_configuration.AdoConsoleMaxIssuesToLog,
                    emitTargetErrorEvent: true
                );

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

            public void ConfigureTraceLogFile(AbsolutePath logFilePath)
            {
                m_tracerFileListener = AddFileBasedListener(
                    logFilePath,
                    (writer) =>
                    {
                        var listener = new TracerEventListener(
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

            private void ConfigureAdditionalFileLoggers(IReadOnlyDictionary<AbsolutePath, (IReadOnlyList<int> eventIds, EventLevel? nonMaskableLevel)> additionalLoggers)
            {
                foreach (var additionalLogger in additionalLoggers)
                {
                    AddFileBasedListener(
                        additionalLogger.Key,
                        (writer) =>
                        {
                            var eventMask = new EventMask(enabledEvents: additionalLogger.Value.eventIds, disabledEvents: null, nonMaskableLevel: additionalLogger.Value.nonMaskableLevel);
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
            var stopwatch = new StopwatchVar();
            PerformanceCollector collector = null;

            using (stopwatch.Start())
            {
                if (m_configuration.Logging.LogMemory)
                {
                    Logger.Log.MemoryLoggingEnabled(loggingContext);
                }

                if (m_configuration.Logging.LogCounters)
                {
                    collector = new PerformanceCollector(
                        TimeSpan.FromMilliseconds(m_configuration.Logging.PerfCollectorFrequencyMs),
                        collectBytesHeld: m_configuration.Logging.LogMemory,
                        errorHandler: (ex) => Logger.Log.PerformanceCollectorInitializationFailed(loggingContext, ex.Message),
                        queryJobObject: BuildXLJobObjectCpu);
                }
            }

            Tracing.Logger.Log.Statistic(
                loggingContext,
                new Statistic()
                {
                    Name = Statistics.PerformanceCollectorInitializationDurationMs,
                    Value = (long)stopwatch.TotalElapsed.TotalMilliseconds,
                });

            return collector;
        }

        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        private static unsafe (ulong? KernelTime, ulong? UserTime, ulong? NumProcesses) BuildXLJobObjectCpu()
        {
            var info = default(JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION);

            if (!Native.Processes.ProcessUtilities.QueryInformationJobObject(
                  IntPtr.Zero,
                  JOBOBJECTINFOCLASS.JobObjectBasicAndIOAccountingInformation,
                  &info,
                  (uint)Marshal.SizeOf(info),
                  out _))
            {
                return (null, null, null);
            }

            return (info.BasicAccountingInformation.TotalKernelTime, info.BasicAccountingInformation.TotalUserTime, info.BasicAccountingInformation.TotalProcesses);
        }

        internal void OnUnexpectedCondition(string condition)
        {
            if (m_appLoggingContext != null)
            {
                BuildXL.Tracing.UnexpectedCondition.Log(m_appLoggingContext, condition);
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

            if (ExceptionUtilities.IsKnownUnobservedException(exception))
            {
                // Avoid crashing on well know innocuous unobserved exceptions
                return;
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
                        WriteToConsole(Strings.App_LogsDirectory, loggers.RootLogDirectory);
                        WriteToConsole("Collecting some information about this crash...");
                        break;
                }

                // Send a catastrophic failure telemetry event. This should be earlier in the handling process to ensure
                // we have the best shot of getting telemetry out before more complicated tasks like taking a process dump happen.
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

                // Mark failure for future recovery.
                // TODO - FailureRecovery relies on the configuration object and path table. It really shouldn't since these mutate over
                // the corse of the build. This currently makes them pretty ineffective since m_PathTable.IsValue will most likely
                // false here due to graph cache reloading.
                if (m_pathTable != null && m_pathTable.IsValid)
                {
                    var recovery = FailureRecoveryFactory.Create(pm.LoggingContext, m_pathTable, m_configuration);
                    Analysis.IgnoreResult(recovery.TryMarkFailure(exception, rootCause));
                }

                loggers.Dispose();

                pm.Dispose();

                if (rootCause == ExceptionRootCause.Unknown)
                {
                    // Sometimes the crash dumps don't actually get attached to the WER report. Stick a full heap dump
                    // next to the log file for good measure.
                    try
                    {
                        string logPrefix = Path.GetFileNameWithoutExtension(loggers.LogPath);
                        string dumpDir = Path.Combine(loggers.RootLogDirectory, logPrefix, "dumps");
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
                        string[] filesToAttach = new[] { loggers.LogPath };

                        WindowsErrorReporting.CreateDump(exception, s_buildInfo, filesToAttach, m_loggingContextForCrashHandler?.Session?.Id);
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
            catch (Exception ex)
            {
                // Oh my, this isn't going very well.
                WriteErrorToConsole("Unhandled exception in exception handler");
                WriteErrorToConsole(ex.DemystifyToString());
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
            LoggingContext loggingContext,
            EngineContext engineContext,
            ICommandLineConfiguration configuration,
            PerformanceCollector performanceCollector,
            IFrontEndControllerFactory factory,
            TrackingEventListener trackingEventListener,
            EngineState engineState)
        {
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
                m_buildViewModel,
                performanceCollector,
                m_startTimeUtc,
                trackingEventListener,
                rememberAllChangedTrackedInputs: true,
                commitId: s_buildInfo?.IsDeveloperBuild == false ? s_buildInfo.CommitId : null,
                buildVersion:  s_buildInfo?.IsDeveloperBuild == false ? s_buildInfo.Build : null);

            if (engine == null)
            {
                return engineState;
            }

            // Ensure BuildXL terminates before CloudBuild Timeout
            SetCbTimeoutCleanExit();

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
            
            using (loggingQueue?.EnterAsyncLoggingScope(asyncLoggingContext))
            {
                result = engine.Run(asyncLoggingContext, engineState);
            }

            Contract.AssertNotNull(result, "Running the engine should return a valid engine result.");

            if (!result.IsSuccess)
            {
                // When async logging is enabled, all async logging needs to complete before the following code that checks for
                // the state of logging contexts or tracking event listeners.
                // The interactions with app loggers (specifically with the TrackingEventListener) presume all
                // logged events have been flushed. If async logging were still active the state may not be correct
                // with respect to the Engine's return value.

                Contract.Assert(
                    (trackingEventListener == null || trackingEventListener.HasFailures) && loggingContext.ErrorWasLogged,
                    I($"The build has failed but the logging infrastructure has not encountered an error: TrackingEventListener has errors: {trackingEventListener == null || trackingEventListener.HasFailures} | LoggingContext has errors: [{string.Join(", ", loggingContext.ErrorsLoggedById.ToArray())}]"));
            }

            // Graph caching complicates some things. we'll have to reload state which invalidates the pathtable and everything that holds
            // a pathtable like configuration.
            m_pathTable = engine.Context.PathTable;
            m_configuration = engine.Configuration;

            var engineRunDuration = (int)(DateTime.UtcNow - m_startTimeUtc).TotalMilliseconds;

            AppPerformanceInfo appPerfInfo = new AppPerformanceInfo
            {
                AppInitializationDurationMs = appInitializationDurationMs,
                EnginePerformanceInfo = result.EnginePerformanceInfo,
                ServerModeUsed = m_appHost is AppServer,
                ServerModeEnabled = m_initialConfiguration.Server == ServerMode.Enabled,
                EngineRunDurationMs = engineRunDuration,
            };

            ReportStatsForBuildSummary(appPerfInfo);

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

                AnalyzeAndLogPerformanceSummary(loggingContext, configuration, appPerfInfo);
            }

            return result.EngineState;
        }

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
                Contract.RequiresNotNull(schedulerInfo);

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
                foreach (var enumValue in Enum.GetValues(typeof(PipExecutionStep)))
                {
                    if (enumValue is PipExecutionStep step)
                    {
                        allStepsDuration += (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(step).TotalMilliseconds;
                    }
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
                var retriedProcessPips = ComputeTimePercentage(
                    (long)schedulerInfo.CanceledProcessExecuteDurationMs,
                    allStepsMinusPipExecution);
                var scheduling = ComputeTimePercentage(
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.ChooseWorkerCpu).TotalMilliseconds +
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.ChooseWorkerCacheLookup).TotalMilliseconds +
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.DelayedCacheLookup).TotalMilliseconds,
                    allStepsMinusPipExecution);
                var handleResult = ComputeTimePercentage(
                    (long)schedulerInfo.PipExecutionStepCounters.GetElapsedTime(PipExecutionStep.HandleResult).TotalMilliseconds,
                    allStepsMinusPipExecution);

                var processOverheadOther = Math.Max(0, 100 - hashingInputs.Item1 - checkingForCacheHit.Item1 - processOutputs.Item1 - replayFromCache.Item1 - prepareSandbox.Item1 - nonProcessPips.Item1 - retriedProcessPips.Item1 - scheduling.Item1 - handleResult.Item1);

                StringBuilder sb = new StringBuilder();
                if (schedulerInfo.DiskStatistics != null)
                {
                    foreach (var item in schedulerInfo.DiskStatistics)
                    {
                        sb.AppendFormat("{0}:{1}% ", item.Drive, item.CalculateActiveTime(lastOnly: false));
                    }
                }

                // The performance summary looks at counters that don't get aggregated and sent back to the orchestrator from
                // all workers. So it only applies to single machine builds.
                if (config.Distribution.BuildWorkers == null || config.Distribution.BuildWorkers.Count == 0)
                {
                    Logger.Log.DominoPerformanceSummary(
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
                        retriedProcessPips: retriedProcessPips.Item1,
                        scheduling: scheduling.Item1,
                        handleResult: handleResult.Item1,
                        averageCpu: schedulerInfo.AverageMachineCPU,
                        minAvailableMemoryMb: (int)schedulerInfo.MachineMinimumAvailablePhysicalMB,
                        diskUsage: sb.ToString(),
                        limitingResourcePercentages: perfInfo.EnginePerformanceInfo.LimitingResourcePercentages ?? new LimitingResourcePercentages());
                }

                if (schedulerInfo.ProcessPipsUncacheable > 0)
                {
                    LogPerfSmell(context, () => Logger.Log.ProcessPipsUncacheable(context, schedulerInfo.ProcessPipsUncacheable));
                }

                // Make sure there were some misses since a complete noop with incremental scheduling shouldn't cause this to trigger
                if (schedulerInfo.CriticalPathTableHits == 0 && schedulerInfo.CriticalPathTableMisses != 0)
                {
                    LogPerfSmell(context, () => Logger.Log.NoCriticalPathTableHits(context));
                }

                // Make sure some source files were hashed since a complete noop build with incremental scheduling shouldn't cause this to trigger
                if (schedulerInfo.FileContentStats.SourceFilesUnchanged == 0 && schedulerInfo.FileContentStats.SourceFilesHashed != 0)
                {
                    LogPerfSmell(context, () => Logger.Log.NoSourceFilesUnchanged(context));
                }

                if (!perfInfo.ServerModeEnabled)
                {
                    LogPerfSmell(context, () => Logger.Log.ServerModeDisabled(context));
                }

                if (!perfInfo.EnginePerformanceInfo.GraphCacheCheckJournalEnabled)
                {
                    LogPerfSmell(context, () => Logger.Log.GraphCacheCheckJournalDisabled(context));
                }

                if (perfInfo.EnginePerformanceInfo.CacheInitializationDurationMs > 5000)
                {
                    LogPerfSmell(context, () => Logger.Log.SlowCacheInitialization(context, perfInfo.EnginePerformanceInfo.CacheInitializationDurationMs));
                }

                if (perfInfo.EnginePerformanceInfo.SchedulerPerformanceInfo.HitLowMemorySmell)
                {
                    LogPerfSmell(context, () => Scheduler.Tracing.Logger.Log.HitLowMemorySmell(context));
                }

                if (config.Sandbox.LogProcesses)
                {
                    LogPerfSmell(context, () => Logger.Log.LogProcessesEnabled(context));
                }

                if (perfInfo.EnginePerformanceInfo.FrontEndIOWeight > 5)
                {
                    LogPerfSmell(context, () => Logger.Log.FrontendIOSlow(context, perfInfo.EnginePerformanceInfo.FrontEndIOWeight));
                }
            }
        }

        private static string ComputeTelemetryTagsPerformanceSummary(SchedulerPerformanceInfo schedulerInfo)
        {
            string telemetryTagPerformanceSummary = string.Empty;
            if (schedulerInfo != null && schedulerInfo.ProcessPipCountersByTelemetryTag != null && schedulerInfo.ExecuteProcessDurationMs > 0)
            {
                var elapsedTimesByTelemetryTag = schedulerInfo.ProcessPipCountersByTelemetryTag.GetElapsedTimes(PipCountersByGroup.ExecuteProcessDuration);

                // TelemetryTag counters get incremented when a pip is cancelled due to ctrl-c or resource exhaustion. Make sure to include the total
                // cancelled time in the denomenator when calculating the time percentage
                var executeAndCancelledExecuteDuration = schedulerInfo.ExecuteProcessDurationMs + schedulerInfo.CanceledProcessExecuteDurationMs;

                int percentagesTotal = 0;
                telemetryTagPerformanceSummary = string.Join(Environment.NewLine, elapsedTimesByTelemetryTag.OrderByDescending(tag => tag.Value.Ticks).Select(
                    elapedTime =>
                    {
                        var computedPercentages = ComputeTimePercentage((long)elapedTime.Value.TotalMilliseconds, executeAndCancelledExecuteDuration);
                        percentagesTotal += computedPercentages.Item1;
                        return string.Format("{0,-12}{1,-39}{2}%", string.Empty, elapedTime.Key, computedPercentages.Item1);
                    }));

                // Humans are picky about percentages adding up neatly to 100%. Make sure other accounts for any rounding slop
                telemetryTagPerformanceSummary += string.Format("{0}{1,-12}{2,-39}{3}%{0}", Environment.NewLine, string.Empty, "Other:", 100 - percentagesTotal);
            }

            return telemetryTagPerformanceSummary;
        }

        private void LogPerfSmell(LoggingContext context, Action action)
        {
            if (m_firstSmell)
            {
                Logger.Log.BuildHasPerfSmells(context);
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

            string time = TimeSpan.FromMilliseconds(numerator).TotalSeconds + "sec";
            string combined = percent + "% (" + time + ")";
            return new Tuple<int, string, string>(percent, time, combined);
        }


        private void ReportStatsForBuildSummary(AppPerformanceInfo appInfo)
        {
            var summary = m_buildViewModel.BuildSummary;
            if (summary == null)
            {
                return;
            }

            // Overall Duration information
            var tree = new PerfTree("Build Duration", appInfo.EngineRunDurationMs)
                       {
                           new PerfTree("Application Initialization", appInfo.AppInitializationDurationMs)
                       };

            var engineInfo = appInfo.EnginePerformanceInfo;

            if (engineInfo != null)
            {
                tree.Add(new PerfTree("Graph Construction", engineInfo.GraphCacheCheckDurationMs + engineInfo.GraphReloadDurationMs + engineInfo.GraphConstructionDurationMs)
                           {
                               new PerfTree("Checking for pip graph reuse", engineInfo.GraphCacheCheckDurationMs),
                               new PerfTree("Reloading pip graph", engineInfo.GraphReloadDurationMs),
                               new PerfTree("Create graph", engineInfo.GraphConstructionDurationMs)
                           });
                tree.Add(new PerfTree("Scrubbing", engineInfo.ScrubbingDurationMs));
                tree.Add(new PerfTree("Scheduler Initialization", engineInfo.SchedulerInitDurationMs));
                tree.Add(new PerfTree("Execution Phase", engineInfo.ExecutePhaseDurationMs));

                // Cache stats
                var schedulerInfo = engineInfo.SchedulerPerformanceInfo;
                if (schedulerInfo != null)
                {
                    summary.CacheSummary.ProcessPipCacheHit = schedulerInfo.ProcessPipCacheHits;
                    summary.CacheSummary.TotalProcessPips = schedulerInfo.TotalProcessPips;
                }
            }

            summary.DurationTree = tree;
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

            if (loggingConfiguration.OptimizeConsoleOutputForAzureDevOps
                || loggingConfiguration.OptimizeProgressUpdatingForAzureDevOps
                || loggingConfiguration.OptimizeVsoAnnotationsForAzureDevOps)
            {
                // Use a very simple logger for azure devops
                return new StandardConsole(colorize: false, animateTaskbar: false, supportsOverwriting: false, pathTranslator: translator);
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

        private void SetCbTimeoutCleanExit()
        {
            if (EngineEnvironmentSettings.CbUtcTimeoutTicks.Value != null)
            {
                TimeSpan timeRemaining = new TimeSpan(EngineEnvironmentSettings.CbUtcTimeoutTicks.Value.Value).Subtract(new TimeSpan(DateTime.UtcNow.Ticks));
                int calculatedEarlyTimeout = (int)Math.Ceiling(timeRemaining.TotalMinutes * EarlyCbTimeoutPercentage / 100.0);
                int earlyTimeoutMins = Math.Clamp(calculatedEarlyTimeout, MinEarlyCbTimeoutMins, MaxEarlyCbTimeoutMins);
                long cbTimeoutTicks = EngineEnvironmentSettings.CbUtcTimeoutTicks.Value.Value - DateTime.UtcNow.AddMinutes(earlyTimeoutMins).Ticks;
                try
                {
                    int msUntilTimeout = Convert.ToInt32(cbTimeoutTicks / TimeSpan.TicksPerMillisecond);
                    Logger.Log.CbTimeoutInfo(m_appLoggingContext, earlyTimeoutMins, msUntilTimeout / (1000 * 60));

                    CbTimeoutCleanExitAsync(earlyTimeoutMins, msUntilTimeout).Forget();
                }
                catch (OverflowException)
                {
                    // Log warning and ignore invalid timeout info
                    Logger.Log.CbTimeoutInvalid(
                        m_appLoggingContext,
                        DateTime.UtcNow.Ticks.ToString(),
                        EngineEnvironmentSettings.CbUtcTimeoutTicks.Value.Value.ToString());
                    return;
                }
            }
        }

        private async Task CbTimeoutCleanExitAsync(int earlyCbTimeoutMins, int msUntilTimeout)
        {
            if (EngineEnvironmentSettings.CbUtcTimeoutTicks.Value.Value <= DateTime.UtcNow.Ticks)
            {
                // Timeout time specified by CB has already passed
                Logger.Log.CbTimeoutTooLow(m_appLoggingContext, earlyCbTimeoutMins);
                m_cancellationSource.Cancel();
                return;
            }

            await Task.Delay(msUntilTimeout);
            Logger.Log.CbTimeoutReached(
                m_appLoggingContext,
                earlyCbTimeoutMins,
                Convert.ToInt32(TimeSpan.FromMilliseconds(msUntilTimeout).TotalMinutes));
            m_cancellationSource.Cancel();
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
