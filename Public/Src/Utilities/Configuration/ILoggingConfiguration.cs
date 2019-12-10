// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using JetBrains.Annotations;

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
        /// <see cref="FingerprintStoreMode"/>.
        /// </summary>
        FingerprintStoreMode FingerprintStoreMode { get; }

        /// <summary>
        /// The maximum entry age in minutes of an entry in the fingerprint store. Any entry older than this will
        /// be removed at the end of the build.
        /// </summary>
        int FingerprintStoreMaxEntryAgeMinutes { get; }

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
        [CanBeNull]
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
        /// Collects various performance counters and logs phase specific aggregations.
        /// </summary>
        bool LogCounters { get; }

        /// <summary>
        /// Queries the GC for memory usage when collecting counters. This has a negative performance implication
        /// </summary>
        bool LogMemory { get; }

        /// <summary>
        /// Logs key/value statistics to a file specified by path. If a file path is not specified, one will be chosen based on the location of the main log file
        /// </summary>
        bool LogStats { get; }

        /// <summary>
        /// Indicates whether async logging is enabled which queues log messages and processes them on a dedicated thread.
        /// </summary>
        bool? EnableAsyncLogging { get; }

        /// <summary>
        /// Logs key/value statistics to a file specified by path. If a file path is not specified, one will be chosen based on the location of the main log file
        /// </summary>
        AbsolutePath StatsLog { get; }

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
        /// Logs the usage of resources and queues.
        /// </summary>
        bool LogStatus { get; }

        /// <summary>
        /// Logs the usage of resources and queues to a file specified by path. If a file path is not specified, one will be chosen based on the location of the main log file.
        /// </summary>
        AbsolutePath StatusLog { get; }

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
        /// How frequently BuildXL needs to sample the usage of resources and queues.
        /// </summary>
        int StatusFrequencyMs { get; }

        /// <summary>
        /// Whether the Build is set not to fail pips on FileAccessError, but to continue the build.
        /// </summary>
        bool FailPipOnFileAccessError { get; }

        /// <summary>
        /// Whether warnings should be replayed from the cache
        /// </summary>
        bool ReplayWarnings { get; }

        /// <summary>
        /// Whether the pip descriptions should be shortened to (SemiStableHash, CustomerSuppliedPipDescription)
        /// </summary>
        bool UseCustomPipDescriptionOnConsole { get; }

        /// <summary>
        /// On-the-fly cache miss analysis option
        /// </summary>
        CacheMissAnalysisOption CacheMissAnalysisOption { get; }

        /// <summary>
        /// Diff format for cache miss analysis.
        /// </summary>
        CacheMissDiffFormat CacheMissDiffFormat { get; }

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
    }
}
