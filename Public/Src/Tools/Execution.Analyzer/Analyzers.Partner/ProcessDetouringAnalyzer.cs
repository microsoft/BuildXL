// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Analyzer InitializeProcessDetouringStatusAnalyzer()
        {
            string outputFilePath = null;
            long? semistableHash = 0;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.StartsWith("pip", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("p", StringComparison.OrdinalIgnoreCase))
                {
                    semistableHash = ParseSemistableHash(opt);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new ProcessDetouringAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
                TargetSemiStableHash = semistableHash == null ? 0L : semistableHash.Value,
            };
        }

        private static void WriteProcessDetouringHelp(HelpWriter writer)
        {
            writer.WriteBanner("Process Detoring Status Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ProcessDetouringStatus), "Generates a text file containing ProcessDetouringStatusData as discovered at build progress");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
            writer.WriteOption("pip", "Optional. The pip for which the report to be created", shortName: "p");
        }
    }

    /// <summary>
    /// Analyzer used to dump observed inputs
    /// </summary>
    internal sealed class ProcessDetouringAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;
        public long TargetSemiStableHash;

        private readonly Dictionary<PipId, IReadOnlyCollection<ProcessDetouringStatusData>> m_processDetouringsMap = new Dictionary<PipId, IReadOnlyCollection<ProcessDetouringStatusData>>();

        public ProcessDetouringAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (var writer = new StreamWriter(outputStream))
                {
                    foreach (var processDetouringSet in m_processDetouringsMap.OrderBy(kvp => PipGraph.GetPipFromPipId(kvp.Key).SemiStableHash))
                    {
                        if (processDetouringSet.Value != null)
                        {
                            foreach (var processDetouring in processDetouringSet.Value)
                            {
                                if (TargetSemiStableHash == 0 || processDetouringSet.Key.Value == TargetSemiStableHash)
                                {
                                    writer.WriteLine("{0})", PipGraph.GetPipFromPipId(processDetouringSet.Key).GetDescription(PipGraph.Context));
                                    writer.WriteLine("    ProcessName = {0}", processDetouring.ProcessName);
                                    writer.WriteLine("    ProcessId = {0}", processDetouring.ProcessId);
                                    writer.WriteLine("    ReportStatus = {0}", processDetouring.ReportStatus);
                                    writer.WriteLine("    StartApplicationName = {0}", processDetouring.StartApplicationName);

                                    writer.WriteLine("    StartCommandLine = {0}", processDetouring.StartCommandLine);
                                    writer.WriteLine("    NeedsInjection = {0}", processDetouring.NeedsInjection);
                                    writer.WriteLine("    Job = {0}", processDetouring.Job);
                                    writer.WriteLine("    DisableDetours = {0}", processDetouring.DisableDetours);
                                    writer.WriteLine("    CreationFlags = {0}", processDetouring.CreationFlags);

                                    writer.WriteLine("    Detoured = {0}", processDetouring.Detoured);
                                    writer.WriteLine("    Error = {0}", processDetouring.Error);
                                    writer.WriteLine("    CreateProcessStatusReturn = {0}", processDetouring.CreateProcessStatusReturn);

                                    writer.WriteLine();
                                }
                            }
                        }
                    }
                }
            }

            return 0;
        }

        /// <inheritdoc />
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            m_processDetouringsMap[data.PipId] = data.ProcessDetouringStatuses;
        }
    }
}
