// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
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
        }
    }

    /// <summary>
    /// Generic benchmark dispatcher. Add new operations as methods and register them in the Run() switch.
    /// </summary>
    internal sealed class BenchmarkAnalyzer
    {
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
                default:
                    throw CommandLineUtilities.Error("Unknown benchmark operation: {0}", m_operation);
            }
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
    }
}
