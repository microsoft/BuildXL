// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializePipExecutionPerformanceAnalyzer()
        {
            string outputFilePath = null;
            bool isSimplifiedCsv = false;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("csv", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("c", StringComparison.OrdinalIgnoreCase))
                {
                    isSimplifiedCsv = true;
                }
                else
                {
                    throw Error("Unknown option for pip performance analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("outputFile parameter is required");
            }

            return new PipExecutionPerformanceAnalyzer(GetAnalysisInput(), outputFilePath, isSimplifiedCsv);
        }

        private static void WritePipExecutionPerformanceAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Pip Performance Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.PipExecutionPerformance), "Generates a JSON file containing PipExecutionPerformance as discovered at build progress");
            writer.WriteOption("outputFile", "Required. The location of the output file for pip performance analysis.", shortName: "o");
            writer.WriteOption("csv", "Optional. The output will be simplified and written to a csv file.", shortName: "c");
        }
    }

    /// <summary>
    /// Class for extracting pip execution performance data.
    /// </summary>
    internal sealed class PipExecutionPerformanceAnalyzer : Analyzer
    {
        private const string Indentation = "    ";
        private readonly StreamWriter m_writer;
        private readonly bool m_isCsvFormat;

        private readonly Dictionary<PipId, ProcessPipExecutionPerformance> m_processPerformance =
            new Dictionary<PipId, ProcessPipExecutionPerformance>();

        private Dictionary<PipId, TimeSpan> m_cpuStepDurations = new Dictionary<PipId, TimeSpan>();

        private string m_indent = string.Empty;

        /// <nodoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public PipExecutionPerformanceAnalyzer(AnalysisInput input, string outputFilePath, bool isCsvFormat)
            : base(input)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(outputFilePath));

            System.Diagnostics.Debugger.Launch();
            m_writer = new StreamWriter(outputFilePath);
            m_isCsvFormat = isCsvFormat;
        }

        public override int Analyze()
        {
            if (m_isCsvFormat)
            {
                WriteSimplifiedCsvFile();
            }
            else
            {
                WriteDetailedJsonFile();
            }

            return 0;
        }

        private void WriteSimplifiedCsvFile()
        {
            m_writer.WriteLine("Description,Step,Wall,User,Kernel,User/Wall,ReadByes,WriteBytes,OtherBytes,Processes,Memory");
            foreach (var processIdAndExecutionPerformance in m_processPerformance.OrderByDescending(pip => pip.Value.ProcessExecutionTime.TotalMilliseconds))
            {
                var performance = processIdAndExecutionPerformance.Value;

                m_cpuStepDurations.TryGetValue(processIdAndExecutionPerformance.Key, out TimeSpan cpuStepDuration);

                var process = CachedGraph.PipGraph.GetPipFromPipId(processIdAndExecutionPerformance.Key) as Process;
                Contract.Assert(process != null);

                WriteColumn(process.GetDescription(CachedGraph.Context));
                WriteColumn(Math.Round(cpuStepDuration.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.ProcessExecutionTime.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.UserTime.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.KernelTime.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.UserTime.TotalMinutes / performance.ProcessExecutionTime.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.IO.ReadCounters.TransferCount / 1024 / 1024 / 1024.0, 1).ToString());
                WriteColumn(Math.Round(performance.IO.WriteCounters.TransferCount / 1024 / 1024 / 1024.0, 1).ToString());
                WriteColumn(Math.Round(performance.IO.OtherCounters.TransferCount / 1024 / 1024 / 1024.0, 1).ToString());
                WriteColumn(performance.NumberOfProcesses.ToString());
                m_writer.Write(performance.PeakMemoryUsage.ToString());

                m_writer.WriteLine();
            }

            m_writer.Flush();
        }

        private void WriteDetailedJsonFile()
        {
            WriteLineIndented("{");
            IncrementIndent();

            WriteLineIndented("\"processes\" : [");
            IncrementIndent();

            int i = 0;

            foreach (var processIdAndExecutionPerformance in m_processPerformance.OrderByDescending(pip => pip.Value.ProcessExecutionTime.TotalMilliseconds))
            {
                var process = CachedGraph.PipGraph.GetPipFromPipId(processIdAndExecutionPerformance.Key) as Process;
                Contract.Assert(process != null);

                var performance = processIdAndExecutionPerformance.Value;

                WriteLineIndented("{");
                IncrementIndent();

                WriteLineIndented(I($"\"id\" : \"{process.PipId.ToString()}\","));
                WriteLineIndented(I($"\"semiStableHash\" : \"{Pip.SemiStableHashPrefix}{process.SemiStableHash:X16}\","));
                WriteLineIndented(I($"\"tool\" : \"{NormalizeString(process.GetToolName(CachedGraph.Context.PathTable).ToString(CachedGraph.Context.StringTable))}\","));
                WriteLineIndented(I($"\"description\" : \"{NormalizeString(process.GetDescription(CachedGraph.Context))}\","));
                WriteLineIndented(I($"\"numberOfExecutedProcesses\" : {performance.NumberOfProcesses},"));
                WriteLineIndented(I($"\"start\" : \"{performance.ExecutionStart}\","));
                WriteLineIndented(I($"\"stop\" : \"{performance.ExecutionStop}\","));
                WriteLineIndented(I($"\"result\" : \"{performance.ExecutionLevel}\","));
                WriteLineIndented(I($"\"executionTimeInMs\" : {performance.ProcessExecutionTime.TotalMilliseconds},"));
                WriteLineIndented(I($"\"userExecutionTimeInMs\" : {performance.UserTime.TotalMilliseconds},"));
                WriteLineIndented(I($"\"kernelExecutionTimeInMs\" : {performance.KernelTime.TotalMilliseconds},"));
                WriteLineIndented(I($"\"peakMemoryUsageInMb\" : {performance.PeakMemoryUsage},"));

                WriteLineIndented("\"io\" : {");
                IncrementIndent();

                WriteLineIndented("\"read\" : {");
                IncrementIndent();
                WriteLineIndented(I($"\"operationCount\" : {performance.IO.ReadCounters.OperationCount},"));
                WriteLineIndented(I($"\"transferCountInByte\" : {performance.IO.ReadCounters.TransferCount}"));
                DecrementIndent();
                WriteLineIndented("},");

                WriteLineIndented("\"write\" : {");
                IncrementIndent();
                WriteLineIndented(I($"\"operationCount\" : {performance.IO.WriteCounters.OperationCount},"));
                WriteLineIndented(I($"\"transferCountInByte\" : {performance.IO.WriteCounters.TransferCount}"));
                DecrementIndent();
                WriteLineIndented("},");

                WriteLineIndented("\"other\" : {");
                IncrementIndent();
                WriteLineIndented(I($"\"operationCount\" : {performance.IO.OtherCounters.OperationCount},"));
                WriteLineIndented(I($"\"transferCountInByte\" : {performance.IO.OtherCounters.TransferCount}"));
                DecrementIndent();
                WriteLineIndented("}");

                DecrementIndent();
                WriteLineIndented("},");

                WriteLineIndented("\"tags\" : [");
                IncrementIndent();

                int j = 0;
                foreach (var tag in process.Tags)
                {
                    WriteLineIndented(I($"\"{NormalizeString(tag.ToString(StringTable))}\"") + (j < process.Tags.Length - 1 ? "," : string.Empty));
                    j++;
                }

                DecrementIndent();
                WriteLineIndented("]");

                DecrementIndent();
                WriteLineIndented("}" + (i < m_processPerformance.Count - 1 ? "," : string.Empty));
            }

            DecrementIndent();
            WriteLineIndented("]");

            DecrementIndent();
            WriteIndented("}");
        }

        /// <inheritdoc />
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            if (data.ExecutionPerformance != null)
            {
                ProcessPipExecutionPerformance processPerformance = data.ExecutionPerformance as ProcessPipExecutionPerformance;

                if (processPerformance != null && !m_processPerformance.ContainsKey(data.PipId))
                {
                    m_processPerformance.Add(data.PipId, processPerformance);
                }
            }
        }

        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            if (data.Step == Scheduler.PipExecutionStep.ExecuteProcess)
            {
                if (data.Dispatcher == Scheduler.WorkDispatcher.DispatcherKind.CPU)
                {
                    m_cpuStepDurations[data.PipId] = data.Duration;
                }
            }
        }

        private void WriteColumn(string s)
        {
            m_writer.Write(s);
            m_writer.Write(',');
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_writer.Dispose();
            base.Dispose();
        }

        private void IncrementIndent()
        {
            m_indent += Indentation;
        }

        private void DecrementIndent()
        {
            if (m_indent.Length < Indentation.Length)
            {
                return;
            }

            m_indent = m_indent.Substring(0, m_indent.Length - Indentation.Length);
        }

        private void WriteIndented(string s)
        {
            m_writer.Write(m_indent);
            m_writer.Write(s);
        }

        private void WriteLineIndented(string s)
        {
            WriteIndented(s);
            m_writer.WriteLine();
        }

        private string NormalizeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
