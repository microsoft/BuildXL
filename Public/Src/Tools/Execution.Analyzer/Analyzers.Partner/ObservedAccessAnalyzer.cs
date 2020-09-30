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
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeObservedAccessAnalyzer()
        {
            string outputFilePath = null;
            long? targetPip = null;
            bool sortPaths = true;

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
                    targetPip = ParseSemistableHash(opt);
                }
                else if (opt.Name.TrimEnd('-', '+').Equals("sortPaths", StringComparison.OrdinalIgnoreCase))
                {
                    sortPaths = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            return new ObservedAccessAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
                TargetPip = targetPip,
                SortPaths = sortPaths
            };
        }

        private static void WriteObservedAccessHelp(HelpWriter writer)
        {
            writer.WriteBanner("Observed Access Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ObservedAccess), "Generates a text file containing observed files accesses when executing pips. NOTE: Requires build with /logObservedFileAccesses.");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
            writer.WriteOption("pip", "Optional. The pip which file accesses will be dumped", shortName: "p");
        }
    }

    /// <summary>
    /// Analyzer used to dump observed inputs
    /// </summary>
    internal sealed class ObservedAccessAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        /// <summary>
        /// The target pip for wich to dump the data.
        /// </summary>
        public long? TargetPip;

        /// <summary>
        /// Order accesses by path.
        /// </summary>
        public bool SortPaths = true;

        private readonly Dictionary<PipId, IReadOnlyCollection<ReportedFileAccess>> m_observedAccessMap = new Dictionary<PipId, IReadOnlyCollection<ReportedFileAccess>>();

        public ObservedAccessAnalyzer(AnalysisInput input)
            : base(input)
        {
            Console.WriteLine($"ObservedAccessAnalyzer: Constructed at {DateTime.Now}.");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            Console.WriteLine($"ObservedAccessAnalyzer: Starting analysis of {m_observedAccessMap.Count} observed access map entries at {DateTime.Now}.");

            int totalAccesses = 0;

            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (var writer = new StreamWriter(outputStream))
                {
                    foreach (var observedAccess in m_observedAccessMap.OrderBy(kvp => PipGraph.GetPipFromPipId(kvp.Key).SemiStableHash))
                    {
                        if (TargetPip == null || TargetPip.Value == PipGraph.GetPipFromPipId(observedAccess.Key).SemiStableHash)
                        {
                            writer.WriteLine("{0})", PipGraph.GetPipFromPipId(observedAccess.Key).GetDescription(PipGraph.Context));
                            writer.WriteLine("    ObservedAccessByPath:{0}", observedAccess.Value.Count);
                            var accesses = SortPaths 
                                ? observedAccess.Value.OrderBy(item => item.GetPath(PathTable))
                                : (IEnumerable<ReportedFileAccess>)observedAccess.Value;
                            foreach (var access in accesses)
                            {
                                writer.WriteLine("    Path = {0}", (access.Path ?? access.ManifestPath.ToString(PathTable)).ToCanonicalizedPath());
                                writer.WriteLine("    {0}", access.Describe());
                                totalAccesses++;
                            }

                            writer.WriteLine();
                        }
                    }
                }
            }

            Console.WriteLine($"ObservedAccessAnalyzer: Dumped information on {m_observedAccessMap.Count} observed access map entries and {totalAccesses} total accesses at {DateTime.Now}.");

            return 0;
        }

        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            m_observedAccessMap[data.PipId] = data.ReportedFileAccesses;
        }
    }
}
