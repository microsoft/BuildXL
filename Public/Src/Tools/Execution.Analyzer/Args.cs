// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using HelpLevel = BuildXL.ToolSupport.HelpLevel;

namespace BuildXL.Execution.Analyzer
{
    internal class ConsoleEventListener : FormattingEventListener
    {
        public ConsoleEventListener(Events eventSource, DateTime baseTime, WarningMapper warningMapper = null, EventLevel level = EventLevel.Verbose, bool captureAllDiagnosticMessages = false, TimeDisplay timeDisplay = TimeDisplay.None, EventMask eventMask = null, DisabledDueToDiskWriteFailureEventHandler onDisabledDueToDiskWriteFailure = null, bool listenDiagnosticMessages = false, bool useCustomPipDescription = false)
            : base(eventSource, baseTime, warningMapper, level, captureAllDiagnosticMessages, timeDisplay, eventMask, onDisabledDueToDiskWriteFailure, listenDiagnosticMessages, useCustomPipDescription)
        {
        }

        protected override void Output(EventLevel level, EventWrittenEventArgs eventData, string text, bool doNotTranslatePaths = false)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.ff}] {text}");
        }
    }

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
        public readonly ConsoleEventListener ConsoleListener = new ConsoleEventListener(Events.Log, DateTime.UtcNow);

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

            // The fingerprint store based cache miss analyzer
            // only uses graph information from the newer build, so skip loading the graph for the earlier build
            if (m_mode.Value != AnalysisMode.CacheMiss)
            {
                if (!m_analysisInput.LoadCacheGraph(cachedGraphDirectory))
                {
                    throw Error($"Could not load cached graph from directory {cachedGraphDirectory}");
                }
            }

            switch (m_mode.Value)
            {
                case AnalysisMode.Allowlist:
                case AnalysisMode.Whitelist:
                    m_analyzer = InitializeAllowlistAnalyzer();
                    break;
                case AnalysisMode.BuildStatus:
                    m_analyzer = InitializeBuildStatus(m_analysisInput);
                    break;
                case AnalysisMode.CacheDump:
                    m_analyzer = InitializeCacheDumpAnalyzer(m_analysisInput);
                    break;
#if FEATURE_VSTS_ARTIFACTSERVICES
                case AnalysisMode.CacheHitPredictor:
                    m_analyzer = InitializeCacheHitPredictor();
                    break;
#endif
                case AnalysisMode.CacheMiss:
                    // This analyzer does not rely on the execution log
                    if (!m_analysisInputOther.LoadCacheGraph(null))
                    {
                        throw Error("Could not load second cached graph");
                    }
                    m_analyzer = InitializeFingerprintStoreAnalyzer(m_analysisInput, m_analysisInputOther);
                    break;
                case AnalysisMode.CacheMissLegacy:
                    m_analyzer = InitializeCacheMissAnalyzer(m_analysisInput);
                    if (!m_analysisInputOther.LoadCacheGraph(null))
                    {
                        throw Error("Could not load second cached graph");
                    }

                    m_analyzerOther = ((CacheMissAnalyzer)m_analyzer).GetDiffAnalyzer(m_analysisInputOther);
                    break;
                case AnalysisMode.Codex:
                    m_analyzer = InitializeCodexAnalyzer();
                    break;
                case AnalysisMode.CopyFile:
                    m_analyzer = InitializeCopyFilesAnalyzer();
                    break;
                case AnalysisMode.CosineDumpPip:
                    m_analyzer = InitializeCosineDumpPip();
                    break;
                case AnalysisMode.CosineJson:
                    m_analyzer = InitializeCosineJsonExport();
                    break;
                case AnalysisMode.CriticalPath:
                    m_analyzer = InitializeCriticalPathAnalyzer();
                    break;
                case AnalysisMode.DebugLogs:
                    ConsoleListener.RegisterEventSource(ETWLogger.Log);
                    ConsoleListener.RegisterEventSource(FrontEnd.Script.Debugger.ETWLogger.Log);
                    m_analyzer = InitializeDebugLogsAnalyzer();
                    break;
                case AnalysisMode.DependencyAnalyzer:
                    m_analyzer = InitializeDependencyAnalyzer();
                    break;
                case AnalysisMode.Dev:
                    m_analyzer = InitializeDevAnalyzer();
                    break;
                case AnalysisMode.DirMembership:
                    m_analyzer = InitializeDirMembershipAnalyzer();
                    break;
                case AnalysisMode.DumpMounts:
                    m_analyzer = InitializeDumpMountsAnalyzer();
                    break;
                case AnalysisMode.DumpPip:
                    m_analyzer = InitializeDumpPipAnalyzer();
                    break;
                case AnalysisMode.DumpPipLite:
                    m_analyzer = InitializeDumpPipLiteAnalyzer(m_analysisInput);
                    break;
                case AnalysisMode.DumpProcess:
                    m_analyzer = InitializeDumpProcessAnalyzer();
                    break;
                case AnalysisMode.DumpStringTable:
                    m_analyzer = InitializeDumpStringTableAnalyzer();
                    break;
                case AnalysisMode.EventStats:
                    m_analyzer = InitializeEventStatsAnalyzer();
                    break;
                case AnalysisMode.ExportDgml:
                    m_analyzer = InitializeExportDgmlAnalyzer();
                    break;
                case AnalysisMode.ExportGraph:
                    m_analyzer = InitializePipGraphExporter();
                    break;
                case AnalysisMode.ExtraDependencies:
                    m_analyzer = InitializeExtraDependenciesAnalyzer();
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
                case AnalysisMode.FailedPipInput:
                    m_analyzer = InitializeFailedPipInputAnalyzer();
                    break;
                case AnalysisMode.FileConsumption:
                    m_analyzer = InitializeFileConsumptionAnalyzer();
                    break;
                case AnalysisMode.FileChangeTracker:
                    m_analyzer = InitializeFileChangeTrackerAnalyzer();
                    break;
                case AnalysisMode.FileImpact:
                    m_analyzer = InitializeFileImpactAnalyzer();
                    break;
                case AnalysisMode.FilterLog:
                    m_analyzer = InitializeFilterLogAnalyzer();
                    break;
                case AnalysisMode.FingerprintText:
                    m_analyzer = InitializeFingerprintTextAnalyzer();
                    break;
                case AnalysisMode.GraphDiffAnalyzer:
                    if (!m_analysisInputOther.LoadCacheGraph(null))
                    {
                        throw Error("Could not load second cached graph");
                    }
                    m_analyzer = InitializeGraphDiffAnalyzer();
                    break;
                case AnalysisMode.IdeGenerator:
                    m_analyzer = InitializeIdeGenerator();
                    break;
                case AnalysisMode.IncrementalSchedulingState:
                    m_analyzer = InitializeIncrementalSchedulingStateAnalyzer();
                    break;
                case AnalysisMode.InputTracker:
                    m_analyzer = InitializeInputTrackerAnalyzer();
                    break;
                case AnalysisMode.JavaScriptDependencyFixer:
                    m_analyzer = JavaScriptDependencyFixerAnalyzer();
                    break;
                case AnalysisMode.LogCompare:
                    m_analyzer = InitializeSummaryAnalyzer(m_analysisInput);
                    if (!m_analysisInputOther.LoadCacheGraph(null))
                    {
                        throw Error("Could not load second cached graph");
                    }

                    m_analyzerOther = InitializeSummaryAnalyzer(m_analysisInputOther, true);
                    break;
                case AnalysisMode.ObservedAccess:
                    m_analyzer = InitializeObservedAccessAnalyzer();
                    break;
                case AnalysisMode.ObservedInput:
                    m_analyzer = InitializeObservedInputResult();
                    break;
                case AnalysisMode.ObservedInputSummary:
                    m_analyzer = InitializeObservedInputSummaryResult();
                    break;
                case AnalysisMode.PackedExecutionExporter:
                    m_analyzer = InitializePackedExecutionExporter();
                    break;
                case AnalysisMode.PerfSummary:
                    m_analyzer = InitializePerfSummaryAnalyzer();
                    break;
                case AnalysisMode.PipExecutionPerformance:
                    m_analyzer = InitializePipExecutionPerformanceAnalyzer();
                    break;
                case AnalysisMode.PipFilter:
                    m_analyzer = InitializePipFilterAnalyzer();
                    break;
                case AnalysisMode.PipFingerprint:
                    m_analyzer = InitializePipFingerprintAnalyzer(m_analysisInput);
                    break;
                case AnalysisMode.ProcessDetouringStatus:
                    m_analyzer = InitializeProcessDetouringStatusAnalyzer();
                    break;
                case AnalysisMode.ReportedProcesses:
                    m_analyzer = InitializeReportedProcessesAnalyzer();
                    break;
                case AnalysisMode.ProcessRunScript:
                    m_analyzer = InitializeProcessRunScriptAnalyzer();
                    break;
                case AnalysisMode.RequiredDependencies:
                    m_analyzer = InitializeRequiredDependencyAnalyzer();
                    break;
                case AnalysisMode.ScheduledInputsOutputs:
                    m_analyzer = InitializeScheduledInputsOutputsAnalyzer();
                    break;
                case AnalysisMode.Simulate:
                    m_analyzer = InitializeBuildSimulatorAnalyzer(m_analysisInput);
                    break;
                case AnalysisMode.ToolEnumeration:
                    m_analyzer = InitializeToolEnumerationAnalyzer();
                    break;
                case AnalysisMode.WinIdeDependency:
                    m_analyzer = InitializeWinIdeDependencyAnalyzer();
                    break;
                case AnalysisMode.ConcurrentPipsAnalyzer:
                    m_analyzer = InitializeConcurrentPipsAnalyzer();
                    break;
                case AnalysisMode.LinuxPipDebug:
                    m_analyzer = InitializeLinuxPipDebugAnalyzer();
                    break;
                case AnalysisMode.AstredAnalyzer:
                    m_analyzer = InitializeAstredAnalyzer();
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

        private static void PrintInvalidXlgError(Exception e)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("ERROR: " + e.Message);
            Console.ForegroundColor = originalColor;
            var path = FileUtilities.GetTempFileName();
            File.WriteAllText(path, e.ToString());
            Console.WriteLine("Full stack trace saved to " + path);
        }

        public static void TruncatedXlgWarning()
        {
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine("WARNING: Execution log file possibly truncated, results may be incomplete!");
            Console.ForegroundColor = originalColor;
        }

        public int Analyze()
        {
            if (m_analyzer == null)
            {
                return 0;
            }

            try
            {
                m_analyzer.Prepare();
                bool dataIsComplete = true;
                if (m_analysisInput.ExecutionLogPath != null)
                {
                    // NOTE: We call Prepare above so we don't need to prepare as a part of reading the execution log
                    var reader = Task.Run(() => dataIsComplete &= m_analyzer.ReadExecutionLog(prepare: false));
                    if (m_mode == AnalysisMode.LogCompare)
                    {
                        m_analyzerOther.Prepare();
                        var otherReader = Task.Run(() => dataIsComplete &= m_analyzerOther.ReadExecutionLog());
                        otherReader.Wait();
                    }

                    if (m_mode == AnalysisMode.FailedPipsDump && m_analyzerOther != null)
                    {
                        var start = DateTime.Now;
                        Console.WriteLine($"[{start}] Reading compare to Log");
                        var otherReader = Task.Run(() => dataIsComplete &= m_analyzerOther.ReadExecutionLog());
                        otherReader.Wait();
                        var duration = DateTime.Now - start;
                        Console.WriteLine($"Done reading compare to log : duration = [{duration}]");
                    }

                    reader.Wait();

                    if (m_mode == AnalysisMode.CacheMissLegacy)
                    {
                        // First pass just to read in PipCacheMissType data
                        var otherReader = Task.Run(() => dataIsComplete &= m_analyzerOther.ReadExecutionLog());
                        otherReader.Wait();

                        // Second pass to do fingerprint differences analysis
                        otherReader = Task.Run(() => dataIsComplete &= m_analyzerOther.ReadExecutionLog());
                        otherReader.Wait();
                    }
                }

                if (!dataIsComplete)
                {
                    TruncatedXlgWarning();
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

                return exitCode;
            }
            catch (InvalidDataException e)
            {
                PrintInvalidXlgError(e);
                return -1;
            }
            finally
            {
                m_analyzer?.Dispose();
                m_analyzerOther?.Dispose();
                TelemetryShutdown();
            }
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
            WriteExtraDependenciesAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteObservedInputHelp(writer);

            writer.WriteLine("");
            WriteProcessDetouringHelp(writer);

            writer.WriteLine("");
            WriteReportedProcessesHelp(writer);

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
            WriteDumpPipLiteAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteDumpStringTableAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteProcessRunScriptAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteAllowlistAnalyzerHelp(writer);

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

            writer.WriteLine("");
            WriteJavaScriptDependencyFixerHelp(writer);

            writer.WriteLine("");
            WriteFileConsumptionAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteConcurrentPipsAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteLinuxPipDebugAnalyzerHelp(writer);

            writer.WriteLine("");
            WriteAstredAnalyzerHelp(writer);
        }

        public void LogEventSummary()
        {
            Tracing.Logger.Log.ExecutionAnalyzerEventCount(LoggingContext, TrackingEventListener.ToEventCountDictionary());
        }

        public long ParseSemistableHash(Option opt)
        {
            var adjustedOption = new Option() { Name = opt.Name, Value = opt.Value.ToUpper().Replace(Pip.SemiStableHashPrefix.ToUpper(), "") };
            if (!Int64.TryParse(ParseStringOption(adjustedOption), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long sshValue) || sshValue == 0)
            {
                throw Error("Invalid pip: {0}. Id must be a semistable hash that starts with Pip i.e.: PipC623BCE303738C69", opt.Value);
            }

            return sshValue;
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
