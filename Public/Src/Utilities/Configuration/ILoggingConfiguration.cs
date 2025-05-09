// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Logging related Configuration settings
    /// </summary>
    public interface ILoggingConfiguration : IWarningHandling
    {
        /// <summary>
        /// The Logs Directory
        /// </summary>
        AbsolutePath LogsDirectory { get; }

        /// <summary>
        /// The Logs root directory, returning the root logs folder with respect to how many logs to retain <see cref="LogsToRetain"/>
        /// </summary>
        AbsolutePath LogsRootDirectory(PathTable table);

        /// <summary>
        /// A redirected logs directory that provides a stable path for <see cref="LogsDirectory"/>.
        /// </summary>
        /// <remarks>
        /// This redirected log directory can be a directory symlink or a junction.
        /// </remarks>
        AbsolutePath RedirectedLogsDirectory { get; }

        /// <summary>
        /// The prefix for all log files (default: BuildXL)
        /// </summary>
        string LogPrefix { get; }

        /// <summary>
        /// Specifies the path to the log file. If this parameter is not specified, then the default behavior is to choose a log path in the main output directory.
        /// </summary>
        AbsolutePath Log { get; }

        /// <summary>
        /// Specifies the path to the optional error log file.
        /// </summary>
        AbsolutePath ErrorLog { get; }

        /// <summary>
        /// Specifies the path to the optional warning log file.
        /// </summary>
        AbsolutePath WarningLog { get; }

        /// <summary>
        /// Specifies whether execution log is emitted.
        /// </summary>
        bool LogExecution { get; }

        /// <summary>
        /// Specifies whether packed execution log is emitted.
        /// </summary>
        /// <remarks>
        /// No effect if LogExecution is not set.
        /// </remarks>
        bool LogPackedExecution { get; }

        /// <summary>
        /// Specifies the path to the execution log file. If a file path is not specified, one will be chosen based on the location of the main log file
        /// </summary>
        AbsolutePath ExecutionLog { get; }

        /// <summary>
        /// Specifies the path to the engine cache log directory.
        /// This contains hardlinks or copies of files and directories from <see cref="EngineCacheLogDirectory"/>.
        /// </summary>
        AbsolutePath EngineCacheLogDirectory { get; }

        /// <summary>
        /// Specifies the path to the engine cache's corrupt files log directory.
        /// This contains any EngineCache files that were deemed to be corrupt or incorrect during the build.
        /// </summary>
        AbsolutePath EngineCacheCorruptFilesLogDirectory { get; }

        /// <summary>
        /// Specifies whether fingerprint computation inputs are stored on disk in the fingerprint store.
        /// The fingerprint store is stored under the <see cref="ILayoutConfiguration.EngineCacheDirectory"/>
        /// by default but can be customized with <see cref="ILayoutConfiguration.FingerprintStoreDirectory"/>.
        /// </summary>
        /// <remarks>
        /// If this is unset, the value is decided during the build based off the build environment.
        /// Passing an explicit command line flag takes precedent over the build's default values.
        /// </remarks>
        bool? StoreFingerprints { get; }

        /// <summary>
        /// <see cref="FingerprintStoreMode"/>
        /// </summary>
        FingerprintStoreMode FingerprintStoreMode { get; }

        /// <summary>
        /// Whether to save fingerprint stores to Logs
        /// </summary>
        bool? SaveFingerprintStoreToLogs { get;}

        /// <summary>
        /// The maximum entry age in minutes of an entry in the fingerprint store. Any entry older than this will
        /// be removed at the end of the build.
        /// </summary>
        int FingerprintStoreMaxEntryAgeMinutes { get; }

        /// <summary>
        /// Bulk load fingerprint store to (hopefully) speed up writes.
        /// </summary>
        /// <remarks>
        /// This option is temporary for AB testing.
        /// </remarks>
        bool FingerprintStoreBulkLoad { get; }

        /// <summary>
        /// Specifies the path to the fingerprints log directory which contains FingerprintStores.
        /// </summary>
        AbsolutePath FingerprintsLogDirectory { get; }

        /// <summary>
        /// Specifies the path to the fingerprint store log directory. The log directory represents a snapshot of the persistent fingerprint store at the
        /// end of a particular build.
        /// </summary>
        AbsolutePath ExecutionFingerprintStoreLogDirectory { get; }

        /// <summary>
        /// Specifies the path to the cache lookup fingerprint store log directory. This is a per-build store and does not persist build-over-build.
        /// </summary>
        AbsolutePath CacheLookupFingerprintStoreLogDirectory { get; }

        /// <summary>
        /// Specifies the path to the historic metadata cache log directory.
        /// </summary>
        AbsolutePath HistoricMetadataCacheLogDirectory { get; }

        /// <summary>
        /// Creates a custom log file for a specific set of event IDs. Event list should be comma separated integers excluding the DX prefix.
        /// EventLevel specifies the non-skippable events. All events of this of higher event level will be included in the log
        /// regardless whether they are specified in the event list. Defaults to null (i.e., log will include only the specified events)
        /// </summary>
        [NotNull]
        IReadOnlyDictionary<AbsolutePath, (IReadOnlyList<int>, EventLevel?)> CustomLog { get; }

        /// <summary>
        /// Specifies the ETW log kind for custom logs by path. Normal text log kind is 'default'.
        /// </summary>
        [MaybeNull]
        IReadOnlyDictionary<AbsolutePath, string> CustomLogEtwKinds { get; }

        /// <summary>
        /// Removes a set of event IDs from the standard log. Does not apply to warning, error, critical, and always level events.
        /// </summary>
        [NotNull]
        IReadOnlyList<int> NoLog { get; }

        /// <summary>
        /// Removes a set of event IDs from the execution log.
        /// </summary>
        [NotNull]
        IReadOnlyList<int> NoExecutionLog { get; }

        /// <summary>
        /// Specifies a list of verbose events to forward from workers to the orchestrator (apart from error and warning events, which are always forwarded).
        /// </summary>
        [NotNull]
        IReadOnlyList<int> ForwardableWorkerEvents { get; }

        /// <summary>
        /// Enables diagnostic logging for a functional area. This option may be specified multiple times. Areas: Scheduler, Parser, Storage, Engine, Viewer, PipExecutor,
        /// PipInputAssertions, ChangeJournalService, HostApplication, CommonInfrastructure,CacheInteraction, HybridInterop. (short form: /diag)
        /// </summary>
        DiagnosticLevels Diagnostic { get; }

        /// <summary>
        /// Sets the console logging verbosity. Allowed values are 'Off', 'Error', 'Warning', 'Informational' and 'Verbose', and the single-character prefixes of those values. Defaults to
        /// Informational. (short form: /cv)
        /// </summary>
        VerbosityLevel ConsoleVerbosity { get; }

        /// <summary>
        /// Sets the file logging verbosity. Allowed values are 'Off', 'Error', 'Warning', 'Informational' and 'Verbose', and the single-character prefixes of those values. Defaults to
        /// Verbose. (short form: /fv)
        /// </summary>
        VerbosityLevel FileVerbosity { get; }

        /// <summary>
        /// Queries the GC for memory usage when collecting counters. This has a negative performance implication
        /// </summary>
        bool LogMemory { get; }

        /// <summary>
        /// Indicates whether async logging is enabled which queues log messages and processes them on a dedicated thread.
        /// </summary>
        bool? EnableAsyncLogging { get; }

        /// <summary>
        /// Logs key/value statistics to a file specified by path. If a file path is not specified, one will be chosen based on the location of the main log file
        /// </summary>
        AbsolutePath StatsLog { get; }

        /// <summary>
        /// Logs performance statistics to a json file specified by path. If a file path is not specified, one will be chosen based on the location of the main log file
        /// </summary>
        AbsolutePath StatsPrfLog { get; }

        /// <summary>
        /// The file where to store events summary.
        /// </summary>
        AbsolutePath EventSummaryLog { get; }

        /// <summary>
        /// Environment build is running in. Allowed values 'Unset,SelfHostLKG,SelfHostPrivateBuild,OsgLab,OsgDevMachine,NightlyPerformanceRun'
        /// </summary>
        ExecutionEnvironment Environment { get; }

        /// <summary>
        /// When enabled, sends telemetry information for remote collection. Defaults to false.
        /// </summary>
        RemoteTelemetry? RemoteTelemetry { get; }

        /// <summary>
        /// When <see cref="RemoteTelemetry"/> is enabled, this value can be used to override telemetry flush timeout.
        /// </summary>
        TimeSpan? RemoteTelemetryFlushTimeout { get; }

        /// <summary>
        /// Attaches tracing information to the build. May be specified multiple times. Ex: /TraceInfo:Branch=MyBranch
        /// </summary>
        [NotNull]
        IReadOnlyDictionary<string, string> TraceInfo { get; }

        /// <summary>
        /// Use colors for warnings and errors. Defaults to using colors.
        /// </summary>
        bool Color { get; }

        /// <summary>
        /// Animates BuildXL's taskbar icon to indicate progress. Default is to animate.
        /// </summary>
        bool AnimateTaskbar { get; }

        /// <summary>
        /// An external related ETW activity identifier. The top level BuildXL activity will be logged as a child of this one.
        /// </summary>
        string RelatedActivityId { get; }

        /// <summary>
        /// The number of previous logs to retain.
        /// </summary>
        int LogsToRetain { get; }

        /// <summary>
        /// Uses a console that updates status lines rather than only appending new lines
        /// </summary>
        bool FancyConsole { get; }

        /// <summary>
        /// Maximum number of concurrently executing pips to render in Fancy Console view
        /// </summary>
        int FancyConsoleMaxStatusPips { get; }

        /// <summary>
        /// Original root that is being substituted to <see cref="SubstTarget"/>. This configuration is currently only
        /// used for the sake of logging. It is expected that the path has been mapped to <see cref="SubstTarget"/>
        /// before this process starts.
        /// </summary>
        AbsolutePath SubstSource { get; }

        /// <summary>
        /// Root that <see cref="SubstSource"/> has been substituted to.
        /// </summary>
        AbsolutePath SubstTarget { get; }

        /// <summary>
        /// Disables path translation in file and console logging. This is helpful when debugging file access issues
        /// when accesses go through remapped paths. Non-logging related translations should still happen even when this is
        /// set to true.
        /// </summary>
        bool DisableLoggedPathTranslation { get; }

        /// <summary>
        /// Logs the usage of resources and queues to a file specified by path. If a file path is not specified, one will be chosen based on the location of the main log file.
        /// </summary>
        AbsolutePath StatusLog { get; }

        /// <summary>
        /// Trace log
        /// </summary>
        AbsolutePath TraceLog { get; }

        /// <summary>
        /// Cache miss messages
        /// </summary>
        AbsolutePath CacheMissLog { get; }

        /// <summary>
        /// Custom dev log
        /// </summary>
        AbsolutePath DevLog { get; }

        /// <summary>
        /// Log file with the Rpc messages
        /// </summary>
        AbsolutePath RpcLog { get; }

        /// <summary>
        /// Log file with the pip outputs
        /// </summary>
        AbsolutePath PipOutputLog { get; }

        /// <summary>
        /// Log file with plugin messages
        /// </summary>
        AbsolutePath PluginLog { get; }

        /// <summary>
        /// How frequently BuildXL needs to call UpdateStatus
        /// </summary>
        int StatusFrequencyMs { get; }

        /// <summary>
        /// How frequently BuildXL needs to sample the usage of resources and queues.
        /// </summary>
        int PerfCollectorFrequencyMs { get; }

        /// <summary>
        /// Whether the Build is set not to fail pips on FileAccessError, but to continue the build.
        /// </summary>
        bool FailPipOnFileAccessError { get; }

        /// <summary>
        /// Whether warnings should be replayed from the cache
        /// </summary>
        bool? ReplayWarnings { get; }

        /// <summary>
        /// Whether the pip descriptions should be shortened to (SemiStableHash, CustomerSuppliedPipDescription)
        /// </summary>
        bool UseCustomPipDescriptionOnConsole { get; }

        /// <summary>
        /// Specifies the maximum number of issues(errors or warnings) to be displayed on the ADO console.
        /// </summary>
       public int AdoConsoleMaxIssuesToLog { get; }

        /// <summary>
        /// On-the-fly cache miss analysis option
        /// </summary>
        CacheMissAnalysisOption CacheMissAnalysisOption { get; }

        /// <summary>
        /// Diff format for cache miss analysis.
        /// </summary>
        CacheMissDiffFormat CacheMissDiffFormat { get; }

        /// <summary>
        /// Whether cache miss analysis results should be batched when reporting to telemetry
        /// </summary>
        bool CacheMissBatch { get; }

        /// <summary>
        /// Whether console output should be optimized for Azure DevOps output.
        /// </summary>
        bool OptimizeConsoleOutputForAzureDevOps { get; }

        /// <summary>
        /// The expanded command line arguments for the invocation for logging
        /// </summary>
        IReadOnlyList<string> InvocationExpandedCommandLineArguments { get; }

        /// <summary>
        /// Whether progress updating should be optimized for Azure DevOps output.
        /// </summary>
        bool OptimizeProgressUpdatingForAzureDevOps { get; }

        /// <summary>
        /// Whether Vso annotations should be optimized for Azure DevOps output.
        /// </summary>
        bool OptimizeVsoAnnotationsForAzureDevOps { get; }

        /// <summary>
        /// Specifies the internal max message size to be allowed for each individual messages sent to Aria.
        /// Current default set at 0.8Mb to have enough space for other fields specified in the same message.
        /// </summary>
        /// <remarks>
        /// According to https://www.aria.ms/developers/deep-dives/input-constraints/, 
        /// The maximum length of an event can be upto 2.5Mb.
        /// However, it was found that the maximum length of a column in an event is 1MB.
        /// There is no documentation found about this limit.
        /// </remarks>
        public int AriaIndividualMessageSizeLimitBytes { get; }

        /// <summary>
        /// Specifies the maximum number of PerProcessPipPerformanceInformation batched messages to be sent to Aria
         /// </summary>
        public int MaxNumPipTelemetryBatches { get; }

        /// <summary>
        /// When set to true, the dump pip lite runtime analyzer will be enabled to dump information about failing pips.
        /// </summary>
        /// <remarks>
        /// This option is enabled by default.
        /// </remarks>
        public bool? DumpFailedPips { get; }

        /// <summary>
        /// When the DumpFailedPips flag is set, this flag will limit the amount of dumps generated by the runtime dump pip lite analyzer.
        /// </summary>
        public int? DumpFailedPipsLogLimit { get; }

        /// <summary>
        /// When enabled, dump pip lite will dump dynamic file/process observations (must have logfileaccesses and/or logprocesses set)
        /// </summary>
        public bool? DumpFailedPipsWithDynamicData { get; set; }

        /// <summary>
        /// Whether all cached pip outputs should be logged.
        /// </summary>
        public bool LogCachedPipOutputs { get; }

        /// <summary>
        /// The blob URI to send log events to.
        /// </summary>
        /// <remarks>
        /// Expected to be in the format of https://{storage-account-name}.blob.core.windows.net/{container-name}
        /// Default value is <a href="https://adomessages.blob.core.windows.net/adomessages"/>
        /// </remarks>
        public string LogToKustoBlobUri { get; }

        /// <summary>
        /// The identity ID to use when sending log events to Kusto.
        /// </summary>
        /// <remarks>
        /// Default value points to the Bxl-owned identity 6e0959cf-a9ba-4988-bbf1-7facd9deda51
        /// </remarks>
        public string LogToKustoIdentityId { get; }

        /// <summary>
        /// Collection of log event ids that should be sent to the console
        /// </summary>
        public IReadOnlyList<int> LogEventsToConsole { get; }

        /// <summary>
        /// Whether to print event time in .err and .wrn logs.
        /// </summary>
        /// <remarks>
        /// False by default
        /// </remarks>
        public bool DisplayWarningErrorTime { get; }

        /// <summary>
        /// Whether to enable CloudBuild specific ETW logging events.
        /// </summary>
        /// <remarks>
        /// False by default
        /// </remarks>
        public bool EnableCloudBuildEtwLoggingIntegration { get; }
    }
}
