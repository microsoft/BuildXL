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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeExtraDependenciesAnalyzer()
        {
            string outputFilePath = null;
            long? targetPip = null;

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
                else
                {
                    throw Error("Unknown option: {0}", opt.Name);
                }
            }

            return new ExtraDependenciesAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
                TargetPip = targetPip,
            };
        }

        private static void WriteExtraDependenciesAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Extra Dependencies Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ExtraDependencies), "Generates a json file containing extra dependencies specified in pips. NOTE: Requires build with /logObservedFileAccesses.");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
            writer.WriteOption("pip", "Optional. The pip for which extra dependencies will be dumped", shortName: "p");
        }
    }

    internal sealed class FilePathIdMap
    {
        private readonly IDictionary<string, ulong> m_pathToIdMap = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        private readonly Cache.ContentStore.FileSystem.PassThroughFileSystem m_passThroughFileSystem = new Cache.ContentStore.FileSystem.PassThroughFileSystem();

        public ulong GetFileId(string filePath)
        {
            ulong fileId;
            if (!m_pathToIdMap.TryGetValue(filePath, out fileId))
            {
                fileId = m_passThroughFileSystem.GetFileId(new BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath(filePath));
                m_pathToIdMap.Add(filePath, fileId);
            }

            return fileId;
        }
    }

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

        private readonly Dictionary<PipId, IReadOnlyCollection<ReportedFileAccess>> m_observedAccessMap = new Dictionary<PipId, IReadOnlyCollection<ReportedFileAccess>>();

        private readonly FilePathIdMap m_filePathIdMap = new FilePathIdMap();

        public ExtraDependenciesAnalyzer(AnalysisInput input)
            : base(input)
        {
            Console.WriteLine($"ExtraDependenciesAnalyzer: Constructed at {DateTime.Now}.");
        }

        private string GetAccessPath(ReportedFileAccess access)
        {
            return (access.Path ?? access.ManifestPath.ToString(PathTable)).ToCanonicalizedPath();
        }

        public override int Analyze()
        {
            Console.WriteLine($"ExtraDependenciesAnalyzer: Starting analysis of {m_observedAccessMap.Count} observed access map entries at {DateTime.Now}.");

            var jsonPips = new JArray();
            var pathTable = PipGraph.Context.PathTable;
            var jsonSerializer = new JsonSerializer();
            jsonSerializer.Formatting = Formatting.Indented;

            using (var outputFileStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            using (var outputFileStreamWriter = new StreamWriter(outputFileStream))
            using (var jsonTextWriter = new JsonTextWriter(outputFileStreamWriter))
            {
                foreach (var observedAccess in m_observedAccessMap.OrderBy(kvp => PipGraph.GetPipFromPipId(kvp.Key).SemiStableHash))
                {
                    var pip = PipGraph.GetPipFromPipId(observedAccess.Key);

                    if (pip.PipType != Pips.Operations.PipType.Process)
                        continue;

                    if (TargetPip == null || TargetPip.Value == pip.SemiStableHash)
                    {
                        var jsonPip = new JObject();
                        jsonPip.Add("Description", pip.GetDescription(PipGraph.Context));

                        var jsonExtraDeps = new JArray();

                        var accesses = observedAccess.Value;
                        var processPip = (Pips.Operations.Process)pip;

                        var pipInputFiles = processPip.Dependencies
                            .Select(file =>
                            {
                                var filePath = file.Path.ToString(pathTable);
                                return new KeyValuePair<string, ulong>(filePath, m_filePathIdMap.GetFileId(filePath));
                            });

                        var observedFileReads = accesses
                            .Where(access => access.RequestedAccess.HasFlag(RequestedAccess.Read))
                            .Select(GetAccessPath)
                            .Select(m_filePathIdMap.GetFileId)
                            .ToHashSet();

                        var extraDependencies = new List<string>();

                        foreach (var pipInputFile in pipInputFiles)
                        {
                            if (!observedFileReads.Contains(pipInputFile.Value))
                            {
                                extraDependencies.Add(pipInputFile.Key);
                            }
                        }

                        extraDependencies.Sort(StringComparer.OrdinalIgnoreCase);

                        foreach (var extraDep in extraDependencies)
                        {
                            jsonExtraDeps.Add(extraDep);
                        }

                        jsonPip.Add("ExtraDeps", jsonExtraDeps);
                        jsonPips.Add(jsonPip);
                    }
                }

                jsonSerializer.Serialize(jsonTextWriter, jsonPips);
            }

            Console.WriteLine($"ExtraDependenciesAnalyzer: Finished analysis at {DateTime.Now}.");

            return 0;
        }

        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            m_observedAccessMap[data.PipId] = data.ReportedFileAccesses;
        }
    }
}
