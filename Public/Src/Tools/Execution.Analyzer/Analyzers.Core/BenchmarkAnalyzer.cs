// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Pips.DirectedGraph;
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
    }
}
