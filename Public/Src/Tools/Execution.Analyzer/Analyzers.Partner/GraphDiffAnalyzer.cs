// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeGraphDiffAnalyzer()
        {
            string outputDirectory = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = ParseSingletonPathOption(opt, outputDirectory);
                }
                else
                {
                    throw Error("Unknown option for DependencyAnalyzer analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw Error("Missing required argument 'outputDirectory'");
            }

            Console.WriteLine("Loaded both cached graphs");

            return new GraphDiffAnalyzer(m_analysisInput, m_analysisInputOther, outputDirectory);
        }

        private static void WriteGraphDiffAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("GraphDiffAnalyzer");
            writer.WriteModeOption(nameof(AnalysisMode.GraphDiffAnalyzer), "Diffs 2 PIP graphs");
            writer.WriteOption("outputDirectory", "Path to the output diff", shortName: "o");
            writer.WriteOption("xl", "Two of these are required. Paths to first and second XLGs.");
        }
    }

    internal class GraphDiffAnalyzer : Analyzer
    {
        // The inputs which control the analyzer
        private readonly string m_outputDirectoryPath;
        private AnalysisInput m_firstGraphAnalysisInput;
        private AnalysisInput m_secondGraphAnalysisInput;

        public GraphDiffAnalyzer(AnalysisInput firstGraphAnalysisInput, AnalysisInput secondGraphAnalysisInput, string outputDirectoryPath) : base(secondGraphAnalysisInput)
        {
            m_outputDirectoryPath = outputDirectoryPath;
            m_firstGraphAnalysisInput = firstGraphAnalysisInput;
            m_secondGraphAnalysisInput = secondGraphAnalysisInput;

            EnsureEmptyOutputDirectory();
        }

        public override int Analyze()
        {
            Console.WriteLine("Diffing pips");

            Dictionary<AbsolutePath, PipId> firstGraphOutputToPip = null;
            Dictionary<AbsolutePath, PipId> secondGraphOutputToPip = null;
            Parallel.Invoke(
                () =>
                {
                    firstGraphOutputToPip = GetOutputToPip(m_firstGraphAnalysisInput.CachedGraph);
                },
                () =>
                {
                    secondGraphOutputToPip = GetOutputToPip(m_secondGraphAnalysisInput.CachedGraph);
                }
            );

            var firstGraphPathExpander = m_firstGraphAnalysisInput.CachedGraph.PipGraph.SemanticPathExpander;
            var secondGraphPathExpander = m_secondGraphAnalysisInput.CachedGraph.PipGraph.SemanticPathExpander;

            var matchingPips = new HashSet<KeyValuePair<PipId, PipId>>();
            var firstGraphMatchedPips = new HashSet<PipId>();
            var secondGraphMatchedPips = new HashSet<PipId>();

            var firstGraphPipsWithUniqueOutputs = new HashSet<PipId>();
            var secondGraphPipsWithUniqueOutputs = new HashSet<PipId>();

            // - Find the unique pips in the first graph
            // - Find the potentially matching pips in first and second graphs
            foreach (var firstGraphEntry in firstGraphOutputToPip)
            {
                var outputFilePath = firstGraphPathExpander.ExpandPath(m_firstGraphAnalysisInput.CachedGraph.Context.PathTable, firstGraphEntry.Key);

                AbsolutePath secondGraphOutputFile;
                if (!AbsolutePath.TryGet(m_secondGraphAnalysisInput.CachedGraph.Context.PathTable, outputFilePath, out secondGraphOutputFile))
                {
                    firstGraphPipsWithUniqueOutputs.Add(firstGraphEntry.Value);
                    continue;
                }

                PipId secondGraphMatchingPip;
                if (!secondGraphOutputToPip.TryGetValue(secondGraphOutputFile, out secondGraphMatchingPip))
                {
                    firstGraphPipsWithUniqueOutputs.Add(firstGraphEntry.Value);
                    continue;
                }

                matchingPips.Add(new KeyValuePair<PipId, PipId>(firstGraphEntry.Value, secondGraphMatchingPip));
                firstGraphMatchedPips.Add(firstGraphEntry.Value);
                secondGraphMatchedPips.Add(secondGraphMatchingPip);
            }

            // Save some memory
            firstGraphOutputToPip = null;

            // Find the unique pips in the second graph
            foreach (var secondGraphEntry in secondGraphOutputToPip)
            {
                var outputFilePath = secondGraphPathExpander.ExpandPath(m_secondGraphAnalysisInput.CachedGraph.Context.PathTable, secondGraphEntry.Key);

                AbsolutePath firstGraphOutputFile;
                if (!AbsolutePath.TryGet(m_firstGraphAnalysisInput.CachedGraph.Context.PathTable, outputFilePath, out firstGraphOutputFile))
                {
                    secondGraphPipsWithUniqueOutputs.Add(secondGraphEntry.Value);
                    continue;
                }
            }

            // Save some memory
            secondGraphOutputToPip = null;

            // We only care about the pips which are brand new.
            Parallel.Invoke(
                () =>
                {
                    firstGraphPipsWithUniqueOutputs.ExceptWith(firstGraphMatchedPips);
                },
                () =>
                {
                    secondGraphPipsWithUniqueOutputs.ExceptWith(secondGraphMatchedPips);
                }
            );

            var differentPipPairs = FindDifferentPipPairs(matchingPips);

            var diffSummaryFilePath = Path.Combine(m_outputDirectoryPath, "diff_summary.json");

            Console.WriteLine($"Writing the diff summary to {diffSummaryFilePath}");

            // This writes a summary to get a high level overview about diffs
            using (var diffSummaryWriter = new GraphDiffSummaryWriter(diffSummaryFilePath))
            {
                diffSummaryWriter.WriteFirstGraphUniquePips(firstGraphPipsWithUniqueOutputs, m_firstGraphAnalysisInput.CachedGraph);
                diffSummaryWriter.WriteSecondGraphUniquePips(secondGraphPipsWithUniqueOutputs, m_secondGraphAnalysisInput.CachedGraph);
                diffSummaryWriter.WriteNumberOfSamePips(matchingPips.Count - differentPipPairs.Count);
                diffSummaryWriter.WriteNumberOfDifferentPips(differentPipPairs.Count);
                diffSummaryWriter.WriteDifferentPips(differentPipPairs, m_firstGraphAnalysisInput.CachedGraph, m_secondGraphAnalysisInput.CachedGraph);
            }

            Console.WriteLine($"Dumping out the different pips in {m_outputDirectoryPath}");

            // Dump out all pips so users can use a diffing tool to view the differences between pips
            {
                var pipDumper = new GraphDiffPipDumper(m_outputDirectoryPath, m_firstGraphAnalysisInput, m_secondGraphAnalysisInput);

                differentPipPairs
                    .AsParallel()
                    .ForAll(differentPipPair =>
                    {
                        pipDumper.DumpPips(differentPipPair.FirstPip, differentPipPair.SecondPip);
                    });
            }

            return 0;
        }

        /// <summary>
        /// Note: ThreadPool is not used in this implementation because it doesn't fully saturate the CPU
        /// for some reason. This implementation ensures that the CPU is fully saturated. On small graphs,
        /// I've noticed a 1.5x speedup. On bigger graphs, there is still some perf benefit but not as much.
        /// </summary>
        private List<DifferentPipPair> FindDifferentPipPairs(IEnumerable<KeyValuePair<PipId, PipId>> matchingPips)
        {
            var differentPipPairComparer = new DifferentPipPairComparer();
            var result = new ConcurrentBag<DifferentPipPair>();
            var matchingPipsQueue = new ConcurrentQueue<KeyValuePair<PipId, PipId>>(matchingPips);
            var threadCount = (int)(Environment.ProcessorCount * 1.25);

            var threads = Enumerable.Range(0, threadCount).Select(_ =>
                new Thread(() =>
                {
                    while (matchingPipsQueue.TryDequeue(out var pipPair))
                    {
                        var firstPip = m_firstGraphAnalysisInput.CachedGraph.PipGraph.GetPipFromPipId(pipPair.Key);
                        var secondPip = m_secondGraphAnalysisInput.CachedGraph.PipGraph.GetPipFromPipId(pipPair.Value);

                        var firstPipStrings = PipStrings.CreatePipStrings(firstPip, m_firstGraphAnalysisInput.CachedGraph);
                        var secondPipStrings = PipStrings.CreatePipStrings(secondPip, m_secondGraphAnalysisInput.CachedGraph);

                        var differentPipPair = firstPipStrings.GetDifferencesIfAny(secondPipStrings);
                        if (differentPipPair != null)
                        {
                            result.Add(differentPipPair);
                        }
                    }
                })
            ).ToList();

            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            return result
                .OrderBy(pipPair => pipPair, differentPipPairComparer)
                .ToList();
        }

        private void EnsureEmptyOutputDirectory()
        {
            if (File.Exists(m_outputDirectoryPath))
            {
                throw new InvalidArgumentException("A file exists at the output directory path");
            }

            if (Directory.Exists(m_outputDirectoryPath))
            {
                var dirInfo = new DirectoryInfo(m_outputDirectoryPath);

                foreach (var file in dirInfo.GetFiles())
                {
                    file.Delete();
                }

                foreach (var dir in dirInfo.GetDirectories())
                {
                    dir.Delete(true);
                }
            }
            else
            {
                Directory.CreateDirectory(m_outputDirectoryPath);
            }
        }

        private static Dictionary<AbsolutePath, PipId> GetOutputToPip(CachedGraph cachedGraph)
        {
            var result = new Dictionary<AbsolutePath, PipId>();

            var pips = cachedGraph
                .PipTable
                .Keys
                .Where(pipId =>
                {
                    return IsRelevantPipType(cachedGraph, pipId);
                })
                .Select(cachedGraph.PipGraph.GetPipFromPipId);

            foreach (var pip in pips)
            {
                var outputFiles = GetOutputsForPip(pip);

                foreach (var path in outputFiles)
                {
                    result.Add(path, pip.PipId);
                }
            }

            return result;
        }

        private static IEnumerable<AbsolutePath> GetOutputsForPip(Pip pip)
        {
            if (pip.PipType == PipType.Process)
            {
                var processPip = pip as Process;

                var result = processPip.FileOutputs.Select(x => x.Path).ToList();
                result.AddRange(processPip.DirectoryOutputs.Select(x => x.Path));

                return result;
            }
            else if (pip.PipType == PipType.CopyFile)
            {
                var copyFilePip = pip as CopyFile;
                return new List<AbsolutePath> { copyFilePip.Destination.Path };
            }
            else if (pip.PipType == PipType.WriteFile)
            {
                var writeFilePip = pip as WriteFile;
                return new List<AbsolutePath> { writeFilePip.Destination.Path };
            }

            // No other pip types have declared outputs
            return Enumerable.Empty<AbsolutePath>();
        }

        private static bool IsRelevantPipType(CachedGraph cachedGraph, PipId pipId)
        {
            var pipType = cachedGraph.PipTable.GetPipType(pipId);
            return (pipType == PipType.Process) || (pipType == PipType.CopyFile) || (pipType == PipType.WriteFile);
        }
    }

    internal class DifferentPipPairComparer : IComparer<DifferentPipPair>
    {
        public int Compare(DifferentPipPair x, DifferentPipPair y)
        {
            int LongToNegativePositiveOrZero(long num)
            {
                if (num == 0)
                {
                    return 0;
                }
                else if (num < 0)
                {
                    return -1;
                }
                else
                {
                    return 1;
                }
            }

            if (x.FirstPip != y.FirstPip)
            {
                return LongToNegativePositiveOrZero((long)x.FirstPip.Value - (long)y.FirstPip.Value);
            }
            else if (x.SecondPip != y.SecondPip)
            {
                return LongToNegativePositiveOrZero((long)x.SecondPip.Value - (long)y.SecondPip.Value);
            }
            else
            {
                return 0;
            }
        }
    }

    internal class DifferentPipPair
    {
        public PipId FirstPip = PipId.Invalid;
        public PipId SecondPip = PipId.Invalid;
        public IList<DifferenceReason> DiffReasons = new List<DifferenceReason>();

        public enum DifferenceReason
        {
            InputFiles,
            OutputFiles,
            OpaqueInputDirs,
            OpaqueOutputDirs,
            Executable,
            StdInFile,
            StdInData,
            EnvVars,
            WorkingDir,
            UniqueOutputDir,
            TempDir,
            Arguments,
            SealDirInputs,
            Tags,
            UntrackedPaths,
            UntrackedScopes,
            WriteFileContents,
            WriteFileEncoding,
        }
    }

    internal class PipStrings
    {
        private Pip m_pip;

        private HashSet<string> m_inputFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> m_outputFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> m_opaqueInputDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> m_opaqueOutputDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string m_executable = null;
        private string m_stdInFile = null;
        private string m_stdInData = null;
        private HashSet<KeyValuePair<string, string>> m_envVars = new HashSet<KeyValuePair<string, string>>();
        private string m_workingDir = null;
        private string m_uniqueOutputDir = null;
        private string m_tempDir = null;
        private string m_arguments = null;
        private HashSet<SealDirectoryStrings> m_sealDirectoryInputs = new HashSet<SealDirectoryStrings>(); // no opaques here
        private HashSet<string> m_tags = new HashSet<string>();
        private HashSet<string> m_untrackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> m_untrackedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string m_writeFileContents = null;
        private WriteFileEncoding m_writeFileEncoding = 0;

        public static PipStrings CreatePipStrings(Pip pip, CachedGraph cachedGraph)
        {
            PipStrings result;

            if (pip.PipType == PipType.Process)
            {
                result = new PipStrings(pip as Process, cachedGraph);
            }
            else if (pip.PipType == PipType.CopyFile)
            {
                result = new PipStrings(pip as CopyFile, cachedGraph);
            }
            else if (pip.PipType == PipType.WriteFile)
            {
                result = new PipStrings(pip as WriteFile, cachedGraph);
            }
            else
            {
                result = new PipStrings();
            }

            result.m_pip = pip;

            // Tags
            {
                var stringTable = cachedGraph.Context.StringTable;

                foreach (var tag in pip.Tags)
                {
                    result.m_tags.Add(tag.ToString(stringTable));
                }
            }

            return result;
        }

        /// <summary>
        /// If there are no differences, then null is returned
        /// </summary>
        public DifferentPipPair GetDifferencesIfAny(PipStrings other)
        {
            var result = new DifferentPipPair();
            result.FirstPip = m_pip.PipId;
            result.SecondPip = other.m_pip.PipId;

            if (!m_inputFiles.SetEquals(other.m_inputFiles))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.InputFiles);
            }

            if (!m_outputFiles.SetEquals(other.m_outputFiles))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.OutputFiles);
            }

            if (!m_opaqueInputDirs.SetEquals(other.m_opaqueInputDirs))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.OpaqueInputDirs);
            }

            if (!m_opaqueOutputDirs.SetEquals(other.m_opaqueOutputDirs))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.OpaqueOutputDirs);
            }

            if (m_executable != other.m_executable)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.Executable);
            }

            if (m_stdInFile != other.m_stdInFile)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.StdInFile);
            }

            if (m_stdInData != other.m_stdInData)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.StdInData);
            }

            if (!m_envVars.SetEquals(other.m_envVars))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.EnvVars);
            }

            if (m_workingDir != other.m_workingDir)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.WorkingDir);
            }

            if (m_uniqueOutputDir != other.m_uniqueOutputDir)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.UniqueOutputDir);
            }

            if (m_tempDir != other.m_tempDir)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.TempDir);
            }

            if (m_arguments != other.m_arguments)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.Arguments);
            }
            if (!m_sealDirectoryInputs.SetEquals(other.m_sealDirectoryInputs))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.SealDirInputs);
            }

            if (!m_tags.SetEquals(other.m_tags))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.Tags);
            }

            if (!m_untrackedPaths.SetEquals(other.m_untrackedPaths))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.UntrackedPaths);
            }

            if (!m_untrackedScopes.SetEquals(other.m_untrackedScopes))
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.UntrackedScopes);
            }

            if (m_writeFileContents != other.m_writeFileContents)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.WriteFileContents);
            }

            if (m_writeFileEncoding != other.m_writeFileEncoding)
            {
                result.DiffReasons.Add(DifferentPipPair.DifferenceReason.WriteFileEncoding);
            }

            return result.DiffReasons.Any() ? result : null;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        private PipStrings()
        { }

        private PipStrings(Process processPip, CachedGraph cachedGraph)
        {
            var pathTable = cachedGraph.Context.PathTable;
            var stringTable = cachedGraph.Context.StringTable;

            // Input files
            {
                var inputFiles = processPip
                .Dependencies
                .Select(x => x.Path.ToString(pathTable));

                foreach (var inputFile in inputFiles)
                {
                    m_inputFiles.Add(inputFile);
                }
            }

            // Output files
            {
                var outputFiles = processPip
                .FileOutputs
                .Select(x => x.Path.ToString(pathTable));

                foreach (var outputFile in outputFiles)
                {
                    m_outputFiles.Add(outputFile);
                }
            }

            // Opaque input dirs
            {
                var opaqueInputDirs = processPip
                .DirectoryDependencies
                .Select(x => x.Path.ToString(pathTable));

                foreach (var inputDir in opaqueInputDirs)
                {
                    m_opaqueInputDirs.Add(inputDir);
                }
            }

            // Opaque output dirs
            {
                var opaqueOutputDirs = processPip
                .DirectoryOutputs
                .Select(x => x.Path.ToString(pathTable));

                foreach (var outputDir in opaqueOutputDirs)
                {
                    m_opaqueOutputDirs.Add(outputDir);
                }
            }

            m_executable = processPip.Executable.Path.ToString(pathTable);
            m_stdInFile = processPip.StandardInput.File.IsValid ? processPip.StandardInput.File.Path.ToString(pathTable) : null;
            m_stdInData = processPip.StandardInput.Data.IsValid ? processPip.StandardInput.Data.ToString(pathTable) : null;

            foreach (var envVar in processPip.EnvironmentVariables)
            {
                var varName = envVar.Name.ToString(stringTable);
                var varValue = envVar.Value.IsValid ? envVar.Value.ToString(pathTable) : null;

                m_envVars.Add(new KeyValuePair<string, string>(varName, varValue));
            }

            m_workingDir = processPip.WorkingDirectory.ToString(pathTable);
            m_uniqueOutputDir = processPip.UniqueOutputDirectory.ToString(pathTable);
            m_tempDir = processPip.TempDirectory.ToString(pathTable);

            m_arguments = processPip.Arguments.ToString(pathTable);

            // Partially / source seal directory inputs
            {
                var sealDirectoryInputs = cachedGraph
                    .PipGraph
                    .RetrievePipImmediateDependencies(processPip)
                    .Where(pip => pip.PipType == PipType.SealDirectory)
                    .Select(pip => pip as SealDirectory)
                    .Where(sealDirPip => sealDirPip.Kind != SealDirectoryKind.Opaque && sealDirPip.Kind != SealDirectoryKind.SharedOpaque)
                    .Select(sealDirPip => new SealDirectoryStrings(sealDirPip, cachedGraph));

                foreach (var input in sealDirectoryInputs)
                {
                    m_sealDirectoryInputs.Add(input);
                }
            }

            // Untracked paths
            {
                var untrackedPaths = processPip
                    .UntrackedPaths
                    .Select(path => path.ToString(pathTable));

                foreach (var path in untrackedPaths)
                {
                    m_untrackedPaths.Add(path);
                }
            }

            // Untracked scopes
            {
                var untrackedScopes = processPip
                    .UntrackedScopes
                    .Select(path => path.ToString(pathTable));

                foreach (var scope in untrackedScopes)
                {
                    m_untrackedScopes.Add(scope);
                }
            }
        }

        private PipStrings(CopyFile copyFilePip, CachedGraph cachedGraph)
        {
            var pathTable = cachedGraph.Context.PathTable;

            m_inputFiles.Add(copyFilePip.Source.Path.ToString(pathTable));
            m_outputFiles.Add(copyFilePip.Destination.Path.ToString(pathTable));
        }

        private PipStrings(WriteFile writeFilePip, CachedGraph cachedGraph)
        {
            var pathTable = cachedGraph.Context.PathTable;

            m_outputFiles.Add(writeFilePip.Destination.Path.ToString(pathTable));

            if (writeFilePip.Contents.IsValid)
            {
                m_writeFileContents = writeFilePip.Contents.ToString(pathTable);
            }

            m_writeFileEncoding = writeFilePip.Encoding;
        }
    }

    internal class SealDirectoryStrings
    {
        private string m_directoryRoot;
        private HashSet<string> m_patterns = new HashSet<string>();
        private HashSet<string> m_contents = new HashSet<string>();
        private bool m_recursive;

        public SealDirectoryStrings(SealDirectory sealDirectory, CachedGraph cachedGraph)
        {
            var pathTable = cachedGraph.Context.PathTable;
            var stringTable = cachedGraph.Context.StringTable;

            m_directoryRoot = sealDirectory.DirectoryRoot.ToString(pathTable).ToLowerInvariant();

            {
                var patterns = sealDirectory
                    .Patterns
                    .Select(pattern => pattern.ToString(stringTable));

                foreach (var pattern in patterns)
                {
                    m_patterns.Add(pattern.ToLowerInvariant());
                }
            }

            {
                var contents = sealDirectory
                    .Contents
                    .Select(fileArtifact => fileArtifact.Path.ToString(pathTable));

                foreach (var content in contents)
                {
                    m_contents.Add(content.ToLowerInvariant());
                }
            }

            m_recursive = sealDirectory.Kind == SealDirectoryKind.SourceAllDirectories;
        }

        public override bool Equals(object obj)
        {
            var other = obj as SealDirectoryStrings;

            if (other == null)
            {
                return false;
            }

            return m_directoryRoot == other.m_directoryRoot
                && m_patterns.SetEquals(other.m_patterns)
                && m_contents.SetEquals(other.m_contents)
                && m_recursive == other.m_recursive;
        }

        public override int GetHashCode()
        {
            int hashCode = 0;

            hashCode ^= m_directoryRoot.GetHashCode();

            foreach (var pattern in m_patterns)
            {
                hashCode ^= pattern.GetHashCode();
            }

            foreach (var content in m_contents)
            {
                hashCode ^= content.GetHashCode();
            }

            hashCode ^= m_recursive.GetHashCode();

            return hashCode;
        }
    }

    internal class GraphDiffPipDumper
    {
        private string m_diffDirectoryPath;
        private AnalysisInput m_firstAnalysisInput;
        private AnalysisInput m_secondAnalysisInput;

        private string m_firstDiffDirectoryPath;
        private string m_secondDiffDirectoryPath;

        public GraphDiffPipDumper(string diffDirectoryPath, AnalysisInput firstAnalysisInput, AnalysisInput secondAnalysisInput)
        {
            m_diffDirectoryPath = diffDirectoryPath;
            m_firstAnalysisInput = firstAnalysisInput;
            m_secondAnalysisInput = secondAnalysisInput;

            m_firstDiffDirectoryPath = Path.Combine(m_diffDirectoryPath, "first");
            m_secondDiffDirectoryPath = Path.Combine(m_diffDirectoryPath, "second");

            Directory.CreateDirectory(m_firstDiffDirectoryPath);
            Directory.CreateDirectory(m_secondDiffDirectoryPath);
        }

        public void DumpPips(PipId firstPipId, PipId secondPipId)
        {
            var pipDumpFileName = ComputePipDumpFileName(firstPipId, secondPipId);
            var firstPipDumpPath = Path.Combine(m_firstDiffDirectoryPath, pipDumpFileName);
            var secondPipDumpPath = Path.Combine(m_secondDiffDirectoryPath, pipDumpFileName);

            var firstPip = m_firstAnalysisInput.CachedGraph.PipGraph.GetPipFromPipId(firstPipId);
            var secondPip = m_secondAnalysisInput.CachedGraph.PipGraph.GetPipFromPipId(secondPipId);

            DumpPip(firstPip, m_firstAnalysisInput, firstPipDumpPath);
            DumpPip(secondPip, m_secondAnalysisInput, secondPipDumpPath);
        }

        private void DumpPip(Pip pip, AnalysisInput analysisInput, string dumpPath)
        {
            var dumpPipAnalyzer = new DumpPipAnalyzer(analysisInput, dumpPath, pip.SemiStableHash, true, false);
            var result = dumpPipAnalyzer.Analyze();

            if (result != 0)
            {
                throw new Exception($"DumpPip failed for {pip.FormattedSemiStableHash}");
            }
        }

        private string ComputePipDumpFileName(PipId firstPipId, PipId secondPipId)
        {
            var firstPipTable = m_firstAnalysisInput.CachedGraph.PipTable;
            var secondPipTable = m_secondAnalysisInput.CachedGraph.PipTable;

            return $"{firstPipTable.GetFormattedSemiStableHash(firstPipId)}-{secondPipTable.GetFormattedSemiStableHash(secondPipId)}";
        }
    }

    internal class GraphDiffSummaryWriter : IDisposable
    {
        private JsonTextWriter m_writer;

        public GraphDiffSummaryWriter(string outputFilePath)
        {
            m_writer = new JsonTextWriter(File.CreateText(outputFilePath));
            m_writer.Formatting = Formatting.Indented;
            m_writer.WriteStartObject();
        }

        public void Dispose()
        {
            m_writer.WriteEndObject();
            m_writer.Close();
        }

        public void WriteFirstGraphUniquePips(IEnumerable<PipId> pipIds, CachedGraph cachedGraph)
        {
            WriteUniquePips(pipIds, cachedGraph, "FirstGraphUniquePips");
        }

        public void WriteSecondGraphUniquePips(IEnumerable<PipId> pipIds, CachedGraph cachedGraph)
        {
            WriteUniquePips(pipIds, cachedGraph, "SecondGraphUniquePips");
        }

        public void WriteNumberOfSamePips(int numSamePips)
        {
            m_writer.WritePropertyName("NumberOfSamePips");
            m_writer.WriteValue(numSamePips);
        }

        public void WriteNumberOfDifferentPips(int numDifferentPips)
        {
            m_writer.WritePropertyName("NumberOfDifferentPips");
            m_writer.WriteValue(numDifferentPips);
        }

        public void WriteDifferentPips(IEnumerable<DifferentPipPair> differentPips, CachedGraph firstCachedGraph, CachedGraph secondCachedGraph)
        {
            m_writer.WritePropertyName("DifferentPips");
            m_writer.WriteStartArray();

            foreach (var pipPair in differentPips)
            {
                m_writer.WriteStartObject();

                m_writer.WritePropertyName("Old");
                m_writer.WriteValue(firstCachedGraph.PipTable.GetFormattedSemiStableHash(pipPair.FirstPip));

                m_writer.WritePropertyName("New");
                m_writer.WriteValue(secondCachedGraph.PipTable.GetFormattedSemiStableHash(pipPair.SecondPip));

                {
                    m_writer.WritePropertyName("Reasons");
                    m_writer.WriteStartArray();

                    foreach (var diffReason in pipPair.DiffReasons)
                    {
                        m_writer.WriteValue(diffReason.ToString());
                    }

                    m_writer.WriteEndArray();
                }

                m_writer.WriteEndObject();
            }

            m_writer.WriteEndArray();
        }

        private void WriteUniquePips(IEnumerable<PipId> pipIds, CachedGraph cachedGraph, string propertyName)
        {
            m_writer.WritePropertyName(propertyName);
            m_writer.WriteStartArray();

            foreach (var pipId in pipIds)
            {
                m_writer.WriteValue(cachedGraph.PipTable.GetFormattedSemiStableHash(pipId));
            }

            m_writer.WriteEndArray();
        }
    }
}
