// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeReportedProcessesAnalyzer()
        {
            string listFilePath = null;
            string summaryFilePath = null;
            long? semistableHash = 0;
            bool caseInsensitive = false;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("listFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("l", StringComparison.OrdinalIgnoreCase))
                {
                    listFilePath = ParseSingletonPathOption(opt, listFilePath);
                }
                else if (opt.Name.StartsWith("summaryFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("s", StringComparison.OrdinalIgnoreCase))
                {
                    summaryFilePath = ParseSingletonPathOption(opt, summaryFilePath);
                }
                else if (opt.Name.StartsWith("pip", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("p", StringComparison.OrdinalIgnoreCase))
                {
                    semistableHash = ParseSemistableHash(opt);
                }
                else if (opt.Name.StartsWith("caseInsensitive", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase))
                {
                    caseInsensitive = true;
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new ReportedProcessesAnalyzer(GetAnalysisInput())
            {
                ListFilePath = listFilePath,
                SummaryFilePath = summaryFilePath,
                CaseInsensitive = caseInsensitive,
                TargetSemiStableHash = semistableHash == null ? 0L : semistableHash.Value,
            };
        }

        private static void WriteReportedProcessesHelp(HelpWriter writer)
        {
            writer.WriteBanner("Process Reported Processes Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ProcessDetouringStatus), "Generates a text file about the processes executed during a build that used the /logProcesses option.");
            writer.WriteOption("listFile", "Optional. The file to list out all the processes.", shortName: "l");
            writer.WriteOption("summaryFile", "Optional. A file to summarize statistics by unique process path.", shortName: "l");
            writer.WriteOption("caseInsensitive", "Optional. Summarize using lower case paths (i.e.: Windows path are case insensitive).", shortName: "c");
            writer.WriteOption("pip", "Optional. Only output for a given pip", shortName: "p");
        }
    }

    class PathSummary
    {
        public HashSet<PipId> Pips = new HashSet<PipId>();
        public long Count = 0;
        public long ZeroExitCodeCount = 0;
        public TimeSpan WallClockTime = TimeSpan.Zero;
        public TimeSpan KernelTime = TimeSpan.Zero;
        public TimeSpan UserTime = TimeSpan.Zero;
    }

    internal sealed class ReportedProcessesAnalyzer : Analyzer
    {
        public string ListFilePath;
        public string SummaryFilePath;
        public long TargetSemiStableHash;
        public bool CaseInsensitive;

        private readonly Dictionary<PipId, IReadOnlyCollection<ReportedProcess>> m_reportedProcesses = new Dictionary<PipId, IReadOnlyCollection<ReportedProcess>>();
        private readonly Dictionary<string, PathSummary> m_summaryByPath = new Dictionary<string, PathSummary>();

        public ReportedProcessesAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
        {
            return eventId == ExecutionEventId.ProcessExecutionMonitoringReported;
        }

        public override int Analyze()
        {
            if (ListFilePath != null)
            {
                using (var outputStream = File.Create(ListFilePath, bufferSize: 64 << 10 /* 64 KB */))
                {
                    using (var writer = new StreamWriter(outputStream))
                    {
                        foreach (var reportedProcesses in m_reportedProcesses.OrderBy(kvp => PipGraph.GetPipFromPipId(kvp.Key).SemiStableHash))
                        {
                        	var pipDescription = PipGraph.GetPipFromPipId(reportedProcesses.Key).GetDescription(PipGraph.Context);
                            if (reportedProcesses.Value != null)
                            {
                                foreach (var reportedProcess in reportedProcesses.Value)
                                {
                                    if (TargetSemiStableHash == 0 || reportedProcesses.Key.Value == TargetSemiStableHash)
                                    {
                                        writer.WriteLine("{0}", pipDescription);
                                        writer.WriteLine("    Path = {0}", reportedProcess.Path);
                                        writer.WriteLine("    ProcessId = {0}", reportedProcess.ProcessId);
                                        writer.WriteLine("    ParentProcessId = {0}", reportedProcess.ProcessId);
                                        writer.WriteLine("    ProcessArgs = {0}", reportedProcess.ProcessArgs);
                                        writer.WriteLine("    ExitCode = {0}", reportedProcess.ExitCode);

                                        writer.WriteLine("    CreationTime = {0}", reportedProcess.CreationTime);
                                        writer.WriteLine("    ExitTime = {0}", reportedProcess.ExitTime);
                                        writer.WriteLine("    KernelTime = {0}", reportedProcess.KernelTime);
                                        writer.WriteLine("    UserTime = {0}", reportedProcess.UserTime);

                                        writer.WriteLine("    Read.Operations = {0}", reportedProcess.IOCounters.ReadCounters.OperationCount);
                                        writer.WriteLine("    Read.Bytes = {0}", reportedProcess.IOCounters.ReadCounters.TransferCount);
                                        writer.WriteLine("    Write.Operations = {0}", reportedProcess.IOCounters.WriteCounters.OperationCount);
                                        writer.WriteLine("    Write.Bytes = {0}", reportedProcess.IOCounters.WriteCounters.TransferCount);
                                        writer.WriteLine("    Other.Operations = {0}", reportedProcess.IOCounters.OtherCounters.OperationCount);
                                        writer.WriteLine("    Other.Bytes = {0}", reportedProcess.IOCounters.OtherCounters.TransferCount);

                                        writer.WriteLine();
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (SummaryFilePath != null)
            {
                using (var outputStream = File.Create(SummaryFilePath, bufferSize: 64 << 10 /* 64 KB */))
                {
                    using (var writer = new StreamWriter(outputStream))
                    {
                        foreach (var pathSummary in m_summaryByPath.OrderBy(kvp => kvp.Key))
                        {
                            writer.WriteLine("{0}", pathSummary.Key);
                            writer.WriteLine("    PipCount = {0}", pathSummary.Value.Pips.Count());
                            writer.WriteLine("    Count = {0}", pathSummary.Value.Count);
                            writer.WriteLine("    ZeroExitCodeCount = {0}", pathSummary.Value.ZeroExitCodeCount);
                            writer.WriteLine("    WallClockTime = {0}", pathSummary.Value.WallClockTime);
                            writer.WriteLine("    KernelTime = {0}", pathSummary.Value.KernelTime);
                            writer.WriteLine("    UserTime = {0}", pathSummary.Value.UserTime);
                            writer.WriteLine();
                        }
                    }
                }
            }

            return 0;
        }

        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            if (ListFilePath != null)
            {
                m_reportedProcesses[data.PipId] = data.ReportedProcesses;
            }

            if (SummaryFilePath != null)
            {
                foreach (var reportedProcess in data.ReportedProcesses)
                {
                    var pathKey = CaseInsensitive ? reportedProcess.Path.ToLowerInvariant() : reportedProcess.Path;
                    PathSummary pathSummary;
                    if (!m_summaryByPath.TryGetValue(pathKey, out pathSummary))
                    {
                        pathSummary = m_summaryByPath[pathKey] = new PathSummary();
                    }

                    pathSummary.Pips.Add(data.PipId);
                    pathSummary.Count++;
                    pathSummary.ZeroExitCodeCount += (reportedProcess.ExitCode == 0 ? 1 : 0);
                    pathSummary.WallClockTime += (reportedProcess.ExitTime - reportedProcess.CreationTime);
                    pathSummary.KernelTime += reportedProcess.KernelTime;
                    pathSummary.UserTime += reportedProcess.UserTime;
                }
            }
        }
    }
}
