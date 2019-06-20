// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Engine;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer.Analyzers.Simulator
{
    internal partial class PipExecutionData
    {
        #region Loaded State

        // Loaded state
        public DirectedGraph DataflowGraph { get; set; }

        public CachedGraph CachedGraph { get; }
        public ConcurrentNodeDictionary<ulong> StartTimes = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<ulong> Durations = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<string> FormattedSemistableHashes = new ConcurrentNodeDictionary<string>(false);

        public ulong MinStartTime = ulong.MaxValue;
        public ulong MaxEndTime;

        public ulong TotalDuration => MaxEndTime - MinStartTime;

        #endregion

        #region Computed State

        // Post loaded computed state
        public int[] PipTypeCounts = new int[EnumTraits<PipType>.MaxValue + 1];
        public ConcurrentNodeDictionary<NodeId> CriticalChain = new ConcurrentNodeDictionary<NodeId>(false);
        public ConcurrentNodeDictionary<ulong> AggregateCosts = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<ulong> ConstrainedAggregateCosts = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<ulong> BottomUpAggregateCosts = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<int> MaxConeConcurrency = new ConcurrentNodeDictionary<int>(false);
        public ulong MaxAggregateCost = 0;
        public NodeId CriticalPathHeadNode = NodeId.Invalid;
        public List<NodeId> CriticalPath = new List<NodeId>();

        public SymbolTable SymbolTable = new SymbolTable();

        public SortedSet<PipAndPriority> LongestRunningPips = new SortedSet<PipAndPriority>();
        public SortedSet<PipAndPriority> ShortestRunningProcesses = new SortedSet<PipAndPriority>();
        public int ActualConcurrency;

        // private readonly ConcurrentNodeDictionary<Pip> Pips = new ConcurrentNodeDictionary<Pip>();
        public ConcurrentNodeDictionary<int> PipIds = new ConcurrentNodeDictionary<int>(false);
        public ConcurrentNodeDictionary<int> RefCounts = new ConcurrentNodeDictionary<int>(false);
        public ConcurrentNodeDictionary<bool> Computed = new ConcurrentNodeDictionary<bool>(false);
        public object[] Locks = Enumerable.Range(0, 31).Select(i => new object()).ToArray();

        public int ComputedPips = 0;
        public List<PipSpan> Spans;

        #endregion

        public PipExecutionData(CachedGraph cachedGraph, DirectedGraph dataflowGraphOverride = null)
        {
            CachedGraph = cachedGraph;
            DataflowGraph = dataflowGraphOverride ?? CachedGraph.DataflowGraph;
        }

        public string GetName(NodeId node)
        {
            var pip = CachedGraph.PipTable.HydratePip(node.ToPipId(), BuildXL.Pips.PipQueryContext.ViewerAnalyzer);
            return pip.GetDescription(CachedGraph.Context);
        }

        public PipType GetPipType(NodeId node)
        {
            return CachedGraph.PipTable.GetPipType(node.ToPipId());
        }

        public SortedSet<PipAndPriority> GetSortedPips(int count, bool max, Func<NodeId, bool> selector, Func<NodeId, ulong> getPriority)
        {
            Comparer<PipAndPriority> comparer = Comparer<PipAndPriority>.Default;
            if (max)
            {
                comparer = Comparer<PipAndPriority>.Create((p1, p2) => p2.CompareTo(p1));
            }

            var sorted = new SortedSet<PipAndPriority>(comparer);
            var baseline = default(PipAndPriority);
            if (!max)
            {
                baseline.Priority = ulong.MaxValue;
            }

            foreach (var node in DataflowGraph.Nodes)
            {
                if (selector(node))
                {
                    var pipAndDuration = new PipAndPriority()
                    {
                        Node = node,
                        Priority = getPriority(node),
                    };

                    if (sorted.Count < count || CompareExchange(ref baseline, pipAndDuration, max))
                    {
                        sorted.Add(pipAndDuration);
                        baseline = sorted.Max;

                        if (sorted.Count > count)
                        {
                            sorted.Remove(baseline);
                            baseline = sorted.Max;
                        }
                    }
                }
            }

            return sorted;
        }

        public void Compute()
        {
            Console.WriteLine("Computing actual concurrency...");
            ComputeActualConcurrency();
            Console.WriteLine("End computing actual concurrency");
            Console.WriteLine("Computing aggregate costs...");
            ComputeAggregateCosts();
            Console.WriteLine("End computing aggregate costs");
        }

        /// <summary>
        /// Attempts to compute how many pips actually ran concurrently (max) during the actual build.
        /// </summary>
        public void ComputeActualConcurrency()
        {
            List<PipSpan> spans = new List<PipSpan>();

            LongestRunningPips = GetSortedPips(20, true, n => true, n => Durations[n]);
            ShortestRunningProcesses = GetSortedPips(20, false, n => GetPipType(n) == PipType.Process, n => Durations[n]);

            foreach (var node in DataflowGraph.Nodes)
            {
                var pipType = GetPipType(node);
                Interlocked.Increment(ref PipTypeCounts[(int)pipType]);
                var startTime = StartTimes[node];
                if (pipType == PipType.Process)
                {
                    var duration = Durations[node];
                    if (duration > 0)
                    {
                        if (MinStartTime > startTime)
                        {
                            System.Diagnostics.Debugger.Launch();
                        }

                        spans.Add(new PipSpan()
                        {
                            Id = node,
                            StartTime = startTime - MinStartTime,
                            Duration = duration,
                        });
                    }
                }
            }

            spans.Sort(new ComparerBuilder<PipSpan>().CompareByAfter(s => s.StartTime).CompareByAfter(s => s.Duration));
            Spans = spans;

            ConcurrentDenseIndex<int> concurrencyIndex = new ConcurrentDenseIndex<int>(false);
            ConcurrentDenseIndex<int> concurrencyCount = new ConcurrentDenseIndex<int>(false);
            for (int i = 0; i < spans.Count; i++)
            {
                PipSpan s = spans[i];
                ulong endTime = s.EndTime;
                for (int j = i; j < spans.Count; j++)
                {
                    PipSpan t = spans[j];
                    if (t.StartTime < s.EndTime)
                    {
                        int value = concurrencyIndex[(uint)j] + 1;
                        concurrencyIndex[(uint)j] = value;
                        Max(ref ActualConcurrency, value);
                    }
                    else
                    {
                        break;
                    }
                }

                var c = concurrencyIndex[(uint)i];
                concurrencyCount[(uint)c] = concurrencyCount[(uint)c] + 1;
            }
        }

        public static bool CompareExchange<T>(ref T value, T comparand, bool max) where T : IComparable<T>
        {
            var result = value.CompareTo(comparand);
            if (!max)
            {
                result = -result;
            }

            if (result < 0)
            {
                value = comparand;
                return true;
            }

            return false;
        }

        public static bool Max<T>(ref T value, T comparand) where T : IComparable<T>
        {
            if (value.CompareTo(comparand) < 0)
            {
                value = comparand;
                return true;
            }

            return false;
        }

        public static bool Min<T>(ref T value, T comparand) where T : IComparable<T>
        {
            if (value.CompareTo(comparand) > 0)
            {
                value = comparand;
                return true;
            }

            return false;
        }

        public void ComputeAggregateCosts()
        {
            PipAndPriority maxPipAndPriority = default(PipAndPriority);
            List<NodeId> sortedNodes = new List<NodeId>();
            sortedNodes.AddRange(DataflowGraph.Nodes);
            sortedNodes.Sort((n1, n2) => DataflowGraph.GetNodeHeight(n1).CompareTo(DataflowGraph.GetNodeHeight(n2)));
            foreach (var node in sortedNodes)
            {
                ulong aggregateCost = 0;
                foreach (var incoming in DataflowGraph.GetIncomingEdges(node))
                {
                    BottomUpAggregateCosts[incoming.OtherNode].Max(ref aggregateCost);
                }

                aggregateCost += Durations[node];
                BottomUpAggregateCosts[node] = aggregateCost;
            }

            sortedNodes.Sort((n1, n2) => -DataflowGraph.GetNodeHeight(n1).CompareTo(DataflowGraph.GetNodeHeight(n2)));
            foreach (var node in sortedNodes)
            {
                // int maxConeConcurrency = 0;
                ulong aggregateCost = 0, constrainedAggregateCost = 0;
                NodeId maxChild = NodeId.Invalid;
                foreach (var outgoing in DataflowGraph.GetOutgoingEdges(node))
                {
                    ConstrainedAggregateCosts[outgoing.OtherNode].Max(ref constrainedAggregateCost);

                    if (AggregateCosts[outgoing.OtherNode].Max(ref aggregateCost) || !maxChild.IsValid)
                    {
                        maxChild = outgoing.OtherNode;
                    }
                }

                FormattedSemistableHashes[node] = CachedGraph.PipTable.GetFormattedSemiStableHash(node.ToPipId());
                aggregateCost += Durations[node];
                AggregateCosts[node] = aggregateCost;
                aggregateCost.Max(ref MaxAggregateCost);
                CriticalChain[node] = maxChild;

                new PipAndPriority
                {
                    Node = node,
                    Priority = aggregateCost,
                }.Max(ref maxPipAndPriority);
            }

            CriticalPathHeadNode = maxPipAndPriority.Node;

            NodeId criticalChainNode = CriticalPathHeadNode;
            while (criticalChainNode.IsValid)
            {
                CriticalPath.Add(criticalChainNode);
                criticalChainNode = CriticalChain[criticalChainNode];
            }
        }
    }
}
