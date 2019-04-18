// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Execution.Analyzer;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeFileImpactAnalyzer()
        {
            string outputFilePath = null;
            string fileImpactOutputPath = null;
            string pipToListOfAffectingChangesFile = null;
            bool simulateBuildHistory = false;
            bool impactByChangeCount = false;
            string srcRootMount = "OfficeSrcRoot";
            string fileToGetImpactFor = null;
            string changesFile = null;
            string filesListFile = null;
            string packageListFile = null;
            string nugetMachineInstallRootMount = "OfficeNuGetCacheRoot";
            string simulatedBuildsOutputFile = null;
            string nugetPackageToGetImpactFor = null;
            string criticalPathOutputFile = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("pipToChangesFile", StringComparison.OrdinalIgnoreCase))
                {
                    pipToListOfAffectingChangesFile = ParseSingletonPathOption(opt, pipToListOfAffectingChangesFile);
                }
                else if (opt.Name.Equals("fileImpactOutput", StringComparison.OrdinalIgnoreCase))
                {
                    fileImpactOutputPath = ParseSingletonPathOption(opt, fileImpactOutputPath);
                }
                else if (opt.Name.Equals("criticalPathOutputFile", StringComparison.OrdinalIgnoreCase))
                {
                    criticalPathOutputFile = ParseSingletonPathOption(opt, criticalPathOutputFile);
                }
                else if (opt.Name.Equals("simulatedBuildsOutputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("so", StringComparison.OrdinalIgnoreCase))
                {
                    simulatedBuildsOutputFile = ParseSingletonPathOption(opt, simulatedBuildsOutputFile);
                }
                else if (opt.Name.Equals("simulateChanges", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("s", StringComparison.OrdinalIgnoreCase))
                {
                    simulateBuildHistory = true;
                }
                else if (opt.Name.Equals("impactByChangeCount", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("c", StringComparison.OrdinalIgnoreCase))
                {
                    impactByChangeCount = true;
                }
                else if (opt.Name.Equals("srcRootMount", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("r", StringComparison.OrdinalIgnoreCase))
                {
                    srcRootMount = ParseSingletonPathOption(opt, srcRootMount).ToLower();
                }
                else if (opt.Name.Equals("nugetMachineInstallRootMount", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("n", StringComparison.OrdinalIgnoreCase))
                {
                    nugetMachineInstallRootMount = ParseSingletonPathOption(opt, nugetMachineInstallRootMount).ToLower();
                }
                else if (opt.Name.Equals("fileToGetImpactFor", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    fileToGetImpactFor = ParseSingletonPathOption(opt, fileToGetImpactFor);
                }
                else if (opt.Name.Equals("nugetPackageToGetImpactFor", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("pi", StringComparison.OrdinalIgnoreCase))
                {
                    nugetPackageToGetImpactFor = ParseSingletonPathOption(opt, nugetPackageToGetImpactFor);
                }
                else if (opt.Name.Equals("changesFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("f", StringComparison.OrdinalIgnoreCase))
                {
                    changesFile = ParseSingletonPathOption(opt, changesFile);
                }
                else if (opt.Name.Equals("impactFileList", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("l", StringComparison.OrdinalIgnoreCase))
                {
                    filesListFile = ParseSingletonPathOption(opt, filesListFile);
                }
                else if (opt.Name.Equals("impactPackageList", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    packageListFile = ParseSingletonPathOption(opt, packageListFile);
                }
                else
                {
                    throw Error("Unknown option for file impact analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("outputFile parameter is required");
            }

            if (!string.IsNullOrWhiteSpace(filesListFile) && string.IsNullOrWhiteSpace(fileImpactOutputPath))
            {
                throw Error("fileImpactOutput parameter is required");
            }

            if (simulateBuildHistory || impactByChangeCount)
            {
                if (string.IsNullOrWhiteSpace(srcRootMount))
                {
                    throw Error("srcrootmount parameter is required to simulate builds");
                }
                else if (string.IsNullOrWhiteSpace(nugetMachineInstallRootMount))
                {
                    throw Error("nugetMachineInstallRootMount parameter is required to simulate builds");
                }
                else if (string.IsNullOrWhiteSpace(changesFile))
                {
                    throw Error("changesFile parameter is required to simulate builds");
                }
                else if (string.IsNullOrWhiteSpace(filesListFile))
                {
                    throw Error("filesListFile parameter is required to simulate builds");
                }
                else if (string.IsNullOrWhiteSpace(packageListFile))
                {
                    throw Error("packageListFile parameter is required to simulate builds");
                }

                if (simulateBuildHistory)
                {
                    if (string.IsNullOrWhiteSpace(simulatedBuildsOutputFile))
                    {
                        throw Error("simulatedBuildsOutputFile parameter is required to simulate builds");
                    }
                }
            }

            return new FileImpactAnalyzer(
                GetAnalysisInput(),
                outputFilePath,
                fileImpactOutputPath,
                simulatedBuildsOutputFile,
                criticalPathOutputFile,
                !impactByChangeCount,
                impactByChangeCount,
                simulateBuildHistory,
                fileToGetImpactFor,
                nugetPackageToGetImpactFor,
                srcRootMount,
                nugetMachineInstallRootMount,
                changesFile,
                filesListFile,
                packageListFile,
                pipToListOfAffectingChangesFile);
        }

        private static void WriteFileImpactAnalyzerAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("File Impact Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.FileImpact), "Generates file containing information about how long the build would take if each file was invalidated.");
            writer.WriteOption("outputFile", "Required. The location of the output file.", shortName: "o");
            writer.WriteOption("simulatedBuildsOutputFile", "The location of the output file.", shortName: "so");
            writer.WriteOption("criticalPathOutputFile", "The location of the output file where critical paths are written.", shortName: "co");
            writer.WriteOption("simulateChanges", "If true, simulate changes over time.", shortName: "s");
            writer.WriteOption("impactByChangeCount", "If true, get file impact based upon how many times the file has changed.  If set, changesFile must be set.", shortName: "c");
            writer.WriteOption("srcRootMount", "Name of mount point where source files live.  Required if using change history", shortName: "r");
            writer.WriteOption("nugetMachineInstallRootMount", "Name of mount point where nuget packages live.  Required if using change history", shortName: "n");
            writer.WriteOption("fileToGetImpactFor", "Single file to find impact for.", shortName: "i");
            writer.WriteOption("impactFileList", "File containing list of files to find impact for.", shortName: "l");
            writer.WriteOption("impactPackageList", "File containing list of nuget packages to find impact for.", shortName: "p");
            writer.WriteOption("changesFile", "File with list of changes from source control", shortName: "f");
            writer.WriteOption("nugetPackageToGetImpactFor", "Nuget package to get the impact for", shortName: "pi");
            writer.WriteOption("pipToChangesFile", "File path that contains one line per pip, formatted as [semi stable hash change 1 id change 2 id etc...].  Change ids are whatever number you want them to be.");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    internal sealed class FileImpactAnalyzer : Analyzer
    {
        private struct Times
        {
            // This is not wallclocktime. It is just duration of execute step, which includes other things as well including storing outputs to cache.
            public TimeSpan ExecuteDuration;
        }

        private readonly StreamWriter m_pipImpactwriter;

        private readonly StreamWriter m_fileImpactwriter;

        private readonly StreamWriter m_simulatedBuildWriter;

        private readonly StreamWriter m_criticalPathWriter;

        private readonly ConcurrentDenseIndex<Times> m_elapsedTimes = new ConcurrentDenseIndex<Times>(false);
        private readonly ConcurrentDenseIndex<IEnumerable<ObservedInput>> m_fingerprintComputations = new ConcurrentDenseIndex<IEnumerable<ObservedInput>>(false);
        private readonly HashSet<NodeId> m_nodesWithObservedInputs = new HashSet<NodeId>();
        private readonly bool m_computeImpactForAllFiles;
        private readonly bool m_computeImpactfulFilesByNumberOfTimesChanged;
        private readonly bool m_simulateBuildHistory;
        private readonly string m_srcRootMount;
        private readonly string m_nugetMachineInstallRootMount;
        private readonly string m_changesFile;

        // Single file to determine impact for
        private readonly string m_fileToDetermineImpactFor;

        // Single package to determine impact for
        private readonly string m_packageToDetermineImpactFor;

        // File containing list of files to determine impact for.
        private readonly string m_filesListFile;

        // File containing list of packages to determine impact for.
        private readonly string m_packagesListFile;

        /// <summary>
        /// File containing list of [semistable hash change 1 id change 2 id etc]
        /// WHere change 1 and change 2 are changes that affected the pip, and the ids can be arbitrary numbers.
        /// </summary>
        private readonly string m_pipToListOfAffectingChangesFile;

        /// <summary>
        /// Creates an exporter which writes text to <paramref name="output" />.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        internal FileImpactAnalyzer(
            AnalysisInput input,
            string outputFilePath,
            string fileImpactOutputFilePath,
            string simulatedBuildsOutputFile,
            string criticalPathOutputFile,
            bool computeImpactForAllFiles,
            bool computeImpactfulFilesByNumberOfTimesChanged,
            bool simulateBuildHistory,
            string fileToDetermineImpactFor,
            string packageToDetermineImpactFor,
            string srcRootMount,
            string nugetMachineInstallRootMount,
            string changesFile,
            string filesListFile,
            string packagesListFile,
            string pipToListOfAffectingChangesFile)
            : base(input)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(outputFilePath));
            m_pipImpactwriter = new StreamWriter(outputFilePath);
            if (!string.IsNullOrWhiteSpace(fileImpactOutputFilePath))
            {
                m_fileImpactwriter = new StreamWriter(fileImpactOutputFilePath);
            }

            if (!string.IsNullOrWhiteSpace(simulatedBuildsOutputFile))
            {
                m_simulatedBuildWriter = new StreamWriter(simulatedBuildsOutputFile);
            }

            if (!string.IsNullOrWhiteSpace(criticalPathOutputFile))
            {
                m_criticalPathWriter = new StreamWriter(criticalPathOutputFile);
            }

            m_computeImpactForAllFiles = computeImpactForAllFiles;
            m_computeImpactfulFilesByNumberOfTimesChanged = computeImpactfulFilesByNumberOfTimesChanged;
            m_simulateBuildHistory = simulateBuildHistory;
            m_fileToDetermineImpactFor = fileToDetermineImpactFor;
            m_srcRootMount = srcRootMount;
            m_nugetMachineInstallRootMount = nugetMachineInstallRootMount;
            m_changesFile = changesFile;
            m_filesListFile = filesListFile;
            m_packagesListFile = packagesListFile;
            m_packageToDetermineImpactFor = packageToDetermineImpactFor;
            m_pipToListOfAffectingChangesFile = pipToListOfAffectingChangesFile;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (m_pipImpactwriter != null)
            {
                m_pipImpactwriter.Dispose();
            }

            if (m_fileImpactwriter != null)
            {
                m_fileImpactwriter.Dispose();
            }

            if (m_simulatedBuildWriter != null)
            {
                m_simulatedBuildWriter.Dispose();
            }

            if (m_criticalPathWriter != null)
            {
                m_criticalPathWriter.Dispose();
            }

            base.Dispose();
        }

        public override int Analyze()
        {
            Console.WriteLine("Starting file impact analysis started at: " + DateTime.Now);
            HashSet<NodeId> allNodes = GetAllNodes();

            // All files and nuget seal directory roots used by allNodes
            bool computeFileImpact = !string.IsNullOrWhiteSpace(m_fileToDetermineImpactFor);

            HashSet<AbsolutePath> paths = GetPaths(allNodes);
            IDictionary<AbsolutePath, string> pathToPackage = ComputePathToNugetPackage(paths);
            IDictionary<NodeId, Tuple<TimeSpan, NodeId>> nodeToCriticalPath = ComputeNodeCriticalPaths(allNodes);
            IDictionary<AbsolutePath, Tuple<TimeSpan, NodeId>> fileToCriticalPath = ComputeFileCriticalPaths(allNodes, paths, nodeToCriticalPath);

            // Second argument is frontier nodes.  Pass a different value to only do computations on part of the graph.
            IDictionary<NodeId, int> nodeToDepth = ComputeAllNodeDepths(allNodes, allNodes);
            IDictionary<NodeId, List<int>> nodeToAffectedCriticalPath = ParseNodeIdToAffectedChanges();
            var (pathToDownstreamNodes, nodeToTotalCpu, changeToDownstreamNodes) = ComputeAllDownstreamNodesAndPipCpuMinutes(paths, nodeToDepth, nodeToAffectedCriticalPath);
            IDictionary<AbsolutePath, double> pathToCpuMillis = ComputePathCpuTime(pathToDownstreamNodes);
            IDictionary<AbsolutePath, double> fileToCacheHitRate = ComputePathToCacheHitRate(pathToDownstreamNodes);

            if (m_criticalPathWriter != null)
            {
                PrintCriticalPaths(nodeToCriticalPath, m_criticalPathWriter);
            }

            if (changeToDownstreamNodes.Count > 0)
            {
                WriteChangeImpactLines(nodeToCriticalPath, changeToDownstreamNodes, m_pipImpactwriter);
            }
            else
            {
                WritePipImpactLines(nodeToCriticalPath, nodeToTotalCpu, m_pipImpactwriter);
            }

            if (!m_computeImpactfulFilesByNumberOfTimesChanged)
            {
                WriteFileImpactLines(pathToPackage, fileToCriticalPath, pathToCpuMillis, fileToCacheHitRate, null, null, m_fileImpactwriter);
            }
            else
            {
                // Format of this file is on each line: original package name\tnew package name
                List<Tuple<DateTime, long, List<AbsolutePath>, List<string>>> changeLists = GetChangelists(paths, CachedGraph.Context.PathTable, m_changesFile, m_srcRootMount);
                if (m_simulateBuildHistory)
                {
                    IDictionary<string, TimeSpan> packageToCriticalPath = ComputeNugetCriticalPaths(allNodes, pathToPackage, nodeToCriticalPath);
                    IDictionary<string, double> packageToImpactingFileTime = ComputeNugetCpuTime(pathToDownstreamNodes, pathToPackage);
                    IDictionary<string, HashSet<NodeId>> packageToDownstreamPips = ComputePackageToDownstreamNodes(pathToDownstreamNodes, pathToPackage);
                    List<SimulateChangesOverTime.SimulatedBuildStats> times = SimulateChangesOverTime.SimulateBuilds(
                        nodeToDepth.Count,
                        GetElapsed,
                        pathToDownstreamNodes,
                        packageToDownstreamPips,
                        fileToCriticalPath,
                        pathToCpuMillis,
                        packageToCriticalPath,
                        packageToImpactingFileTime,
                        changeLists,
                        5 * 80, // 5 machines * 8 cores per machine
                        90 * 5 / 4 + 25, // machines use 80% cpu on cloud and are 90% slower per core
                        14, // Approx cloud critical path / btw critical path
                        48 * 60 * 1000); // 48 minutes for metabuild + queuing
                    SimulateChangesOverTime.WriteSimulatedBuilds(m_simulatedBuildWriter, times, (AbsolutePath path) => GetPath(path));
                }

                IDictionary<AbsolutePath, int> fileChangeCount = ComputeFileChangeCount(changeLists);
                IDictionary<string, int> nugetPackageChangeCounts = ComputeNugetChangeCount(changeLists);
                WriteFileImpactLines(pathToPackage, fileToCriticalPath, pathToCpuMillis, fileToCacheHitRate, fileChangeCount, nugetPackageChangeCounts, m_fileImpactwriter);
            }

            Console.WriteLine("File impact analysis finished at: " + DateTime.Now);
            return 0;
        }

        private void PrintCriticalPaths(IDictionary<NodeId, Tuple<TimeSpan, NodeId>> criticalPaths, StreamWriter writer)
        {
            writer.WriteLine("Pip Id,Time Taken(mins),Critical Path (mins),Next Pip Id");
            foreach (var criticalPath in criticalPaths.OrderByDescending(x => x.Value.Item1).ThenBy(x => x.Key.Value))
            {
                var pip = CachedGraph.PipTable.HydratePip(criticalPath.Key.ToPipId(), PipQueryContext.ViewerAnalyzer);
                var nextPip = CachedGraph.PipTable.HydratePip(criticalPath.Value.Item2.ToPipId(), PipQueryContext.ViewerAnalyzer);

                writer.WriteLine(pip.FormattedSemiStableHash + "," + GetElapsed(criticalPath.Key) + "," + criticalPath.Value.Item1.TotalMinutes + "," + nextPip.FormattedSemiStableHash);
            }
        }

        private IDictionary<NodeId, List<int>> ParseNodeIdToAffectedChanges()
        {
            IDictionary<NodeId, List<int>> nodeToAffectedCriticalPath = new Dictionary<NodeId, List<int>>();
            IDictionary<long, List<int>> sshToAffectedCriticalPath = new Dictionary<long, List<int>>();

            if (!string.IsNullOrWhiteSpace(m_pipToListOfAffectingChangesFile))
            {
                foreach (var line in File.ReadAllLines(m_pipToListOfAffectingChangesFile))
                {
                    string[] parts = line.Split(' ');
                    long semiStableHash = Convert.ToInt64(parts[0].ToUpperInvariant().Replace("PIP", string.Empty), 16);
                    sshToAffectedCriticalPath[semiStableHash] = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        sshToAffectedCriticalPath[semiStableHash].Add(int.Parse(parts[i]));
                    }
                }

                foreach (var pipId in CachedGraph.PipTable.StableKeys)
                {
                    var possibleMatch = CachedGraph.PipTable.GetPipSemiStableHash(pipId);
                    if (sshToAffectedCriticalPath.ContainsKey(possibleMatch))
                    {
                        nodeToAffectedCriticalPath.Add(pipId.ToNodeId(), sshToAffectedCriticalPath[possibleMatch]);
                    }
                }
            }

            return nodeToAffectedCriticalPath;
        }

        private string GetMountPath(string mountName)
        {
            AbsolutePath root;
            if (!CachedGraph.MountPathExpander.TryGetRootByMountName(mountName, out root))
            {
                return null;
            }

            return GetPath(root);
        }

        private string GetPath(AbsolutePath path)
        {
            return CachedGraph.PipGraph.SemanticPathExpander.ExpandPath(CachedGraph.Context.PathTable, path);
        }

        private IDictionary<AbsolutePath, double> ComputePathToCacheHitRate(IDictionary<AbsolutePath, HashSet<NodeId>> pathToDownstreamNodes)
        {
            IDictionary<AbsolutePath, double> pathToCacheHitRate = new ConcurrentDictionary<AbsolutePath, double>();
            Parallel.ForEach(pathToDownstreamNodes, path =>
            {
                int tot = path.Value.Count * 32;
                int numInvalidated = path.Value.Count;
                pathToCacheHitRate[path.Key] = 100 * (tot - numInvalidated) / tot;
            });

            return pathToCacheHitRate;
        }

        private HashSet<AbsolutePath> GetPaths(IEnumerable<NodeId> allNodes)
        {
            string[] files;
            if (!string.IsNullOrWhiteSpace(m_fileToDetermineImpactFor))
            {
                files = new string[] { m_fileToDetermineImpactFor };
            }
            else if (!string.IsNullOrWhiteSpace(m_filesListFile))
            {
                if (m_srcRootMount == null)
                {
                    throw new ArgumentException("Srcroot mount name must not be null");
                }

                string srcRoot = GetMountPath(m_srcRootMount);
                if (srcRoot == null)
                {
                    throw new ArgumentException("Srcroot must not be null");
                }

                files = File.ReadAllLines(m_filesListFile).Select(file => Path.Combine(srcRoot, file.Replace('/', '\\'))).ToArray();
            }
            else
            {
                files = new string[0];
            }

            if (m_nugetMachineInstallRootMount == null)
            {
                throw new ArgumentException("nugetMachineInstallRoot mount name must not be null");
            }

            string nugetMachineInstallRoot = GetMountPath(m_nugetMachineInstallRootMount);
            if (nugetMachineInstallRoot == null)
            {
                throw new ArgumentException("nugetMachineInstallRoot must not be null");
            }

            // ICollection<AbsolutePath> nugetFiles = GetNugetFiles(allNodes, nugetMachineInstallRoot);
            HashSet<AbsolutePath> paths = GetAbsolutePathsFromFileNames(files);
            // paths.UnionWith(nugetFiles);
            return paths;
        }

        private IDictionary<AbsolutePath, string> ComputePathToNugetPackage(HashSet<AbsolutePath> paths)
        {
            string[] nugetPackages;
            if (!string.IsNullOrWhiteSpace(m_packageToDetermineImpactFor))
            {
                nugetPackages = new string[] { m_packageToDetermineImpactFor };
            }
            else if (!string.IsNullOrWhiteSpace(m_packagesListFile))
            {
                nugetPackages = File.ReadAllLines(m_packagesListFile);
            }
            else
            {
                nugetPackages = new string[0];
            }

            string nugetMachineInstallRoot = GetMountPath(m_nugetMachineInstallRootMount);
            if (nugetMachineInstallRoot == null)
            {
                throw new ArgumentException("Could not file srcroot or nugetMachineInstallRoot");
            }

            string aggregateNugetMachineInstallRoot = Path.Combine(nugetMachineInstallRoot, ".A");
            return ComputePathToNugetPackage(paths, nugetPackages, nugetMachineInstallRoot, aggregateNugetMachineInstallRoot);
        }

        private HashSet<AbsolutePath> GetAbsolutePathsFromFileNames(IEnumerable<string> files)
        {
            HashSet<AbsolutePath> paths = new HashSet<AbsolutePath>();
            foreach (var file in files)
            {
                AbsolutePath path;
                if (AbsolutePath.TryGet(CachedGraph.Context.PathTable, file, out path))
                {
                    paths.Add(path);
                }
                else
                {
                    Console.WriteLine("Couldn't get path: " + file);
                }
            }

            return paths;
        }

        private void WriteChangeImpactLines(
            IDictionary<NodeId, Tuple<TimeSpan, NodeId>> pipToCriticalPath,
            IDictionary<int, HashSet<NodeId>> changeToDownstreamPips,
            StreamWriter writer)
        {
            Console.WriteLine("Computing change impact output strings");
            var outputLines = new ConcurrentDictionary<int, string>();
            var criticalPathTimes = new ConcurrentDictionary<int, TimeSpan>();
            Parallel.ForEach(changeToDownstreamPips, change =>
            {
                TimeSpan criticalPathTime = TimeSpan.MinValue;
                TimeSpan totalCpuTime = TimeSpan.Zero;
                NodeId nodeForCriticalPath = change.Value.FirstOrDefault();
                foreach (var downstreamPip in change.Value)
                {
                    if (criticalPathTime <= pipToCriticalPath[downstreamPip].Item1)
                    {
                        criticalPathTime = pipToCriticalPath[downstreamPip].Item1;
                        nodeForCriticalPath = downstreamPip;
                    }

                    totalCpuTime += GetElapsed(downstreamPip);
                }

                criticalPathTimes[change.Key] = criticalPathTime;
                var pip = CachedGraph.PipTable.HydratePip(nodeForCriticalPath.ToPipId(), PipQueryContext.ViewerAnalyzer);
                outputLines[change.Key] = change.Key + "," + criticalPathTime.TotalMinutes + "," + totalCpuTime.TotalMinutes + "," + change.Value.Count + "," + pip.FormattedSemiStableHash;
            });

            var sortedOutputLines = outputLines.OrderByDescending(x => criticalPathTimes[x.Key]).ThenBy(x => x.Value).ToList();
            Console.WriteLine("Writing change impact");
            string headerLine = "Change Id,Longest Critical Path Affected (mins),Total CPU Time Affected (mins),Number of pips affected,First pip in critical path";
            writer.WriteLine(headerLine);
            foreach (var criticalPath in sortedOutputLines)
            {
                writer.WriteLine(criticalPath.Value);
            }

            Console.WriteLine("Done writing change impact");
        }

        private void WritePipImpactLines(
            IDictionary<NodeId, Tuple<TimeSpan, NodeId>> pipToCriticalPath,
            IDictionary<NodeId, double> pipToCpuMillis,
            StreamWriter writer)
        {
            Console.WriteLine("Computing pip impact output strings");
            var outputLines = new ConcurrentDictionary<NodeId, string>();
            Parallel.ForEach(pipToCriticalPath, criticalPath =>
            {
                var pip = CachedGraph.PipTable.HydratePip(criticalPath.Key.ToPipId(), PipQueryContext.ViewerAnalyzer);
                double critPathTime = criticalPath.Value.Item1.TotalMilliseconds / 1000 / 60;
                double cpuTime = pipToCpuMillis.ContainsKey(criticalPath.Key) ? ToMins(pipToCpuMillis[criticalPath.Key]) : -1;
                outputLines[criticalPath.Key] = pip.FormattedSemiStableHash + "," + critPathTime + "," + cpuTime + "," + GetElapsed(criticalPath.Key).TotalMinutes + "," + pip.GetDescription(CachedGraph.Context);
            });

            var sortedOutputLines = outputLines.OrderByDescending(x => pipToCriticalPath[x.Key].Item1).ThenBy(x => x.Value).ToList();
            Console.WriteLine("Writing pip impact");
            string headerLine = "Pip,Longest Critical Path Affected (mins),Total CPU Time Affected (mins),Exe Time (mins),Description";
            writer.WriteLine(headerLine);
            foreach (var criticalPath in sortedOutputLines)
            {
                writer.WriteLine(criticalPath.Value);
            }

            Console.WriteLine("Done writing pip impact");
        }


        private void WriteFileImpactLines(
             IDictionary<AbsolutePath, string> pathToPackage,
             IDictionary<AbsolutePath, Tuple<TimeSpan, NodeId>> fileToCriticalPath,
             IDictionary<AbsolutePath, double> pathToCpuMillis,
             IDictionary<AbsolutePath, double> fileToCacheHitRate,
             IDictionary<AbsolutePath, int> fileChangeCount,
             IDictionary<string, int> nugetPackageChangeCounts,
             StreamWriter writer)
        {
            if (pathToCpuMillis == null || pathToCpuMillis.Count == 0)
            {
                return;
            }

            var outputLines = new ConcurrentDictionary<AbsolutePath, string>();
            bool useChangeCount = fileChangeCount != null || nugetPackageChangeCounts != null;
            Console.WriteLine("Sorting critical paths strings");
            Parallel.ForEach(fileToCriticalPath, criticalPath =>
            {
                double critPathTime = criticalPath.Value.Item1.TotalMilliseconds / 1000 / 60;
                double cpuTime = pathToCpuMillis.ContainsKey(criticalPath.Key) ? ToMins(pathToCpuMillis[criticalPath.Key]) : -1;
                string outputLine = GetPath(criticalPath.Key) + "," + critPathTime + "," + cpuTime;
                if (useChangeCount)
                {
                    int changeCount = 0;
                    if (fileChangeCount != null && fileChangeCount.ContainsKey(criticalPath.Key))
                    {
                        changeCount = fileChangeCount[criticalPath.Key];
                    }
                    else if (nugetPackageChangeCounts != null && pathToPackage != null && pathToPackage.ContainsKey(criticalPath.Key) && nugetPackageChangeCounts.ContainsKey(pathToPackage[criticalPath.Key]))
                    {
                        changeCount = nugetPackageChangeCounts[pathToPackage[criticalPath.Key]];
                    }

                    double critPathImpact = critPathTime * changeCount;
                    double cpuImpact = cpuTime * changeCount;
                    double cacheHitRate = fileToCacheHitRate[criticalPath.Key];
                    outputLine += "," + cacheHitRate + "," + changeCount + "," + critPathImpact + "," + cpuImpact;
                }

                NodeId firstNodeInCriticalPath = criticalPath.Value.Item2;
                var pip = CachedGraph.PipTable.HydratePip(firstNodeInCriticalPath.ToPipId(), PipQueryContext.ViewerAnalyzer);
                outputLines[criticalPath.Key] = outputLine + "," + pip.GetDescription(CachedGraph.Context);
            });

            var sortedOutputLines = outputLines.OrderByDescending(x => pathToCpuMillis[x.Key]).ThenBy(x => x.Value).ToList();

            Console.WriteLine("Writing critical paths");
            string headerLine = "File Path,Longest Critical Path Affected (mins),Total CPU Time Affected (mins)";
            if (useChangeCount)
            {
                headerLine += ",Cache Hit Rate,Change Count,Critical Path Impact,Cpu Impact";
            }

            headerLine += ",Description";

            if (sortedOutputLines.Count > 0)
            {
                writer.WriteLine(headerLine);
            }

            foreach (var criticalPath in sortedOutputLines)
            {
                writer.WriteLine(criticalPath.Value);
            }
        }

        private double ToMins(double millis)
        {
            return millis / 1000 / 60;
        }

        private HashSet<NodeId> GetAllNodes()
        {
            var allNodes = new HashSet<NodeId>(CachedGraph.PipTable.Keys.Where(pipId => IsValidNode(CachedGraph.PipTable.GetPipType(pipId))).Select(x => x.ToNodeId()));
            double totalTime = allNodes.Sum(node => GetElapsed(node).TotalMilliseconds);
            Console.WriteLine("Total milliseconds taken during this build: " + totalTime + " Total hours: " + ToMins(totalTime) / 60);
            return allNodes;
        }

        private List<Tuple<DateTime, long, List<AbsolutePath>, List<string>>> GetChangelists(HashSet<AbsolutePath> paths, PathTable pathTable, string changeListsFile, string srcRootMount)
        {
            Console.WriteLine("Getting changlists " + DateTime.Now);
            string srcRoot = GetMountPath(srcRootMount);
            if (srcRoot == null)
            {
                throw new ArgumentException("Srcroot must not be null");
            }

            DateTime date = DateTime.MinValue;
            long cl_num = -1;
            List<AbsolutePath> absolutePaths = new List<AbsolutePath>();
            List<string> nugetPackages = new List<string>();
            List<Tuple<DateTime, long, List<AbsolutePath>, List<string>>> changeLists = new List<Tuple<DateTime, long, List<AbsolutePath>, List<string>>>();
            foreach (var line in File.ReadLines(changeListsFile))
            {
                if (line[0] == '=')
                {
                    if (absolutePaths.Any())
                    {
                        changeLists.Add(new Tuple<DateTime, long, List<AbsolutePath>, List<string>>(date, cl_num, absolutePaths, nugetPackages));
                        absolutePaths = new List<AbsolutePath>();
                        nugetPackages = new List<string>();
                    }

                    string[] parts = line.Split('\t');
                    date = DateTime.Parse(parts[2]);
                    cl_num = long.Parse(parts[1]);
                }
                else if (line.StartsWith(@"nugetcache\"))
                {
                    nugetPackages.Add(line.ToLower().Substring(@"nugetcache\".Length));
                }
                else
                {
                    string path = Path.Combine(srcRoot, line.Replace('/', '\\'));
                    AbsolutePath absolutePath;
                    if (AbsolutePath.TryGet(pathTable, path, out absolutePath))
                    {
                        if (paths.Contains(absolutePath))
                        {
                            absolutePaths.Add(absolutePath);
                        }
                    }
                }
            }

            Console.WriteLine("Done getting changlists " + DateTime.Now);
            Console.WriteLine("Found: " + changeLists.Count + " changelists.");
            return changeLists;
        }

        private IDictionary<AbsolutePath, int> ComputeFileChangeCount(List<Tuple<DateTime, long, List<AbsolutePath>, List<string>>> changeLists)
        {
            IDictionary<AbsolutePath, int> changeCounts = new Dictionary<AbsolutePath, int>();
            foreach (var changelist in changeLists)
            {
                foreach (var path in changelist.Item3)
                {
                    changeCounts[path] = changeCounts.ContainsKey(path) ? changeCounts[path] + 1 : 1;
                }
            }

            return changeCounts;
        }

        private IDictionary<string, int> ComputeNugetChangeCount(List<Tuple<DateTime, long, List<AbsolutePath>, List<string>>> changeLists)
        {
            IDictionary<string, int> changeCounts = new Dictionary<string, int>();
            foreach (var changelist in changeLists)
            {
                foreach (var path in changelist.Item4)
                {
                    changeCounts[path] = changeCounts.ContainsKey(path) ? changeCounts[path] + 1 : 1;
                }
            }

            return changeCounts;
        }

        private IDictionary<AbsolutePath, double> ComputePathCpuTime(IDictionary<AbsolutePath, HashSet<NodeId>> pathToDownstreamPips)
        {
            Console.WriteLine("Computing sum of cpu time for files");
            int i = 0;
            ConcurrentDictionary<AbsolutePath, double> pathToCpuMinutes = new ConcurrentDictionary<AbsolutePath, double>();
            Parallel.ForEach(pathToDownstreamPips, file =>
            {
                double time = 0;

                foreach (var pip in file.Value)
                {
                    time += GetElapsed(pip).TotalMilliseconds;
                }
                pathToCpuMinutes[file.Key] = time;
                if (i % 100000 == 0)
                {
                    Console.WriteLine("File Cpu Time: " + i * 100 / pathToDownstreamPips.Count);
                }

                i++;
            });
            Console.WriteLine("Done computing sum of cpu time for files");
            return pathToCpuMinutes;
        }

        private IDictionary<string, HashSet<NodeId>> ComputePackageToDownstreamNodes(IDictionary<AbsolutePath, HashSet<NodeId>> pathToDownstreamnodes, IDictionary<AbsolutePath, string> pathToPackage)
        {
            Console.WriteLine("Computing package downstreams");
            IDictionary<string, HashSet<NodeId>> packageToDownstreamPips = new ConcurrentDictionary<string, HashSet<NodeId>>(StringComparer.OrdinalIgnoreCase);
            var packageToPaths = pathToDownstreamnodes.Where(path => pathToPackage.ContainsKey(path.Key)).ToList().GroupBy(x => pathToPackage[x.Key]).ToList();
            int done = 0;
            Parallel.ForEach(packageToPaths, packagePaths =>
            {
                var downstreamPips = new HashSet<NodeId>();
                foreach (var path in packagePaths)
                {
                    downstreamPips.UnionWith(path.Value);
                }

                packageToDownstreamPips[packagePaths.Key] = downstreamPips;

                Console.WriteLine("Adding package: " + packagePaths.Key);
                if (done % 1000 == 0)
                {
                    Console.WriteLine("Done: " + done + " out of " + packageToPaths.Count);
                }

                done++;
            });

            Console.WriteLine("Done computing package downstreams");
            return packageToDownstreamPips;
        }


        private IDictionary<AbsolutePath, string> ComputePathToNugetPackage(IEnumerable<AbsolutePath> files, IEnumerable<string> allNugetPackageNames, string nugetMachineInstallRoot, string nugetAggregateRoot)
        {
            Console.WriteLine("Computing path to package");
            allNugetPackageNames = allNugetPackageNames.OrderByDescending(x => x.Length).ToList();
            IDictionary<AbsolutePath, string> pathToNugetPackage = new ConcurrentDictionary<AbsolutePath, string>();
            int i = 0;
            int numFiles = files.Count();
            Parallel.ForEach(files, file =>
            {
                string fileName = GetPath(file);
                string correctNugetPackageName = null;
                if (fileName.StartsWith(nugetAggregateRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string name = fileName.Substring(nugetAggregateRoot.Length);
                    foreach (var nugetPackageName in allNugetPackageNames)
                    {
                        if (name.StartsWith(nugetPackageName, StringComparison.OrdinalIgnoreCase))
                        {
                            correctNugetPackageName = nugetPackageName;
                            break;
                        }
                    }
                }
                else if (fileName.StartsWith(nugetMachineInstallRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string name = fileName.Substring(nugetMachineInstallRoot.Length);
                    foreach (var nugetPackageName in allNugetPackageNames)
                    {
                        if (name.StartsWith(nugetPackageName, StringComparison.OrdinalIgnoreCase))
                        {
                            correctNugetPackageName = nugetPackageName;
                            break;
                        }
                    }
                }

                if (correctNugetPackageName != null)
                {
                    pathToNugetPackage.Add(file, correctNugetPackageName.ToLower());
                }

                if (i % 100000 == 0)
                {
                    Console.WriteLine("Computing path to package: " + i * 100 / numFiles);
                }

                i++;
            });

            Console.WriteLine("Done computing path to package. Found: " + pathToNugetPackage.Count + " files in nuget packages");
            return pathToNugetPackage;
        }

        private IDictionary<string, double> ComputeNugetCpuTime(IDictionary<AbsolutePath, HashSet<NodeId>> pathToDownstreamPips, IDictionary<AbsolutePath, string> pathToNugetPackage)
        {
            Console.WriteLine("Computing sum of cpu time for nuget packages");
            int i = 0;
            ConcurrentDictionary<string, double> nugetPackageToCpuMinutes = new ConcurrentDictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Parallel.ForEach(pathToDownstreamPips, file =>
            {
                string nugetPackage;
                if (!pathToNugetPackage.TryGetValue(file.Key, out nugetPackage))
                {
                    return;
                }

                double time = 0;
                foreach (var pip in file.Value)
                {
                    time += GetElapsed(pip).TotalMilliseconds;
                }
                nugetPackageToCpuMinutes.AddOrUpdate(nugetPackage, time, (key, oldValue) => { return Math.Max(oldValue, time); });
                if (i % 100000 == 0)
                {
                    Console.WriteLine("Nuget package Cpu Time: " + i * 100 / pathToDownstreamPips.Count);
                }

                i++;
            });
            Console.WriteLine("Done computing sum of cpu time for packages");
            return nugetPackageToCpuMinutes;
        }

        private IDictionary<NodeId, Tuple<TimeSpan, NodeId>> ComputeNodeCriticalPaths(HashSet<NodeId> allNodes)
        {
            CriticalPath criticalPathCalculator = new CriticalPath(GetElapsed, (nodeId) => CachedGraph.DataflowGraph.GetOutgoingEdges(nodeId).Select(edge => edge.OtherNode));
            var now = DateTime.Now;
            Console.WriteLine("Computing critical paths " + now);
            TimeSpan longestCriticalPath = TimeSpan.Zero;
            ConcurrentDictionary<NodeId, Tuple<TimeSpan, NodeId>> nodeToCriticalPath = new ConcurrentDictionary<NodeId, Tuple<TimeSpan, NodeId>>();
            foreach (var sourceNode in allNodes)
            {
                nodeToCriticalPath[sourceNode] = criticalPathCalculator.ComputeCriticalPath(sourceNode);
                if (nodeToCriticalPath[sourceNode].Item1.Ticks > longestCriticalPath.Ticks)
                {
                    Console.WriteLine("new longest critical path: " + nodeToCriticalPath[sourceNode]);
                    longestCriticalPath = new TimeSpan(Math.Max(longestCriticalPath.Ticks, nodeToCriticalPath[sourceNode].Item1.Ticks));
                }
            }

            Console.WriteLine("Done computing critical paths in " + (DateTime.Now - now));
            return nodeToCriticalPath;
        }

        private IDictionary<AbsolutePath, Tuple<TimeSpan, NodeId>> ComputeFileCriticalPaths(HashSet<NodeId> allNodes, HashSet<AbsolutePath> paths, IDictionary<NodeId, Tuple<TimeSpan, NodeId>> nodeToCriticalPath)
        {
            var now = DateTime.Now;
            Console.WriteLine("Computing file critical paths " + now);
            var pathToCriticalPath = new ConcurrentDictionary<AbsolutePath, Tuple<TimeSpan, NodeId>>();
            if (paths.Count == 0)
            {
                return pathToCriticalPath;
            }

            int i = 0;
            foreach (var sourceNode in allNodes)
            {
                TimeSpan criticalPathTime = nodeToCriticalPath[sourceNode].Item1;
                Parallel.ForEach(CachedGraph.PipGraph.RetrievePipReferenceImmediateDependencies(sourceNode.ToPipId(), null).Where(pipRef => pipRef.PipType == PipType.HashSourceFile || pipRef.PipType == PipType.SealDirectory), pipRef =>
                {
                    var pip = CachedGraph.PipTable.HydratePip(pipRef.PipId, PipQueryContext.ViewerAnalyzer);

                    HashSourceFile hashSourceFilePip = pip as HashSourceFile;
                    AbsolutePath path;
                    if (hashSourceFilePip == null)
                    {
                        SealDirectory sealDirectoryPip = pip as SealDirectory;
                        path = sealDirectoryPip.DirectoryRoot;
                    }
                    else
                    {
                        path = hashSourceFilePip.Artifact.Path;
                    }

                    if (!paths.Contains(path))
                    {
                        return;
                    }

                    if (!pathToCriticalPath.ContainsKey(path) || (criticalPathTime > pathToCriticalPath[path].Item1))
                    {
                        pathToCriticalPath[path] = new Tuple<TimeSpan, NodeId>(criticalPathTime, sourceNode);
                    }
                });

                if (m_nodesWithObservedInputs.Contains(sourceNode))
                {
                    Parallel.ForEach(m_fingerprintComputations[sourceNode.Value], observedInput =>
                    {
                        if (!paths.Contains(observedInput.Path))
                        {
                            return;
                        }

                        if (!pathToCriticalPath.ContainsKey(observedInput.Path) || (criticalPathTime > pathToCriticalPath[observedInput.Path].Item1))
                        {
                            pathToCriticalPath[observedInput.Path] = new Tuple<TimeSpan, NodeId>(criticalPathTime, sourceNode);
                        }
                    });
                }

                if (i % (allNodes.Count / 100) == 0)
                {
                    Console.WriteLine((i * 100 / allNodes.Count) + "% done");
                }

                i++;
            }

            Console.WriteLine("Done computing file critical paths " + DateTime.Now.Subtract(now));
            return pathToCriticalPath;
        }

        private IDictionary<string, TimeSpan> ComputeNugetCriticalPaths(HashSet<NodeId> allNodes, IDictionary<AbsolutePath, string> pathToPackage, IDictionary<NodeId, Tuple<TimeSpan, NodeId>> nodeToCriticalPath)
        {
            var now = DateTime.Now;
            Console.WriteLine("Computing nuget file critical paths " + now);
            ConcurrentDictionary<string, TimeSpan> pathToCriticalPath = new ConcurrentDictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (var sourceNode in allNodes)
            {
                TimeSpan criticalPathTime = nodeToCriticalPath[sourceNode].Item1;
                Parallel.ForEach(CachedGraph.PipGraph.RetrievePipReferenceImmediateDependencies(sourceNode.ToPipId(), null).Where(pipRef => pipRef.PipType == PipType.HashSourceFile || pipRef.PipType == PipType.SealDirectory), pipRef =>
                {
                    var pip = CachedGraph.PipTable.HydratePip(pipRef.PipId, PipQueryContext.ViewerAnalyzer);
                    HashSourceFile hashSourceFilePip = pip as HashSourceFile;
                    AbsolutePath path;
                    if (hashSourceFilePip == null)
                    {
                        SealDirectory sealDirectoryPip = pip as SealDirectory;
                        path = sealDirectoryPip.DirectoryRoot;
                    }
                    else
                    {
                        path = hashSourceFilePip.Artifact.Path;
                    }

                    if (pathToPackage.ContainsKey(path))
                    {
                        MaxDictionaryValue(pathToCriticalPath, pathToPackage[path], criticalPathTime);

                    }
                });

                if (m_nodesWithObservedInputs.Contains(sourceNode))
                {
                    Parallel.ForEach(m_fingerprintComputations[sourceNode.Value], observedInput =>
                    {
                        if (pathToPackage.ContainsKey(observedInput.Path))
                        {
                            MaxDictionaryValue(pathToCriticalPath, pathToPackage[observedInput.Path], criticalPathTime);
                        }
                    });
                }

                if (i % (allNodes.Count / 100) == 0)
                {
                    Console.WriteLine((i * 100 / allNodes.Count) + "% done");
                }

                i++;
            }

            Console.WriteLine("Done computing nuget file critical paths " + DateTime.Now.Subtract(now));
            return pathToCriticalPath;
        }

        private void MaxDictionaryValue<T>(IDictionary<T, TimeSpan> dictionary, T key, TimeSpan valueToMax)
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key] = new TimeSpan(Math.Max(dictionary[key].Ticks, valueToMax.Ticks));
            }
            else
            {
                dictionary[key] = valueToMax;
            }
        }

        private IDictionary<NodeId, int> GetNodeToIndex(IEnumerable<NodeId> allNodes)
        {
            IDictionary<NodeId, int> nodeToIndex = new Dictionary<NodeId, int>();
            int ind = 0;
            foreach (var nodeId in allNodes)
            {
                nodeToIndex[nodeId] = ind;
                ind++;
            }

            return nodeToIndex;
        }

        private IDictionary<int, NodeId> GetIndexToNode(IEnumerable<NodeId> nodeToDepth)
        {
            IDictionary<int, NodeId> indexToNode = new Dictionary<int, NodeId>();
            int ind = 0;
            foreach (var nodeId in nodeToDepth)
            {
                indexToNode[ind] = nodeId;
                ind++;
            }

            return indexToNode;
        }

        private Tuple<IDictionary<AbsolutePath, HashSet<NodeId>>, IDictionary<NodeId, double>, IDictionary<int, HashSet<NodeId>>> ComputeAllDownstreamNodesAndPipCpuMinutes(
            HashSet<AbsolutePath> paths,
            IDictionary<NodeId, int> nodeToDepth,
            IDictionary<NodeId, List<int>> nodeToChanges)
        {
            var nodeToDownstreamPips = new ConcurrentDictionary<NodeId, HashSet<NodeId>>();
            var pathToDownstreamPips = new ConcurrentDictionary<AbsolutePath, HashSet<NodeId>>();
            var pipToCpuMinutes = new ConcurrentDictionary<NodeId, double>();
            var changeToDownStreamPips = new ConcurrentDictionary<int, HashSet<NodeId>>();

            int depthToComputeCpuMinutes = 0;
            var nodesAtDepth = nodeToDepth.Where(x => x.Value == depthToComputeCpuMinutes).Select(x => x.Key);
            var groups = nodesAtDepth.GroupBy(x => x.Value);
            int num = nodesAtDepth.Count();
            while (num > 0)
            {
                Console.WriteLine(depthToComputeCpuMinutes + " " + num);
                depthToComputeCpuMinutes++;
                num = nodeToDepth.Where(x => x.Value == depthToComputeCpuMinutes).Select(x => x.Key).Count();
            }

            depthToComputeCpuMinutes--;
            nodesAtDepth = nodeToDepth.Where(x => x.Value == depthToComputeCpuMinutes).Select(x => x.Key);
            int cnt = nodeToDepth.Count;
            int size = nodeToDepth.Count / 32 + 1;
            while (depthToComputeCpuMinutes >= 0)
            {
                Console.WriteLine("At Depth: " + depthToComputeCpuMinutes);
                int j = 0;
                ConcurrentDictionary<NodeId, IEnumerable<NodeId>> incomingEdges = new ConcurrentDictionary<NodeId, IEnumerable<NodeId>>();
                Parallel.ForEach(nodesAtDepth, nodeId =>
                {
                    incomingEdges[nodeId] = GetIncomingEdges(nodeId, null, nodeToDepth).ToList();
                });

                Parallel.ForEach(nodesAtDepth, nodeId =>
                {
                    if (j++ % 1000 == 0)
                    {
                        Console.WriteLine(j + " / " + nodesAtDepth.Count());
                    }

                    HashSet<NodeId> downstreamPips = nodeToDownstreamPips.GetOrAdd(nodeId, (node) => { return new HashSet<NodeId>(); });
                    downstreamPips.Add(nodeId);
                    double time = 0;
                    foreach (var pip in downstreamPips)
                    {
                        time += GetElapsed(pip).TotalMilliseconds;
                    }

                    if (nodeToChanges.ContainsKey(nodeId))
                    {
                        Parallel.ForEach(nodeToChanges[nodeId], changeId =>
                        {
                            changeToDownStreamPips.GetOrAdd(changeId, (node) => { return new HashSet<NodeId>(); });
                            lock (changeToDownStreamPips[changeId])
                            {
                                changeToDownStreamPips[changeId].UnionWith(downstreamPips);
                            }
                        });
                    }

                    pipToCpuMinutes[nodeId] = time;
                    if (paths.Count > 0)
                    {
                        Parallel.ForEach(CachedGraph.PipGraph.RetrievePipReferenceImmediateDependencies(nodeId.ToPipId(), null).Where(pipRef => pipRef.PipType == PipType.HashSourceFile || pipRef.PipType == PipType.SealDirectory), pipRef =>
                        {
                            var pip = CachedGraph.PipTable.HydratePip(pipRef.PipId, PipQueryContext.ViewerAnalyzer);

                            HashSourceFile hashSourceFilePip = pip as HashSourceFile;
                            AbsolutePath path;
                            if (hashSourceFilePip == null)
                            {
                                SealDirectory sealDirectoryPip = pip as SealDirectory;
                                path = sealDirectoryPip.DirectoryRoot;
                            }
                            else
                            {
                                path = hashSourceFilePip.Artifact.Path;
                            }

                            if (!paths.Contains(path))
                            {
                                return;
                            }

                            pathToDownstreamPips.GetOrAdd(path, (node) => { return new HashSet<NodeId>(); });
                            lock (pathToDownstreamPips[path])
                            {
                                pathToDownstreamPips[path].UnionWith(downstreamPips);
                            }
                        });

                        if (m_nodesWithObservedInputs.Contains(nodeId))
                        {
                            Parallel.ForEach(m_fingerprintComputations[nodeId.Value], observedInput =>
                            {
                                if (!paths.Contains(observedInput.Path))
                                {
                                    return;
                                }

                                pathToDownstreamPips.GetOrAdd(observedInput.Path, (node) => { return new HashSet<NodeId>(); });
                                lock (pathToDownstreamPips[observedInput.Path])
                                {
                                    pathToDownstreamPips[observedInput.Path].UnionWith(downstreamPips);
                                }
                            });
                        }
                    }

                    foreach (NodeId upstreamEdge in incomingEdges[nodeId])
                    {
                        if (!nodeToDepth.ContainsKey(upstreamEdge))
                        {
                            continue;
                        }

                        nodeToDownstreamPips.GetOrAdd(upstreamEdge, (node) => { return new HashSet<NodeId>(); });
                        lock (nodeToDownstreamPips[upstreamEdge])
                        {
                            nodeToDownstreamPips[upstreamEdge].UnionWith(downstreamPips);
                        }
                    }

                    nodeToDownstreamPips[nodeId] = null;
                });

                depthToComputeCpuMinutes--;
                nodesAtDepth = nodeToDepth.Where(x => x.Value == depthToComputeCpuMinutes).Select(x => x.Key);
            }

            return new Tuple<IDictionary<AbsolutePath, HashSet<NodeId>>, IDictionary<NodeId, double>, IDictionary<int, HashSet<NodeId>>>(pathToDownstreamPips, pipToCpuMinutes, changeToDownStreamPips);
        }

        public IEnumerable<NodeId> GetIncomingEdges(NodeId nodeId, HashSet<NodeId> allNodes, IDictionary<NodeId, int> nodeToDepth)
        {
            foreach (var incomingNode in CachedGraph.DataflowGraph.GetIncomingEdges(nodeId))
            {
                if ((allNodes == null || allNodes.Contains(incomingNode.OtherNode)) && (nodeToDepth == null || nodeToDepth.ContainsKey(incomingNode.OtherNode)))
                {
                    yield return incomingNode.OtherNode;
                }
            }

            if (m_nodesWithObservedInputs.Contains(nodeId))
            {
                foreach (var observedPath in m_fingerprintComputations[nodeId.ToPipId().Value])
                {
                    PipId? pipId = CachedGraph.PipGraph.TryFindProducerPipId(observedPath.Path, VersionDisposition.Latest, null);
                    if (pipId.HasValue)
                    {
                        var newNodeId = pipId.Value.ToNodeId();
                        if (pipId.Value.Value == nodeId.Value)
                        {
                            Console.Error.WriteLine("Pip depends on himself: " + GetPath(observedPath.Path));
                        }
                        else if ((allNodes == null || allNodes.Contains(newNodeId)) && (nodeToDepth == null || nodeToDepth.ContainsKey(newNodeId)))
                        {
                            yield return newNodeId;
                        }
                    }
                }
            }
        }

        public bool IsValidNode(PipType pipType)
        {
            return pipType == PipType.Process || pipType == PipType.CopyFile || pipType == PipType.WriteFile;
        }

        private TimeSpan GetElapsed(NodeId node)
        {
            return m_elapsedTimes[node.Value].ExecuteDuration;
        }

        private IDictionary<NodeId, int> ComputeAllNodeDepths(HashSet<NodeId> allNodes, HashSet<NodeId> frontierNodes)
        {
            Console.WriteLine("Computing node depth");
            ConcurrentDictionary<NodeId, int> nodeToDepth = new ConcurrentDictionary<NodeId, int>();
            Parallel.ForEach(allNodes, sourceNode =>
            {
                ComputeNodeDepth(sourceNode, allNodes, frontierNodes, nodeToDepth);
            });

            var frontierNodeToDepth = nodeToDepth.Where(x => x.Value >= 0).ToDictionary(x => x.Key, x => x.Value);
            Console.WriteLine("Total nodes found: " + nodeToDepth.Count + " Nodes in the frontier cone: " + frontierNodeToDepth.Count);
            return frontierNodeToDepth;
        }

        private int ComputeNodeDepth(NodeId node, HashSet<NodeId> allNodes, HashSet<NodeId> frontierNodes, IDictionary<NodeId, int> nodeToDepth)
        {
            int depth;
            bool hasDepth = nodeToDepth.TryGetValue(node, out depth);
            if (hasDepth)
            {
                return depth;
            }

            depth = frontierNodes.Contains(node) ? 0 : -1;
            foreach (var dependency in GetIncomingEdges(node, allNodes, null))
            {
                int dependencyDepth = ComputeNodeDepth(dependency, allNodes, frontierNodes, nodeToDepth);
                int computedDepth = dependencyDepth == -1 ? -1 : 1 + dependencyDepth;
                depth = Math.Max(depth, computedDepth);
            }

            nodeToDepth[node] = depth;
            return depth;
        }

        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            if (data.Step == PipExecutionStep.ExecuteProcess || data.Step == PipExecutionStep.ExecuteNonProcessPip)
            {
                var times = m_elapsedTimes[data.PipId.Value];

                times.ExecuteDuration = data.Duration > times.ExecuteDuration ? data.Duration : times.ExecuteDuration;
                m_elapsedTimes[data.PipId.Value] = times;
            }
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (data.StrongFingerprintComputations.Any() && data.StrongFingerprintComputations.Last().ObservedInputs.IsValid)
            {
                m_nodesWithObservedInputs.Add(data.PipId.ToNodeId());
                m_fingerprintComputations[data.PipId.Value] = data.StrongFingerprintComputations.Last().ObservedInputs.Where(x => x.Type == ObservedInputType.FileContentRead).ToList();
            }
        }
    }
}
