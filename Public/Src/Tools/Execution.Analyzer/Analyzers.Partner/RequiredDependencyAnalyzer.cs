// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Execution.Analyzer.Analyzers.Simulator;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeRequiredDependencyAnalyzer()
        {
            var analyzer = new RequiredDependencyAnalyzer(GetAnalysisInput());

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("out", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    analyzer.OutputFilePath = ParseSingletonPathOption(opt, analyzer.OutputFilePath);
                }
                else if (opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    analyzer.SemiStableHashes.Add(ParseSemistableHash(opt));
                }
                else if (opt.Name.Equals("noedges", StringComparison.OrdinalIgnoreCase))
                {
                    analyzer.AddEdgesForPips = !ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("simulatorIncrement", StringComparison.OrdinalIgnoreCase))
                {
                    analyzer.SimulatorIncrement = ParseInt32Option(opt, 1, int.MaxValue);
                }
                else if (opt.Name.Equals("allAccesses", StringComparison.OrdinalIgnoreCase))
                {
                    analyzer.AllAccesses = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for required dependency analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(analyzer.OutputFilePath))
            {
                throw Error("Output file must be specified with /out");
            }

            return analyzer;
        }
    }

    /// <summary>
    /// Provides analysis for determining required vs optional dependencies for processes based on the declared and observed dependencies
    /// </summary>
    internal sealed class RequiredDependencyAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the fingerprint file
        /// </summary>
        public string OutputFilePath;

        private StreamWriter m_writer;

        public long ProcessingPips = 0;
        public long ProcessedPips = 0;
        public bool AddEdgesForPips = true;
        public int? SimulatorIncrement = null;
        public bool AllAccesses = false;
        public HashSet<long> SemiStableHashes = new HashSet<long>();

        private static readonly Comparer<PipEntry> s_pipEntryComparer = new ComparerBuilder<PipEntry>()
            .CompareByAfter(p => p.SpecFile.RawValue)
            .CompareByAfter(p => p.PipId.Value);

        private static readonly Comparer<DirectoryArtifact> s_directoryComparer = new ComparerBuilder<DirectoryArtifact>()
            .CompareByAfter(d => d.Path.RawValue)
            .CompareByAfter(d => d.PartialSealId);

        private static readonly Comparer<FileArtifact> s_fileComparer = new ComparerBuilder<FileArtifact>()
           .CompareByAfter(d => d.Path.RawValue)
           .CompareByAfter(d => d.RewriteCount);

        private static readonly Comparer<FileReference> s_fileReferenceComparer = new ComparerBuilder<FileReference>()
            .CompareByAfter(r => r.Producer, s_pipEntryComparer)
            .CompareByAfter(r => r.Directory?.Producer, s_pipEntryComparer)
            .CompareByAfter(r => r.Directory?.Directory ?? DirectoryArtifact.Invalid, s_directoryComparer)
            .CompareByAfter(r => r.ConsumedFile.File.Artifact, s_fileComparer);

        private ConcurrentBigMap<AbsolutePath, PipId> m_dynamicFileProducers = new ConcurrentBigMap<AbsolutePath, PipId>();
        private ConcurrentBigMap<PipId, PipId> m_dynamicDirectoryProducers = new ConcurrentBigMap<PipId, PipId>();
        private ConcurrentBigMap<PipId, bool> m_isSourceOnlySeal = new ConcurrentBigMap<PipId, bool>();
        private ConcurrentBigMap<DirectoryArtifact, ReadOnlyArray<FileArtifact>> m_dynamicContents = new ConcurrentBigMap<DirectoryArtifact, ReadOnlyArray<FileArtifact>>();

        private ConcurrentBigMap<PipId, PipEntry> m_pipEntries = new ConcurrentBigMap<PipId, PipEntry>();
        private ConcurrentBigMap<FileArtifact, FileEntry> m_fileEntries = new ConcurrentBigMap<FileArtifact, FileEntry>();
        private ConcurrentBigMap<DirectoryArtifact, DirectoryEntry> m_directoryEntries = new ConcurrentBigMap<DirectoryArtifact, DirectoryEntry>();

        private ConcurrentBigMap<FileArtifact, FileArtifact> m_copiedFilesByTarget = new ConcurrentBigMap<FileArtifact, FileArtifact>();

        private ObjectPool<WorkerAnalyzer> m_pool;
        private List<WorkerAnalyzer> m_analyzers = new List<WorkerAnalyzer>();
        private ActionBlockSlim<Action> m_block = new ActionBlockSlim<Action>(12, a => a());

        private MutableDirectedGraph m_mutableGraph = new MutableDirectedGraph();
        private PipTable m_pipTable;

        public RequiredDependencyAnalyzer(AnalysisInput input)
            : base(input)
        {
            m_pool = new ObjectPool<WorkerAnalyzer>(() => CreateAnalyzer(), w => { });
            m_pipTable = EngineSchedule.CreateEmptyPipTable(input.CachedGraph.Context);
        }

        private WorkerAnalyzer CreateAnalyzer()
        {
            var w = new WorkerAnalyzer(this, "worker");

            lock (m_analyzers)
            {
                m_analyzers.Add(w);
            }

            return w;
        }

        public bool IsSourceOnlySeal(PipId pipId)
        {
            return m_isSourceOnlySeal.GetOrAdd(pipId, true, (k, v) =>
            {
                var sealKind = PipTable.GetSealDirectoryKind(k);
                if (sealKind == SealDirectoryKind.SourceAllDirectories || sealKind == SealDirectoryKind.SourceTopDirectoryOnly)
                {
                    return true;
                }

                if (sealKind == SealDirectoryKind.Full || sealKind == SealDirectoryKind.Partial)
                {
                    var isSourceOnly = true;
                    var sd = (SealDirectory)GetPip(k);
                    foreach (var file in sd.Contents)
                    {
                        if (file.IsOutputFile)
                        {
                            isSourceOnly = false;
                        }
                    }

                    return isSourceOnly;
                }

                return false;
            }).Item.Value;
        }

        public override void Prepare()
        {
            Directory.CreateDirectory(OutputFilePath);
            m_writer = new StreamWriter(Path.Combine(OutputFilePath, "results.txt"));

            Console.WriteLine("Creating nodes");

            foreach (var node in DataflowGraph.Nodes)
            {
                m_mutableGraph.CreateNode();
            }

            Console.WriteLine("Created nodes");

            foreach (var entry in PipGraph.AllOutputDirectoriesAndProducers)
            {
                var sealId = PipGraph.GetSealedDirectoryNode(entry.Key).ToPipId();
                m_dynamicDirectoryProducers[sealId] = entry.Value;
            }

            foreach (CopyFile copyFile in PipGraph.RetrievePipsOfType(PipType.CopyFile))
            {
                m_copiedFilesByTarget[copyFile.Destination] = copyFile.Source;
            }

            foreach (var directory in PipGraph.AllSealDirectories)
            {
                var sealId = PipGraph.GetSealedDirectoryNode(directory).ToPipId();
                var sealKind = PipTable.GetSealDirectoryKind(sealId);

                // Populate map of whether this is a source only seal
                IsSourceOnlySeal(sealId);

                if (sealKind == SealDirectoryKind.Full || sealKind == SealDirectoryKind.Partial)
                {
                    PipId? singleProducer = null;
                    foreach (var file in PipGraph.ListSealedDirectoryContents(directory))
                    {
                        if (file.IsOutputFile)
                        {
                            var producer = PipGraph.TryGetProducer(file);
                            if (singleProducer == null)
                            {
                                singleProducer = producer;
                            }
                            else if (singleProducer != producer)
                            {
                                singleProducer = PipId.Invalid;
                            }
                        }
                    }

                    if (singleProducer.HasValue && singleProducer.Value.IsValid)
                    {
                        m_dynamicDirectoryProducers[sealId] = singleProducer.Value;
                    }
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            Console.WriteLine($"Analyzing");

            m_block.Complete();
            m_block.CompletionAsync().GetAwaiter().GetResult();

            Console.WriteLine($"Writing Graph");

            foreach (var pip in PipGraph.RetrieveAllPips())
            {
                var serializedPip = pip;
                var nodeId = pip.PipId.ToNodeId();

                bool addEdges = true;
                if (pip.PipType == PipType.Process)
                {
                    var entry = GetEntry(pip.PipId);
                    serializedPip = entry.Process;
                    addEdges = !entry.AddedEdges;
                }

                if (addEdges && AddEdgesForPips)
                {
                    using (var scope = m_mutableGraph.AcquireExclusiveIncomingEdgeScope(nodeId))
                    {
                        foreach (var edge in DataflowGraph.GetIncomingEdges(nodeId))
                        {
                            scope.AddEdge(edge.OtherNode, edge.IsLight);
                        }
                    }
                }

                serializedPip.ResetPipIdForTesting();
                m_pipTable.Add(nodeId.Value, serializedPip);
            }

            m_mutableGraph.Seal();

            CachedGraph.Serializer.SerializeToFileAsync(
                    GraphCacheFile.DirectedGraph,
                    m_mutableGraph.Serialize,
                    Path.Combine(OutputFilePath, nameof(GraphCacheFile.DirectedGraph)))
                .GetAwaiter().GetResult();

            CachedGraph.Serializer.SerializeToFileAsync(
                    GraphCacheFile.PipTable,
                    w => m_pipTable.Serialize(w, Environment.ProcessorCount),
                    Path.Combine(OutputFilePath, nameof(GraphCacheFile.PipTable)))
                .GetAwaiter().GetResult();

            CachedGraph.Serializer.SerializeToFileAsync(
                    GraphCacheFile.PipGraphId,
                    PipGraph.SerializeGraphId,
                    Path.Combine(OutputFilePath, nameof(GraphCacheFile.PipGraphId)))
                .GetAwaiter().GetResult();

            Console.WriteLine($"Simulating [Reading]");
            var simulator = new BuildSimulatorAnalyzer(Input);
            simulator.Increment = SimulatorIncrement ?? simulator.Increment;
            simulator.ExecutionData.DataflowGraph = m_mutableGraph;

            simulator.OutputDirectory = OutputFilePath;
            simulator.ReadExecutionLog();

            Console.WriteLine($"Simulating [Analyzing]");
            simulator.Analyze();

            Console.WriteLine($"Blocking Dependency Analysis");

            DisplayTable<DepColumn> depTable = new DisplayTable<DepColumn>(" , ");
            foreach (var pipId in PipTable.Keys)
            {
                var pipType = PipTable.GetPipType(pipId);
                if (pipType == PipType.Process)
                {
                    var entry = GetEntry(pipId);
                    (PipId node, ulong cost) maxConsumedDependency = default;
                    (PipId node, ulong cost) maxDependency = default;

                    foreach (var dep in entry.PipDependencies)
                    {
                        var cost = simulator.ExecutionData.BottomUpAggregateCosts[dep.Key.ToNodeId()];
                        if (!maxDependency.node.IsValid || cost > maxDependency.cost)
                        {
                            maxDependency = (dep.Key, cost);
                        }

                        if (dep.Value != null && dep.Value.HasFlag(ContentFlag.Consumed))
                        {
                            if (!maxConsumedDependency.node.IsValid || cost > maxConsumedDependency.cost)
                            {
                                maxConsumedDependency = (dep.Key, cost);
                            }
                        }
                    }

                    depTable.NextRow();
                    depTable.Set(DepColumn.Id, $"{entry.SpecFileName}-{entry.Identifier}");
                    depTable.Set(DepColumn.MaxConsumedDependency, ToString(maxConsumedDependency.node));
                    depTable.Set(DepColumn.MaxConsumedDependencyChainCost, maxConsumedDependency.cost.ToMinutes());
                    depTable.Set(DepColumn.MaxDependency, ToString(maxDependency.node));
                    depTable.Set(DepColumn.MaxDependencyChainCost, maxDependency.cost.ToMinutes());
                }
                else if (pipType == PipType.SealDirectory
                    && !PipTable.GetSealDirectoryKind(pipId).IsSourceSeal()
                    && !IsSourceOnlySeal(pipId))
                {
                    var seal = (SealDirectory)GetPip(pipId);
                    var entry = GetEntry(seal.Directory);
                    (PipId node, ulong cost) maxDependency = default;

                    foreach (var dep in DataflowGraph.GetIncomingEdges(pipId.ToNodeId()))
                    {
                        var cost = simulator.ExecutionData.BottomUpAggregateCosts[dep.OtherNode];
                        if (!maxDependency.node.IsValid || cost > maxDependency.cost)
                        {
                            maxDependency = (dep.OtherNode.ToPipId(), cost);
                        }
                    }

                    depTable.NextRow();
                    depTable.Set(DepColumn.Id, $"{entry.SpecFileName}-{entry.Identifier} ({entry.FileCount} files)");
                    depTable.Set(DepColumn.MaxDependency, ToString(maxDependency.node));
                    depTable.Set(DepColumn.MaxDependencyChainCost, maxDependency.cost.ToMinutes());
                    depTable.Set(DepColumn.Directory, seal.DirectoryRoot.ToString(PathTable));
                }
            }

            using (var blockAnalysisWriter = new StreamWriter(Path.Combine(OutputFilePath, "blockAnalysis.txt")))
            {
                depTable.Write(blockAnalysisWriter);
            }

            m_writer.Dispose();

            Console.WriteLine($"Analyzing complete");

            return 0;
        }

        private string ToString(PipId pipId)
        {
            if (!pipId.IsValid)
            {
                return string.Empty;
            }

            var pipType = PipTable.GetPipType(pipId);
            var type = pipType == PipType.SealDirectory ? PipTable.GetSealDirectoryKind(pipId).ToString() : pipType.ToString();
            return $"{PipTable.GetFormattedSemiStableHash(pipId)} [{type}]";
        }

        private enum DepColumn
        {
            Id,
            MaxConsumedDependency,
            MaxConsumedDependencyChainCost,
            MaxDependency,
            MaxDependencyChainCost,
            Directory
        }

        private PipId GetActualDependencyId(PipId pipId)
        {
            if (m_dynamicDirectoryProducers.TryGetValue(pipId, out var actual))
            {
                return actual;
            }

            return pipId;
        }

        private PipEntry GetEntry(PipId pipId)
        {
            if (!pipId.IsValid)
            {
                return null;
            }

            var result = m_pipEntries.GetOrAdd(pipId, this, (key, _this) => _this.CreateEntry(key));
            var entry = result.Item.Value;
            return entry;
        }

        private PipEntry CreateEntry(PipId pipId)
        {
            var pip = GetPip(pipId);

            var entry = new PipEntry()
            {
                PipId = pipId,
                PipType = pip.PipType,
                Process = pip as Process,
                SemistableHash = pip.SemiStableHash,
                Identifier = $"{pip.FormattedSemiStableHash} [{pip.PipType}]",
                SpecFile = pip.Provenance?.Token.Path ?? AbsolutePath.Invalid,
            };

            if (entry.SpecFile.IsValid)
            {
                entry.SpecFileName = entry.SpecFile.GetName(PathTable).ToString(StringTable);
            }

            return entry;
        }

        private FileEntry GetEntry(FileArtifact file)
        {
            var result = m_fileEntries.GetOrAdd(file, this, (key, _this) => _this.CreateEntry(key));
            var entry = result.Item.Value;
            if (file.IsOutputFile && entry.Producer == null)
            {
                var producer = GetProducer(file);
                if (producer.IsValid)
                {
                    entry.Producer = GetEntry(producer);
                }
            }

            return entry;
        }

        private FileEntry CreateEntry(FileArtifact file)
        {
            var pipId = GetProducer(file);

            return new FileEntry()
            {
                Artifact = file,
                Producer = GetEntry(pipId),
            };
        }

        private DirectoryEntry GetEntry(DirectoryArtifact dir)
        {
            if (!dir.IsValid)
            {
                return null;
            }

            var result = m_directoryEntries.GetOrAdd(dir, this, (key, _this) => _this.CreateEntry(key));
            var entry = result.Item.Value;
            if (!result.IsFound)
            {
                var pipId = PipGraph.TryGetProducer(dir);
                entry.Producer = GetEntry(pipId);
            }

            return entry;
        }

        private DirectoryEntry CreateEntry(DirectoryArtifact dir)
        {
            var sealId = PipGraph.GetSealedDirectoryNode(dir).ToPipId();
            var sealKind = PipTable.GetSealDirectoryKind(sealId);

            var pipId = PipGraph.TryGetProducer(dir);
            var producer = GetEntry(pipId);

            var pip = (SealDirectory)GetPip(sealId);

            var kind = pip.Kind.ToString();
            if (pip.IsComposite)
            {
                kind = "Composite" + kind;
            }

            var entry = new DirectoryEntry()
            {
                Directory = dir,
                Producer = GetEntry(pipId),
                FileCount = GetContents(dir).Length,
                Kind = sealKind,
                SemistableHash = producer?.Identifier ?? string.Empty,
                Identifier = $"{pip.FormattedSemiStableHash} [{kind}]",
                Id = dir.PartialSealId.ToString(),
            };

            if (pip.Provenance.Token.Path.IsValid)
            {
                entry.SpecFileName = pip.Provenance.Token.Path.GetName(PathTable).ToString(StringTable);
            }

            return entry;
        }

        private bool TryResolve(FileArtifact file, PipEntry consumer, out FileArtifact resolvedFile)
        {
            resolvedFile = default;

            bool result = false;
            while (file.IsOutputFile)
            {
                if (m_copiedFilesByTarget.TryGetValue(file, out var source))
                {
                    resolvedFile = source;
                    file = source;
                    result = true;
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        private ReadOnlyArray<FileArtifact> GetContents(DirectoryArtifact directoryArtifact)
        {
            var sealId = PipGraph.GetSealedDirectoryNode(directoryArtifact).ToPipId();
            var sealKind = PipTable.GetSealDirectoryKind(sealId);

            if (sealKind == SealDirectoryKind.Full || sealKind == SealDirectoryKind.Partial)
            {
                return PipGraph.ListSealedDirectoryContents(directoryArtifact);
            }
            else if (sealKind.IsOpaqueOutput())
            {
                return m_dynamicContents[directoryArtifact];
            }

            return ReadOnlyArray<FileArtifact>.Empty;
        }

        private PipId GetProducer(FileArtifact file)
        {
            var producer = PipGraph.TryGetProducer(file);
            if (producer.IsValid)
            {
                return producer;
            }

            if (m_dynamicFileProducers.TryGetValue(file, out producer))
            {
                return producer;
            }

            return PipId.Invalid;
        }

        public override bool CanHandleWorkerEvents => true;

        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var entry = GetEntry(data.FileArtifact);
            entry.Size = data.FileContentInfo.Length;
        }

        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var entry in data.DirectoryOutputs)
            {
                m_dynamicContents[entry.directoryArtifact] = entry.fileArtifactArray;

                foreach (var file in entry.fileArtifactArray)
                {
                    m_dynamicFileProducers[file] = data.PipId;
                }
            }
        }

        private void Write(StringBuilder builder)
        {
            lock (m_writer)
            {
                m_writer.Write(builder.ToString());
            }
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (data.Kind == FingerprintComputationKind.Execution)
            {
                m_block.Post(() =>
                {
                    using (var wrapper = m_pool.GetInstance())
                    {
                        wrapper.Instance.ProcessFingerprintComputed(data);

                        if ((Interlocked.Increment(ref ProcessedPips) % 10) == 0)
                        {
                            Console.WriteLine($"Processing {ProcessedPips}");
                        }
                    }
                });
            }
        }

        public override void Dispose()
        {
            m_writer.Dispose();
        }

        [Flags]
        private enum ContentFlag
        {
            None = 0,
            Declared = 1,

            Static = 1 << 1,
            Dynamic = 1 << 7,

            Probe = 1 << 2,
            Content = 1 << 3,

            Consumed = 1 << 4,

            Source = 1 << 5,
            Output = 1 << 8,
            Unknown = 1 << 6,

            Copy = 1 << 9,

            /// <summary>
            /// A path was probed, but did not exist.
            /// </summary>
            AbsentPathProbe = 1 << (10 + ObservedInputType.AbsentPathProbe),

            /// <summary>
            /// A file with known contents was read.
            /// </summary>
            FileContentRead = 1 << (10 + ObservedInputType.FileContentRead),

            /// <summary>
            /// A directory was enumerated (kind of like a directory read).
            /// </summary>
            DirectoryEnumeration = 1 << (10 + ObservedInputType.DirectoryEnumeration),

            /// <summary>
            /// An existing directory probe.
            /// </summary>
            ExistingDirectoryProbe = 1 << (10 + ObservedInputType.ExistingDirectoryProbe),

            /// <summary>
            /// An existing file probe.
            /// </summary>
            ExistingFileProbe = 1 << (10 + ObservedInputType.ExistingFileProbe),
        }

        private class PipEntry
        {
            public PipId PipId;
            public PipType PipType;
            public Process Process;
            public string Identifier;
            public long SemistableHash;
            public AbsolutePath SpecFile;
            public string SpecFileName;
            public bool AddedEdges;
            public readonly List<FileReference> FileDependencies = new List<FileReference>();
            public readonly Dictionary<PipId, PipReference> PipDependencies = new Dictionary<PipId, PipReference>();
        }

        private class FileEntry
        {
            public PipEntry Producer;
            public FileArtifact Artifact;
            public long Size;
        }

        private class DirectoryEntry
        {
            public PipEntry Producer;
            public DirectoryArtifact Directory;
            public SealDirectoryKind Kind;
            public int FileCount;
            public string SemistableHash;
            public string SpecFileName;
            public string Id;
            public string Identifier;
        }

        private class PipReference
        {
            public PipEntry Pip;
            public ContentFlag Flags = ContentFlag.None;

            public bool HasFlag(ContentFlag flag)
            {
                return (Flags & flag) == flag;
            }

            public void AddFlag(ContentFlag flag)
            {
                Flags |= flag;
            }
        }

        private class FileReference
        {
            public ConsumedFile ConsumedFile;
            public DirectoryEntry Directory;

            public PipEntry Producer => ConsumedFile.File.Producer ?? Directory?.Producer;
        }

        private class ConsumedFile
        {
            public FileEntry File;
            public ContentFlag Flags;
            public ConsumedFile FinalFile;
            public ConsumedFile SourceFile;

            public ConsumedFile AddFlag(ContentFlag flag)
            {
                Flags |= flag;
                return this;
            }

            public bool HasFlag(ContentFlag flag)
            {
                return (Flags & flag) == flag;
            }
        }

        private class WorkerAnalyzer
        {
            private readonly RequiredDependencyAnalyzer m_analyzer;

            private Dictionary<AbsolutePath, ConsumedFile> m_consumedFilesByPath = new Dictionary<AbsolutePath, ConsumedFile>();
            private Dictionary<PipId, int> m_dependencyConsumedFileIndex = new Dictionary<PipId, int>();
            private Dictionary<PipId, int> m_dependencyConsumedFileEndIndex = new Dictionary<PipId, int>();
            private StringBuilder m_builder = new StringBuilder();
            private HashSet<(PipId, DirectoryArtifact)> m_dependencies = new HashSet<(PipId, DirectoryArtifact)>();
            private Dictionary<DirectoryArtifact, bool> m_directoryDependenciesFilterMap = new Dictionary<DirectoryArtifact, bool>();
            private HashSet<DirectoryArtifact> m_directoryHasSources = new HashSet<DirectoryArtifact>();
            private PipEntry m_consumer;

            public string Name { get; }

            public WorkerAnalyzer(RequiredDependencyAnalyzer analyzer, string name)
            {
                m_analyzer = analyzer;
                Name = name;
            }

            public void AddConsumedFile(FileArtifact file, DirectoryArtifact directory, ContentFlag flag)
            {
                var consumedFile = AddConsumedFile(file, flag);

                m_consumer.FileDependencies.Add(
                    new FileReference()
                    {
                        ConsumedFile = consumedFile,
                        Directory = m_analyzer.GetEntry(directory)
                    });

                if (m_analyzer.TryResolve(file, m_consumer, out FileArtifact resolvedFile))
                {
                    var resolvedConsumedFile = AddConsumedFile(resolvedFile, flag);
                    resolvedConsumedFile.AddFlag(ContentFlag.Copy);
                    consumedFile.SourceFile = resolvedConsumedFile;
                    resolvedConsumedFile.FinalFile = consumedFile;

                    m_consumer.FileDependencies.Add(
                        new FileReference()
                        {
                            ConsumedFile = resolvedConsumedFile,
                            Directory = m_analyzer.GetEntry(directory)
                        });
                }
            }

            private ConsumedFile AddConsumedFile(FileArtifact file, ContentFlag flag)
            {
                if (!m_consumedFilesByPath.TryGetValue(file, out var consumedFile))
                {
                    consumedFile = new ConsumedFile()
                    {
                        File = m_analyzer.GetEntry(file),
                        Flags = file.IsSourceFile ? ContentFlag.Source : ContentFlag.Output
                    };

                    m_consumedFilesByPath[file] = consumedFile;
                }

                consumedFile.Flags |= flag;
                return consumedFile;
            }

            private enum Columns
            {
                Producer,
                ProducerSpec,
                Path,
                RwCount,
                Dir,
                DirId,
                DirSsh,
                Flags,
            }

            public void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
            {
                if (data.Kind != FingerprintComputationKind.Execution)
                {
                    return;
                }

                m_consumer = m_analyzer.GetEntry(data.PipId);

                m_consumedFilesByPath.Clear();
                m_dependencyConsumedFileIndex.Clear();
                m_dependencyConsumedFileEndIndex.Clear();
                m_dependencies.Clear();
                m_builder.Clear();
                m_directoryDependenciesFilterMap.Clear();
                m_directoryHasSources.Clear();

                var computation = data.StrongFingerprintComputations[0];
                var pip = (Process)m_analyzer.GetPip(data.PipId);
                PipArtifacts.ForEachInput(pip, input =>
                {
                    if (input.IsFile)
                    {
                        AddConsumedFile(input.FileArtifact, DirectoryArtifact.Invalid, ContentFlag.Static | ContentFlag.Consumed);
                    }
                    else
                    {
                        foreach (var file in m_analyzer.GetContents(input.DirectoryArtifact))
                        {
                            if (file.IsSourceFile)
                            {
                                m_directoryHasSources.Add(input.DirectoryArtifact);
                            }

                            AddConsumedFile(file, input.DirectoryArtifact, ContentFlag.Dynamic);
                        }
                    }

                    return true;
                }, includeLazyInputs: false);

                foreach (var input in computation.ObservedInputs)
                {
                    var flag = (ContentFlag)((int)ContentFlag.AbsentPathProbe << (int)input.Type) | ContentFlag.Consumed;
                    if (input.Type == ObservedInputType.FileContentRead || input.Type == ObservedInputType.ExistingFileProbe)
                    {
                        if (m_consumedFilesByPath.TryGetValue(input.Path, out var file))
                        {
                            file.AddFlag(ContentFlag.Consumed);
                            if (file.SourceFile != null)
                            {
                                file.SourceFile.AddFlag(ContentFlag.Consumed);
                            }

                            if (file.FinalFile != null)
                            {
                                file.FinalFile.AddFlag(ContentFlag.Consumed);
                            }
                        }
                        else
                        {
                            AddConsumedFile(FileArtifact.CreateSourceFile(input.Path), m_analyzer.PipGraph.TryGetSealSourceAncestor(input.Path), flag | ContentFlag.Unknown);
                        }
                    }
                    else if (m_analyzer.AllAccesses)
                    {
                        AddConsumedFile(FileArtifact.CreateSourceFile(input.Path), m_analyzer.PipGraph.TryGetSealSourceAncestor(input.Path), flag);
                    }
                }

                var entry = m_consumer;

                // Sort file dependencies for consistent output
                entry.FileDependencies.Sort(s_fileReferenceComparer);

                foreach (var fileDependency in entry.FileDependencies)
                {
                    if (fileDependency.Producer != null)
                    {
                        var reference = entry.PipDependencies.GetOrAdd(fileDependency.Producer.PipId, p => new PipReference());
                        if (reference.Pip == null)
                        {
                            reference.Pip = m_analyzer.GetEntry(fileDependency.Producer.PipId);
                        }

                        reference.Flags |= fileDependency.ConsumedFile.Flags;
                    }
                }

                string describe(PipEntry pe)
                {
                    return $"{pe.SpecFileName}-{m_analyzer.GetDescription(m_analyzer.GetPip(pe.PipId))}";
                }

                m_builder.AppendLine(describe(entry));
                foreach (var fileDependency in entry.FileDependencies)
                {
                    if (fileDependency.Producer != null
                        && fileDependency.ConsumedFile.File.Artifact.IsOutputFile)
                    {
                        var pipId = fileDependency.Producer.PipId;
                        var pipReference = entry.PipDependencies[pipId];
                        var directory = fileDependency.Directory?.Directory ?? DirectoryArtifact.Invalid;
                        if (m_dependencies.Add((pipId, directory)))
                        {
                            if (pipReference.HasFlag(ContentFlag.Consumed))
                            {
                                m_directoryDependenciesFilterMap[directory] = true;
                                m_builder.AppendLine($"{entry.Identifier} -> Retaining pip dependency on '{describe(pipReference.Pip)}' (declared via directory '{ToString(fileDependency.Directory)}') (consumes '{ToString(fileDependency.ConsumedFile.File.Artifact)}')");
                            }
                            else
                            {
                                m_directoryDependenciesFilterMap.TryAdd(directory, false);
                                m_builder.AppendLine($"{entry.Identifier} -> Removing pip dependency on '{describe(pipReference.Pip)}' (declared via directory '{ToString(fileDependency.Directory)}')");
                            }
                        }
                    }
                }

                var trimmedDirectoryDependencies = new List<DirectoryArtifact>();

                foreach (var d in entry.Process.DirectoryDependencies)
                {
                    if (m_directoryDependenciesFilterMap.TryGetValue(d, out var shouldInclude))
                    {
                        if (shouldInclude)
                        {
                            m_builder.AppendLine($"{entry.Identifier} -> Retaining directory dependency on '{ToString(d)}' (used)");
                        }
                        else if (m_directoryHasSources.Contains(d))
                        {
                            m_builder.AppendLine($"{entry.Identifier} -> Retaining directory dependency on '{ToString(d)}' (has sources)");
                        }
                        else
                        {
                            m_builder.AppendLine($"{entry.Identifier} -> Removing directory dependency on '{ToString(d)}'");
                            continue;
                        }
                    }
                    else
                    {
                        var sealId = m_analyzer.PipGraph.GetSealedDirectoryNode(d).ToPipId();
                        if (!m_directoryHasSources.Contains(d) && !m_analyzer.PipTable.GetSealDirectoryKind(sealId).IsSourceSeal())
                        {
                            m_builder.AppendLine($"{entry.Identifier} -> Removing directory dependency on '{ToString(d)}' (unused output directory)");
                            continue;
                        }
                    }

                    entry.PipDependencies.TryAdd(m_analyzer.PipGraph.GetSealedDirectoryNode(d).ToPipId(), default);
                    trimmedDirectoryDependencies.Add(d);
                }

                // Update directory dependencies which trimmed directory dependencies to allow writing
                // a pip into the serialized pip table that can run without the unnecessary dependencies
                entry.Process.UnsafeUpdateDirectoryDependencies(trimmedDirectoryDependencies.ToReadOnlyArray());

                m_builder.AppendLine();

                // Update the graph
                var modifiedGraph = m_analyzer.m_mutableGraph;
                using (var scope = modifiedGraph.AcquireExclusiveIncomingEdgeScope(entry.PipId.ToNodeId()))
                {
                    foreach (var dependency in entry.PipDependencies)
                    {
                        if (dependency.Value == null || dependency.Value.HasFlag(ContentFlag.Consumed))
                        {
                            scope.AddEdge(dependency.Key.ToNodeId());
                        }
                    }

                    entry.AddedEdges = true;
                }

                if (m_analyzer.SemiStableHashes.Contains(entry.SemistableHash))
                {
                    using (var writer = new StreamWriter(Path.Combine(m_analyzer.OutputFilePath,
                        $"{GetFileName(entry.SpecFile)}_Pip{pip.FormattedSemiStableHash}.csv")))
                    {
                        var table = new DisplayTable<Columns>(" , ");

                        foreach (var dependency in entry.FileDependencies)
                        {
                            table.NextRow();
                            table.Set(Columns.Path, ToString(dependency.ConsumedFile.File.Artifact.Path));
                            table.Set(Columns.RwCount, dependency.ConsumedFile.File.Artifact.RewriteCount.ToString());
                            table.Set(Columns.Flags, dependency.ConsumedFile.Flags.ToString());
                            table.Set(Columns.Producer, dependency.Producer?.Identifier);
                            table.Set(Columns.ProducerSpec, GetFileName(dependency.Producer?.SpecFile ?? AbsolutePath.Invalid));
                            table.Set(Columns.Dir, ToString(dependency.Directory));
                            table.Set(Columns.DirId, dependency.Directory?.Id);
                            table.Set(Columns.DirSsh, dependency.Directory?.SemistableHash);
                        }

                        table.Write(writer);
                    }
                }

                if (m_builder.Length != 0)
                {
                    m_analyzer.Write(m_builder);
                }
            }

            private string ToString(AbsolutePath path)
            {
                return path.ToString(m_analyzer.PathTable);
            }

            private string GetFileName(AbsolutePath path)
            {
                if (!path.IsValid)
                {
                    return string.Empty;
                }

                return path.GetName(m_analyzer.PathTable).ToString(m_analyzer.StringTable);
            }

            public string ToString(DirectoryEntry directory)
            {
                if (directory == null)
                {
                    return string.Empty;
                }

                return $"{directory.Producer?.SpecFileName}-{directory.Directory.Path.ToString(m_analyzer.PathTable)} [{directory.Kind}] ({directory.FileCount} files)";
            }

            private string GetPipType(Pip dependency)
            {
                if (dependency.PipType == PipType.SealDirectory)
                {
                    return ((SealDirectory)dependency).Kind.ToString();
                }

                return dependency.PipType.ToString();
            }

            private bool IsSourceDependency(Pip p)
            {
                if (p.PipType == PipType.HashSourceFile)
                {
                    return true;
                }

                if (p.PipType == PipType.SealDirectory)
                {
                    return m_analyzer.IsSourceOnlySeal(p.PipId);
                }

                return false;
            }

            private string Describe(ContentFlag flags)
            {
                return flags.ToString();
            }

            private string Describe(FileArtifact file)
            {
                return file.Path.ToString(m_analyzer.PathTable);
            }

            private string DepDescribe(Pip pip)
            {
                if (pip.PipType != PipType.SealDirectory)
                {
                    return pip.FormattedSemiStableHash;
                }

                SealDirectory sd = (SealDirectory)pip;
                return $"{pip.FormattedSemiStableHash}({sd.Contents.Length} files)";
            }

            private string Describe(Pip pip)
            {
                return pip.GetShortDescription(m_analyzer.CachedGraph.Context);
            }
        }
    }
}
