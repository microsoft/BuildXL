// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using static BuildXL.ToolSupport.CommandLineUtilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Available benchmark operations.
    /// </summary>
    internal enum BenchmarkOperation
    {
        LoadDirectedGraph,
        FilterGraph,
        ConvertPipTable,
        HydratePipTable,
    }

    internal partial class Args
    {
        public BenchmarkAnalyzer InitializeBenchmarkAnalyzer()
        {
            BenchmarkOperation? operation = null;
            var remainingOptions = new List<Option>();

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("operation", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("op", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Enum.TryParse(ParseStringOption(opt), ignoreCase: true, out BenchmarkOperation parsed))
                    {
                        throw Error("Unknown benchmark operation '{0}'. Available operations: {1}", opt.Value, string.Join(", ", Enum.GetNames(typeof(BenchmarkOperation))));
                    }

                    operation = parsed;
                }
                else
                {
                    // Pass unrecognized options through to the operation
                    remainingOptions.Add(opt);
                }
            }

            if (operation == null)
            {
                throw Error("/operation is a required parameter. Available operations: {0}", string.Join(", ", Enum.GetNames(typeof(BenchmarkOperation))));
            }

            return new BenchmarkAnalyzer(operation.Value, remainingOptions);
        }

        private static void WriteBenchmarkAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Benchmark - Run performance benchmarks");
            writer.WriteOption("operation", "Required. The benchmark operation to run (e.g., LoadDirectedGraph).", shortName: "op");
            writer.WriteBanner("  LoadDirectedGraph options:");
            writer.WriteOption("graphFile", "Required. Path to a serialized DirectedGraph file. The file must be named 'DirectedGraph'.", shortName: "g");
            writer.WriteBanner("  FilterGraph options:");
            writer.WriteOption("graphDir", "Required. Path to a directory containing serialized graph files (PipGraph, PipTable, etc.).", shortName: "gd");
            writer.WriteOption("filterFile", "Required. Path to a text file containing the filter expression (same format as /f: flag).", shortName: "ff");
            writer.WriteBanner("  ConvertPipTable options:");
            writer.WriteOption("graphDir", "Required. Path to a directory containing version 0 StringTable, PathTable, SymbolTable, and PipTable files.", shortName: "gd");
            writer.WriteOption("outputFile", "Required. Path for the converted PipTable.", shortName: "o");
            writer.WriteBanner("  HydratePipTable options:");
            writer.WriteOption("graphDir", "Required. Path to a directory containing StringTable, PathTable, and SymbolTable files.", shortName: "gd");
            writer.WriteOption("pipTableFile", "Required. Path to the PipTable to hydrate.", shortName: "pt");
            writer.WriteOption("iterations", "Number of hydration iterations. Defaults to 3.", shortName: "i");
            writer.WriteOption("degreeOfParallelism", "Maximum parallel pip hydration workers. Defaults to the processor count.", shortName: "dop");
        }
    }

    /// <summary>
    /// Generic benchmark dispatcher. Add new operations as methods and register them in the Run() switch.
    /// </summary>
    internal sealed class BenchmarkAnalyzer
    {
        private sealed class LegacyPipReader : PipReader
        {
            public LegacyPipReader(bool debug, StringTable stringTable, Stream stream, bool leaveOpen)
                : base(debug, stringTable, stream, leaveOpen)
            {
            }

            public override ReadOnlyArray<AbsolutePath> ReadDeltaEncodedAbsolutePathArray() =>
                ReadReadOnlyArray(reader => reader.ReadAbsolutePath());

            public override ReadOnlyArray<FileArtifact> ReadDeltaEncodedFileArtifactArray() =>
                ReadReadOnlyArray(reader => reader.ReadFileArtifact());

            public override ReadOnlyArray<FileArtifactWithAttributes> ReadDeltaEncodedFileArtifactWithAttributesArray() =>
                ReadReadOnlyArray(ReadLegacyFileArtifactWithAttributes);

            public override ReadOnlyArray<DirectoryArtifact> ReadDeltaEncodedDirectoryArtifactArray() =>
                ReadReadOnlyArray(reader => reader.ReadDirectoryArtifact());

            private static FileArtifactWithAttributes ReadLegacyFileArtifactWithAttributes(BuildXLReader reader)
            {
                var path = reader.ReadAbsolutePath();
                uint metadata = reader.ReadUInt32();
                int rewriteCount = (int)(metadata & 0x00FFFFFF);
                var fileExistence = (FileExistence)((metadata >> 24) & 0x7F);
                bool isUndeclaredFileRewrite = (metadata & 0x80000000) != 0;
                return new FileArtifact(path, rewriteCount).WithAttributes(fileExistence, isUndeclaredFileRewrite);
            }
        }

        private readonly BenchmarkOperation m_operation;
        private readonly List<Option> m_options;

        public BenchmarkAnalyzer(BenchmarkOperation operation, List<Option> options)
        {
            m_operation = operation;
            m_options = options;
        }

        public int Run()
        {
            switch (m_operation)
            {
                case BenchmarkOperation.LoadDirectedGraph:
                    return RunLoadDirectedGraph();
                case BenchmarkOperation.FilterGraph:
                    return RunFilterGraph();
                case BenchmarkOperation.ConvertPipTable:
                    return RunConvertPipTable();
                case BenchmarkOperation.HydratePipTable:
                    return RunHydratePipTable();
                default:
                    throw CommandLineUtilities.Error("Unknown benchmark operation: {0}", m_operation);
            }
        }

        private int RunHydratePipTable()
        {
            string graphDir = null;
            string pipTableFile = null;
            int iterations = 3;
            int degreeOfParallelism = Environment.ProcessorCount;

            foreach (var opt in m_options)
            {
                if (opt.Name.Equals("graphDir", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("gd", StringComparison.OrdinalIgnoreCase))
                {
                    graphDir = opt.Value;
                }
                else if (opt.Name.Equals("pipTableFile", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("pt", StringComparison.OrdinalIgnoreCase))
                {
                    pipTableFile = opt.Value;
                }
                else if (opt.Name.Equals("iterations", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    iterations = ParseInt32Option(opt, 1, 100);
                }
                else if (opt.Name.Equals("degreeOfParallelism", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("dop", StringComparison.OrdinalIgnoreCase))
                {
                    degreeOfParallelism = ParseInt32Option(opt, 1, 1024);
                }
                else
                {
                    throw CommandLineUtilities.Error("Unknown option for HydratePipTable benchmark: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(graphDir) || !Directory.Exists(graphDir))
            {
                throw CommandLineUtilities.Error("/graphDir must name an existing graph directory.");
            }

            if (string.IsNullOrEmpty(pipTableFile) || !File.Exists(pipTableFile))
            {
                throw CommandLineUtilities.Error("/pipTableFile must name an existing PipTable.");
            }

            var loggingContext = new LoggingContext("Benchmark.HydratePipTable");
            var serializer = new EngineSerializer(loggingContext, graphDir, readOnly: true);
            var stringTableTask = serializer.DeserializeFromFileAsync(GraphCacheFile.StringTable, StringTable.DeserializeAsync, skipHeader: true);
            var pathTableTask = serializer.DeserializeFromFileAsync(
                GraphCacheFile.PathTable,
                reader => reader.ReadPathTableAsync(stringTableTask),
                skipHeader: true);
            var symbolTableTask = serializer.DeserializeFromFileAsync(
                GraphCacheFile.SymbolTable,
                reader => reader.ReadSymbolTableAsync(stringTableTask),
                skipHeader: true);

            for (int iteration = 1; iteration <= iterations; iteration++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var totalStopwatch = Stopwatch.StartNew();
                using (var stream = new FileStream(pipTableFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    PipTable.FileEnvelope.ReadHeader(stream);
                    if (stream.ReadByte() != 0)
                    {
                        throw CommandLineUtilities.Error("HydratePipTable requires an uncompressed PipTable.");
                    }

                    using (var reader = new BuildXLReader(debug: false, stream, leaveOpen: true))
                    using (var pipTable = PipTable.DeserializeAsync(
                        reader,
                        pathTableTask,
                        symbolTableTask,
                        initialBufferSize: 1 << 20,
                        maxDegreeOfParallelism: Environment.ProcessorCount,
                        debug: false).GetAwaiter().GetResult())
                    {
                        var loadElapsed = totalStopwatch.Elapsed;
                        int pipCount = 0;
                        long checksum = 0;
                        var hydrationStopwatch = Stopwatch.StartNew();
                        Parallel.ForEach(
                            pipTable.StableKeys,
                            new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
                            () => new HydrationResult(),
                            (pipId, _, localResult) =>
                            {
                                Pip pip = pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                                localResult.Checksum = unchecked(localResult.Checksum + pip.SemiStableHash);
                                localResult.PipCount++;
                                return localResult;
                            },
                            localResult =>
                            {
                                Interlocked.Add(ref checksum, localResult.Checksum);
                                Interlocked.Add(ref pipCount, localResult.PipCount);
                            });

                        hydrationStopwatch.Stop();
                        totalStopwatch.Stop();
                        Console.WriteLine(
                            $"Iteration {iteration}: pips={pipCount:N0}, load={loadElapsed.TotalMilliseconds:N1} ms, " +
                            $"hydrate={hydrationStopwatch.Elapsed.TotalMilliseconds:N1} ms, total={totalStopwatch.Elapsed.TotalMilliseconds:N1} ms, " +
                            $"read={pipTable.ReadsMilliseconds:N0} ms, dop={degreeOfParallelism}, checksum={checksum}");
                    }
                }
            }

            return 0;
        }

        private struct HydrationResult
        {
            public int PipCount;
            public long Checksum;
        }

        private int RunLoadDirectedGraph()
        {
            string graphFilePath = null;

            foreach (var opt in m_options)
            {
                if (opt.Name.Equals("graphFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("g", StringComparison.OrdinalIgnoreCase))
                {
                    graphFilePath = opt.Value;
                }
                else
                {
                    throw CommandLineUtilities.Error("Unknown option for LoadDirectedGraph benchmark: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(graphFilePath))
            {
                throw CommandLineUtilities.Error("/graphFile is a required parameter for LoadDirectedGraph benchmark.");
            }

            if (!File.Exists(graphFilePath))
            {
                throw CommandLineUtilities.Error("Graph file does not exist: {0}", graphFilePath);
            }

            string expectedFileName = nameof(GraphCacheFile.DirectedGraph);
            string actualFileName = Path.GetFileName(graphFilePath);
            if (!string.Equals(actualFileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw CommandLineUtilities.Error(
                    "The specified graph file name '{0}' does not match the expected name '{1}'. Due to the way the deserializer works, the file must be named '{1}'.",
                    actualFileName,
                    expectedFileName);
            }

            string directory = Path.GetDirectoryName(graphFilePath) ?? Directory.GetCurrentDirectory();
            var loggingContext = new LoggingContext("Benchmark.LoadDirectedGraph");
            var serializer = new EngineSerializer(loggingContext, directory, readOnly: true);

            Console.WriteLine($"Deserializing {graphFilePath}...");

            var sw = Stopwatch.StartNew();
            var graphTask = serializer.DeserializeFromFileAsync(
                GraphCacheFile.DirectedGraph,
                DeserializedDirectedGraph.DeserializeAsync,
                skipHeader: true);
            var graph = graphTask.GetAwaiter().GetResult();
            sw.Stop();

            Console.WriteLine($"Nodes: {graph.NodeCount:N0}, Edges: {graph.EdgeCount:N0}");
            Console.WriteLine($"Load time: {sw.ElapsedMilliseconds:N0} ms");

            return 0;
        }

        private int RunFilterGraph()
        {
            string graphDir = null;
            string filterFile = null;

            foreach (var opt in m_options)
            {
                if (opt.Name.Equals("graphDir", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("gd", StringComparison.OrdinalIgnoreCase))
                {
                    graphDir = opt.Value;
                }
                else if (opt.Name.Equals("filterFile", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("ff", StringComparison.OrdinalIgnoreCase))
                {
                    filterFile = opt.Value;
                }
                else
                {
                    throw CommandLineUtilities.Error("Unknown option for FilterGraph benchmark: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(graphDir))
            {
                throw CommandLineUtilities.Error("/graphDir is a required parameter for FilterGraph benchmark.");
            }

            if (!Directory.Exists(graphDir))
            {
                throw CommandLineUtilities.Error("Graph directory does not exist: {0}", graphDir);
            }

            if (string.IsNullOrEmpty(filterFile))
            {
                throw CommandLineUtilities.Error("/filterFile is a required parameter for FilterGraph benchmark.");
            }

            if (!File.Exists(filterFile))
            {
                throw CommandLineUtilities.Error("Filter file does not exist: {0}", filterFile);
            }

            string filterText = File.ReadAllText(filterFile).Trim();
            if (filterText.StartsWith("/f:", StringComparison.OrdinalIgnoreCase))
            {
                filterText = filterText.Substring(3);
            }

            Console.WriteLine($"Filter text length: {filterText.Length:N0} characters");

            // Phase 1: Load the cached graph
            Console.WriteLine($"Loading cached graph from {graphDir}...");
            var loggingContext = new LoggingContext("Benchmark.FilterGraph");
            var swLoad = Stopwatch.StartNew();
            var cachedGraph = CachedGraph.LoadAsync(graphDir, loggingContext, preferLoadingEngineCacheInMemory: false).GetAwaiter().GetResult();
            swLoad.Stop();

            if (cachedGraph == null)
            {
                throw CommandLineUtilities.Error("Failed to load cached graph from: {0}", graphDir);
            }

            var pipGraph = cachedGraph.PipGraph;
            Console.WriteLine($"Graph loaded in {swLoad.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"  Nodes: {cachedGraph.DirectedGraph.NodeCount:N0}, Edges: {cachedGraph.DirectedGraph.EdgeCount:N0}");
            Console.WriteLine($"  Pips: {pipGraph.PipTable.StableKeys.Count:N0}");

            // Phase 2: Parse the filter
            Console.WriteLine("Parsing filter...");
            var swParse = Stopwatch.StartNew();
            var context = cachedGraph.Context;
            var parser = new FilterParser(context, cachedGraph.MountPathExpander.TryGetRootByMountName, filterText, canonicalize: true);
            if (!parser.TryParse(out var rootFilter, out var parseError))
            {
                throw CommandLineUtilities.Error(
                    "Failed to parse filter at position {0}: {1}",
                    parseError.Position,
                    parseError.Message);
            }

            swParse.Stop();
            Console.WriteLine($"Filter parsed in {swParse.ElapsedMilliseconds:N0} ms");

            // Phase 3: Run FilterNodesToBuild
            var swFilter = Stopwatch.StartNew();
            bool success = pipGraph.FilterNodesToBuild(loggingContext, rootFilter, out var filteredNodes);
            swFilter.Stop();

            if (!success)
            {
                Console.WriteLine("WARNING: FilterNodesToBuild returned false (no pips matched).");
            }
            else
            {
                int nodeCount = filteredNodes.Count();
                int processCount = filteredNodes.Count(n => pipGraph.PipTable.GetPipType(n.ToPipId()) == PipType.Process);
                Console.WriteLine($"  Filter time:     {swFilter.ElapsedMilliseconds:N0} ms");
                Console.WriteLine($"  Matching nodes:  {nodeCount:N0}");
                Console.WriteLine($"  Matching procs:  {processCount:N0}");
            }

            // Report working set
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            Console.WriteLine($"  Peak working set: {proc.PeakWorkingSet64 / (1024 * 1024):N0} MB");

            Console.WriteLine();
            Console.WriteLine("--- Summary ---");
            Console.WriteLine($"  Graph load:  {swLoad.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"  Parse:       {swParse.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"  Filter:      {swFilter.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"  Peak WS:     {proc.PeakWorkingSet64 / (1024 * 1024):N0} MB");

            return 0;
        }

        private int RunConvertPipTable()
        {
            string graphDir = null;
            string outputFile = null;

            foreach (var opt in m_options)
            {
                if (opt.Name.Equals("graphDir", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("gd", StringComparison.OrdinalIgnoreCase))
                {
                    graphDir = opt.Value;
                }
                else if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = opt.Value;
                }
                else
                {
                    throw CommandLineUtilities.Error("Unknown option for ConvertPipTable benchmark: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(graphDir) || !Directory.Exists(graphDir))
            {
                throw CommandLineUtilities.Error("/graphDir must name an existing graph directory.");
            }

            if (string.IsNullOrEmpty(outputFile))
            {
                throw CommandLineUtilities.Error("/outputFile is a required parameter for ConvertPipTable.");
            }

            var loggingContext = new LoggingContext("Benchmark.ConvertPipTable");
            var serializer = new EngineSerializer(loggingContext, graphDir, readOnly: true);
            var stringTableTask = serializer.DeserializeFromFileAsync(GraphCacheFile.StringTable, StringTable.DeserializeAsync, skipHeader: true);
            var pathTableTask = serializer.DeserializeFromFileAsync(
                GraphCacheFile.PathTable,
                reader => reader.ReadPathTableAsync(stringTableTask),
                skipHeader: true);
            var symbolTableTask = serializer.DeserializeFromFileAsync(
                GraphCacheFile.SymbolTable,
                reader => reader.ReadSymbolTableAsync(stringTableTask),
                skipHeader: true);

            string pipTablePath = Path.Combine(graphDir, nameof(GraphCacheFile.PipTable));
            long inputSize = new FileInfo(pipTablePath).Length;
            var loadStopwatch = Stopwatch.StartNew();
            FileEnvelopeId correlationId;
            int pipCount;
            bool storeDebug;

            using (var stream = new FileStream(pipTablePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var legacyEnvelope = new FileEnvelope(name: "PipTable", version: 0);
                correlationId = legacyEnvelope.ReadHeader(stream);
                if (stream.ReadByte() != 0)
                {
                    throw CommandLineUtilities.Error("ConvertPipTable requires an uncompressed input PipTable.");
                }

                using (var binaryReader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    const uint FileBackedFormatMarker = 0x46505442;
                    uint formatMarker = binaryReader.ReadUInt32();
                    if (formatMarker != FileBackedFormatMarker)
                    {
                        throw CommandLineUtilities.Error("The input PipTable is not in the expected file-backed format.");
                    }

                    storeDebug = binaryReader.ReadBoolean();
                    pipCount = binaryReader.ReadInt32();
                    if (pipCount < 0)
                    {
                        throw new InvalidDataException("The file-backed PipTable has a negative pip count.");
                    }

                    long pageDataLength = 0;
                    for (int i = 0; i < pipCount; i++)
                    {
                        int length = binaryReader.ReadInt32();
                        if (length < 0)
                        {
                            throw new InvalidDataException($"Serialized pip {i + 1} has a negative length.");
                        }

                        pageDataLength = checked(pageDataLength + length);
                    }

                    if (stream.Position > stream.Length - pageDataLength)
                    {
                        throw new EndOfStreamException("The serialized pip payload exceeds the PipTable file length.");
                    }
                }

                PathTable pathTable = pathTableTask.GetAwaiter().GetResult();
                SymbolTable symbolTable = symbolTableTask.GetAwaiter().GetResult();
                loadStopwatch.Stop();
                var newTable = new PipTable(
                    pathTable,
                    symbolTable,
                    initialBufferSize: 1 << 20,
                    maxDegreeOfParallelism: Environment.ProcessorCount,
                    debug: false);

                int processCount = 0;
                var conversionStopwatch = Stopwatch.StartNew();
                using (var reader = new LegacyPipReader(
                    storeDebug,
                    stringTableTask.GetAwaiter().GetResult(),
                    stream,
                    leaveOpen: true))
                {
                    for (uint pipIdValue = 1; pipIdValue <= pipCount; pipIdValue++)
                    {
                        Pip pip = reader.ReadPip();
                        if (pip.PipType == PipType.Process)
                        {
                            processCount++;
                        }

                        newTable.Add(pipIdValue, pip);

                        if ((pipIdValue % 100000) == 0)
                        {
                            Console.WriteLine($"Converted {pipIdValue:N0} of {pipCount:N0} pips...");
                        }
                    }
                }

                newTable.WhenDone().GetAwaiter().GetResult();
                conversionStopwatch.Stop();

                string outputDirectory = Path.GetDirectoryName(outputFile);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var serializationStopwatch = Stopwatch.StartNew();
                using (var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 20))
                {
                    PipTable.FileEnvelope.WriteHeader(outputStream, correlationId);
                    outputStream.WriteByte(0);
                    using (var writer = new BuildXLWriter(debug: false, outputStream, leaveOpen: true, logStats: false))
                    {
                        newTable.Serialize(writer, Environment.ProcessorCount);
                    }

                    PipTable.FileEnvelope.FixUpHeader(outputStream, correlationId);
                }

                serializationStopwatch.Stop();
                long outputSize = new FileInfo(outputFile).Length;
                long savedBytes = inputSize - outputSize;

                Console.WriteLine();
                Console.WriteLine("--- PipTable conversion results ---");
                Console.WriteLine($"Pips:              {pipCount:N0}");
                Console.WriteLine($"Process pips:      {processCount:N0}");
                Console.WriteLine($"Input bytes:       {inputSize:N0}");
                Console.WriteLine($"Output bytes:      {outputSize:N0}");
                Console.WriteLine($"Bytes saved:       {savedBytes:N0}");
                Console.WriteLine($"Reduction:         {(double)savedBytes / inputSize:P2}");
                Console.WriteLine($"Header/table load: {loadStopwatch.Elapsed}");
                Console.WriteLine($"Hydrate/re-encode: {conversionStopwatch.Elapsed}");
                Console.WriteLine($"Final serialize:   {serializationStopwatch.Elapsed}");
                Console.WriteLine($"Peak working set:  {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024):N0} MB");

                newTable.Dispose();
            }

            return 0;
        }
    }
}
