// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Execution.Analyzer.Model;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeCriticalPathAnalyzer()
        {
            string outputFilePath = null;
            List<ExecutionEventId> excludedEvents = new List<ExecutionEventId>();
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("exclude", StringComparison.OrdinalIgnoreCase))
                {
                    excludedEvents.Add(ParseEnumOption<ExecutionEventId>(opt));
                }
                else
                {
                    throw Error("Unknown option for critical path analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("outputFile parameter is required");
            }

            return new CriticalPathAnalyzer(GetAnalysisInput(), outputFilePath);
        }

        private static void WriteCriticalPathAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Critical Path Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.CriticalPath), "Generates file containing information about build critical path");
            writer.WriteOption("outputFile", "Required. The location of the output file for critical path analysis.", shortName: "o");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    public sealed class CriticalPathAnalyzer : Analyzer
    {
        public CriticalPathData criticalPathData = new CriticalPathData();

        private struct Times
        {
            public TimeSpan WallClockTime;
            public TimeSpan UserTime;
            public TimeSpan KernalTime;
        }

        private readonly TextWriter m_writer;
        private readonly int m_criticalPathCount = 20;

        private readonly TopQueue m_topWallClockPriorityQueue = new TopQueue();
        private readonly TopQueue m_topUserTimePriorityQueue = new TopQueue();
        private readonly TopQueue m_topKernelTimePriorityQueue = new TopQueue();

        private readonly ConcurrentDenseIndex<Times> m_elapsedTimes = new ConcurrentDenseIndex<Times>(false);
        private readonly ConcurrentDenseIndex<NodeAndCriticalPath> m_criticalPaths = new ConcurrentDenseIndex<NodeAndCriticalPath>(false);

        /// <summary>
        /// Creates an exporter which writes text to <paramref name="output" />.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CriticalPathAnalyzer(AnalysisInput input, string outputFilePath)
            : base(input)
        {
            if (outputFilePath != null)
            {
                m_writer = new StreamWriter(outputFilePath);
            }
            else
            {
                m_writer = new StringWriter();
            }                      
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_writer.Dispose();
            base.Dispose();
        }

        public override int Analyze()
        {
            Console.WriteLine("Starting analysis");
            HashSet<NodeId> sourceNodes = new HashSet<NodeId>(CachedGraph.DataflowGraph.GetSourceNodes());
            NodeAndCriticalPath[] criticalPaths = new NodeAndCriticalPath[sourceNodes.Count];

            Console.WriteLine("Computing critical paths");
            VisitationTracker visitedNodes = new VisitationTracker(CachedGraph.DataflowGraph);
            int i = 0;
            foreach (var sourceNode in sourceNodes)
            {
                criticalPaths[i] = ComputeCriticalPath(sourceNode, visitedNodes);
                i++;
            }

            Console.WriteLine("Sorting critical paths");
            Array.Sort(criticalPaths, (c1, c2) => -c1.Time.CompareTo(c2.Time));

            Console.WriteLine("Writing critical paths");
            int count = Math.Min(m_criticalPathCount, criticalPaths.Length);
            m_writer.WriteLine("Top {0} critical paths (dependencies first)", count);

            for (int j = 0; j < count; j++)
            {
                var nodeAndCriticalPath = criticalPaths[j];

                m_writer.WriteLine("CRITICAL PATH {0}", j + 1);
                m_writer.WriteLine("TIME: {0} seconds", ToSeconds(nodeAndCriticalPath.Time));
                m_writer.WriteLine();

                m_writer.WriteLine(PrintCriticalPath(nodeAndCriticalPath, GetElapsed, GetKernelTime, GetUserTime, GetCriticalPath, out var criticalpath));
                m_writer.WriteLine();

                criticalPathData.criticalPaths.Add(criticalpath);
            }

            criticalPathData.wallClockTopPips = PrintQueue("Wall Clock", m_topWallClockPriorityQueue);
            criticalPathData.userTimeTopPips = PrintQueue("User Time", m_topUserTimePriorityQueue);
            criticalPathData.kernelTimeTopPips = PrintQueue("Kernel Time", m_topKernelTimePriorityQueue);

            return 0;
        }

        private List<Node> PrintQueue(string title, TopQueue queue)
        {
            var nodes = new List<Node>();

            m_writer.WriteLine("{0} top pips", title);
            foreach (var topPip in queue.Nodes())
            {
                m_writer.WriteLine("({0}) {1}",
                    ToSeconds(topPip.Item2).PadLeft(12),
                    GetDescription(GetPip(topPip.Item1)));

                var node = new Node();
                node.pipId = topPip.Item1.Value.ToString("X16", CultureInfo.InvariantCulture).TrimStart(new Char[] { '0' });
                node.duration = ToSeconds(topPip.Item2).PadLeft(12);
                node.shortDescription = GetDescription(GetPip(topPip.Item1));

                nodes.Add(node);
            }

            m_writer.WriteLine();

            return nodes;
        }

        private NodeAndCriticalPath ComputeCriticalPath(NodeId node, VisitationTracker visitedNodes)
        {
            var criticalPath = GetCriticalPath(node);

            if (criticalPath.Node.IsValid)
            {
                return criticalPath;
            }

            criticalPath = new NodeAndCriticalPath()
            {
                Node = node,
                Time = GetElapsed(node),
            };

            if (!visitedNodes.MarkVisited(node))
            {
                return criticalPath;
            }

            NodeAndCriticalPath maxDependencyCriticalPath = default(NodeAndCriticalPath);
            foreach (var dependency in CachedGraph.DataflowGraph.GetOutgoingEdges(node))
            {
                var dependencyCriticalPath = ComputeCriticalPath(dependency.OtherNode, visitedNodes);
                if (dependencyCriticalPath.Time > maxDependencyCriticalPath.Time)
                {
                    maxDependencyCriticalPath = dependencyCriticalPath;
                }
            }

            criticalPath.Next = maxDependencyCriticalPath.Node;
            criticalPath.Time += maxDependencyCriticalPath.Time;

            SetCriticalPath(node, criticalPath);
            return criticalPath;
        }

        private NodeAndCriticalPath GetCriticalPath(NodeId node)
        {
            return m_criticalPaths[node.Value];
        }

        private void SetCriticalPath(NodeId node, NodeAndCriticalPath criticalPath)
        {
            m_criticalPaths[node.Value] = criticalPath;

            var times = m_elapsedTimes[node.Value];
            m_topWallClockPriorityQueue.Add(node, times.WallClockTime);
            m_topKernelTimePriorityQueue.Add(node, times.KernalTime);
            m_topUserTimePriorityQueue.Add(node, times.UserTime);
        }

        private TimeSpan GetElapsed(NodeId node)
        {
            return m_elapsedTimes[node.Value].WallClockTime;
        }

        private TimeSpan GetKernelTime(NodeId node)
        {
            return m_elapsedTimes[node.Value].KernalTime;
        }

        private TimeSpan GetUserTime(NodeId node)
        {
            return m_elapsedTimes[node.Value].UserTime;
        }

        /// <inheritdoc />
        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            if (data.Step.IncludeInRunningTime())
            {
                var times = m_elapsedTimes[data.PipId.Value];
                times.WallClockTime = data.Duration > times.WallClockTime ? data.Duration : times.WallClockTime;
                m_elapsedTimes[data.PipId.Value] = times;
            }
        }

        /// <inheritdoc />
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            if (data.ExecutionPerformance != null)
            {
                ProcessPipExecutionPerformance processPerformance = data.ExecutionPerformance as ProcessPipExecutionPerformance;
                if (processPerformance != null)
                {
                    var times = m_elapsedTimes[data.PipId.Value];
                    times.KernalTime = processPerformance.KernelTime;
                    times.UserTime = processPerformance.UserTime;

                    m_elapsedTimes[data.PipId.Value] = times;
                }
            }
        }

        private class TopQueue
        {
            private const int MaxCount = 20;
            private readonly PriorityQueue<StructTuple<NodeId, TimeSpan>> m_queue
                = new PriorityQueue<StructTuple<NodeId, TimeSpan>>(MaxCount + 1, Comparer<StructTuple<NodeId, TimeSpan>>.Create(Compare));
            
            public IEnumerable<StructTuple<NodeId, TimeSpan>> Nodes()
            {
                StructTuple<NodeId, TimeSpan>[] nodes = new StructTuple<NodeId, TimeSpan>[m_queue.Count];
                for (int i = nodes.Length - 1; i >= 0; i--)
                {
                    nodes[i] = m_queue.Top;
                    m_queue.Pop();
                }

                return nodes;
            }

            public void Add(NodeId node, TimeSpan time)
            {
                m_queue.Push(StructTuple.Create(node, time));
                if (m_queue.Count > MaxCount)
                {
                    m_queue.Pop();
                }
            }

            private static int Compare(StructTuple<NodeId, TimeSpan> x, StructTuple<NodeId, TimeSpan> y)
            {
                // Compare with least first
                return x.Item2.CompareTo(y.Item2);
            }
        }
    }
}
