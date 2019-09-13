// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Execution.Analyzer.Analyzers.CacheMiss;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public global::BuildXL.Execution.Analyzer.Analyzer InitializeFailedPipsDumpAnalyzer()
        {
            string outputFilePath = null;
            var tokenizeByMounts = false;
            var inclusionMountNames = new List<string>();
            
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("tokenizeByMounts", StringComparison.OrdinalIgnoreCase))
                {
                    tokenizeByMounts = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("includeMount", StringComparison.OrdinalIgnoreCase))
                {
                    inclusionMountNames.Add(ParseStringOption(opt));
                }
                else
                {
                    throw Error("Unknown option for failed pip input analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                MissingRequiredOption("outputFile");
            }

            return new FailedPipsDumpAnalyzer(GetAnalysisInput())
            {
                TokenizeByMounts = tokenizeByMounts,
                InclusionMountNames = inclusionMountNames,
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteFailedPipsDumpAnalyzerHelp(HelpWriter writer)
        {
            // Note analyzer is similar to FailedPipInputAnalyzer the output contains failed pip information that
            // is used to clasify the type of failures. 
            writer.WriteBanner("FailedPipDump Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.FailedPipInput), "Generates a json file containing input of failed pips");
            writer.WriteOption("xl", "Optional. Compare to XLG, this must be the second XLG in the cmd line");
            writer.WriteOption("outputFile", "Required. The name and path of the JSON output .", shortName: "o");
            writer.WriteOption("tokenizeByMounts", "Optional. Indicates whether paths should be tokenized by mount.");
            writer.WriteOption("includeMount", "Optional. Specifies the set of mounts which should be included for tokenization (unspecified implies all).");
        }
    }

    //
    // Things TODO : 
    // Get DX errorcode 

    /// <summary>
    /// Analyzer used to generate failure fingerprint json file
    /// </summary>
    internal sealed class FailedPipsDumpAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        /// <summary>
        /// Indicates whether paths should be tokenized by mount
        /// </summary>
        public bool TokenizeByMounts;

        /// <summary>
        /// Specifies the set of mounts which should be included for tokenization (empty implies all)
        /// </summary>
        public List<string> InclusionMountNames { get; set; } = new List<string>();

        private Pass m_pass;

        private readonly HashSet<PipId> m_failedPips = new HashSet<PipId>();
        private readonly Dictionary<long, PipId> m_semiStableHashProcessPips = new Dictionary<long, PipId>();
        private readonly List<AbsolutePath> inputBuffer = new List<AbsolutePath>();
        private readonly VisitationTracker m_failedPipsClosure;
        private MountPathExpander m_mountPathExpander = null;
        private readonly VisitationTracker m_cachedPips;
        private readonly NodeVisitor nodeVisitor;
        private readonly ConcurrentBigMap<AbsolutePath, CompactSet<PipId>> m_fileToConsumerMap = new ConcurrentBigMap<AbsolutePath, CompactSet<PipId>>();
        private readonly ConcurrentDenseIndex<ProcessPipExecutionPerformance> m_pipPerformance = new ConcurrentDenseIndex<ProcessPipExecutionPerformance>(false);
        private readonly Dictionary<PipId, List<DependencyViolationEventData>> m_pipDependencyViolationEventData = new Dictionary<PipId, List<DependencyViolationEventData>>();
        private readonly Dictionary<PipId, ProcessExecutionMonitoringReportedEventData> m_pipProcessExecutionMonitoringReported = new Dictionary<PipId,ProcessExecutionMonitoringReportedEventData>();
        private readonly Dictionary<PipId, ReadOnlyArray<ObservedInput>> m_failedPipsObservedInputs = new Dictionary<PipId, ReadOnlyArray<ObservedInput>>();

        private readonly PipTable m_pipTable;
        private bool m_performSingleLogAnalysis = true;

        private readonly ConcurrentBigMap<AbsolutePath, FileArtifactContentDecidedEventData> m_fileContentMap = new ConcurrentBigMap<AbsolutePath, FileArtifactContentDecidedEventData>();
        private readonly ConcurrentBigMap<AbsolutePath, CompactSet<PipId>> m_fileToConsumerMapDiff = new ConcurrentBigMap<AbsolutePath, CompactSet<PipId>>();
        private readonly ConcurrentBigMap<AbsolutePath, CompactSet<PipId>> m_fileToConsumerMapAdded = new ConcurrentBigMap<AbsolutePath, CompactSet<PipId>>();

        private enum Pass
        {
            CollectFailedPips,
            CollectFileAccesses
        }

        public FailedPipsDumpAnalyzer(AnalysisInput input)
            : base(input)
        {
            nodeVisitor = new NodeVisitor(DataflowGraph);
            m_failedPipsClosure = new VisitationTracker(DataflowGraph);
            m_cachedPips = new VisitationTracker(DataflowGraph);
            m_pipTable = input.CachedGraph.PipTable;
        }

        public override void Prepare()
        {
            base.Prepare();

            if (!TokenizeByMounts)
            {
                return;
            }

            m_mountPathExpander = new MountPathExpander(PathTable);

            if (InclusionMountNames.Count == 0)
            {
                InclusionMountNames.AddRange(CachedGraph.MountPathExpander.GetAllRoots().Select(r => CachedGraph.MountPathExpander.GetSemanticPathInfo(r).RootName.ToString(StringTable)));
            }

            foreach (var name in InclusionMountNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (CachedGraph.MountPathExpander.TryGetRootByMountName(name, out var mountRoot))
                {
                    m_mountPathExpander.Add(PathTable, CachedGraph.MountPathExpander.GetSemanticPathInfo(mountRoot), forceTokenize: true);
                }
            }

        }

        internal Analyzer GetDiffAnalyzer(AnalysisInput input)
        {
            Console.WriteLine("Loaded new graph from compared to log");
            // Set analyzer to perform Compare and ignore single log analisys
            m_performSingleLogAnalysis = false;
            return new DiffAnalyzer(input, this);
        }

        protected override bool ReadEvents()
        {
            // NOTE: Read the execution log twice on a failed XLG. First to collect failed pips, then to
            // collect file access data. This is a memory optimization because loading all file accesses
            // can use a lot of memory.

            // First pass to get failed pips and transitive dependencies
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            Console.WriteLine($"Pass 1 of 2: Collect failed pips");
            m_pass = Pass.CollectFailedPips;
            var result = base.ReadEvents();
            stopwatch.Stop();
            Console.WriteLine($"Done reading pass 1 : duration = [{stopwatch.Elapsed}]");

            Console.WriteLine("Computing failed pip transitive dependency closure");

            // Compute transitive closure of failed pips
            nodeVisitor.VisitTransitiveDependencies(m_failedPips.Select(p => p.ToNodeId()), m_failedPipsClosure, n => true);

            // Second pass to collect file access data
            // TODO : Do this read in parallel with the other file
            Console.WriteLine("Pass 2 of 2: Collect file accesses pips");
            stopwatch.Start();
            m_pass = Pass.CollectFileAccesses;
            result &= base.ReadEvents();
            stopwatch.Stop();
            Console.WriteLine($"Done reading pass 2 : duration = [{stopwatch.Elapsed}]");

            return result;
        }

        public override int Analyze()
        {
            if (!m_performSingleLogAnalysis)
            {
                // Compare work is done instead
                return 0;
            }

            GenerateOutput(false);

            return 0;
        }

        /// <summary>
        /// Generate the output of failure data
        /// Failed pips + process chain : This points to the tool that failed.
        /// Set of dependencies diff contains: source and output changes. Which of those are directe referenced
        /// 
        /// </summary>
        /// <param name="isDiff">There is diff data from another log</param>
        private void GenerateOutput(bool isDiff)
        {
            Console.WriteLine("Writing failed pip info to '{0}'.", OutputFilePath);
            using (var streamWriter = new StreamWriter(OutputFilePath))
            using (JsonWriter writer = new JsonTextWriter(streamWriter))
            {
                writer.WriteStartObject();

                writer.WritePropertyName("Mounts");
                {
                    writer.WriteStartObject();

                    var mountPathExpander = m_mountPathExpander ?? CachedGraph.MountPathExpander;
                    foreach (var mountRoot in mountPathExpander.GetAllRoots())
                    {
                        var mount = mountPathExpander.GetSemanticPathInfo(mountRoot);
                        writer.WritePropertyName(mount.RootName.ToString(StringTable));
                        writer.WriteValue(mountRoot.ToString(PathTable));
                    }

                    writer.WriteEndObject();
                }

                writer.WriteWhitespace(Environment.NewLine);
                writer.WritePropertyName("Failures");
                {
                    writer.WriteStartArray();

                    foreach (var failedPip in m_failedPips)
                    {
                        var pip = (Process) m_pipTable.HydratePip(failedPip, PipQueryContext.ViewerAnalyzer);
                        writer.WriteWhitespace(Environment.NewLine);
                        writer.WriteStartObject();
                        // prints the semistable hash.
                        WritePropertyAndValue(writer, "PipId", ToDisplayString(failedPip));
                        WritePropertyAndValue(writer, "Working Directory", ToDisplayFilePath(pip.WorkingDirectory));

                        var provenance = pip.Provenance;
                        WritePropertyAndValue(
                            writer,
                            "Qualifier",
                            provenance != null ? PipGraph.Context.QualifierTable.GetCanonicalDisplayString(provenance.QualifierId) : string.Empty);
                        WritePropertyAndValue(
                            writer,
                            "OutputValueSymbol",
                            provenance != null ? provenance.OutputValueSymbol.ToString(SymbolTable) : string.Empty);

                        var pipPerformance = m_pipPerformance[pip.PipId.Value];
                        WritePropertyAndValue(writer, "PeakMemoryUsageMb", pipPerformance.PeakMemoryUsageMb.ToString());
                        WritePropertyAndValue(writer, "NumberOfProcesses", pipPerformance.NumberOfProcesses.ToString());
                        WritePropertyAndValue(
                            writer,
                            "FileMonitoringViolationsNotWhitelisted",
                            pipPerformance.FileMonitoringViolations.NumFileAccessViolationsNotWhitelisted.ToString());

                        if (isDiff)
                        {
                            WritePropertyAndValue(writer, "NumberOfImputChanges", GetDependencyChangesForPip(pip).ToString());
                        }

                        if (m_pipDependencyViolationEventData.TryGetValue(pip.PipId, out var pipDependencyViolationEvent))
                        {
                            foreach (var data in pipDependencyViolationEvent)
                            {
                                WritePropertyAndValue(writer, "DependencyViolationType", data.ViolationType.ToString());
                                WritePropertyAndValue(writer, "Path causing violation", ToDisplayFilePath(data.Path));
                            }
                        }

                        WriteProcessChain(writer, pip);
                        // TODO : Are environment variables usefull for analysis
                        //      : Are are all the failures required or choose count as cmd line arg to take top n
                        writer.WriteWhitespace(Environment.NewLine);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }

                writer.WriteWhitespace(Environment.NewLine);

                var fileToConsumerMap = m_fileToConsumerMap;
                var propertyName = "FileInfo";
                if (isDiff)
                {
                    propertyName = "FileInfoDiff";
                    fileToConsumerMap = m_fileToConsumerMapDiff;

                    // If any added dependencies add them to the output
                    if (m_fileToConsumerMapAdded.Count > 0)
                    {
                        WriteFileDependencies("FileInfoAdded", m_fileToConsumerMapAdded, writer);
                    }
                    // TODO:Add removed dependencies when compared to other log
                }
                WriteFileDependencies(propertyName, fileToConsumerMap, writer);

                writer.WriteWhitespace(Environment.NewLine);
                writer.WritePropertyName("PipGraph");
                {
                    writer.WriteStartObject();

                    m_failedPipsClosure.UnsafeReset();
                    writer.WritePropertyName("root");
                    {
                        writer.WriteStartArray();

                        foreach (var failedPip in m_failedPips)
                        {
                            writer.WriteValue(ToDisplayString(failedPip));
                        }

                        writer.WriteEndArray();
                    }

                    List<NodeId> dependencyBuffer = new List<NodeId>();

                    nodeVisitor.VisitTransitiveDependencies(
                        m_failedPips.Select(p => p.ToNodeId()),
                        m_failedPipsClosure,
                        visitNode: node =>
                                   {
                                       dependencyBuffer.Clear();
                                       foreach (var dependencyEdge in DataflowGraph.GetIncomingEdges(node))
                                       {
                                           if (PipTable.GetPipType(dependencyEdge.OtherNode.ToPipId()) != PipType.HashSourceFile)
                                           {
                                               dependencyBuffer.Add(dependencyEdge.OtherNode);
                                           }
                                       }

                                       if (dependencyBuffer.Count != 0)
                                       {
                                           writer.WritePropertyName(ToDisplayString(node.ToPipId()));
                                           {
                                               writer.WriteStartArray();

                                               foreach (var dependencyNode in dependencyBuffer)
                                               {
                                                   writer.WriteValue(ToDisplayString(dependencyNode.ToPipId()));
                                               }

                                               writer.WriteEndArray();
                                           }
                                       }

                                       return true;
                                   });

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }
        }

        /// <summary>
        /// This assumes the compare has been done
        /// TODO: add removed dependencies
        /// 
        /// </summary>
        /// <param name="processPip"></param>
        /// <returns>count of changes</returns>
        private int GetDependencyChangesForPip(Process processPip)
        {
            var changes = 0;
            foreach (var input in processPip.Dependencies)
            {
                if (m_fileToConsumerMapDiff.ContainsKey(input.Path) || m_fileToConsumerMapAdded.ContainsKey(input.Path))
                {
                    changes++;
                }
            }

            if (m_failedPipsObservedInputs.TryGetValue(processPip.PipId, out var observedInputs))
            {
                foreach (var input in observedInputs)
                {
                    if (m_fileToConsumerMapDiff.ContainsKey(input.Path) || m_fileToConsumerMapAdded.ContainsKey(input.Path))
                    {
                        changes++;
                    }
                }
            }
            return changes;
        }

        private void WriteFileDependencies(string property, ConcurrentBigMap<AbsolutePath, CompactSet<PipId>> fileToConsumerMap,  JsonWriter writer)
        {
            writer.WritePropertyName(property);
            {
                writer.WriteStartArray();

                foreach (var fileEntry in fileToConsumerMap)
                {
                    var file = fileEntry.Key;
                    var consumers = fileEntry.Value;
                    var path = ToDisplayFilePath(file);
                    if (path != null)
                    {
                        var fileArtifactContentDecidedEventData = m_fileContentMap[file];
                        // Print : { "Path": "value", "Type": "source/output", "FailPipReferenceCount": "count", "TotalPipsReferenceCount": "count", "Hash": "value", [pip array]}
                        writer.WriteStartObject();
                        WritePropertyAndValue(writer, "Path", path);
                        var typeOfFileArtifact = fileArtifactContentDecidedEventData.FileArtifact.IsSourceFile ? "SourceFile" : "OutputFile";
                        WritePropertyAndValue(writer, "Type", typeOfFileArtifact);
                        WritePropertyAndValue(writer, "FailPipReferenceCount", GetCountOfFailPips(consumers).ToString());
                        WritePropertyAndValue(writer, "TotalPipsReferenceCount", consumers.Count.ToString());
                        WritePropertyAndValue(writer, "Hash", fileArtifactContentDecidedEventData.FileContentInfo.Hash.ToString());
                        writer.WritePropertyName("Pips", true);
                        {
                            writer.WriteStartArray();

                            foreach (var consumer in consumers)
                            {
                                writer.WriteValue(ToDisplayString(consumer));
                            }

                            writer.WriteEndArray();
                        }
                        writer.WriteEndObject();
                        writer.WriteWhitespace(Environment.NewLine);
                    }
                }
                writer.WriteEndArray();
            }
        }

        private long GetCountOfFailPips(CompactSet<PipId> consumers)
        {
            return consumers.LongCount(pipId => m_failedPips.Contains(pipId));
        }

        private void WriteProcessChain(JsonWriter writer, Process pip)
        {
            writer.WriteWhitespace(Environment.NewLine);
            writer.WritePropertyName("ProcessChain");
            if (m_pipProcessExecutionMonitoringReported.TryGetValue(pip.PipId, out var processExecutionMonitoringReportedEvent))
            {
                var reportedProcesses = processExecutionMonitoringReportedEvent.ReportedProcesses;
                // Get the process chain for the object 
                // The pip's firstinvoked Proces.Executable and Process.Arguments from the pip
                {
                    writer.WriteStartArray();
                    foreach (var data in reportedProcesses)
                    {
                        writer.WriteStartObject();
                        WritePropertyAndValue(writer, "ProcessId", data.ProcessId.ToString(CultureInfo.InvariantCulture));
                        WritePropertyAndValue(writer, "ParentProcessId", data.ParentProcessId.ToString(CultureInfo.InvariantCulture));
                        WritePropertyAndValue(writer, "Path", data.Path);
                        WritePropertyAndValue(writer, "CommandLine", data.ProcessArgs);
                        WritePropertyAndValue(writer, "CreationTime", data.CreationTime.ToString(CultureInfo.InvariantCulture));
                        WritePropertyAndValue(writer, "ExitTime", data.ExitTime.ToString(CultureInfo.InvariantCulture));
                        WritePropertyAndValue(writer, "ExitCode", data.ExitCode.ToString(CultureInfo.InvariantCulture));
                        WritePropertyAndValue(writer, "KernelTime", data.KernelTime.ToString());
                        WritePropertyAndValue(writer, "UserTime", data.UserTime.ToString());
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
            }
            else
            {
                writer.WriteStartArray();
                writer.WriteStartObject();
                //TODO: place the BuildXL error here. This is an infrastructure error
                WritePropertyAndValue(writer, "BuildXLError", "Pip did not execute" + ToDisplayFilePath(pip.Executable.Path));
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
        }

        /// <summary>
        /// Performs the compare of the two logs 
        /// </summary>
        /// <param name="analyzer"></param>
        /// <returns></returns>
        public int Compare(Analyzer analyzer)
        {
            var diffAnalyzer = (DiffAnalyzer)analyzer;

            // Start with File Diff for the set of file input dependencies of all the failed  Pips
            // After this there is a collection of all the diff,added,removed
            // There are 3 cases:
            // 1- Same input paths on both XLG's for the failed Pips
            // 2- Added inputs on failed Pip
            // 3- Removed inputs on the failed Pip

            // Todo: Deal with other type of comparisons
            // - Added Pips
            // - Graph dependencies changes
            Console.WriteLine($"Count of file set to compare {m_fileToConsumerMap.Count}");

            var sourceFileCountDiff = 0;
            var outputFileCountDiff = 0;
            foreach (var fileEntry in m_fileToConsumerMap)
            {
                var file = fileEntry.Key;
                if (!m_fileContentMap.TryGetValue(file, out var fileArtifactContentDecidedEventData))
                {
                    Console.WriteLine($"{ToDisplayFilePath(file)} has no fileArtifactContentDecidedEventData event");
                    continue;
                }
                var fileHash = fileArtifactContentDecidedEventData.FileContentInfo.Hash;

                //                var fileArtifactContentDecidedEventData = m_fileContentMap[file];

                // Get file from the FileArtifactContentDecidedEventData from the other XLG to compare
                // The file may not be in the other XLG
                var path = ToDisplayFilePath(file).ToLowerInvariant();
                if (diffAnalyzer.TryGetFileArtifactContentDecidedEventData(path, out var fileArtifactContentDecidedEventDataOther))
                {
                    // There is a file
                    var fileHashOther = fileArtifactContentDecidedEventDataOther.FileContentInfo.Hash;

                    if (fileHash != fileHashOther)
                    {
                        // This will include tools also
                        m_fileToConsumerMapDiff[file] = fileEntry.Value;
                        if (fileArtifactContentDecidedEventData.FileArtifact.IsSourceFile)
                        {
                            sourceFileCountDiff++;
                        }
                        else
                        {
                            outputFileCountDiff++;
                        }
                    }
                }
                else
                {
                    // This is an added dependency on this XLG when compared to the previous one
                    // Ignore untracked files, and consider if to ignore AbsentFile
                    var isUntrackedFile = fileArtifactContentDecidedEventDataOther.FileContentInfo.Hash.Equals(WellKnownContentHashes.UntrackedFile);

                    if (!isUntrackedFile)
                    {
                        m_fileToConsumerMapAdded[file] = fileEntry.Value;
                    }
                }
            }

            // Write some statistics to console
            Console.WriteLine($"Diff dependency count: {m_fileToConsumerMapDiff.Count}");
            Console.WriteLine($"Diff source file count: {sourceFileCountDiff}");
            Console.WriteLine($"Diff output file count: {outputFileCountDiff}");
            Console.WriteLine($"Added dependency count: {m_fileToConsumerMapAdded.Count}");

            // TODO: Deal with environment 
            // Compare the environment for each failed/transitive dependencies 

            GenerateOutput(true);

            return 0;
        }

        private static void WritePropertyAndValue(JsonWriter writer, string property, string value)
        {
            writer.WritePropertyName(property);
            writer.WriteValue(value);
        }

        private string ToDisplayString(PipId pipId)
        {
            return "pip" + PipTable.GetPipSemiStableHash(pipId).ToString("X16", CultureInfo.InvariantCulture);
        }

        private string ToDisplayFilePath(AbsolutePath file)
        {
            return TokenizeByMounts
                ? m_mountPathExpander?.ExpandPath(PathTable, file)
                : file.ToString(PathTable);
        }

        private bool IsFailedPipOrDependency(PipId pipId)
        {
            return m_failedPipsClosure.WasVisited(pipId.ToNodeId());
        }

        /// <summary>
        /// This method is intended to be called by diff analyzer using the semistable hash
        /// </summary>
        /// <param name="semistableHash"></param>
        /// <returns>true if is one of the failed pips</returns>
        public bool IsFailedPipOrDependencyHash(long semistableHash)
        {
            // TODO: Keep track of dependencies also, not just failed pips to allow better comparison of changes
            return m_semiStableHashProcessPips.ContainsKey(semistableHash);
        }

        private bool IsCached(PipId pipId)
        {
            return m_cachedPips.WasVisited(pipId.ToNodeId());
        }

        private bool TryGetPipBySemiStableHash(long stableKey, out Process pip)
        {
            if (m_semiStableHashProcessPips.TryGetValue(stableKey, out var pipId))
            {
                pip = (Process)CachedGraph.PipGraph.GetPipFromPipId(pipId);
                return true;
            }

            pip = null;
            return false;
        }

        #region Log processing
        public override bool CanHandleWorkerEvents => true;

        public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
        {
            if (m_pass == Pass.CollectFailedPips)
            {
                return eventId == ExecutionEventId.PipExecutionPerformance || eventId == ExecutionEventId.FileArtifactContentDecided;
            }

            switch (eventId)
            {
                case ExecutionEventId.ProcessFingerprintComputation:
                case ExecutionEventId.ProcessExecutionMonitoringReported:
                case ExecutionEventId.DependencyViolationReported:

                    return true;
                case ExecutionEventId.ResourceUsageReported:
                case ExecutionEventId.PipExecutionPerformance:
                case ExecutionEventId.BuildSessionConfiguration:
                case ExecutionEventId.DirectoryMembershipHashed:
                case ExecutionEventId.ObservedInputs:
                case ExecutionEventId.WorkerList:
                case ExecutionEventId.PipExecutionStepPerformanceReported:
                case ExecutionEventId.PipCacheMiss:
                case ExecutionEventId.PipExecutionDirectoryOutputs:
                    return false;
                default:
                    return false;
            }
        }

        private IEnumerable<AbsolutePath> GetInputs(ProcessFingerprintComputationEventData data)
        {
            inputBuffer.Clear();
            var pip = GetPip(data.PipId);
            PipArtifacts.ForEachInput(pip, input =>
            {
                if (input.IsFile)
                {
                    inputBuffer.Add(input.Path);
                }

                return true;
            },
            includeLazyInputs: true);

            foreach (var input in inputBuffer)
            {
                yield return input;
            }

            if (CacheMissHelpers.TryGetUsedStrongFingerprintComputation(data, out var usedComputation))
            {
                // Save the observe inputs for failed pips
                if (m_failedPips.Contains(data.PipId))
                {
                    m_failedPipsObservedInputs.Add(data.PipId, usedComputation.ObservedInputs);
                }

                foreach (var observedInput in usedComputation.ObservedInputs)
                {
                    if (observedInput.Type == ObservedInputType.FileContentRead || observedInput.Type == ObservedInputType.ExistingFileProbe)
                    {
                        yield return observedInput.Path;
                    }
                }
            }
        }

        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            if (!IsFailedPipOrDependency(data.PipId))
            {
                // Only record inputs for failed pips or their transitive dependencies
                return;
            }

            // Contains : ReportedProcesses (process chain), 
            //            ReportedFileAccesses, WhitelistedReportedFileAccesses, ProcessDetouringStatuses
            m_pipProcessExecutionMonitoringReported.Add(data.PipId, data);
        }

        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            if (!IsFailedPipOrDependency(data.ViolatorPipId))
            {
                // Only record inputs for failed pips or their transitive dependencies
                return;
            }

            // Contains : ReportedProcesses (process chain), 
            //            ReportedFileAccesses, WhitelistedReportedFileAccesses, ProcessDetouringStatuses
            if (m_pipDependencyViolationEventData.TryGetValue(data.ViolatorPipId, out var entry))
            {
                entry.Add(data);
            }
            else
            {
                entry = new List<DependencyViolationEventData>()
                        {
                            data
                        };
                m_pipDependencyViolationEventData.Add(data.ViolatorPipId, entry);
            }
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (!IsFailedPipOrDependency(data.PipId))
            {
                // Only record inputs for failed pips or their transitive dependencies
                return;
            }

            if (IsCached(data.PipId) != (data.Kind == FingerprintComputationKind.CacheCheck))
            {
                // Only use cache lookup result when the pip was cached
                return;
            }

            foreach (var path in GetInputs(data))
            {
                m_fileToConsumerMap.AddOrUpdate(
                    path,
                    data.PipId,
                    addValueFactory: (key, pipId) =>
                    {
                        return new CompactSet<PipId>().Add(pipId);
                    },
                    updateValueFactory: (key, pipId, pipSet) =>
                    {
                        return pipSet.Add(pipId);
                    });
            }
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            // Need to save all this events to allow comparison. It might be possible to 
            // reduce the set as an optiomization once all the failed pip imputs have been stablished
            // The good thing here is that there is no need at this point to convert all the paths into strings
            m_fileContentMap[data.FileArtifact.Path] = data;
        }

        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            // Record set of failed pips and cached pips
            // Note: Having the DXXXX error code as part of this event will help
            // make an early desicion of the action to take for processing.
            if (CachedGraph.PipTable.GetPipType(data.PipId) == PipType.Process)
            {
                switch (data.ExecutionPerformance.ExecutionLevel)
                {
                    case PipExecutionLevel.Cached:
                    case PipExecutionLevel.UpToDate:
                        m_cachedPips.MarkVisited(data.PipId.ToNodeId());
                        break;
                    case PipExecutionLevel.Failed:
                    {
                        m_failedPips.Add(data.PipId);
                        if (data.ExecutionPerformance is ProcessPipExecutionPerformance processPipPerformance)
                        {
                            // Save the performance event of failed Pips
                            m_pipPerformance[data.PipId.Value] = processPipPerformance;
                        }
                        // Keep knowledge of semistable hash to pip 
                        // Hydradting pip here is expensive so get the semistable hash fron the CachedGraph
                        // var pip = (Process)m_pipTable.HydratePip(data.PipId, PipQueryContext.ViewerAnalyzer);
                        var semistableHash = CachedGraph.PipTable.GetPipSemiStableHash(data.PipId);
                        m_semiStableHashProcessPips.Add(semistableHash, data.PipId);
                    }
                        break;
                    case PipExecutionLevel.Executed:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        #endregion

        #region DiffAnalyzer
        
        /// <inheritdoc />
        /// <summary>
        /// This analyzer should just read the events that match failed pip dump analyzer
        /// and store pip information to allow comparison.
        /// </summary>
        private sealed class DiffAnalyzer : Analyzer
        {
            private readonly FailedPipsDumpAnalyzer m_failedPipsDumpAnalyzer;
            private readonly ConcurrentBigMap<AbsolutePath, CompactSet<PipId>> m_fileToConsumerMap = new ConcurrentBigMap<AbsolutePath, CompactSet<PipId>>();
            private readonly ConcurrentDenseIndex<ProcessPipExecutionPerformance> m_pipPerformance = new ConcurrentDenseIndex<ProcessPipExecutionPerformance>(false);
            private readonly ConcurrentDenseIndex<ProcessFingerprintComputationEventData> m_processFingerprintComputationEventData = new ConcurrentDenseIndex<ProcessFingerprintComputationEventData>(false);
            private readonly Dictionary<PipId, ProcessExecutionMonitoringReportedEventData> m_pipProcessExecutionMonitoringReported = new Dictionary<PipId, ProcessExecutionMonitoringReportedEventData>();
            private readonly Dictionary<long, PipId> m_semiStableHashProcessPips = new Dictionary<long, PipId>();
            private readonly List<AbsolutePath> inputBuffer = new List<AbsolutePath>();
            private readonly VisitationTracker m_failedPipsClosure;
            private readonly NodeVisitor nodeVisitor;
            private readonly PipTable m_pipTable;
            private MountPathExpander m_mountPathExpander = null;

            // This analyzer will allow query of file artifacts by name
            private readonly Dictionary<string, FileArtifactContentDecidedEventData> m_fileContentMap = new Dictionary<string, FileArtifactContentDecidedEventData>();

            /// <summary>
            /// Indicates whether paths should be tokenized by mount
            /// </summary>
            private readonly bool m_tokenizeByMounts;

            /// <summary>
            /// Specifies the set of mounts which should be included for tokenization (empty implies all)
            /// </summary>
            private List<string> InclusionMountNames { get; set; }

            public DiffAnalyzer(AnalysisInput input, FailedPipsDumpAnalyzer analyzer)
                : base(input)
            {
                m_failedPipsDumpAnalyzer = analyzer;
                m_failedPipsClosure = new VisitationTracker(DataflowGraph);
                nodeVisitor = new NodeVisitor(DataflowGraph);
                m_pipTable = input.CachedGraph.PipTable;
                m_tokenizeByMounts = analyzer.TokenizeByMounts;
                InclusionMountNames = analyzer.InclusionMountNames;
            }

            public override void Prepare()
            {
                base.Prepare();

                if (!m_tokenizeByMounts)
                {
                    return;
                }

                m_mountPathExpander = new MountPathExpander(PathTable);

                if (InclusionMountNames.Count == 0)
                {
                    InclusionMountNames.AddRange(CachedGraph.MountPathExpander.GetAllRoots().Select(r => CachedGraph.MountPathExpander.GetSemanticPathInfo(r).RootName.ToString(StringTable)));
                }

                foreach (var name in InclusionMountNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (CachedGraph.MountPathExpander.TryGetRootByMountName(name, out var mountRoot))
                    {
                        m_mountPathExpander.Add(PathTable, CachedGraph.MountPathExpander.GetSemanticPathInfo(mountRoot), forceTokenize: true);
                    }
                }

            }

            public override int Analyze()
            {
                // No analysis is done in this one just load the appropriate events to allow compare
                // TODO: Compute transitive closure of failed pips to see if the graph has changes, but this may be too noise in some cases
                return 0;
            }

            /// <summary>
            /// Determine if is a failed dependency of interest from the the failed build XLG
            /// TODO: Include transitive dependencies, because right now is just fintering on direct failed pips
            /// </summary>
            /// <param name="semistableHash"></param>
            /// <returns></returns>
            private bool IsFailedPipOrDependency(long  semistableHash)
            {
                return m_failedPipsDumpAnalyzer.IsFailedPipOrDependencyHash(semistableHash);
            }

            /// <summary>
            /// Gets the process pip by semistable hash
            /// </summary>
            /// <param name="semistableHash"></param>
            /// <returns></returns>
            public Process GetProcessPip(long semistableHash)
            {
                if (m_semiStableHashProcessPips.ContainsKey(semistableHash))
                {
                    var pipId = m_semiStableHashProcessPips[semistableHash];
                    return (Process)m_pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                }
                return null;
            }

            public bool TryGetFileArtifactContentDecidedEventData(string path, out FileArtifactContentDecidedEventData data)
            {
                return m_fileContentMap.TryGetValue(path, out data);
            }

            private string ToDisplayFilePath(AbsolutePath file)
            {
                return m_tokenizeByMounts
                    ? m_mountPathExpander?.ExpandPath(PathTable, file)
                    : file.ToString(PathTable);
            }

            #region Log processing

            public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
            {
                var semistableHash = CachedGraph.PipTable.GetPipSemiStableHash(data.PipId);
                if (!IsFailedPipOrDependency(semistableHash))
                {
                    // Only record inputs for failed pips or their transitive dependencies
                    return;
                }

                // Contains : ReportedProcesses (process chain), 
                //            ReportedFileAccesses, WhitelistedReportedFileAccesses, ProcessDetouringStatuses
                m_pipProcessExecutionMonitoringReported.Add(data.PipId, data);
            }

            public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
            {
                var semistableHash = CachedGraph.PipTable.GetPipSemiStableHash(data.PipId);
                if (!IsFailedPipOrDependency(semistableHash))
                {
                    // Only record inputs for failed pips or their transitive dependencies
                    return;
                }

                // Save the latest fingerprint event that has all the inputs 
                m_processFingerprintComputationEventData[data.PipId.Value] = data;
            }

            public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
            {
                var semistableHash = CachedGraph.PipTable.GetPipSemiStableHash(data.PipId);
                if (!IsFailedPipOrDependency(semistableHash))
                {
                    // Only record inputs for failed pips or their transitive dependencies
                    return;
                }

                if (data.ExecutionPerformance is ProcessPipExecutionPerformance processPipPerformance)
                {
                    // Save the performance event of failed Pips
                    m_pipPerformance[data.PipId.Value] = processPipPerformance;
                }

                m_semiStableHashProcessPips.Add(semistableHash, data.PipId);
            }

            [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
            public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
            {
                // need to save all this events to allow comparison
                var path = ToDisplayFilePath(data.FileArtifact.Path).ToLowerInvariant();
                m_fileContentMap[path] = data;
            }

            public override bool CanHandleWorkerEvents => true;

            public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
            {
                switch (eventId)
                {
                    // For perf reasons only load the file artifacts events
                    // May eventually need the ProcessFingerprintComputation event to 
                    // resolve removed inputs
                    case ExecutionEventId.FileArtifactContentDecided:
                        return true;
                    case ExecutionEventId.ProcessFingerprintComputation:
                    case ExecutionEventId.ProcessExecutionMonitoringReported:
                    case ExecutionEventId.PipExecutionPerformance:

                    case ExecutionEventId.ResourceUsageReported:
                    case ExecutionEventId.BuildSessionConfiguration:
                    case ExecutionEventId.DirectoryMembershipHashed:
                    case ExecutionEventId.ObservedInputs:
                    case ExecutionEventId.WorkerList:
                    case ExecutionEventId.DependencyViolationReported:
                    case ExecutionEventId.PipExecutionStepPerformanceReported:
                    case ExecutionEventId.PipCacheMiss:
                    case ExecutionEventId.PipExecutionDirectoryOutputs:
                        return false;
                    default:
                        return false;
                }
            }
#endregion
        }
#endregion
    }
}
