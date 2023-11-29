// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BuildXL.Pips;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeConcurrentPipsAnalyzer()
        {
            string outputFilePath = null;
            long pipSemiStableHash = 0;
            DateTime? dateTime = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                        opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    pipSemiStableHash = ParseSemistableHash(opt);
                }
                else if (opt.Name.Equals("time", StringComparison.OrdinalIgnoreCase) ||
                        opt.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
                {
                    dateTime = ParseDateTime(opt);
                }
                else
                {
                    throw Error("Unknown option for concurrent pips analyzer: {0}", opt.Name);
                }
            }

            if (dateTime == null && pipSemiStableHash == 0)
            {
                throw Error("Either /time or /pip must be specified.");
            }

            return new ConcurrentPipsAnalyzer(GetAnalysisInput(), outputFilePath, pipSemiStableHash, dateTime);
        }

        private static void WriteConcurrentPipsAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Concurrent Pips Analyzer");
            writer.WriteModeOption(nameof(AnalysisMode.ConcurrentPipsAnalyzer), "Lists all pips running concurrently with a given pip or point in time");
            writer.WriteOption("outputFile", "Optional. The location of the output file. If not present the logs directory of the build associated with the provided binary log is used.", shortName: "o");
            writer.WriteOption("pip", "Optional. The output file will contain a list of pips running concurrently with the provided pip. Either this parameter or 'time' have to be present. E.g. /pip:Pip48A15006B871D7E7", shortName: "p");
            writer.WriteOption("time", "Optional. The output file will contain a list of pips running at the provided point in time. An absolute date/time is expected (by default parsed in local time). Either this parameter or 'pip' have to be present. E.g. /time:\"11/28/2023 6:45:04 PM\"", shortName: "t");
        }
    }

    /// <summary>
    /// Lists the pips that were runing concurrently with a given pip.
    /// </summary>
    public class ConcurrentPipsAnalyzer : Analyzer
    {
        private readonly PipTable m_pipTable;
        private readonly PipExecutionContext m_context;
        private readonly string m_outputFilePath;
        private readonly long m_pipSemiStableHash;
        private PipExecutionPerformanceEventData? m_designatedPipExecutionPerformanceEventData = null;
        private readonly DateTime? m_dateTime;

        private readonly Queue<PipExecutionPerformanceEventData> m_runningPips = new();
        private readonly Queue<PipExecutionPerformanceEventData> m_toBeProcessedPips = new();

        /// <nodoc/>
        public ConcurrentPipsAnalyzer(AnalysisInput input, string outputFilePath, long pipSemiStableHash, DateTime? dateTime) : base(input)
        {
            if (string.IsNullOrEmpty(outputFilePath))
            {
                outputFilePath = Path.Combine(Path.GetDirectoryName(input.ExecutionLogPath), 
                    pipSemiStableHash != 0 ? $"Pip{pipSemiStableHash:X16}.txt" : $"Time{dateTime?.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt");
                Console.WriteLine($"Missing option /outputFilePath using: {outputFilePath}");
            }

            m_outputFilePath = outputFilePath;
            m_pipSemiStableHash = pipSemiStableHash;
            m_dateTime = dateTime;

            m_pipTable = input.CachedGraph.PipTable;
            m_context = input.CachedGraph.Context;
         }

        /// <inheritdoc/>
        public override int Analyze()
        {   
            if (!m_dateTime.HasValue && m_designatedPipExecutionPerformanceEventData == null)
            {
                throw new InvalidArgumentException($"Specified Pip 'Pip{m_pipSemiStableHash:X}' does not exist.");
            }

            // Let's remove potentially stale files and create the output directory if needed
            File.Delete(m_outputFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(m_outputFilePath));

            // Ideally we would like to print times relative to the beginning of the build (since that's what logs show). But unfortunately this information
            // is not available in the binary log today. The currently available Timestamp field carries the number of ticks since the scheduler was initialized,
            // but that's not the beginning of the build since scheduler initialization happens after the frontend is done (which may include parsing/evaluation/etc).
            // For the time being we just print the absolute time, which is probably good enough for understanding what was concurrently executing at a given point in time.
            if (m_dateTime.HasValue)
            {
                File.WriteAllText(m_outputFilePath, $"Pips running at time {m_dateTime}:{Environment.NewLine}");
            }
            else
            {
                var pip = m_pipTable.HydratePip(m_designatedPipExecutionPerformanceEventData.Value.PipId, PipQueryContext.PipGraphRetrieveAllPips);
                File.WriteAllText(m_outputFilePath, $"Pips running concurrently with {pip.GetDescription(m_context)} " +
                    $"[{m_designatedPipExecutionPerformanceEventData.Value.ExecutionPerformance.ExecutionStart} - {m_designatedPipExecutionPerformanceEventData.Value.ExecutionPerformance.ExecutionStop}]:{Environment.NewLine}");
            }

            // If no other pips are running concurrently, explicitly write down 'None' to make it explicit
            if (m_runningPips.Count == 0)
            {
                File.AppendAllText(m_outputFilePath, $"None{Environment.NewLine}");
                return 0;
            }

            while (m_runningPips.TryDequeue(out var data))
            {
                var executionPerf = data.ExecutionPerformance;
                var pip = m_pipTable.HydratePip(data.PipId, PipQueryContext.DumpPipLiteAnalyzer);
                File.AppendAllText(m_outputFilePath, $"[{executionPerf.ExecutionStart} - {executionPerf.ExecutionStop}] ({executionPerf.ExecutionLevel}) {pip.GetDescription(m_context)}{Environment.NewLine}");
            }

            return 0;
        }

        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            // If a point in time was specified, just collect all pips that were running at that time.
            var executionPerf = data.ExecutionPerformance;
            if (m_dateTime.HasValue)
            {
                if (ShouldIncludePip(executionPerf) && executionPerf.ExecutionStart <= m_dateTime.Value && executionPerf.ExecutionStop >= m_dateTime.Value)
                {
                    m_runningPips.Enqueue(data);
                }

                return;
            }

            // If we already saw the perf info of the designated pip, we can directly collect the pips that were running concurrently with it.
            if (m_designatedPipExecutionPerformanceEventData != null)
            {
                if (IsExecutingConcurrentlyWithDesignatedPip(executionPerf))
                {
                    m_runningPips.Enqueue(data);
                }

                return;
            }

            // If we are seeing the designated pip for the first time, pin its execution performance info and process all queued pips.
            var possibleMatch = m_pipTable.GetPipSemiStableHash(data.PipId);
            if (possibleMatch == m_pipSemiStableHash)
            {
                m_designatedPipExecutionPerformanceEventData = data;

                ProcessQueuedPips();

                return;
            }

            // Otherwise, we need to defer processing to the point where we see the designated pip. Just queue the pip for later processing.
            m_toBeProcessedPips.Enqueue(data);
        }

        private bool ShouldIncludePip(PipExecutionPerformance pipPerformance) =>
            pipPerformance.ExecutionLevel == PipExecutionLevel.Executed || pipPerformance.ExecutionLevel == PipExecutionLevel.Failed;

        private bool IsExecutingConcurrentlyWithDesignatedPip(PipExecutionPerformance pipPerformance) =>
            ShouldIncludePip(pipPerformance) &&    
            pipPerformance.ExecutionStart <= m_designatedPipExecutionPerformanceEventData.Value.ExecutionPerformance.ExecutionStop && pipPerformance.ExecutionStop >= m_designatedPipExecutionPerformanceEventData.Value.ExecutionPerformance.ExecutionStart;

        private void ProcessQueuedPips()
        {
            while (m_toBeProcessedPips.TryDequeue(out var data))
            {
                if (IsExecutingConcurrentlyWithDesignatedPip(data.ExecutionPerformance))
                {
                    m_runningPips.Enqueue(data);
                }
            }
        }   
    }
}
