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
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Profile;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeExtraDependenciesAnalyzer()
        {
            string outputFilePath = null;
            long? targetPip = null;
            bool sortPaths = true;
            bool verbose = false;

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
                else if (opt.Name.Equals("v", StringComparison.OrdinalIgnoreCase))
                {
                    verbose = true;
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

            return new ExtraDependenciesAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
                TargetPip = targetPip,
                SortPaths = sortPaths,
                Verbose = verbose
            };
        }

        private static void WriteExtraDependenciesAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Observed Access Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ObservedAccess), "Generates a text file containing observed files accesses when executing pips. NOTE: Requires build with /logObservedFileAccesses.");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
            writer.WriteOption("pip", "Optional. The pip which file accesses will be dumped", shortName: "p");
            writer.WriteOption("v", "Optional. Verbose output");
        }
    }

    /// <summary>
    /// Analyzer used to dump observed inputs
    /// </summary>
    internal sealed class ExtraDependenciesAnalyzer : Analyzer
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

        /// <summary>
        /// Verbose output
        /// </summary>
        public bool Verbose = false;

        private readonly Dictionary<PipId, IReadOnlyCollection<ReportedFileAccess>> m_observedAccessMap = new Dictionary<PipId, IReadOnlyCollection<ReportedFileAccess>>();

        public ExtraDependenciesAnalyzer(AnalysisInput input)
            : base(input)
        {
            Console.WriteLine($"ObservedAccessAnalyzer: Constructed at {DateTime.Now}.");
        }

        private string GetAccessPath(ReportedFileAccess access)
        {
            return (access.Path ?? access.ManifestPath.ToString(PathTable)).ToCanonicalizedPath();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            Console.WriteLine($"ObservedAccessAnalyzer: Starting analysis of {m_observedAccessMap.Count} observed access map entries at {DateTime.Now}.");

            var jsonPips = new Newtonsoft.Json.Linq.JArray();
            var pathTable = PipGraph.Context.PathTable;
            var serializer = new Newtonsoft.Json.JsonSerializer();
            serializer.Formatting = Newtonsoft.Json.Formatting.Indented;

            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            using (var writer = new StreamWriter(outputStream))
            using (var jsonTextWriter = new Newtonsoft.Json.JsonTextWriter(writer))
            {
                foreach (var observedAccess in m_observedAccessMap.OrderBy(kvp => PipGraph.GetPipFromPipId(kvp.Key).SemiStableHash))
                {
                    var pip = PipGraph.GetPipFromPipId(observedAccess.Key);

                    if (TargetPip == null || TargetPip.Value == pip.SemiStableHash)
                    {
                        var jsonPip = new Newtonsoft.Json.Linq.JObject();
                        jsonPip.Add("Description", pip.GetDescription(PipGraph.Context));

                        var jsonExtraDeps = new Newtonsoft.Json.Linq.JArray();

                        var accesses = SortPaths
                            ? observedAccess.Value.OrderBy(item => item.GetPath(PathTable))
                            : (IEnumerable<ReportedFileAccess>)observedAccess.Value;

                        if (pip.PipType != Pips.Operations.PipType.Process)
                            continue;

                        var extraDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        var processPip = (Pips.Operations.Process)pip;
                        processPip.Dependencies
                            .Select(file => file.Path.ToString(pathTable))
                            .ForEach(path => extraDependencies.Add(path));

                        var observedInputs = accesses
                            .Where(access => access.RequestedAccess.HasFlag(RequestedAccess.Read))
                            .ToList();

                        foreach (var observedInput in observedInputs)
                        {
                            var accessPath = GetAccessPath(observedInput);
                            extraDependencies.Remove(accessPath);
                        }

                        foreach (var inputFile in extraDependencies)
                        {
                            jsonExtraDeps.Add(inputFile);
                        }

                        jsonPip.Add("ExtraDeps", jsonExtraDeps);
                        jsonPips.Add(jsonPip);
                    }
                }

                serializer.Serialize(jsonTextWriter, jsonPips);
            }

            return 0;
        }

        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            m_observedAccessMap[data.PipId] = data.ReportedFileAccesses;
        }
    }
}
