// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly ConcurrentDenseIndex<List<NodeId>> m_incomingEdges = new ConcurrentDenseIndex<List<NodeId>>(false);
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

            ComputeIncomingEdges(allNodes);

            IDictionary<NodeId, CriticalPathStats> nodeToCriticalPath = ComputeNodeCriticalPaths(allNodes);
            IDictionary<NodeId, int> nodeToDepth = ComputeAllNodeDepthsInExecutionCone(allNodes);
            (IDictionary<NodeId, List<int>> nodeToAffectedChanges, ISet<int> allChanges) = ParseNodeIdToAffectedChanges();
            IList<IList<NodeId>> nodeDepthList = ComputeNodeDepthList(nodeToDepth);
            IDictionary<NodeId, HashSet<int>> nodesToAffectedChange = ComputeAllDownstreamNodesAndPipCpuMinutes(nodeDepthList, new HashSet<NodeId>(nodeToDepth.Keys), nodeToAffectedChanges);
            IDictionary<int, ChangeImpactStats> changeToImpactStats = ComputeChangeImpact(nodeToCriticalPath, allChanges, nodesToAffectedChange);

            if (m_criticalPathWriter != null)
            {
                PrintCriticalPaths(nodeToCriticalPath, m_criticalPathWriter);
            }

            if (allChanges.Count > 0)
            {
                WriteChangeImpactLines(changeToImpactStats, m_pipImpactwriter);
            }

            Console.WriteLine("File impact analysis finished at: " + DateTime.Now);
            return 0;
        }

        private void ComputeIncomingEdges(HashSet<NodeId> allNodes)
        {
            Console.WriteLine("Starting incoming edges computation: " + DateTime.Now);
            Parallel.ForEach(allNodes, node =>
            {
                m_incomingEdges[node.Value] = GetIncomingEdges(node, allNodes).ToList();
            });
            Console.WriteLine("Ending incoming edges computation: " + DateTime.Now);
        }

        private struct ChangeImpactStats
        {
            public TimeSpan CriticalPathTime;
            public NodeId FirstNodeInCriticalPath;
            public TimeSpan TotalCpuTime;
            public TimeSpan SlowestNodeImpactedTime;
            public NodeId SlowestNodeImpacted;
        }

        private IDictionary<int, ChangeImpactStats> ComputeChangeImpact(
            IDictionary<NodeId, CriticalPathStats> nodeToCriticalPath,
            ISet<int> allChanges,
            IDictionary<NodeId, HashSet<int>> nodesToAffectedChange)
        {
            Console.WriteLine("Starting computing change impact " + DateTime.Now);
            var changeToImpactStats = new ConcurrentDictionary<int, ChangeImpactStats>();
            foreach (var change in allChanges)
            {
                changeToImpactStats[change] = new ChangeImpactStats { CriticalPathTime = TimeSpan.MinValue, TotalCpuTime = TimeSpan.Zero };
            }
            int i = 0;
            foreach (var nodeToAffectedChange in nodesToAffectedChange)
            {
                if (i % 10000 == 0)
                {
                    Console.WriteLine("Computed change impact for: " + i + " nodes out of " + nodesToAffectedChange.Count);
                }

                i++;
                Parallel.ForEach(nodeToAffectedChange.Value, affectedChange =>
                {
                    TimeSpan elapsedTime = GetElapsed(nodeToAffectedChange.Key);
                    bool betterCriticalPathTime = nodeToCriticalPath[nodeToAffectedChange.Key].CriticalPathTime > changeToImpactStats[affectedChange].CriticalPathTime;
                    bool slowerNodeAffected = elapsedTime > changeToImpactStats[affectedChange].SlowestNodeImpactedTime;
                    changeToImpactStats[affectedChange] =
                        new ChangeImpactStats
                        {
                            CriticalPathTime = betterCriticalPathTime ? nodeToCriticalPath[nodeToAffectedChange.Key].CriticalPathTime : changeToImpactStats[affectedChange].CriticalPathTime,
                            FirstNodeInCriticalPath = betterCriticalPathTime ? nodeToAffectedChange.Key : changeToImpactStats[affectedChange].FirstNodeInCriticalPath,
                            TotalCpuTime = elapsedTime + changeToImpactStats[affectedChange].TotalCpuTime,
                            SlowestNodeImpacted = slowerNodeAffected ? nodeToAffectedChange.Key : changeToImpactStats[affectedChange].SlowestNodeImpacted,
                            SlowestNodeImpactedTime = slowerNodeAffected ? elapsedTime : changeToImpactStats[affectedChange].SlowestNodeImpactedTime
                        };
                });
            }

            Console.WriteLine("Done computing change impact " + DateTime.Now);
            return changeToImpactStats;
        }

        private void PrintCriticalPaths(IDictionary<NodeId, CriticalPathStats> criticalPaths, StreamWriter writer)
        {
            writer.WriteLine("Pip Id,Time Taken(mins),Critical Path (mins),Next Pip Id");
            foreach (var criticalPath in criticalPaths.OrderByDescending(x => x.Value.CriticalPathTime).ThenBy(x => x.Key.Value))
            {
                var pip = CachedGraph.PipTable.HydratePip(criticalPath.Key.ToPipId(), PipQueryContext.ViewerAnalyzer);
                var nextPip = CachedGraph.PipTable.HydratePip(criticalPath.Value.NextNodeInCriticalPath.ToPipId(), PipQueryContext.ViewerAnalyzer);

                writer.WriteLine(pip.FormattedSemiStableHash + "," + GetElapsed(criticalPath.Key) + "," + criticalPath.Value.CriticalPathTime.TotalMinutes + "," + nextPip.FormattedSemiStableHash);
            }
        }

        private (IDictionary<NodeId, List<int>>, HashSet<int>) ParseNodeIdToAffectedChanges()
        {
            Console.WriteLine("Parsing node to change at " + DateTime.Now);
            IDictionary<NodeId, List<int>> nodeToAffectedCriticalPath = new Dictionary<NodeId, List<int>>();
            IDictionary<long, List<int>> sshToAffectedCriticalPath = new Dictionary<long, List<int>>();
            HashSet<int> allChanges = new HashSet<int>();

            if (!string.IsNullOrWhiteSpace(m_pipToListOfAffectingChangesFile))
            {
                foreach (var line in File.ReadAllLines(m_pipToListOfAffectingChangesFile))
                {
                    string[] parts = line.Split(' ');
                    long semiStableHash = Convert.ToInt64(parts[0].ToUpperInvariant().Replace("PIP", string.Empty), 16);
                    sshToAffectedCriticalPath[semiStableHash] = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        int changeId = int.Parse(parts[i]);
                        allChanges.Add(changeId);
                        sshToAffectedCriticalPath[semiStableHash].Add(changeId);
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

            Console.WriteLine("Done parsing node to change at " + DateTime.Now + " " + allChanges.Count + " changes affected " + nodeToAffectedCriticalPath.Count + " nodes.");
            return (nodeToAffectedCriticalPath, allChanges);
        }

        private string GetPath(AbsolutePath path)
        {
            return CachedGraph.PipGraph.SemanticPathExpander.ExpandPath(CachedGraph.Context.PathTable, path);
        }

        private void WriteChangeImpactLines(
            IDictionary<int, ChangeImpactStats> changeImpactStats,
            StreamWriter writer)
        {
            Console.WriteLine("Computing change impact output strings at " + DateTime.Now);
            var outputLines = new ConcurrentDictionary<int, string>();
            var criticalPathTimes = new ConcurrentDictionary<int, TimeSpan>();
            Parallel.ForEach(changeImpactStats, change =>
            {
                var firstPipInCriticalPath = CachedGraph.PipTable.HydratePip(change.Value.FirstNodeInCriticalPath.ToPipId(), PipQueryContext.ViewerAnalyzer);
                string firstPipInCriticalPathDescription = firstPipInCriticalPath.GetDescription(CachedGraph.Context).Replace(',', ';');
                var longestPipAffected = CachedGraph.PipTable.HydratePip(change.Value.SlowestNodeImpacted.ToPipId(), PipQueryContext.ViewerAnalyzer);
                string longestPipAffectedDescription = longestPipAffected.GetDescription(CachedGraph.Context).Replace(',', ';');
                outputLines[change.Key] = string.Join(",",
                    change.Key,
                    change.Value.CriticalPathTime.TotalMinutes,
                    change.Value.TotalCpuTime.TotalMinutes,
                    change.Value.SlowestNodeImpactedTime.TotalMinutes,
                    firstPipInCriticalPathDescription,
                    longestPipAffectedDescription);
            });

            var sortedOutputLines = outputLines.OrderByDescending(x => changeImpactStats[x.Key].CriticalPathTime).ThenBy(x => x.Value).ToList();
            Console.WriteLine("Writing change impact at " + DateTime.Now);
            string headerLine = "Change Id,Longest critical path affected (mins),Total CPU time affected (mins),Time taken for longest pip affected,First pip in critical path,Longest pip affected";
            writer.WriteLine(headerLine);
            foreach (var criticalPath in sortedOutputLines)
            {
                writer.WriteLine(criticalPath.Value);
            }

            Console.WriteLine("Done writing change impact at " + DateTime.Now);
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

        private IDictionary<NodeId, CriticalPathStats> ComputeNodeCriticalPaths(HashSet<NodeId> allNodes)
        {
            CriticalPath criticalPathCalculator = new CriticalPath(GetElapsed, (nodeId) => CachedGraph.DataflowGraph.GetOutgoingEdges(nodeId).Select(edge => edge.OtherNode));
            var now = DateTime.Now;
            Console.WriteLine("Computing critical paths " + now);
            TimeSpan longestCriticalPath = TimeSpan.Zero;
            var nodeToCriticalPath = new ConcurrentDictionary<NodeId, CriticalPathStats>();
            foreach (var sourceNode in allNodes)
            {
                nodeToCriticalPath[sourceNode] = criticalPathCalculator.ComputeCriticalPath(sourceNode);
                if (nodeToCriticalPath[sourceNode].CriticalPathTime > longestCriticalPath)
                {
                    Console.WriteLine("new longest critical path: " + nodeToCriticalPath[sourceNode].CriticalPathTime);
                    longestCriticalPath = nodeToCriticalPath[sourceNode].CriticalPathTime;
                }
            }

            Console.WriteLine("Done computing critical paths in " + (DateTime.Now - now));
            return nodeToCriticalPath;
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

        private IList<IList<NodeId>> ComputeNodeDepthList(IDictionary<NodeId, int> nodeToDepth)
        {
            Console.WriteLine("Computing node to depth list at " + DateTime.Now);
            int maxDepth = nodeToDepth.Values.Max();
            IList<IList<NodeId>> nodesAtDepths = new List<IList<NodeId>>(maxDepth + 1);
            HashSet<NodeId> allNodes = new HashSet<NodeId>();
            for (int i = 0; i < maxDepth + 1; i++)
            {
                nodesAtDepths.Add(new List<NodeId>());
            }

            foreach (var node in nodeToDepth)
            {
                if (node.Value >= 0)
                {
                    nodesAtDepths[node.Value].Add(node.Key);
                }
            }

            Console.WriteLine("Done computing node to depth list at " + DateTime.Now);
            return nodesAtDepths;
        }

        private IDictionary<NodeId, HashSet<int>> ComputeAllDownstreamNodesAndPipCpuMinutes(
            IList<IList<NodeId>> nodesAtDepths,
            HashSet<NodeId> allNodes,
            IDictionary<NodeId, List<int>> nodeToChanges)
        {
            var nodeToAffectedChanges = new ConcurrentDictionary<NodeId, HashSet<int>>();
            for (int currentDepth = 0; currentDepth < nodesAtDepths.Count; currentDepth++)
            {
                var nodesAtDepth = nodesAtDepths[currentDepth];
                Console.WriteLine("At Depth: " + currentDepth + " of " + nodesAtDepths.Count + " with: " + nodesAtDepth.Count + " nodes at this depth.");
                Parallel.ForEach(nodesAtDepth, nodeId =>
                {
                    bool assosiatedWithChange = nodeToChanges.ContainsKey(nodeId);
                    if (assosiatedWithChange || m_incomingEdges[nodeId.Value].Count > 0)
                    {
                        var affectedChanges = nodeToAffectedChanges.GetOrAdd(nodeId, (node) => { return new HashSet<int>(); });
                        if (assosiatedWithChange)
                        {
                            affectedChanges.AddRange(nodeToChanges[nodeId]);
                        }

                        foreach (var incomingNode in m_incomingEdges[nodeId.Value])
                        {
                            var upstreamAffectedChanges = nodeToAffectedChanges.GetOrAdd(incomingNode, (node) => { return new HashSet<int>(); });
                            affectedChanges.UnionWith(upstreamAffectedChanges);
                        }
                    }
                });
            }

            return nodeToAffectedChanges;
        }

        public IEnumerable<NodeId> GetIncomingEdges(NodeId nodeId, HashSet<NodeId> allNodes)
        {
            foreach (var incomingNode in CachedGraph.DataflowGraph.GetIncomingEdges(nodeId))
            {
                if (allNodes == null || allNodes.Contains(incomingNode.OtherNode))
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
                        else if (allNodes == null || allNodes.Contains(newNodeId))
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

        private IDictionary<NodeId, int> ComputeAllNodeDepthsInExecutionCone(HashSet<NodeId> allNodes)
        {
            Console.WriteLine("Computing node depth at " + DateTime.Now);
            var nodeToDepth = new ConcurrentDictionary<NodeId, int>();
            Parallel.ForEach(allNodes, sourceNode =>
            {
                ComputeNodeDepth(sourceNode, nodeToDepth);
            });

            var frontierNodeToDepth = nodeToDepth.Where(x => x.Value > 0).ToDictionary(x => x.Key, x => x.Value);
            Console.WriteLine("Total nodes found: " + nodeToDepth.Count + " Nodes in the execution cone: " + frontierNodeToDepth.Count + " at: " + DateTime.Now);
            return frontierNodeToDepth;
        }

        private int ComputeNodeDepth(NodeId node, IDictionary<NodeId, int> nodeToDepth)
        {
            int result;
            if (nodeToDepth.TryGetValue(node, out result))
            {
                return result;
            }

            bool builds = GetElapsed(node).Ticks > 0;
            int maxDepth = 1;
            foreach (var dependency in m_incomingEdges[node.Value])
            {
                int dependencyDepth = ComputeNodeDepth(dependency, nodeToDepth);
                int computedDepth = 1 + Math.Abs(dependencyDepth);
                builds |= dependencyDepth > 0;
                maxDepth = Math.Max(maxDepth, computedDepth);
            }

            if (!builds)
            {
                maxDepth *= -1;
            }

            nodeToDepth[node] = maxDepth;
            return nodeToDepth[node];
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
