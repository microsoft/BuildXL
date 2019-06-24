// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using HelpLevel = BuildXL.ToolSupport.HelpLevel;

namespace BuildXL.Execution.Analyzer
{
    internal sealed partial class Args : CommandLineUtilities
    {
        private static readonly string[] s_helpStrings = new[] { "?", "help" };

        private readonly AnalysisMode? m_mode;
        private readonly AnalysisInput m_analysisInput;
        private AnalysisInput m_analysisInputOther;
        private readonly Analyzer m_analyzer;
        private readonly Analyzer m_analyzerOther;
        private readonly bool m_canHandleWorkerEvents = true;

        public readonly IEnumerable<Option> AnalyzerOptions;
        public readonly bool Help;

        public readonly LoggingContext LoggingContext = new LoggingContext("BuildXL.Execution.Analyzer");
        public readonly TrackingEventListener TrackingEventListener = new TrackingEventListener(Events.Log);

        // Variables that are unused without full telemetry
        private readonly bool m_telemetryDisabled = false;
        private readonly Stopwatch m_telemetryStopwatch = new Stopwatch();

        public Args(string[] args)
            : base(args)
        {
            List<Option> analyzerOptions = new List<Option>();
            string cachedGraphDirectory = null;

            // TODO: Embed HashType in XLG file and update analyzer to use that instead of setting HashType globally.
            ContentHashingUtilities.SetDefaultHashType();

            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("executionLog", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("xl", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(m_analysisInput.ExecutionLogPath))
                    {
                        m_analysisInput.ExecutionLogPath = ParsePathOption(opt);
                    }
                    else
                    {
                        m_analysisInputOther.ExecutionLogPath = ParseSingletonPathOption(opt, m_analysisInputOther.ExecutionLogPath);
                    }
                }
                else if (opt.Name.Equals("graphDirectory", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("gd", StringComparison.OrdinalIgnoreCase))
                {
                    cachedGraphDirectory = ParseSingletonPathOption(opt, cachedGraphDirectory);
                }
                else if (opt.Name.Equals("mode", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("m", StringComparison.OrdinalIgnoreCase))
                {
                    m_mode = ParseEnumOption<AnalysisMode>(opt);
                }
                else if (opt.Name.Equals("disableTelemetry"))
                {
                    m_telemetryDisabled = true;
                }
                else if (opt.Name.Equals("disableWorkerEvents", StringComparison.OrdinalIgnoreCase))
                {
                    m_canHandleWorkerEvents = false;
                }
                else if (s_helpStrings.Any(s => opt.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                {
                    // If the analyzer was called with '/help' argument - print help and exit
                    Help = true;
                    WriteHelp();
                    return;
                }
                else
                {
                    analyzerOptions.Add(opt);
                }
            }

            AnalyzerOptions = analyzerOptions;
            if (!m_mode.HasValue)
            {
                throw Error("Mode parameter is required");
            }

            // Add required parameter errors here
            switch (m_mode.Value)
            {
                case AnalysisMode.ObservedAccess:
                {
                    if (!analyzerOptions.Any(opt => opt.Name.Equals("o")))
                    {
                        throw Error("When executing `ObservedAccess` mode, an `/o:PATH_TO_OUTPUT_FILE` parameter is required to store the generated output");
                    }
                    break;
                }
            }

            // Only send telemetry if all arguments were valid
            TelemetryStartup();

            switch (m_mode.Value)
            {
                case AnalysisMode.SpecClosure:
                    var analyzer = InitializeSpecClosureAnalyzer();
                    analyzer.Analyze();
                    break;
            }

            if (string.IsNullOrEmpty(m_analysisInput.ExecutionLogPath) && string.IsNullOrEmpty(cachedGraphDirectory))
            {
                // Try to find the last build log from the user if none was specied.
                var invocation = new global::BuildXL.Engine.Invocations().GetLastInvocation(LoggingContext);
                if (invocation == null || !Directory.Exists(invocation.Value.LogsFolder))
                {
                    throw Error("executionLog or graphDirectory parameter is required");
                }

                Console.WriteLine("Using last build from: '{0}', you can use /executionLog or /graphDirectory arguments to explicitly choose a build", invocation.Value.LogsFolder);
                m_analysisInput.ExecutionLogPath = invocation.Value.LogsFolder;
            }

            if (m_mode.Value == AnalysisMode.LogCompare && string.IsNullOrEmpty(m_analysisInput.ExecutionLogPath))
            {
                throw Error("Additional executionLog to compare parameter is required");
            }

            // The fingerprint store based cache miss analyzer only uses graph information from the newer build,
            // so skip loading the graph for the earlier build
            if (m_mode.Value != AnalysisMode.CacheMiss)
            {
                if (!m_analysisInput.LoadCacheGraph(cachedGraphDirectory))
                {
                    throw Error($"Could not load cached graph from directory {cachedGraphDirectory}");
                }
            }

            switch (m_mode.Value)
            {
                case AnalysisMode.FingerprintText:
                    m_analyzer = InitializeFingerprintTextAnalyzer();
                    break;
                case AnalysisMode.ExportGraph:
                    m_analyzer = InitializePipGraphExporter();
                    break;
                case AnalysisMode.DirMembership:
                    m_analyzer = InitializeDirMembershipAnalyzer();
                    break;
                case AnalysisMode.Dev:
                    m_analyzer = InitializeDevAnalyzer();
                    break;
                case AnalysisMode.DumpProcess:
                    m_analyzer = InitializeDumpProcessAnalyzer();
                    break;
                case AnalysisMode.EventStats:
                    m_analyzer = InitializeEventStatsAnalyzer();
                    break;
                case AnalysisMode.Simulate:
                    m_analyzer = InitializeBuildSimulatorAnalyzer(m_analysisInput);
                    break;
                case AnalysisMode.ExportDgml:
                    m_analyzer = InitializeExportDgmlAnalyzer();
                    break;
                case AnalysisMode.CriticalPath:
                    m_analyzer = InitializeCriticalPathAnalyzer();
                    break;
                case AnalysisMode.FileImpact:
                    m_analyzer = InitializeFileImpactAnalyzer();
                    break;
                case AnalysisMode.FileConsumption:
                    m_analyzer = InitializeFileConsumptionAnalyzer();
                    break;
                case AnalysisMode.Codex:
                    m_analyzer = InitializeCodexAnalyzer();
                    break;
                case AnalysisMode.DumpPip:
                    m_analyzer = InitializeDumpPipAnalyzer();
                    break;
                case AnalysisMode.CosineDumpPip:
                    m_analyzer = InitializeCosineDumpPip();
                    break;
                case AnalysisMode.CosineJson:
                    m_analyzer = InitializeCosineJsonExport();
                    break;
                case AnalysisMode.ProcessRunScript:
                    m_analyzer = InitializeProcessRunScriptAnalyzer();
                    break;
                case AnalysisMode.ObservedInput:
                    m_analyzer = InitializeObservedInputResult();
                    break;
                case AnalysisMode.ObservedInputSummary:
                    m_analyzer = InitializeObservedInputSummaryResult();
                    break;
                case AnalysisMode.ToolEnumeration:
                    m_analyzer = InitializeToolEnumerationAnalyzer();
                    break;
                case AnalysisMode.FailedPipInput:
                    m_analyzer = InitializeFailedPipInputAnalyzer();
                    break;
                case AnalysisMode.FailedPipsDump:
                    m_analyzer = InitializeFailedPipsDumpAnalyzer();
                    if (!string.IsNullOrEmpty(m_analysisInputOther.ExecutionLogPath))
                    {
                        if (!m_analysisInputOther.LoadCacheGraph(null))
                        {
                            throw Error("Could not load second cached graph");
                        }
                        m_analyzerOther = ((FailedPipsDumpAnalyzer)m_analyzer).GetDiffAnalyzer(m_analysisInputOther);
                    }
                    break;
                case AnalysisMode.Whitelist:
                    m_analyzer = InitializeWhitelistAnalyzer();
                    break;
                case AnalysisMode.IdeGenerator:
                    m_analyzer = InitializeIdeGenerator();
                    break;
                case AnalysisMode.PipExecutionPerformance:
                    m_analyzer = InitializePipExecutionPerformanceAnalyzer();
                    break;
                case AnalysisMode.ProcessDetouringStatus:
                    m_analyzer = InitializeProcessDetouringStatusAnalyzer();
                    break;
                case AnalysisMode.ObservedAccess:
                    m_analyzer = InitializeObservedAccessAnalyzer();
                    break;
                case AnalysisMode.CacheDump:
                    m_analyzer = InitializeCacheDumpAnalyzer(m_analysisInput);
                    break;
                case AnalysisMode.BuildStatus:
                    m_analyzer = InitializeBuildStatus(m_analysisInput);
                    break;
                case AnalysisMode.WinIdeDependency:
                    m_analyzer = InitializeWinIdeDependencyAnalyzer();
                    break;
                case AnalysisMode.PerfSummary:
                    m_analyzer = InitializePerfSummaryAnalyzer();
                    break;
                case AnalysisMode.LogCompare:
                    m_analyzer = InitializeSummaryAnalyzer(m_analysisInput);
                    if (!m_analysisInputOther.LoadCacheGraph(null))
                    {
                        throw Error("Could not load second cached graph");
                    }

                    m_analyzerOther = InitializeSummaryAnalyzer(m_analysisInputOther, true);
                    break;
                case AnalysisMode.CacheMissLegacy:
                    m_analyzer = InitializeCacheMissAnalyzer(m_analysisInput);
                    if (!m_analysisInputOther.LoadCacheGraph(null))
                    {
                        throw Error("Could not load second cached graph");
                    }

                    m_analyzerOther = ((CacheMissAnalyzer)m_analyzer).GetDiffAnalyzer(m_analysisInputOther);
                    break;
                case AnalysisMode.IncrementalSchedulingState:
                    m_analyzer = InitializeIncrementalSchedulingStateAnalyzer();
                    break;
                case AnalysisMode.FileChangeTracker:
                    m_analyzer = InitializeFileChangeTrackerAnalyzer();
                    break;
                case AnalysisMode.InputTracker:
                    m_analyzer = InitializeInputTrackerAnalyzer();
                    break;
                case AnalysisMode.FilterLog:
                    m_analyzer = InitializeFilterLogAnalyzer();
                    break;
                case AnalysisMode.PipFilter:
                    m_analyzer = InitializePipFilterAnalyzer();
                    break;
                case AnalysisMode.CacheMiss:
                    // This analyzer does not rely on the execution log
                    if (!m_analysisInputOther.LoadCacheGraph(null))
                    {
                        throw Error("Could not load second cached graph");
                    }
                    m_analyzer = InitializeFingerprintStoreAnalyzer(m_analysisInput, m_analysisInputOther);
                    break;
                case AnalysisMode.PipFingerprint:
                    m_analyzer = InitializePipFingerprintAnalyzer(m_analysisInput);
                    break;
                case AnalysisMode.RequiredDependencies:
                    m_analyzer = InitializeRequiredDependencyAnalyzer();
                    break;
                case AnalysisMode.ScheduledInputsOutputs:
                    m_analyzer = InitializeScheduledInputsOutputsAnalyzer();
                    break;
#if FEATURE_VSTS_ARTIFACTSERVICES
                case AnalysisMode.CacheHitPredictor:
                    m_analyzer = InitializeCacheHitPredictor();
                    break;
#endif
                case AnalysisMode.DependencyAnalyzer:
                    m_analyzer = InitializeDependencyAnalyzer();
                    break;
                case AnalysisMode.GraphDiffAnalyzer:
                    if (!m_analysisInputOther.LoadCacheGraph(null))
                    {
                        throw Error("Could not load second cached graph");
                    }
                    m_analyzer = InitializeGraphDiffAnalyzer();
                    break;
                case AnalysisMode.DumpMounts:
                    m_analyzer = InitializeDumpMountsAnalyzer();
                    break;
                case AnalysisMode.CopyFile:
                    m_analyzer = InitializeCopyFilesAnalyzer();
                    break;
                default:
                    Contract.Assert(false, "Unhandled analysis mode");
                    break;
            }

            Contract.Assert(m_analyzer != null, "Analyzer must be set.");

            m_analyzer.LoggingContext = LoggingContext;
            m_analyzer.CanHandleWorkerEvents = m_canHandleWorkerEvents;
            if (m_analyzerOther != null)
            {
                m_analyzerOther.CanHandleWorkerEvents = m_canHandleWorkerEvents;
            }
        }

        public int Analyze()
        {
            if (m_analyzer == null)
            {
                return 0;
            }

            m_analyzer.Prepare();
            if (m_analysisInput.ExecutionLogPath != null)
            {
                // NOTE: We call Prepare above so we don't need to prepare as a part of reading the execution log
                var reader = Task.Run(() => m_analyzer.ReadExecutionLog(prepare: false));
                if (m_mode == AnalysisMode.LogCompare)
                {
                    m_analyzerOther.Prepare();
                    var otherReader = Task.Run(() => m_analyzerOther.ReadExecutionLog());
                    otherReader.Wait();
                }

                if (m_mode == AnalysisMode.FailedPipsDump && m_analyzerOther != null)
                {
                    var start = DateTime.Now;
                    Console.WriteLine($"[{start}] Reading compare to Log");
                    var otherReader = Task.Run(() => m_analyzerOther.ReadExecutionLog());
                    otherReader.Wait();
                    var duration = DateTime.Now - start;
                    Console.WriteLine($"Done reading compare to log : duration = [{duration}]");
                }

                reader.Wait();

                if (m_mode == AnalysisMode.CacheMissLegacy)
                {
                    // First pass just to read in PipCacheMissType data
                    var otherReader = Task.Run(() => m_analyzerOther.ReadExecutionLog());
                    otherReader.Wait();

                    // Second pass to do fingerprint differences analysis
                    otherReader = Task.Run(() => m_analyzerOther.ReadExecutionLog());
                    otherReader.Wait();
                }
            }

            var exitCode = m_analyzer.Analyze();
            if (m_mode == AnalysisMode.FailedPipsDump && m_analyzerOther != null)
            {
                var failedPipsDump = (FailedPipsDumpAnalyzer)m_analyzer;
                exitCode = failedPipsDump.Compare(m_analyzerOther);
            }

            if (m_mode == AnalysisMode.LogCompare)
            {
                m_analyzerOther.Analyze();
                SummaryAnalyzer summary = (SummaryAnalyzer)m_analyzer;
                exitCode = summary.Compare((SummaryAnalyzer)m_analyzerOther);
            }

            if (m_mode == AnalysisMode.CacheMissLegacy)
            {
                exitCode = m_analyzerOther.Analyze();
            }

            m_analyzer?.Dispose();
            m_analyzerOther?.Dispose();

            TelemetryShutdown();
            return exitCode;
        }

        #region Telemetry

        private void HandleUnhandledFailure(Exception exception)
        {
            // Show the exception to the user
            ConsoleColor original = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(exception.ToString());
            Console.ForegroundColor = original;

            // Log the exception to telemetry
            if (AriaV2StaticState.IsEnabled)
            {
                Tracing.Logger.Log.ExecutionAnalyzerCatastrophicFailure(LoggingContext, m_mode.ToString(), exception.ToString());
                TelemetryShutdown();
            }

            Environment.Exit(ExitCode.FromExitKind(ExitKind.InternalError));
        }

        private void TelemetryStartup()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (sender, eventArgs) =>
                {
                    HandleUnhandledFailure(
                        eventArgs.ExceptionObject as Exception
                        );
                };

            if (!Debugger.IsAttached && !m_telemetryDisabled)
            {
                AriaV2StaticState.Enable(global::BuildXL.Tracing.AriaTenantToken.Key);
                TrackingEventListener.RegisterEventSource(ETWLogger.Log);
                m_telemetryStopwatch.Start();
            }
        }

        private void TelemetryShutdown()
        {
            if (AriaV2StaticState.IsEnabled && !m_telemetryDisabled)
            {
                m_telemetryStopwatch.Stop();
                Tracing.Logger.Log.ExecutionAnalyzerInvoked(LoggingContext, m_mode.ToString(), m_telemetryStopwatch.ElapsedMilliseconds, Environment.CommandLine);
                LogEventSummary();
                // Analyzer telemetry is not critical to BuildXL, so no special handling for telemetry shutdown issues
                AriaV2StaticState.TryShutDown(TimeSpan.FromSeconds(10), out Exception telemetryShutdownException);
            }
        }

        #endregion Telemetry

        private AnalysisInput GetAnalysisInput()
        {
            return m_analysisInput;
        }

        private static void WriteHelp()
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner($"{Branding.AnalyzerExecutableName} - Tool for performing analysis/transformation of cached pip graphs and execution logs.");

            writer.WriteLine("");
            writer.WriteLine("Analysis Modes:");

            writer.WriteLine("");
            WriteFingerprintTextAnalyzerHelp(writer);

            writer.WriteLine("");
            WritePipGraphExporterHelp(writer);

            writer.WriteLine("");
            WriteDirMembershipHelp(writer);

            writer.WriteLine("");
            WriteDumpProcessAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteExportDgmlAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteObservedInputHelp(writer);

            writer.WriteLine("");
            WriteProcessDetouringHelp(writer);

            writer.WriteLine("");
            WritePipExecutionPerformanceAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteObservedInputSummaryHelp(writer);

            writer.WriteLine("");
            WriteObservedAccessHelp(writer);

            writer.WriteLine("");
            WriteToolEnumerationHelp(writer);

            writer.WriteLine("");
            WriteCriticalPathAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteDumpPipAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteProcessRunScriptAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteWhitelistAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteSummaryAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteBuildStatusHelp(writer);

            writer.WriteLine("");
            WriteWinIdeDependencyAnalyzeHelp(writer);

            writer.WriteLine("");
            WritePerfSummaryAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteEventStatsHelp(writer);

            writer.WriteLine("");
            WriteIncrementalSchedulingStateAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteFileChangeTrackerAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteInputTrackerAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteFingerprintStoreAnalyzerHelp(writer);

            writer.WriteLine("");
            WritePipFingerprintAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteCacheMissHelp(writer);

            writer.WriteLine("");
            WriteCacheDumpHelp(writer);

            writer.WriteLine("");
            WriteBuildSimulatorHelp(writer);

#if FEATURE_VSTS_ARTIFACTSERVICES
            writer.WriteLine("");
            WriteCacheHitPredictorHelp(writer);
#endif

            writer.WriteLine("");
            WriteDependencyAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteGraphDiffAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteDumpMountsAnalyzerHelp(writer);

            writer.WriteLine("");
            WritePipFilterHelp(writer);

            writer.WriteLine("");
            WriteFailedPipsDumpAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteCosineDumpPipHelp(writer);

            writer.WriteLine("");
            WriteCopyFilesAnalyzerHelp(writer);
        }

        public void LogEventSummary()
        {
            Tracing.Logger.Log.ExecutionAnalyzerEventCount(LoggingContext, TrackingEventListener.ToEventCountDictionary());
        }

        public long ParseSemistableHash(Option opt)
        {
            var adjustedOption = new Option() { Name = opt.Name, Value = opt.Value.ToUpper().Replace("PIP", "") };
            return Convert.ToInt64(ParseStringOption(adjustedOption), 16);
        }
    }

    /// <summary>
    /// <see cref="HelpWriter"/> for analyzers.
    /// </summary>
    public static class HelpWriterExtensions
    {
        /// <summary>
        /// Writes the mode flag help for each analyzer.
        /// </summary>
        public static void WriteModeOption(this HelpWriter writer, string modeName, string description, HelpLevel level = HelpLevel.Standard)
        {
            writer.WriteOption("mode", string.Format("\"{0}\". {1}", modeName, description), level: level, shortName: "m");
        }
    }
}
