// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public SpecClosureAnalyzer InitializeSpecClosureAnalyzer()
        {
            string outputFilePath = null;
            string dependenciesFilePath = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("output", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("dep", StringComparison.OrdinalIgnoreCase))
                {
                    dependenciesFilePath = ParseSingletonPathOption(opt, dependenciesFilePath);
                }
                else
                {
                    throw Error("Unknown option for whitelist analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("output is a required parameter.");
            }

            if (string.IsNullOrEmpty(dependenciesFilePath))
            {
                throw Error("dep is a required parameter.");
            }

            return new SpecClosureAnalyzer()
            {
                DependenciesFilePath = dependenciesFilePath,
                OutputPath = outputFilePath,
            };
        }

        private static void WriteSpecClosureAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("SpecClosure Analysis");
            writer.WriteOption("dep", "The path to the spec dependencies csv file");
            writer.WriteOption("output", "Required. The file to write analysis results.", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to generate fingerprint text file
    /// </summary>
    internal sealed class SpecClosureAnalyzer
    {
        /// <summary>
        /// The path to the whitelist directory
        /// </summary>
        public string OutputPath;
        public string DependenciesFilePath;
        public PipExecutionContext Context = BuildXLContext.CreateInstanceForTesting();

        public MutableDirectedGraph Graph = new MutableDirectedGraph();
        private SpecData[] m_results;
        public ConcurrentBigMap<string, NodeId> SpecToNodeId = new ConcurrentBigMap<string, NodeId>();

        private int m_index = -1;

        public SpecClosureAnalyzer()
        {
        }

        public void Analyze()
        {
            bool isFirst = true;
            foreach (var line in File.ReadLines(DependenciesFilePath))
            {
                if (isFirst)
                {
                    isFirst = false;
                    continue;
                }

                var parts = line.Split(';').Select(s => s.Trim()).ToArray();

                var consumer = SpecToNodeId.GetOrAdd(parts[1], Graph, (path, graph) => graph.CreateNode()).Item.Value;
                var producer = SpecToNodeId.GetOrAdd(parts[3], Graph, (path, graph) => graph.CreateNode()).Item.Value;
                Graph.AddEdge(producer, consumer);
            }

            m_results = new SpecData[SpecToNodeId.Count];

            int concurrency = 30;
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < concurrency; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    ComputationContext context = new ComputationContext(Graph);
                    while (true)
                    {
                        var index = Interlocked.Increment(ref m_index);
                        if (index >= SpecToNodeId.Count)
                        {
                            return;
                        }

                        var entry = SpecToNodeId.BackingSet[index];
                        ComputeData(context, index, node: entry.Value, path: entry.Key);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());

            using (var writer = new StreamWriter(OutputPath))
            {
                writer.WriteLine("Path, Dependency Count, Dependent Count, Implied Dependency Count");

                foreach (var data in m_results.OrderBy(d => d.DependencyCount))
                {
                    writer.WriteLine("{0}, {1}, {2}, {3}", data.Path, data.DependencyCount, data.DependentCount, data.ImpliedDependencyCount);
                }
            }
        }

        private void ComputeData(ComputationContext context, int index, NodeId node, string path)
        {
            var data = new SpecData();
            data.Path = path;

            context.Visitor.VisitTransitiveDependencies(node, context.Dependencies, n => true);
            data.DependencyCount = context.Dependencies.VisitedCount;

            context.Visitor.VisitTransitiveDependents(node, context.Dependents, n =>
            {
                context.DependentSet.Add(n);
                return true;
            });

            data.DependentCount = context.Dependents.VisitedCount;

            context.Visitor.VisitTransitiveDependencies(context.DependentSet, context.Dependencies, n => true);
            data.ImpliedDependencyCount = context.Dependencies.VisitedCount;

            m_results[index] = data;

            context.Clear();
        }

        public class ComputationContext
        {
            public VisitationTracker Dependencies;
            public VisitationTracker Dependents;
            public NodeVisitor Visitor;
            public HashSet<NodeId> DependentSet = new HashSet<NodeId>();

            public ComputationContext(MutableDirectedGraph graph)
            {
                Dependencies = new VisitationTracker(graph);
                Dependents = new VisitationTracker(graph);
                Visitor = new NodeVisitor(graph);
            }

            public void Clear()
            {
                Dependencies.UnsafeReset();
                Dependents.UnsafeReset();
                DependentSet.Clear();
            }
        }

        private class SpecData
        {
            public string Path;
            public int DependencyCount;
            public int DependentCount;
            public int ImpliedDependencyCount;
        }
    }
}
