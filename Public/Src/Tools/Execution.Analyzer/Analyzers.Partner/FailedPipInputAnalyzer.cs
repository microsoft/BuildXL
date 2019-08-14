// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
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
        public Analyzer InitializeFailedPipInputAnalyzer()
        {
            string outputFilePath = null;
            bool tokenizeByMounts = false;
            List<string> inclusionMountNames = new List<string>();
            
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

            return new FailedPipInputAnalyzer(GetAnalysisInput())
            {
                TokenizeByMounts = tokenizeByMounts,
                InclusionMountNames = inclusionMountNames,
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteFailedPipInputAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("FailedPipInput Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.FailedPipInput), "Generates a json file containing input of failed pips and their transitive dependencies");
            writer.WriteOption("outputFile", "Required. The directory containing the cached pip graph files.", shortName: "o");
            writer.WriteOption("tokenizeByMounts", "Optional. Indicates whether paths should be tokenized by mount.");
            writer.WriteOption("includeMount", "Optional. Specifies the set of mounts which should be included for tokenization (unspecified implies all).");
        }
    }

    /// <summary>
    /// Analyzer used to generate fingerprint text file
    /// </summary>
    internal sealed class FailedPipInputAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the fingerprint file
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
        private readonly List<AbsolutePath> inputBuffer = new List<AbsolutePath>();
        private readonly VisitationTracker m_failedPipsClosure;
        private MountPathExpander m_mountPathExpander = null;
        private readonly VisitationTracker m_cachedPips;
        private readonly NodeVisitor nodeVisitor;
        private readonly ConcurrentBigMap<AbsolutePath, CompactSet<PipId>> m_fileToConsumerMap = new ConcurrentBigMap<AbsolutePath, CompactSet<PipId>>();

        private enum Pass
        {
            CollectFailedPips,
            CollectFileAccesses
        }

        public FailedPipInputAnalyzer(AnalysisInput input)
            : base(input)
        {
            nodeVisitor = new NodeVisitor(DataflowGraph);
            m_failedPipsClosure = new VisitationTracker(DataflowGraph);
            m_cachedPips = new VisitationTracker(DataflowGraph);
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

        protected override bool ReadEvents()
        {
            // NOTE: We read the execution log twice. First to collect failed pips, then to
            // collect file access data. This is a memory optimization because loading all file accesses
            // can use a lot of memory.

            // First pass to get failed pips and transitive dependencies
            Console.WriteLine("Pass 1 of 2: Collect failed pips");
            m_pass = Pass.CollectFailedPips;
            var result = base.ReadEvents();

            Console.WriteLine("Computing failed pip transitive dependency closure");

            // Compute transitive closure of failed pips
            nodeVisitor.VisitTransitiveDependencies(m_failedPips.Select(p => p.ToNodeId()), m_failedPipsClosure, n => true);

            // Second pass to collect file access data
            Console.WriteLine("Pass 2 of 2: Collect file accesses pips");
            m_pass = Pass.CollectFileAccesses;
            return result && base.ReadEvents();
        }

        public override bool CanHandleWorkerEvents => true;

        public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
        {
            if (m_pass == Pass.CollectFailedPips)
            {
                return eventId == ExecutionEventId.PipExecutionPerformance;
            }
            else
            {
                return eventId == ExecutionEventId.ProcessFingerprintComputation;
            }
        }

        public override int Analyze()
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

                writer.WritePropertyName("FileInfo");
                {
                    writer.WriteStartObject();

                    foreach (var fileEntry in m_fileToConsumerMap)
                    {
                        var file = fileEntry.Key;
                        var consumers = fileEntry.Value;
                        var path = ToDisplayFilePath(file);
                        if (path != null)
                        {
                            writer.WritePropertyName(path, true);
                            {
                                writer.WriteStartArray();

                                foreach (var consumer in consumers)
                                {
                                    writer.WriteValue(ToDisplayString(consumer));
                                }

                                writer.WriteEndArray();
                            }
                        }
                    }

                    writer.WriteEndObject();
                }

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

                    nodeVisitor.VisitTransitiveDependencies(m_failedPips.Select(p => p.ToNodeId()), m_failedPipsClosure, visitNode: node =>
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

            return 0;
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

        private bool IsCached(PipId pipId)
        {
            return m_cachedPips.WasVisited(pipId.ToNodeId());
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
                foreach (var observedInput in usedComputation.ObservedInputs)
                {
                    if (observedInput.Type == ObservedInputType.FileContentRead || observedInput.Type == ObservedInputType.ExistingFileProbe)
                    {
                        yield return observedInput.Path;
                    }
                }
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

        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            // Record set of failed pips and cached pips
            if (CachedGraph.PipTable.GetPipType(data.PipId) == PipType.Process)
            {
                switch (data.ExecutionPerformance.ExecutionLevel)
                {
                    case PipExecutionLevel.Cached:
                    case PipExecutionLevel.UpToDate:
                        m_cachedPips.MarkVisited(data.PipId.ToNodeId());
                        break;
                    case PipExecutionLevel.Failed:
                        m_failedPips.Add(data.PipId);
                        break;
                }
            }
        }
    }
}
