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
using BuildXL.Utilities.Core;
using Microsoft.VisualStudio.Services.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using PassThroughFileSystem = BuildXL.Cache.ContentStore.FileSystem.PassThroughFileSystem;

/**
Sample output of the analyzer:
[
  {
    "Description": "Pip1FE925D181A2CF1A, cl.exe, foo, bar1, {} || {Compiling}",
    "ExtraDeps": []
  },
  {
    "Description": "Pip21EE1BBEBFC1FD1A, link.exe, foo, bar2, {} || {Linking}",
    "ExtraDeps": [
      "E:\\visualcpptools\\lib\\native\\bin\\hostx64\\x64\\lib.exe",
      "E:\\visualcpptools\\lib\\native\\bin\\hostx64\\x64\\1033\\linkui.dll"
    ]
  }
]
 */

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
            writer.WriteModeOption(nameof(AnalysisMode.ExtraDependencies), $"Generates a json file containing extra dependencies specified in pips. NOTE: Requires build with /logObservedFileAccesses. {ExtraDependenciesAnalyzer.FilesystemWarning}");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
            writer.WriteOption("pip", "Optional. The pip for which extra dependencies will be dumped", shortName: "p");
        }
    }

    /// <summary>
    /// This represents a map of file path to a number which uniquely identifies that file.
    /// All hardlinks and softlinks to a file will map to the same number.
    /// The file ID computation is cached by this class to reduce filesystem API call overhead.
    /// </summary>
    internal sealed class FilePathIdMap
    {
        private readonly IDictionary<string, ulong> m_pathToIdMap = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        private readonly PassThroughFileSystem m_passThroughFileSystem = new PassThroughFileSystem();

        public ulong GetFileId(string filePath)
        {
            ulong fileId;
            if (!m_pathToIdMap.TryGetValue(filePath, out fileId))
            {
                try
                {
                    fileId = m_passThroughFileSystem.GetFileId(new AbsolutePath(filePath));
                }
                catch (ArgumentException)
                {
                    // Sometimes paths are malformed, starting with \\?\, and we ignore those paths
                    // in our analysis
                    fileId = 0;
                }
                catch (FileNotFoundException)
                {
                    // Sometimes we see temporary files created by BuildXL which get deleted by the time this
                    // analyzer runs, so we ignore them
                    fileId = 0;
                }
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

        public static readonly string FilesystemWarning = "This analyzer must be run on the same machine where the build was run because it looks at the files which were part of the build.";

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
            Console.WriteLine($"NOTE: {FilesystemWarning}");
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
                    {
                        continue;
                    }

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

                        // extraDependencies = pipInputFiles - observedFileReads
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
