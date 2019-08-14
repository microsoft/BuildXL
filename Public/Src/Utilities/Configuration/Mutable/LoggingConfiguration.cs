// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class LoggingConfiguration : WarningHandling, ILoggingConfiguration
    {
        /// <nodoc />
        public LoggingConfiguration()
        {
            CustomLog = new Dictionary<AbsolutePath, (IReadOnlyList<int>, EventLevel?)>();
            CustomLogEtwKinds = new Dictionary<AbsolutePath, string>();
            NoLog = new List<int>();
            NoExecutionLog = new List<int>();
            ConsoleVerbosity = VerbosityLevel.Informational;
            FileVerbosity = VerbosityLevel.Verbose;
            LogCounters = true;
            TraceInfo = new Dictionary<string, string>();
            Color = true;
            AnimateTaskbar = true;
            LogStats = true;
            LogExecution = true;
            FingerprintStoreMode = FingerprintStoreMode.Default;
            FingerprintStoreMaxEntryAgeMinutes = 4320; // 3 days
            EngineCacheLogDirectory = AbsolutePath.Invalid;
            EngineCacheCorruptFilesLogDirectory = AbsolutePath.Invalid;
            FingerprintsLogDirectory = AbsolutePath.Invalid;
            ExecutionFingerprintStoreLogDirectory = AbsolutePath.Invalid;
            CacheLookupFingerprintStoreLogDirectory = AbsolutePath.Invalid;
            HistoricMetadataCacheLogDirectory = AbsolutePath.Invalid;
            ReplayWarnings = true;
            SubstSource = AbsolutePath.Invalid;
            SubstTarget = AbsolutePath.Invalid;
            FancyConsole = true;
            FancyConsoleMaxStatusPips = 5;
            LogStatus = true;
            FailPipOnFileAccessError = true;
            UseCustomPipDescriptionOnConsole = true;
            CacheMissAnalysisOption = CacheMissAnalysisOption.Disabled();
            RedirectedLogsDirectory = AbsolutePath.Invalid;
        }

        /// <nodoc />
        public LoggingConfiguration(ILoggingConfiguration template, PathRemapper pathRemapper)
            : base(template)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            LogsDirectory = pathRemapper.Remap(template.LogsDirectory);
            RedirectedLogsDirectory = pathRemapper.Remap(template.RedirectedLogsDirectory);
            LogPrefix = template.LogPrefix;
            Log = pathRemapper.Remap(template.Log);
            ErrorLog = pathRemapper.Remap(template.ErrorLog);
            WarningLog = pathRemapper.Remap(template.WarningLog);
            LogExecution = template.LogExecution;
            ExecutionLog = pathRemapper.Remap(template.ExecutionLog);
            StoreFingerprints = template.StoreFingerprints;
            FingerprintStoreMode = template.FingerprintStoreMode;
            FingerprintStoreMaxEntryAgeMinutes = template.FingerprintStoreMaxEntryAgeMinutes;
            FingerprintsLogDirectory = pathRemapper.Remap(template.FingerprintsLogDirectory);
            ExecutionFingerprintStoreLogDirectory = pathRemapper.Remap(template.ExecutionFingerprintStoreLogDirectory);
            CacheLookupFingerprintStoreLogDirectory = pathRemapper.Remap(template.CacheLookupFingerprintStoreLogDirectory);
            HistoricMetadataCacheLogDirectory = pathRemapper.Remap(template.HistoricMetadataCacheLogDirectory);
            EngineCacheLogDirectory = pathRemapper.Remap(template.EngineCacheLogDirectory);
            EngineCacheCorruptFilesLogDirectory = pathRemapper.Remap(template.EngineCacheCorruptFilesLogDirectory);
            CustomLog = new Dictionary<AbsolutePath, (IReadOnlyList<int>, EventLevel?)>();
            foreach (var kv in template.CustomLog)
            {
                CustomLog.Add(pathRemapper.Remap(kv.Key), kv.Value);
            }

            CustomLogEtwKinds = new Dictionary<AbsolutePath, string>();
            foreach (var kv in template.CustomLogEtwKinds)
            {
                CustomLogEtwKinds.Add(pathRemapper.Remap(kv.Key), kv.Value);
            }

            NoLog = new List<int>(template.NoLog);
            NoExecutionLog = new List<int>(template.NoExecutionLog);
            Diagnostic = template.Diagnostic;
            ConsoleVerbosity = template.ConsoleVerbosity;
            FileVerbosity = template.FileVerbosity;
            LogCounters = template.LogCounters;
            LogStats = template.LogStats;
            EnableAsyncLogging = template.EnableAsyncLogging;
            StatsLog = pathRemapper.Remap(template.StatsLog);
            EventSummaryLog = pathRemapper.Remap(template.EventSummaryLog);
            Environment = template.Environment;
            RemoteTelemetry = template.RemoteTelemetry;
            TraceInfo = new Dictionary<string, string>();
            foreach (var kv in template.TraceInfo)
            {
                TraceInfo.Add(kv.Key, kv.Value);
            }

            Color = template.Color;
            AnimateTaskbar = template.AnimateTaskbar;
            RelatedActivityId = template.RelatedActivityId;
            LogsToRetain = template.LogsToRetain;
            FancyConsole = template.FancyConsole;
            FancyConsoleMaxStatusPips = template.FancyConsoleMaxStatusPips;
            SubstSource = pathRemapper.Remap(template.SubstSource);
            SubstTarget = pathRemapper.Remap(template.SubstTarget);
            DisableLoggedPathTranslation = template.DisableLoggedPathTranslation;
            LogStatus = template.LogStatus;
            StatusFrequencyMs = template.StatusFrequencyMs;
            StatusLog = pathRemapper.Remap(template.StatusLog);
            CacheMissLog = pathRemapper.Remap(template.CacheMissLog);
            DevLog = pathRemapper.Remap(template.DevLog);
            RpcLog = pathRemapper.Remap(template.RpcLog);
            PipOutputLog = pathRemapper.Remap(template.PipOutputLog);
            FailPipOnFileAccessError = template.FailPipOnFileAccessError;
            LogMemory = template.LogMemory;
            ReplayWarnings = template.ReplayWarnings;
            UseCustomPipDescriptionOnConsole = template.UseCustomPipDescriptionOnConsole;
            CacheMissAnalysisOption = new CacheMissAnalysisOption(
                template.CacheMissAnalysisOption.Mode,
                new List<string>(template.CacheMissAnalysisOption.Keys),
                pathRemapper.Remap(template.CacheMissAnalysisOption.CustomPath));
            OptimizeConsoleOutputForAzureDevOps = template.OptimizeConsoleOutputForAzureDevOps;
        }

        /// <inheritdoc />
        public AbsolutePath LogsDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath RedirectedLogsDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath LogsRootDirectory(PathTable table)
        {
            return LogsToRetain > 0 ? LogsDirectory.GetParent(table) : LogsDirectory;
        }

        /// <inheritdoc />
        public string LogPrefix { get; set; }

        /// <inheritdoc />
        public AbsolutePath Log { get; set; }

        /// <inheritdoc />
        public AbsolutePath ErrorLog { get; set; }

        /// <inheritdoc />
        public AbsolutePath WarningLog { get; set; }

        /// <inheritdoc />
        public bool LogExecution { get; set; }

        /// <inheritdoc />
        public AbsolutePath ExecutionLog { get; set; }

        /// <inheritdoc />
        public AbsolutePath EngineCacheLogDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath EngineCacheCorruptFilesLogDirectory { get; set; }

        /// <inheritdoc />
        public bool? StoreFingerprints { get; set; }

        /// <inheritdoc />
        public FingerprintStoreMode FingerprintStoreMode { get; set; }

        /// <inheritdoc />
        public int FingerprintStoreMaxEntryAgeMinutes { get; set; }

        /// <inheritdoc />
        public AbsolutePath FingerprintsLogDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath ExecutionFingerprintStoreLogDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath CacheLookupFingerprintStoreLogDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath HistoricMetadataCacheLogDirectory { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<AbsolutePath, (IReadOnlyList<int>, EventLevel?)> CustomLog { get; set; }

        /// <inheritdoc />
        IReadOnlyDictionary<AbsolutePath, (IReadOnlyList<int>, EventLevel?)> ILoggingConfiguration.CustomLog => CustomLog;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<AbsolutePath, string> CustomLogEtwKinds { get; set; }

        /// <inheritdoc />
        IReadOnlyDictionary<AbsolutePath, string> ILoggingConfiguration.CustomLogEtwKinds => CustomLogEtwKinds;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<int> NoLog { get; set; }

        /// <inheritdoc />
        IReadOnlyList<int> ILoggingConfiguration.NoLog => NoLog;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<int> NoExecutionLog { get; set; }

        /// <inheritdoc />
        IReadOnlyList<int> ILoggingConfiguration.NoExecutionLog => NoExecutionLog;

        /// <inheritdoc />
        public DiagnosticLevels Diagnostic { get; set; }

        /// <inheritdoc />
        public VerbosityLevel ConsoleVerbosity { get; set; }

        /// <inheritdoc />
        public VerbosityLevel FileVerbosity { get; set; }

        /// <inheritdoc />
        public bool LogCounters { get; set; }

        /// <inheritdoc />
        public bool LogMemory { get; set; }

        /// <inheritdoc />
        public bool LogStats { get; set; }

        /// <inheritdoc />
        public bool? EnableAsyncLogging { get; set; }

        /// <inheritdoc />
        public AbsolutePath StatsLog { get; set; }

        /// <inheritdoc />
        public AbsolutePath EventSummaryLog { get; set; }

        /// <inheritdoc />
        public ExecutionEnvironment Environment { get; set; }

        /// <inheritdoc />
        public RemoteTelemetry? RemoteTelemetry { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<string, string> TraceInfo { get; set; }

        IReadOnlyDictionary<string, string> ILoggingConfiguration.TraceInfo => TraceInfo;

        /// <inheritdoc />
        public bool Color { get; set; }

        /// <inheritdoc />
        public bool AnimateTaskbar { get; set; }

        /// <inheritdoc />
        public string RelatedActivityId { get; set; }

        /// <inheritdoc />
        public int LogsToRetain { get; set; }

        /// <inheritdoc />
        public bool FancyConsole { get; set; }

        /// <inheritdoc />
        public int FancyConsoleMaxStatusPips { get; set; }

        /// <inheritdoc />
        public AbsolutePath SubstSource { get; set; }

        /// <inheritdoc />
        public AbsolutePath SubstTarget { get; set; }

        /// <inheritdoc />
        public bool LogStatus { get; set; }

        /// <inheritdoc />
        public AbsolutePath StatusLog { get; set; }

        /// <inheritdoc />
        public AbsolutePath CacheMissLog { get; set; }

        /// <inheritdoc />
        public AbsolutePath DevLog { get; set; }

        /// <inheritdoc />
        public AbsolutePath RpcLog { get; set; }

        /// <inheritdoc />
        public AbsolutePath PipOutputLog { get; set; }

        /// <inheritdoc />
        public int StatusFrequencyMs { get; set; }

        /// <inheritdoc />
        public bool FailPipOnFileAccessError { get; set; }

        /// <inheritdoc />
        public bool DisableLoggedPathTranslation { get; set; }

        /// <inheritdoc />
        public bool ReplayWarnings { get; set; }

        /// <inheritdoc />
        public bool UseCustomPipDescriptionOnConsole { get; set; }

        /// <inheritdoc />
        public CacheMissAnalysisOption CacheMissAnalysisOption { get; set; }

        /// <inheritdoc />
        public bool OptimizeConsoleOutputForAzureDevOps { get; set; }
    }
}
