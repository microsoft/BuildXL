// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializePipExecutionPerformanceAnalyzer()
        {
            string outputFilePath = null;
            bool isSimplifiedCsv = false;
            bool includeProcessTree = false;
            bool useOriginalPaths = false;
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
                else if (opt.Name.Equals("includeProcessTree", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("pt", StringComparison.OrdinalIgnoreCase))
                {
                    includeProcessTree = true;
                }
                else if (opt.Name.Equals("useOriginalPaths", StringComparison.OrdinalIgnoreCase))
                {
                    useOriginalPaths = true;
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

            return new PipExecutionPerformanceAnalyzer(GetAnalysisInput(), outputFilePath, isSimplifiedCsv, includeProcessTree, useOriginalPaths);
        }

        private static void WritePipExecutionPerformanceAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Pip Performance Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.PipExecutionPerformance), "Generates a JSON file containing PipExecutionPerformance as discovered at build progress");
            writer.WriteOption("outputFile", "Required. The location of the output file for pip performance analysis.", shortName: "o");
            writer.WriteOption("includeProcessTree", "Optional. Include a field with information of all reported processes under that pip.", shortName: "pt");
            writer.WriteOption("useOriginalPaths", "Optional. Use original (non-subst'd) paths for tool paths and arguments in the process tree breakdown.");
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
        private readonly bool m_includeProcessTree;
        private readonly bool m_useOriginalPaths;

        private readonly Dictionary<PipId, ProcessPipExecutionPerformance> m_processPerformance =
            new Dictionary<PipId, ProcessPipExecutionPerformance>();

        private readonly Dictionary<PipId, TimeSpan> m_cpuStepDurations = new Dictionary<PipId, TimeSpan>();
        private readonly Dictionary<PipId, IReadOnlyCollection<Processes.ReportedProcess>> m_reportedProcesses = new Dictionary<PipId, IReadOnlyCollection<Processes.ReportedProcess>>();

        private string m_indent = string.Empty;

        private PathTranslator m_pathTranslator;

        /// <nodoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public PipExecutionPerformanceAnalyzer(AnalysisInput input, string outputFilePath, bool isCsvFormat, bool includeProcessTree, bool useOriginalPaths)
            : base(input)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(outputFilePath));

            m_writer = new StreamWriter(outputFilePath);
            m_isCsvFormat = isCsvFormat;
            m_includeProcessTree = includeProcessTree;
            m_useOriginalPaths = useOriginalPaths;
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

                WriteColumn($"\"{process.GetDescription(CachedGraph.Context).Replace("\"", "\"\"")}\"");
                WriteColumn(Math.Round(cpuStepDuration.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.ProcessExecutionTime.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.UserTime.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.KernelTime.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.UserTime.TotalMinutes / performance.ProcessExecutionTime.TotalMinutes, 1).ToString());
                WriteColumn(Math.Round(performance.IO.ReadCounters.TransferCount / 1024 / 1024 / 1024.0, 1).ToString());
                WriteColumn(Math.Round(performance.IO.WriteCounters.TransferCount / 1024 / 1024 / 1024.0, 1).ToString());
                WriteColumn(Math.Round(performance.IO.OtherCounters.TransferCount / 1024 / 1024 / 1024.0, 1).ToString());
                WriteColumn(performance.NumberOfProcesses.ToString());
                m_writer.Write(performance.MemoryCounters.PeakWorkingSetMb.ToString());

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
                WriteLineIndented(I($"\"semiStableHash\" : \"{Pip.FormatSemiStableHash(process.SemiStableHash)}\","));
                WriteLineIndented(I($"\"tool\" : \"{NormalizeString(process.GetToolName(CachedGraph.Context.PathTable).ToString(CachedGraph.Context.StringTable))}\","));
                WriteLineIndented(I($"\"description\" : \"{NormalizeString(process.GetDescription(CachedGraph.Context))}\","));
                WriteLineIndented(I($"\"numberOfExecutedProcesses\" : {performance.NumberOfProcesses},"));
                WriteLineIndented(I($"\"start\" : \"{performance.ExecutionStart}\","));
                WriteLineIndented(I($"\"stop\" : \"{performance.ExecutionStop}\","));
                WriteLineIndented(I($"\"result\" : \"{performance.ExecutionLevel}\","));
                WriteLineIndented(I($"\"executionTimeInMs\" : {performance.ProcessExecutionTime.TotalMilliseconds},"));
                WriteLineIndented(I($"\"userExecutionTimeInMs\" : {performance.UserTime.TotalMilliseconds},"));
                WriteLineIndented(I($"\"kernelExecutionTimeInMs\" : {performance.KernelTime.TotalMilliseconds},"));
                WriteLineIndented(I($"\"peakMemoryUsageInMb\" : {performance.MemoryCounters.PeakWorkingSetMb},"));

                WriteIOData(performance.IO);

                WriteLineIndented("\"tags\" : [");
                IncrementIndent();

                int j = 0;
                foreach (var tag in process.Tags)
                {
                    WriteLineIndented(I($"\"{NormalizeString(tag.ToString(StringTable))}\"") + (j < process.Tags.Length - 1 ? "," : string.Empty));
                    j++;
                }

                DecrementIndent();

                if (m_includeProcessTree && m_reportedProcesses.ContainsKey(process.PipId))
                {
                    WriteLineIndented("],");
                    WriteLineIndented("\"processTree\" : [");
                    IncrementIndent();
                    WriteProcessTree(process);
                    DecrementIndent();
                }

                WriteLineIndented("]");
                DecrementIndent();
                WriteLineIndented("}" + (i < m_processPerformance.Count - 1 ? "," : string.Empty));
                i++;
            }

            DecrementIndent();
            WriteLineIndented("]");

            DecrementIndent();
            WriteIndented("}");
        }


        private readonly Dictionary<uint, PooledObjectWrapper<List<Processes.ReportedProcess>>> m_childrenOf = new Dictionary<uint, PooledObjectWrapper<List<Processes.ReportedProcess>>>();
        private readonly ISet<uint> m_nonRoots = new HashSet<uint>();
        private readonly ObjectPool<List<Processes.ReportedProcess>> m_listPool = new ObjectPool<List<Processes.ReportedProcess>>(() => new List<Processes.ReportedProcess>(), l => l.Clear());
        
        private void WriteProcessTree(Process process)
        {
            var reportedProcesses = m_reportedProcesses[process.PipId];
            foreach (var p in reportedProcesses)
            {
                if (!m_childrenOf.ContainsKey(p.ParentProcessId))
                {
                    m_childrenOf.Add(p.ParentProcessId, m_listPool.GetInstance());
                }

                m_childrenOf[p.ParentProcessId].Instance.Add(p);
                m_nonRoots.Add(p.ProcessId);
            }

            // If x.ParentProcessId is not on m_roots then it must be the PID of BuildXL
            WriteLevel(reportedProcesses.Where(x => !m_nonRoots.Contains(x.ParentProcessId)).ToList());

            // Clean reusable objects
            foreach (var l in m_childrenOf.Values)
            {
                l.Dispose();
            }
            m_childrenOf.Clear();
            m_nonRoots.Clear();
        }

        private void WriteLevel(IList<Processes.ReportedProcess> level)
        {
            int i = 0;
            while (i < level.Count)
            {
                WriteLineIndented("{");
                IncrementIndent();
                var p = level[i];
                WriteLineIndented(I($"\"tool\" : \"{NormalizeString(GetToolName(p.Path))}\","));
                WriteLineIndented(I($"\"path\" : \"{NormalizeString(TranslatePaths(p.Path))}\","));
                WriteLineIndented(I($"\"commandLine\" : \"{NormalizeString(TranslatePaths(p.ProcessArgs))}\","));
                WriteIOData(p.IOCounters);
                WriteLineIndented(I($"\"creationTime\" : \"{p.CreationTime}\","));
                WriteLineIndented(I($"\"exitTime\" : \"{p.ExitTime}\","));
                WriteLineIndented(I($"\"runTimeInMs\" : \"{(p.ExitTime - p.CreationTime).TotalMilliseconds}\","));
                WriteLineIndented(I($"\"userTimeInMs\" : {p.UserTime.TotalMilliseconds},"));

                if (m_childrenOf.TryGetValue(p.ProcessId, out var children))
                {
                    WriteLineIndented(I($"\"kernelTimeInMs\" : {p.KernelTime.TotalMilliseconds},"));
                    WriteLineIndented("\"children\" : [");
                    IncrementIndent();
                    WriteLevel(children.Instance);
                    DecrementIndent();
                    WriteLineIndented("]");
                }
                else
                {
                    WriteLineIndented(I($"\"kernelTimeInMs\" : {p.KernelTime.TotalMilliseconds}"));
                }

                DecrementIndent();

                if (i < level.Count - 1)
                {
                    WriteLineIndented("},");
                }
                else
                {
                    WriteLineIndented("}");
                }

                i++;
            }
        }

        private void WriteIOData(Native.IO.IOCounters io)
        {
            WriteLineIndented("\"io\" : {");
            IncrementIndent();

            WriteLineIndented("\"read\" : {");
            IncrementIndent();
            WriteLineIndented(I($"\"operationCount\" : {io.ReadCounters.OperationCount},"));
            WriteLineIndented(I($"\"transferCountInByte\" : {io.ReadCounters.TransferCount}"));
            DecrementIndent();
            WriteLineIndented("},");

            WriteLineIndented("\"write\" : {");
            IncrementIndent();
            WriteLineIndented(I($"\"operationCount\" : {io.WriteCounters.OperationCount},"));
            WriteLineIndented(I($"\"transferCountInByte\" : {io.WriteCounters.TransferCount}"));
            DecrementIndent();
            WriteLineIndented("},");

            WriteLineIndented("\"other\" : {");
            IncrementIndent();
            WriteLineIndented(I($"\"operationCount\" : {io.OtherCounters.OperationCount},"));
            WriteLineIndented(I($"\"transferCountInByte\" : {io.OtherCounters.TransferCount}"));
            DecrementIndent();
            WriteLineIndented("}");

            DecrementIndent();
            WriteLineIndented("},");
        }

        private static string GetToolName(string path) => path.Split(new char[] { '\\', '/' }).LastOrDefault();
        
        private string TranslatePaths(string path) => m_pathTranslator?.Translate(path) ?? path;

        /// <inheritdoc />
        public override void BxlInvocation(BxlInvocationEventData data)
        {
            if (m_useOriginalPaths)
            {
                var conf = data.Configuration.Logging;
                m_pathTranslator = GetPathTranslator(conf.SubstSource, conf.SubstTarget, PathTable);
            }
        }

        private static PathTranslator GetPathTranslator(AbsolutePath substSource, AbsolutePath substTarget, PathTable pathTable)
        {
            return substTarget.IsValid && substSource.IsValid
                ? new PathTranslator(substTarget.ToString(pathTable), substSource.ToString(pathTable))
                : null;
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

        /// <inheritdoc />
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            if (data.ReportedProcesses != null && data.ReportedProcesses.Count > 0)
            {
                m_reportedProcesses[data.PipId] = data.ReportedProcesses;
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

        public static string NormalizeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
