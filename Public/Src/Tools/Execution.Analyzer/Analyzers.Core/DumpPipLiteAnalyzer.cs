// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeDumpPipLiteAnalyzer(AnalysisInput input)
        {
            string outputDirectory = null;
            bool dumpObservedFileAccesses = false;
            long pipSemiStableHash = 0;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = ParseSingletonPathOption(opt, outputDirectory);
                }
                else if (opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                        opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    pipSemiStableHash = ParseSemistableHash(opt);
                }
                else if (opt.Name.StartsWith("dumpObservedFileAccesses", StringComparison.OrdinalIgnoreCase))
                {
                    dumpObservedFileAccesses = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error($"Unknown option provided for the dump pip lite analyzer: {opt.Name}");
                }
            }

            if (pipSemiStableHash == 0)
            {
                throw Error("/pip option must be specified.");
            }

            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                throw Error("Specified output directory does not exist.");
            }

            return new DumpPipLiteAnalyzer(input, outputDirectory, pipSemiStableHash, dumpObservedFileAccesses);
        }

        private static void WriteDumpPipLiteAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Dump Pip Lite Analyzer");
            writer.WriteModeOption(nameof(AnalysisMode.DumpPipLite), "Generates a JSON file containing static information about a requested pip or generates a set of JSON files for all failing pips in a given build to the specified directory (or in the same directory as the XLG file if no directory is specified). The output from this analyzer is an easier to interpret subset of the larger dump pip analyzers.");
            writer.WriteOption("pip", "Required. SemiStableHash of the pip to be dumped (must start with 'Pip', e.g., 'PipC623BCE303738C69').");
            writer.WriteOption("dumpObservedFileAccesses[+|-]", "Optional. The analyzer will dump observed file accesses if /logObservedFileAccesses was set for the build.");
            writer.WriteOption("outputDirectory", "Optional. If specifed, the logs will be generated under outputDirectory\\FailedPips, else they will be under XLGPath\\FailedPips.");
        }
    }

    /// <summary>
    /// Dumps static information on a specified pip to a JSON file.
    /// </summary>
    internal sealed class DumpPipLiteAnalyzer : Analyzer
    {
        private readonly Pip m_pip;
        private readonly PipTable m_pipTable;
        private readonly PipGraph m_pipGraph;
        private readonly string m_logPath;
        private readonly bool m_dumpObservedFileAccesses;
        private ProcessExecutionMonitoringReportedEventData? m_dynamicData;

        public DumpPipLiteAnalyzer(AnalysisInput input, string outputDirectory, long semiStableHash, bool dumpObservedFileAccesses)
            : base (input)
        {
            if (string.IsNullOrEmpty(outputDirectory))
            {
                // Use the execution log path
                m_logPath = Path.Combine(Path.GetDirectoryName(input.ExecutionLogPath), "FailedPips");
            }
            else
            {
                m_logPath = Path.Combine(outputDirectory, "FailedPips");
            }

            m_pipTable = input.CachedGraph.PipTable;
            m_pipGraph = input.CachedGraph.PipGraph;
            m_dumpObservedFileAccesses = dumpObservedFileAccesses;
            m_dynamicData = null;

            foreach (var pipId in m_pipTable.StableKeys)
            {
                var possibleMatch = m_pipTable.GetPipSemiStableHash(pipId);
                if (possibleMatch == semiStableHash)
                {
                    m_pip = m_pipTable.HydratePip(pipId, PipQueryContext.DumpPipLiteAnalyzer);
                    break;
                }
            }

            if (m_pip == null)
            {
                // If no matches were found, then we likely got some bad input from the user.
                throw new InvalidArgumentException($"Specified Pip 'Pip{semiStableHash:X}' does not exist.");
            }
        }

        /// <summary>
        /// Dumps the specified pip.
        /// </summary>
        /// <returns>0 if dump pip was successful, or 1 if dump pip was unsuccessful</returns>
        public override int Analyze()
        {
            var directoryCreateResult = DumpPipLiteAnalysisUtilities.CreateLoggingDirectory(m_logPath, LoggingContext);
            var dumpPipResult = false;

            if (directoryCreateResult)
            {
                dumpPipResult = DumpPipLiteAnalysisUtilities.DumpPip(
                    m_pip,
                    m_dynamicData,
                    m_logPath,
                    PathTable,
                    StringTable,
                    SymbolTable,
                    m_pipGraph,
                    LoggingContext
                );
            }

            if (!(directoryCreateResult && dumpPipResult))
            {
                return 1; // An error should be logged for this already
            }

            return 0;
        }

        /// <summary>
        /// Record observed file accesses if the flag is set.
        /// </summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            if (m_dumpObservedFileAccesses && data.PipId == m_pip.PipId)
            {
                m_dynamicData = data;
            }
        }
    }
}
