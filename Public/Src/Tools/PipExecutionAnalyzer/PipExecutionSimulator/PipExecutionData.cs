// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PipExecutionSimulator
{
    public partial class PipExecutionData
    {
        // Serialized state
        public MutableDirectedGraph MutableDataflowGraph = new MutableDirectedGraph();
        public DirectedGraph DataflowGraph;
        public ConcurrentNodeDictionary<ulong> StartTimes = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<ulong> SemiStableHashes = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<FullSymbol> OutputValues = new ConcurrentNodeDictionary<FullSymbol>(false);
        public ConcurrentNodeDictionary<ulong> Durations = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<PipType> PipTypes = new ConcurrentNodeDictionary<PipType>(false);
        public ConcurrentNodeDictionary<NodeId> CriticalChain = new ConcurrentNodeDictionary<NodeId>(false);
        public ulong MinStartTime = ulong.MaxValue;
        public ulong MaxEndTime;
        public ulong TotalDuration;

        public int[] PipTypeCounts = new int[EnumTraits<PipType>.MaxValue + 1];

        public ConcurrentNodeDictionary<ulong> AggregateCosts = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<ulong> ConstrainedAggregateCosts = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<int> MaxConeConcurrency = new ConcurrentNodeDictionary<int>(false);
        public ulong MaxAggregateCost = 0;
        public NodeId CriticalPathHeadNode = NodeId.Invalid;
        public List<NodeId> CriticalPath = new List<NodeId>();

        public SymbolTable SymbolTable = new SymbolTable();

        public SortedSet<PipAndPriority> LongestRunningPips = new SortedSet<PipAndPriority>();
        public SortedSet<PipAndPriority> ShortestRunningProcesses = new SortedSet<PipAndPriority>();
        public int ActualConcurrency;
        //private readonly ConcurrentNodeDictionary<Pip> Pips = new ConcurrentNodeDictionary<Pip>();
        public ConcurrentNodeDictionary<int> PipIds = new ConcurrentNodeDictionary<int>(false);
        public ConcurrentNodeDictionary<int> RefCounts = new ConcurrentNodeDictionary<int>(false);
        public ConcurrentNodeDictionary<bool> Computed = new ConcurrentNodeDictionary<bool>(false);
        public ConcurrentNodeDictionary<List<NodeId>> Refs = new ConcurrentNodeDictionary<List<NodeId>>(false);
        public object[] locks = Enumerable.Range(0, 31).Select(i => new object()).ToArray();

        private ConcurrentDictionary<NodeId, int> reservations = new ConcurrentDictionary<NodeId, int>();
        public int ComputedPips = 0;
        public int ReservationId = 0;
        public List<PipSpan> Spans;

        public string GetName(NodeId node)
        {
            return OutputValues[node].ToString(SymbolTable);
        }

        public SortedSet<PipAndPriority> GetSortedPips(int count, bool max, Func<NodeId, bool> selector, Func<NodeId, ulong> getPriority)
        {
            Comparer<PipAndPriority> comparer = Comparer<PipAndPriority>.Default;
            if (max)
            {
                comparer = Comparer<PipAndPriority>.Create((p1, p2) => p2.CompareTo(p1));
            }

            var sorted = new SortedSet<PipAndPriority>(comparer);
            var baseline = new PipAndPriority();
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
                        Priority = getPriority(node)
                    };

                    if (sorted.Count < 20 || CompareExchange(ref baseline, pipAndDuration, max))
                    {
                        sorted.Add(pipAndDuration);
                        baseline = sorted.Max;

                        if (sorted.Count > 20)
                        {
                            sorted.Remove(baseline);
                            baseline = sorted.Max;
                        }
                    }
                }
            }

            return sorted;
        }

        public void ComputeActualConcurrency()
        {
            List<PipSpan> spans = new List<PipSpan>();

            LongestRunningPips = GetSortedPips(20, true, n => true, n => Durations[n]);
            ShortestRunningProcesses = GetSortedPips(20, false, n => PipTypes[n] == PipType.Process, n => Durations[n]);

            foreach (var node in DataflowGraph.Nodes)
            {
                Interlocked.Increment(ref PipTypeCounts[(int)PipTypes[node]]);
                var startTime = StartTimes[node];
                if (PipTypes[node] == PipType.Process)
                {
                    spans.Add(new PipSpan()
                        {
                            Id = node,
                            StartTime = startTime - MinStartTime,
                            Duration = Durations[node]
                        });
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
                    if (t.StartTime <= s.EndTime)
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

        public ConcurrentNodeDictionary<ulong> ComputeAggregateCosts(ConcurrentNodeDictionary<ulong> durations)
        {
            ConcurrentNodeDictionary<ulong> aggregateCosts = new ConcurrentNodeDictionary<ulong>(true);

            List<NodeId> sortedNodes = new List<NodeId>();
            sortedNodes.AddRange(DataflowGraph.Nodes);
            sortedNodes.Sort((n1, n2) => -DataflowGraph.GetNodeHeight(n1).CompareTo(DataflowGraph.GetNodeHeight(n2)));
            foreach (var node in sortedNodes)
            {
                //int maxConeConcurrency = 0;
                ulong aggregateCost = 0;
                NodeId maxChild = NodeId.Invalid;
                foreach (var outgoing in DataflowGraph.GetOutgoingEdges(node))
                {
                    if (aggregateCosts[outgoing.OtherNode].Max(ref aggregateCost) || !maxChild.IsValid())
                    {
                        maxChild = outgoing.OtherNode;
                    }
                }

                aggregateCost += durations[node];
                aggregateCosts[node] = aggregateCost;
            }

            return aggregateCosts;
        }

        public void ComputeAggregateCosts()
        {
            PipAndPriority maxPipAndPriority = default(PipAndPriority);
            List<NodeId> sortedNodes = new List<NodeId>();
            sortedNodes.AddRange(DataflowGraph.Nodes);
            sortedNodes.Sort((n1, n2) => -DataflowGraph.GetNodeHeight(n1).CompareTo(DataflowGraph.GetNodeHeight(n2)));
            foreach (var node in sortedNodes)
            {
                //int maxConeConcurrency = 0;
                ulong aggregateCost = 0, constrainedAggregateCost = 0;
                NodeId maxChild = NodeId.Invalid;
                foreach (var outgoing in DataflowGraph.GetOutgoingEdges(node))
                {
                    ConstrainedAggregateCosts[outgoing.OtherNode].Max(ref constrainedAggregateCost);

                    if (AggregateCosts[outgoing.OtherNode].Max(ref aggregateCost) || !maxChild.IsValid())
                    {
                        maxChild = outgoing.OtherNode;
                    }
                }

                aggregateCost += Durations[node];
                AggregateCosts[node] = aggregateCost;
                aggregateCost.Max(ref MaxAggregateCost);
                CriticalChain[node] = maxChild;

                new PipAndPriority()
                {
                    Node = node,
                    Priority = aggregateCost
                }.Max(ref maxPipAndPriority);
            }

            CriticalPathHeadNode = maxPipAndPriority.Node;

            NodeId criticalChainNode = CriticalPathHeadNode;
            while (criticalChainNode.IsValid())
            {
                CriticalPath.Add(criticalChainNode);
                criticalChainNode = CriticalChain[criticalChainNode];
            }
        }
    }


    /// <summary>
    /// Typed wrapper around ConcurrentDenseIndex
    /// </summary>
    public struct ConcurrentNodeDictionary<TValue>
    {
        private readonly ConcurrentDenseIndex<TValue> m_index;

        public ConcurrentNodeDictionary(bool debug)
        {
            m_index = new ConcurrentDenseIndex<TValue>(debug: debug);
        }

        public TValue this[NodeId node]
        {
            get { return m_index[node.Value]; }
            set { m_index[node.Value] = value; }
        }

        public TValue this[uint index]
        {
            get { return m_index[index]; }
            set { m_index[index] = value; }
        }
    }
}
