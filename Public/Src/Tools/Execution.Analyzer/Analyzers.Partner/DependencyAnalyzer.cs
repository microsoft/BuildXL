// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        private const string OptIncludeDirs = "includeDirs";
        private const string OptIncludeDirsShort = "d";
        private const string OptOutFile = "outputFile";
        private const string OptOutFileShort = "o";

        public Analyzer InitializeDependencyAnalyzer()
        {
            string outputFile = null;
            bool includeDirectories = false;
            var pathMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals(OptOutFile, StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals(OptOutFileShort, StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = ParseSingletonPathOption(opt, outputFile);
                }
                else if (opt.Name.Equals("mapping", StringComparison.OrdinalIgnoreCase))
                {
                    var mapping = opt.Value.Split('=');

                    if (mapping.Length != 2)
                    {
                        throw Error($"2 strings expected on either side of the = sign {opt.Value}");
                    }

                    pathMappings[mapping[0]] = mapping[1];
                }
                else if (opt.Name.Equals(OptIncludeDirs, StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals(OptIncludeDirsShort, StringComparison.OrdinalIgnoreCase))
                {
                    includeDirectories = true;
                }
                else
                {
                    throw Error("Unknown option for DependencyAnalyzer analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(outputFile))
            {
                throw Error("Missing required argument 'outputFile'");
            }

            return new DependencyAnalyzer(GetAnalysisInput(), includeDirectories, outputFile, pathMappings);
        }

        private static void WriteDependencyAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("DependencyAnalyzer");
            writer.WriteModeOption(nameof(AnalysisMode.DependencyAnalyzer), "Gets a list of output files given a list of inputs");
            writer.WriteOption(OptOutFile, "Path to output file", shortName: OptOutFileShort);
            writer.WriteOption(
                OptIncludeDirs,
                "Whether to include directory dependencies/outputs.  Defaults to false (for backward compatibility reasons)",
                shortName: OptIncludeDirsShort);
        }
    }

    internal class DependencyAnalyzerPip
    {
        public PipId PipId;
        public string Description;
        public HashSet<AbsolutePath> InputFiles;
        public HashSet<AbsolutePath> InputDirs;
        public HashSet<AbsolutePath> OutputFiles;
        public HashSet<AbsolutePath> OutputDirs;
        public HashSet<PipId> DownstreamPips;
    }

    internal class DependencyAnalyzerOutputWriter
    {
        private readonly string m_outputFilePath;
        private readonly CachedGraph m_cachedGraph;
        private readonly uint m_graphVersion;
        private readonly IReadOnlyCollection<AbsolutePath> m_files;
        [CanBeNull] private readonly IReadOnlyCollection<AbsolutePath> m_dirs;
        private readonly IReadOnlyList<DependencyAnalyzerPip> m_pips;
        private readonly IReadOnlyDictionary<string, string> m_pathMappings;

        bool IncludeDirs => m_dirs != null;

        public DependencyAnalyzerOutputWriter(
            string outputFilePath,
            CachedGraph cachedGraph,
            uint graphVersion,
            IReadOnlyCollection<AbsolutePath> files,
            IReadOnlyCollection<AbsolutePath> dirs,
            IReadOnlyList<DependencyAnalyzerPip> pips,
            IReadOnlyDictionary<string, string> pathMappings)
        {
            m_outputFilePath = outputFilePath;
            m_cachedGraph = cachedGraph;
            m_graphVersion = graphVersion;
            m_files = files;
            m_dirs = dirs;
            m_pips = pips;
            m_pathMappings = pathMappings;
        }

        public void Write()
        {
            using (var streamWriter = new StreamWriter(m_outputFilePath))
            {
                streamWriter.WriteLine($"GraphVersion:{m_graphVersion}");
                DumpOutPathMappings(streamWriter);
                DumpOutFiles(streamWriter);
                DumpOutPips(streamWriter);
            }
        }

        private void DumpOutFiles(StreamWriter streamWriter)
        {
            var pathExpander = m_cachedGraph.PipGraph.SemanticPathExpander;

            streamWriter.WriteLine($"NumFiles:{m_files.Count}");
            DumpPaths(m_files);

            if (IncludeDirs)
            {
                streamWriter.WriteLine($"NumDirs:{m_dirs.Count}");
                DumpPaths(m_dirs);
            }

            void DumpPaths(IEnumerable<AbsolutePath> paths)
            {
                foreach (var path in paths)
                {
                    var stringPath = pathExpander.ExpandPath(m_cachedGraph.Context.PathTable, path);
                    streamWriter.WriteLine($"{path.RawValue} {stringPath}");
                }
            }
        }

        private void DumpOutNumsList(IEnumerable<uint> entries, string title, StreamWriter streamWriter)
        {
            streamWriter.Write($"{title}:[");
            foreach (var entry in entries)
            {
                streamWriter.Write(entry);
                streamWriter.Write(',');
            }
            streamWriter.WriteLine(']');
        }

        private void DumpOutPips(StreamWriter streamWriter)
        {
            streamWriter.WriteLine($"NumPips:{m_pips.Count}");

            foreach (var pip in m_pips)
            {
                streamWriter.WriteLine($"ID:{pip.PipId.Value}");
                streamWriter.WriteLine($"Description:{pip.Description}");
                DumpOutNumsList(pip.InputFiles.Select(f => (uint)(f.RawValue)), "InputFiles", streamWriter);
                if (IncludeDirs)
                {
                    DumpOutNumsList(pip.InputDirs.Select(d => (uint)(d.RawValue)), "InputDirs", streamWriter);
                }
                DumpOutNumsList(pip.OutputFiles.Select(f => (uint)(f.RawValue)), "OutputFiles", streamWriter);
                if (IncludeDirs)
                {
                    DumpOutNumsList(pip.OutputDirs.Select(d => (uint)(d.RawValue)), "OutputDirs", streamWriter);
                }
                DumpOutNumsList(pip.DownstreamPips.Select(downstreamPip => downstreamPip.Value), "DownstreamPips", streamWriter);
            }
        }

        private void DumpOutPathMappings(StreamWriter streamWriter)
        {
            streamWriter.WriteLine($"NumPathMappings:{m_pathMappings.Count}");

            foreach (var pathMapping in m_pathMappings)
            {
                streamWriter.WriteLine($"{pathMapping.Key}={pathMapping.Value}");
            }
        }
    }

    internal class DependencyAnalyzer : Analyzer
    {
        private const uint OutputGraphVersion = 1;

        // The inputs which control the analyzer
        private readonly bool m_includeDirs;
        private readonly string m_outputFilePath;
        private readonly Dictionary<string, string> m_pathMappings;

        // Data used by the analyzer to do its job
        private HashSet<NodeId> m_nodesWithObservedInputs = new HashSet<NodeId>();
        private ConcurrentDenseIndex<List<AbsolutePath>> m_fingerprintComputations = new ConcurrentDenseIndex<List<AbsolutePath>>(false);
        private readonly HashSet<AbsolutePath> m_allFiles = new HashSet<AbsolutePath>();
        private readonly HashSet<AbsolutePath> m_allDirs = new HashSet<AbsolutePath>();
        private readonly List<DependencyAnalyzerPip> m_allPips = new List<DependencyAnalyzerPip>();
        private readonly ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>> m_directoryContents = new ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>>();

        public DependencyAnalyzer(AnalysisInput input, bool includeDirs, string outputFilePath, Dictionary<string, string> pathMappings) : base(input)
        {
            m_outputFilePath = outputFilePath;
            m_pathMappings = pathMappings;
            m_includeDirs = includeDirs;
        }

        public override int Analyze()
        {
            // By the time we get here, observed inputs collection is finished. So calling the garbage collector
            // before starting the main pip loop which uses a lot of memory.
            GC.Collect();

            PopulatePipsAndFilesInGraph();

            // Free up some memory before we dump out the graph
            m_nodesWithObservedInputs = null;
            m_fingerprintComputations = null;
            GC.Collect();

            var outputWriter = new DependencyAnalyzerOutputWriter(
                m_outputFilePath,
                CachedGraph,
                OutputGraphVersion,
                m_allFiles,
                m_includeDirs ? m_allDirs : null,
                m_allPips,
                m_pathMappings);
            outputWriter.Write();

            return 0;
        }

        private void GetDeclaredInputsForPip(Pip pip, HashSet<AbsolutePath> inputPaths)
        {
            if (pip.PipType == PipType.Process)
            {
                var processPip = pip as Process;
                inputPaths.UnionWith(processPip.Dependencies.Select(x => x.Path));
            }
            else if (pip.PipType == PipType.CopyFile)
            {
                var copyFilePip = pip as CopyFile;
                inputPaths.Add(copyFilePip.Source.Path);
            }

            // No other pip types have declared inputs
        }

        private void GetObservedInputsForPip(Pip pip, HashSet<AbsolutePath> inputPaths)
        {
            var pipId = pip.PipId;
            var nodeId = pipId.ToNodeId();

            if (m_nodesWithObservedInputs.Contains(nodeId))
            {
                inputPaths.UnionWith(m_fingerprintComputations[pip.PipId.Value]);
            }
        }

        private void GetOutputFilesForPip(Pip pip, HashSet<AbsolutePath> outputPaths)
        {
            if (pip.PipType == PipType.Process)
            {
                var processPip = pip as Process;
                outputPaths.UnionWith(processPip.FileOutputs.Select(x => x.Path));
            }
            else if (pip.PipType == PipType.CopyFile)
            {
                var copyFilePip = pip as CopyFile;
                outputPaths.Add(copyFilePip.Destination.Path);
            }
            else if (pip.PipType == PipType.WriteFile)
            {
                var writeFilePip = pip as WriteFile;
                outputPaths.Add(writeFilePip.Destination.Path);
            }

            // No other pip types have declared outputs
        }

        private bool IsRelevantPipType(PipId pipId)
        {
            PipType pipType = CachedGraph.PipTable.GetPipType(pipId);
            return (pipType == PipType.Process) || (pipType == PipType.CopyFile) || (pipType == PipType.WriteFile);
        }

        private string GetPipDescription(Pip pip)
        {
            // Ensure that the description is a single line because that's what the parser
            // of this output expects it to be
            return pip.GetDescription(CachedGraph.Context)
                .Replace("\r", string.Empty)
                .Replace('\n', ' ');
        }

        /// <summary>
        /// Returns the set of downstream pips. Pips will be of one of the following types:
        /// Process, CopyFile, WriteFile
        /// </summary>
        /// <param name="pip"></param>
        private HashSet<PipId> GetDownstreamPips(Pip rootPip)
        {
            var pipTable = CachedGraph.PipTable;

            var pipsEnumerable = CachedGraph
                .DataflowGraph
                .GetOutgoingEdges(rootPip.PipId.ToNodeId())
                .Select(edge => edge.OtherNode)
                .Select(nodeId => nodeId.ToPipId())
                .Where(IsRelevantPipType);

            return new HashSet<PipId>(pipsEnumerable);
        }

        private void PopulatePipsAndFilesInGraph()
        {
            var pips = CachedGraph
                .PipTable
                .Keys
                .Where(IsRelevantPipType)
                .Select(CachedGraph.PipGraph.GetPipFromPipId);

            foreach (var pip in pips)
            {
                var allInputs = new HashSet<AbsolutePath>();
                var allOutputs = new HashSet<AbsolutePath>();

                GetDeclaredInputsForPip(pip, allInputs);
                GetObservedInputsForPip(pip, allInputs);
                GetOutputFilesForPip(pip, allOutputs);

                var downstreamPips = GetDownstreamPips(pip);

                var process = pip as Process;

                var inputDirs = (process != null && m_includeDirs) ?
                    new HashSet<AbsolutePath>(process.DirectoryDependencies.Select(x => x.Path)) :
                    new HashSet<AbsolutePath>();

                var outputDirs = (process != null && m_includeDirs) ?
                    new HashSet<AbsolutePath>(process.DirectoryOutputs.Select(x => x.Path)) :
                    new HashSet<AbsolutePath>();

                // Get all file members of opaque directory outputs, and add them to the
                // output files list of this pip
                if (process != null)
                {
                    var outputFilesInOpaques = process.DirectoryOutputs.SelectMany(dir =>
                    {
                        if (m_directoryContents.TryGetValue(dir, out var fileMembers))
                        {
                            return fileMembers;
                        }
                        else
                        {
                            return Enumerable.Empty<FileArtifact>();
                        }
                    })
                    .Select(fileArtifact => fileArtifact.Path);

                    allOutputs.UnionWith(outputFilesInOpaques);
                }

                m_allFiles.UnionWith(allInputs);
                m_allFiles.UnionWith(allOutputs);

                m_allDirs.UnionWith(inputDirs);
                m_allDirs.UnionWith(outputDirs);

                var dependencyAnalyzerPip = new DependencyAnalyzerPip
                {
                    PipId = pip.PipId,
                    Description = GetPipDescription(pip),
                    InputFiles = allInputs,
                    InputDirs = inputDirs,
                    OutputFiles = allOutputs,
                    OutputDirs = outputDirs,
                    DownstreamPips = downstreamPips,
                };

                m_allPips.Add(dependencyAnalyzerPip);
            }
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            var lastStrongFingerprintComputation = data
                .StrongFingerprintComputations
                .LastOrDefault();

            if (lastStrongFingerprintComputation.ObservedInputs.IsValid)
            {
                m_nodesWithObservedInputs.Add(data.PipId.ToNodeId());
                m_fingerprintComputations[data.PipId.Value] = lastStrongFingerprintComputation
                    .ObservedInputs
                    .Where(x => x.Type == ObservedInputType.FileContentRead)
                    .Select(observedInput => observedInput.Path)
                    .ToList();
            }
        }

        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var item in data.DirectoryOutputs)
            {
                m_directoryContents[item.directoryArtifact] = item.fileArtifactArray;
            }
        }
    }
}
