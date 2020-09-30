// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using Microsoft.VisualStudio.Services.Common;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeFileConsumptionAnalyzer()
        {
            string outputPath = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath = ParseSingletonPathOption(opt, outputPath);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw Error("Output directory must be specified with /out");
            }

            return new FileConsumptionAnalyzer(GetAnalysisInput())
            {
                OutputDirectoryPath = outputPath,
            };
        }

        private static void WriteFileConsumptionAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("File Consumption Analyzer");
            writer.WriteModeOption(nameof(AnalysisMode.FileConsumption), "Collects information about produced/consumed files");
            writer.WriteOption("outputDirectory", "Required. The directory where to write the results", shortName: "o");
        }
    }

    internal sealed class FileConsumptionAnalyzer : Analyzer
    {
        /// <summary>
        /// Overall summary about produced/consumed files in a build
        /// </summary>
        private StreamWriter m_writerSummary;

        /// <summary>
        /// Information about produced files (path, size, producer/consumer)
        /// </summary>
        private StreamWriter m_writerFiles;

        /// <summary>
        /// Information about process pips (size of consumed/declared outputs)
        /// </summary>
        private StreamWriter m_writerPips;

        /// <summary>
        /// Write out all the details of exactly which files a pip consumes.
        /// </summary>
        private StreamWriter m_writerPipDetails;

        private long m_processedPips;
        private WorkerAnalyzer[] m_workers;
        private readonly Dictionary<DirectoryArtifact, List<AbsolutePath>> m_dynamicDirectoryContent;
        private readonly MultiValueDictionary<AbsolutePath, DirectoryArtifact> m_parentOutputDirectory;
        private readonly ConcurrentDictionary<PipId, ProcessPip> m_executedProcessPips;
        private readonly Dictionary<AbsolutePath, long> m_fileSizes;
        private readonly ConcurrentDictionary<AbsolutePath, OutputFile> m_producedFiles;

        /// <summary>
        /// The path to the output directory
        /// </summary>
        public string OutputDirectoryPath;

        public override bool CanHandleWorkerEvents => true;

        public FileConsumptionAnalyzer(AnalysisInput input)
            : base(input)
        {
            // Create default with just local worker for single machine builds
            m_workers = new[] { new WorkerAnalyzer(this, "Local") };

            m_dynamicDirectoryContent = new Dictionary<DirectoryArtifact, List<AbsolutePath>>();
            m_executedProcessPips = new ConcurrentDictionary<PipId, ProcessPip>();
            m_fileSizes = new Dictionary<AbsolutePath, long>();
            m_parentOutputDirectory = new MultiValueDictionary<AbsolutePath, DirectoryArtifact>();
            m_producedFiles = new ConcurrentDictionary<AbsolutePath, OutputFile>();

            Console.WriteLine($"FileConsumptionAnalyzer: Constructed at {DateTime.Now}.");
        }

        public override void Prepare()
        {
            Contract.Assert(!File.Exists(OutputDirectoryPath), $"Cannot create a directory '{OutputDirectoryPath}' because this path points to a file.");

            Directory.CreateDirectory(OutputDirectoryPath);
            m_writerSummary = new StreamWriter(Path.Combine(OutputDirectoryPath, "summary.txt"));
            m_writerFiles = new StreamWriter(Path.Combine(OutputDirectoryPath, "files.csv"));
            m_writerPips = new StreamWriter(Path.Combine(OutputDirectoryPath, "pips.csv"));
            m_writerPipDetails = new StreamWriter(Path.Combine(OutputDirectoryPath, "pip_details.csv"));
        }

        public override void WorkerList(WorkerListEventData data)
        {
            m_workers = data.Workers.SelectArray(s => new WorkerAnalyzer(this, s));
        }

        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            if (data.FileArtifact.IsOutputFile && data.FileContentInfo.HasKnownLength)
            {
                if (!m_fileSizes.ContainsKey(data.FileArtifact.Path))
                {
                    m_fileSizes.Add(data.FileArtifact.Path, data.FileContentInfo.Length);
                }

                m_producedFiles.AddOrUpdate(
                    data.FileArtifact.Path,
                    new OutputFile() { FileArtifact = data.FileArtifact },
                    (_, file) =>
                    {
                        file.FileArtifact = data.FileArtifact;
                        return file;
                    });
            }

            GetWorkerAnalyzer().FileArtifactContentDecided(data);
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (data.Kind == FingerprintComputationKind.Execution)
            {
                if ((Interlocked.Increment(ref m_processedPips) % 1000) == 0)
                {
                    Console.WriteLine($"Processing {m_processedPips}");
                }
            }

            GetWorkerAnalyzer().ProcessFingerprintComputed(data);
        }

        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var kvp in data.DirectoryOutputs)
            {
                var paths = kvp.fileArtifactArray.Select(fa => fa.Path).ToList();
                m_dynamicDirectoryContent[kvp.directoryArtifact] = paths;
                foreach (var path in paths)
                {
                    m_parentOutputDirectory.Add(path, kvp.directoryArtifact);
                }
            }
        }

        private WorkerAnalyzer GetWorkerAnalyzer()
        {
            return m_workers[CurrentEventWorkerId];
        }

        public override int Analyze()
        {
            Console.WriteLine($"FileConsumptionAnalyzer: Starting analysis at {DateTime.Now}.");

            int totalDeclaredInputFiles = 0, totalDeclaredInputDirectories = 0, totalConsumedFiles = 0, totalActualProcessPips = 0;

            Parallel.ForEach(
                m_executedProcessPips.Keys,
                new ParallelOptions() { MaxDegreeOfParallelism = 1 },//Environment.ProcessorCount },
                pipId =>
                {
                    var pip = m_executedProcessPips[pipId];
                    long totalInputSize = 0;
                    long totalConsumedSize = 0;

                    if (pip.Worker == null)
                    {
                        // this pip was not executed (i.e., cache hit)
                        pip.ConsumedInputSize = -1;
                        pip.DeclaredInputSize = -1;
                        return;
                    }

                    totalActualProcessPips++;

                    using (var pooledSetInputFiles = Pools.GetAbsolutePathSet())
                    using (var pooledSetConsumedFiles = Pools.GetAbsolutePathSet())
                    {
                        var inputFiles = pooledSetInputFiles.Instance;
                        var consumedFiles = pooledSetConsumedFiles.Instance;

                        // BXL executed this pip, so we can safely assume that its inputs were materialized.
                        foreach (var path in pip.DeclaredInputFiles)
                        {
                            if (m_fileSizes.TryGetValue(path, out var size))
                            {
                                conditionallyAddValue(ref totalInputSize, size, path, inputFiles);
                                conditionallyAddValue(ref totalConsumedSize, size, path, consumedFiles);
                                pip.Worker.AddFlag(path, ContentFlag.Materialized);
                            }
                        }
                        totalDeclaredInputFiles += pip.DeclaredInputFiles.Count;

                        foreach (var directoryArtifact in pip.DeclaredInputDirectories)
                        {
                            if (m_dynamicDirectoryContent.TryGetValue(directoryArtifact, out var directoryContent))
                            {
                                pip.Worker.AddFlag(directoryArtifact, ContentFlag.Materialized);

                                foreach (var path in directoryContent)
                                {
                                    if (m_fileSizes.TryGetValue(path, out var size))
                                    {
                                        conditionallyAddValue(ref totalInputSize, size, path, inputFiles);
                                    }
                                }
                            }
                        }
                        totalDeclaredInputDirectories += pip.DeclaredInputDirectories.Count;

                        foreach (var path in pip.ConsumedFiles)
                        {
                            if (m_fileSizes.TryGetValue(path, out var size))
                            {
                                conditionallyAddValue(ref totalConsumedSize, size, path, consumedFiles);

                                if (m_producedFiles.ContainsKey(path))
                                {
                                    m_producedFiles.AddOrUpdate(
                                        path,
                                        _ => throw new Exception(),
                                        (_, file) =>
                                        {
                                            file.Consumers.Enqueue(pipId);
                                            return file;
                                        });
                                }
                            }
                        }
                        totalConsumedFiles += pip.ConsumedFiles.Count;

                        pip.ConsumedInputSize = totalConsumedSize;
                        pip.DeclaredInputSize = totalInputSize;
                    }

                    void conditionallyAddValue(ref long counter, long value, AbsolutePath path, HashSet<AbsolutePath> files)
                    {
                        if (files.Add(path))
                        {
                            counter += value;
                        }
                    }
                });

            Console.WriteLine($"FileConsumptionAnalyzer: Analyzed {totalActualProcessPips} executed process pips ({totalDeclaredInputFiles} declared input files, {totalDeclaredInputDirectories} declared input directories, {totalConsumedFiles} consumed files) at {DateTime.Now}.");

            // set file producer
            Parallel.ForEach(
                m_producedFiles.Keys,
                new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount },
                path =>
                {
                    var outputFile = m_producedFiles[path];

                    // try find a producer of this file
                    var producer = PipGraph.TryGetProducer(outputFile.FileArtifact);
                    if (!producer.IsValid)
                    {
                        // it's a not statically declared file, so it must be produced as a part of an output directory
                        if (m_parentOutputDirectory.TryGetValue(path, out var containingDirectories))
                        {
                            foreach (var directory in containingDirectories)
                            {
                                producer = PipGraph.TryGetProducer(directory);
                                if (producer.IsValid)
                                {
                                    break;
                                }
                            }
                        }
                    }

                    outputFile.Producer = producer;
                });

            Console.WriteLine($"FileConsumptionAnalyzer: Analyzed {m_producedFiles.Count} produced files at {DateTime.Now}.");

            foreach (var worker in m_workers)
            {
                Console.WriteLine($"Completing {worker.Name}");
                worker.Complete();
            }

            const int MaxConsumers = 10;
            m_writerFiles.WriteLine($"PathId,Path,Size,Producer,Consumers");
            foreach (var path in m_producedFiles.Keys.ToListSorted(PathTable.ExpandedPathComparer))
            {
                if (!m_producedFiles[path].Producer.IsValid)
                {
                    continue;
                }

                m_fileSizes.TryGetValue(path, out var size);

                m_writerFiles.WriteLine("{0},\"{1}\",{2},{3},{4}",
                    path.Value,
                    path.ToString(PathTable).ToCanonicalizedPath(),
                    size,
                    PipTable.GetFormattedSemiStableHash(m_producedFiles[path].Producer),
                    string.Join(";", m_producedFiles[path].Consumers.Take(MaxConsumers).Select(pipId => PipTable.GetFormattedSemiStableHash(pipId))));
            }

            m_writerPips.WriteLine("Pip,Description,ConsumedFileCount,ConsumedBytes,TotalInputSize");
            m_writerPipDetails.WriteLine("PipId,PipHash,ConsumedFile,ConsumedFileSize");
            foreach (var pipId in m_executedProcessPips.Keys)
            {
                var pip = m_executedProcessPips[pipId];
                m_writerPips.WriteLine("{0},\"{1}\",{2},{3},{4}",
                    PipTable.GetFormattedSemiStableHash(pipId),
                    pip.Description.Replace("\"", "\"\""),
                    pip.ConsumedFiles?.Count,
                    pip.ConsumedInputSize,
                    pip.DeclaredInputSize);

                if (pip.ConsumedFiles != null)
                {
                    foreach (var consumedFile in pip.ConsumedFiles)
                    {
                        m_writerPipDetails.WriteLine($"{pip.PipId.Value},{pip.SemiStableHash:X},{consumedFile.ToString(PathTable).ToCanonicalizedPath()},{m_fileSizes[consumedFile]}");
                    }
                }
            }

            Console.WriteLine($"FileConsumptionAnalyzer: Wrote out all CSV files at {DateTime.Now}.");

            return 0;
        }

        public override void Dispose()
        {
            m_writerSummary.Close();
            m_writerPips.Close();
            m_writerFiles.Close();
            m_writerSummary.Dispose();
            m_writerPips.Dispose();
            m_writerFiles.Dispose();
        }

        [Flags]
        private enum ContentFlag
        {
            Produced = 1,
            Materialized = 1 << 1,
            MaterializedFromCache = 1 << 2,
            Consumed = 1 << 3,
        }

        private class ProcessPip
        {
            public readonly PipId PipId;
            public readonly long SemiStableHash;
            public readonly string Description;

            /// <summary>
            /// Statically declared input files
            /// </summary>
            /// <remarks>Contains only files produced during a build.</remarks>
            public List<AbsolutePath> DeclaredInputFiles;

            /// <summary>
            /// Input directories
            /// </summary>
            public List<DirectoryArtifact> DeclaredInputDirectories;

            /// <summary>
            /// List of consumed paths
            /// </summary>
            /// <remarks>Contains only files produced during a build.</remarks>
            public List<AbsolutePath> ConsumedFiles;

            // the worker this pip ran on
            public WorkerAnalyzer Worker;

            public long DeclaredInputSize;
            public long ConsumedInputSize;

            public ProcessPip(PipId pipId, long semiStableHash, string description)
            {
                PipId = pipId;
                SemiStableHash = semiStableHash;
                Description = description ?? string.Empty;
            }
        }

        private class OutputFile
        {
            public FileArtifact FileArtifact;
            public PipId Producer;
            public ConcurrentQueue<PipId> Consumers = new ConcurrentQueue<PipId>();
        }

        private class WorkerAnalyzer
        {
            private readonly FileConsumptionAnalyzer m_analyzer;
            private readonly ConcurrentDictionary<AbsolutePath, ContentFlag> m_deployedFileFlags = new ConcurrentDictionary<AbsolutePath, ContentFlag>();
            private readonly ActionBlockSlim<ProcessFingerprintComputationEventData> m_processingBlock;
            private readonly ConcurrentDictionary<DirectoryArtifact, ContentFlag> m_processedDirectories = new ConcurrentDictionary<DirectoryArtifact, ContentFlag>();
            private readonly Dictionary<PathAtom, long> m_sizeByExtension = new Dictionary<PathAtom, long>();

            public string Name { get; }

            public WorkerAnalyzer(FileConsumptionAnalyzer analyzer, string name)
            {
                m_analyzer = analyzer;
                Name = name;
                m_processingBlock = new ActionBlockSlim<ProcessFingerprintComputationEventData>(1, ProcessFingerprintComputedCore);
            }

            public void Complete()
            {
                m_processingBlock.Complete();
                var writer = m_analyzer.m_writerSummary;
                writer.WriteLine($"Worker {Name}:");

                int counterMax = Enum.GetNames(typeof(ContentFlag)).Length;
                var counters = Enumerable.Range(0, counterMax).Select(_ => new Counter()).ToArray();

                foreach (var entry in m_deployedFileFlags)
                {
                    if (m_analyzer.m_fileSizes.TryGetValue(entry.Key, out var size))
                    {
                        for (int i = 0; i < counterMax; i++)
                        {
                            var flag = (ContentFlag)(1 << i);
                            if ((entry.Value & flag) == flag)
                            {
                                counters[i].Add(entry.Key, size);
                            }
                        }

                        m_sizeByExtension.AddOrUpdate(entry.Key.GetExtension(m_analyzer.PathTable), size, (_, oldValue) => oldValue + size);
                    }
                }

                writer.WriteLine($"Total File Sizes by extension:");
                foreach (var entry in m_sizeByExtension.OrderByDescending(e => e.Value))
                {
                    writer.WriteLine($"{ToString(entry.Key)}={entry.Value}");
                }

                writer.WriteLine();
                writer.WriteLine($"Total File Sizes by ContentFlag:");
                for (int i = 0; i < counterMax; i++)
                {
                    var flag = (ContentFlag)(1 << i);
                    counters[i].Write(writer, flag.ToString());
                }

                writer.WriteLine();
            }

            private string ToString(PathAtom key)
            {
                if (!key.IsValid)
                {
                    return "<no extension>";
                }

                return key.ToString(m_analyzer.StringTable);
            }

            public void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
            {
                if (data.FileArtifact.IsOutputFile && data.FileContentInfo.HasKnownLength)
                {
                    // This is a real file produced during a build, record its materialization status if possible
                    switch (data.OutputOrigin)
                    {
                        case Scheduler.PipOutputOrigin.DeployedFromCache:
                            AddFlag(data.FileArtifact.Path, ContentFlag.MaterializedFromCache);
                            break;
                        case Scheduler.PipOutputOrigin.Produced:
                            AddFlag(data.FileArtifact.Path, ContentFlag.Produced);
                            break;
                            // We ignore the following cases:
                            // PipOutputOrigin.NotMaterialized - we cannot infer the current or future materialization status of a file
                            // PipOutputOrigin.UpToDate - not relevant to this analyzer
                    }
                }
            }

            public void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
            {
                m_processingBlock.Post(data);
            }

            internal void AddFlag(AbsolutePath path, ContentFlag flag)
            {
                m_deployedFileFlags.AddOrUpdate(
                    path,
                    flag,
                    (_, oldFlags) =>
                    {
                        if (flag == ContentFlag.Materialized &&
                            ((oldFlags & ContentFlag.Produced) == ContentFlag.Produced
                            || (oldFlags & ContentFlag.MaterializedFromCache) == ContentFlag.MaterializedFromCache))
                        {
                            // Produced and MaterializedFromCache take precedence over Materialized
                            return oldFlags;
                        }
                        else
                        {
                            return oldFlags | flag;
                        }
                    });
            }

            internal void AddFlag(DirectoryArtifact directory, ContentFlag flag)
            {
                ContentFlag? prevValue = null;
                m_processedDirectories.AddOrUpdate(directory, flag, (_, oldFlag) => { prevValue = oldFlag; return oldFlag | flag; });

                // If this particular flag has not been added yet, process it.
                if (prevValue == null || (prevValue.Value & flag) != flag)
                {
                    foreach (var file in m_analyzer.m_dynamicDirectoryContent[directory])
                    {
                        AddFlag(file, flag);
                    }
                }
            }

            public void ProcessFingerprintComputedCore(ProcessFingerprintComputationEventData data)
            {
                var pip = m_analyzer.GetPip(data.PipId) as Process;
                Contract.Assert(pip != null);

                // only interested in the events generated after a corresponding pip was executed
                // however, we still need to save pip description so there would be no missing entries in pips.csv
                if (data.Kind != FingerprintComputationKind.Execution)
                {
                    m_analyzer.m_executedProcessPips.TryAdd(
                        data.PipId,
                        new ProcessPip(data.PipId, pip.SemiStableHash, getPipDescription(pip, m_analyzer.CachedGraph.Context)));
                    return;
                }

                // part 1: collect requested inputs
                // count only output files/directories
                var declaredInputFiles = pip.Dependencies.Where(f => f.IsOutputFile).Select(f => f.Path).ToList();
                var declaredInputsDirs = pip.DirectoryDependencies.Where(d => d.IsOutputDirectory()).ToList();

                // part 2: collect actual inputs                
                var consumedPaths = data.StrongFingerprintComputations.Count == 0
                    ? new List<AbsolutePath>()
                    : data.StrongFingerprintComputations[0].ObservedInputs
                    .Where(input => input.Type == ObservedInputType.FileContentRead || input.Type == ObservedInputType.ExistingFileProbe)
                    // filter out source files
                    .Where(input => m_analyzer.m_fileSizes.ContainsKey(input.Path))
                    .Select(input => input.Path)
                    .ToList();

                m_analyzer.m_executedProcessPips.AddOrUpdate(
                    data.PipId,
                    new ProcessPip(data.PipId, pip.SemiStableHash, getPipDescription(pip, m_analyzer.CachedGraph.Context))
                    {
                        DeclaredInputFiles = declaredInputFiles,
                        DeclaredInputDirectories = declaredInputsDirs,
                        ConsumedFiles = consumedPaths,
                        Worker = this
                    },
                    (pipId, process) =>
                    {
                        process.DeclaredInputFiles = declaredInputFiles;
                        process.DeclaredInputDirectories = declaredInputsDirs;
                        process.ConsumedFiles = consumedPaths;
                        process.Worker = this;
                        return process;
                    });

                // part 3: mark consumed files
                // consumed == [statically declared file dependencies, dynamic observations]
                foreach (var path in declaredInputFiles.Concat(consumedPaths))
                {
                    AddFlag(path, ContentFlag.Consumed);
                }

                string getPipDescription(Process pip, PipExecutionContext context)
                {
                    using (var wrapper = Pools.GetStringBuilder())
                    {
                        var sb = wrapper.Instance;

                        if (pip.Provenance != null)
                        {
                            if (pip.Provenance.ModuleName.IsValid)
                            {
                                sb.Append(pip.Provenance.ModuleName.ToString(context.StringTable));
                            }

                            if (context.SymbolTable != null)
                            {
                                if (pip.Provenance.OutputValueSymbol.IsValid)
                                {
                                    if (sb.Length > 0)
                                    {
                                        sb.Append(", ");
                                    }

                                    sb.Append(pip.Provenance.OutputValueSymbol, context.SymbolTable);
                                }
                            }
                        }

                        return sb.ToString();
                    }
                }
            }

            public class Counter
            {
                public long FileCount = 0;
                public long TotalFileSize = 0;

                public void Add(AbsolutePath path, long fileSize)
                {
                    FileCount++;
                    TotalFileSize += fileSize;
                }

                public void Write(TextWriter writer, string prefix)
                {
                    writer.WriteLine($"    {prefix}: FileCount={FileCount}, TotalFileSize={TotalFileSize}");
                }
            }
        }
    }
}
