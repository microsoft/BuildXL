// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Scheduler.Graph;

namespace BuildXL.Execution.Analyzer.Analyzers.Simulator
{
    internal class SimulationResult
    {
        public PipExecutionData ExecutionData;
        public SortedSet<ExecutionThread> Threads = new SortedSet<ExecutionThread>();
        public ConcurrentNodeDictionary<int> RefCounts = new ConcurrentNodeDictionary<int>(false);
        public ConcurrentNodeDictionary<ulong> EndTimes = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<ulong> MinimumStartTimes = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<NodeId> LastRunningDependency = new ConcurrentNodeDictionary<NodeId>(false);
        public ConcurrentNodeDictionary<NodeId> LongestRunningDependency = new ConcurrentNodeDictionary<NodeId>(false);
        public ConcurrentNodeDictionary<ulong> Priorities;
        public ConcurrentNodeDictionary<ulong> StartTimes = new ConcurrentNodeDictionary<ulong>(false);

        private readonly SortedSet<PipAndPriority> m_readyAtMinimumStartTimePips = new SortedSet<PipAndPriority>();
        private readonly SortedSet<PipAndPriority> m_readyWithPriorityPips = new SortedSet<PipAndPriority>();
        private readonly List<PipAndPriority> m_pipBuffer = new List<PipAndPriority>();

        private ulong m_simulationCurrentTime = 0;
        public ulong TotalTime = 0;
        public ExecutionThread ThreadWithLatestEndTime;
        public ulong TotalActiveTime = 0;
        public int CompletedPipCount;

        public List<PipSpan> GetSpans()
        {
            List<PipSpan> spans = new List<PipSpan>();
            foreach (var thread in Threads)
            {
                spans.AddRange(thread.Executions);
            }

            spans.Sort(new ComparerBuilder<PipSpan>().CompareByAfter(s => s.StartTime).CompareByAfter(s => s.Duration));

            return spans;
        }

        public SimulationResult(PipExecutionData executionData, ConcurrentNodeDictionary<ulong> priorities)
        {
            ExecutionData = executionData;
            Priorities = priorities;
        }

        public ConcurrentNodeDictionary<ulong> GetAdjustedDurations(ulong threshold)
        {
            ConcurrentNodeDictionary<ulong> durations = new ConcurrentNodeDictionary<ulong>(true);

            foreach (var node in ExecutionData.DataflowGraph.Nodes)
            {
                if (EndTimes[node] > threshold)
                {
                    // TODO: find out what this means!
                    // Debugger.Launch();
                }

                durations[node] = ExecutionData.Durations[node] + Math.Max(0, EndTimes[node] - threshold);
            }

            return durations;
        }

        public ulong GetAdjustedStartTime(NodeId pip)
        {
            return ExecutionData.StartTimes[pip] - ExecutionData.MinStartTime;
        }

        public void DetectCycles()
        {
            var graph = ExecutionData.DataflowGraph;
            BitArray bitArray = new BitArray(graph.NodeCount + 1);
            Stack<NodeId> nodeStack = new Stack<NodeId>();
            HashSet<NodeId> visitedNodes = new HashSet<NodeId>();

            foreach (var node in ExecutionData.DataflowGraph.Nodes)
            {
                DetectCyclesCore(node, visitedNodes, nodeStack, bitArray);
            }
        }

        private void DetectCyclesCore(NodeId node, HashSet<NodeId> visitedNodes, Stack<NodeId> nodeStack, BitArray bitArray)
        {
            if (bitArray[(int)node.Value])
            {
                return;
            }

            if (!visitedNodes.Add(node))
            {
                Console.WriteLine("Cycle detected:");
                foreach (var cycleNode in nodeStack)
                {
                    Console.WriteLine($"{ExecutionData.GetName(cycleNode)} [{cycleNode.Value}]");
                }

                Console.WriteLine("------");
                Console.WriteLine();

                throw new Exception("Cycle detected");
            }
            else
            {
                nodeStack.Push(node);
                var graph = ExecutionData.DataflowGraph;
                foreach (var dependent in graph.GetOutgoingEdges(node))
                {
                    DetectCyclesCore(dependent.OtherNode, visitedNodes, nodeStack, bitArray);
                }

                visitedNodes.Remove(node);
                var poppedNode = nodeStack.Pop();
                if (poppedNode != node)
                {
                }
            }

            bitArray[(int)node.Value] = true;
        }

        public void UpdateTimeAndScheduleReadyAfterTimePips(ulong time)
        {
            var newSimulationCurrentTime = Math.Max(time, m_simulationCurrentTime);
            if (newSimulationCurrentTime == m_simulationCurrentTime && time != 0)
            {
                return;
            }

            m_simulationCurrentTime = newSimulationCurrentTime;

            foreach (var readyAtMinimumStartTimePip in m_readyAtMinimumStartTimePips)
            {
                var minimumStartTime = readyAtMinimumStartTimePip.Priority;

                // If pip can start before curent simulation time,
                // it should be removed and added to ready pips with priority
                if (minimumStartTime <= m_simulationCurrentTime)
                {
                    // Add to buffer because you can't remove as you are iterating
                    m_pipBuffer.Add(readyAtMinimumStartTimePip);
                }
                else
                {
                    break;
                }
            }

            foreach (var readyAtMinimumStartTimePip in m_pipBuffer)
            {
                // Actually remove from m_readyAtMinimumStartTimePips
                m_readyAtMinimumStartTimePips.Remove(readyAtMinimumStartTimePip);

                // Add to ready pips with priority since this pip is schedulable at the current simulation
                // time
                m_readyWithPriorityPips.Add(GetPipAndPriority(readyAtMinimumStartTimePip.Node));
            }

            m_pipBuffer.Clear();
        }

        public void Simulate(int maxThreadCount)
        {
            DetectCycles();

            // how many outstanding direct dependencies have not been processed yet
            // does what the scheduler does
            foreach (var node in ExecutionData.DataflowGraph.Nodes)
            {
                RefCounts[node] = ExecutionData.DataflowGraph.GetIncomingEdgesCount(node);
            }

            Contract.Assert(maxThreadCount > 0);

            SortedSet<ExecutionThread> availableThreadsSortedByEndTime = new SortedSet<ExecutionThread>();
            for (int i = 0; i < maxThreadCount; i++)
            {
                availableThreadsSortedByEndTime.Add(AddThread(0));
            }

            // what is a source node? all nodes that have no dependecies, can be build without anything before?
            foreach (var sourceNode in ExecutionData.DataflowGraph.GetSourceNodes())
            {
                AddOrComplete(m_simulationCurrentTime, sourceNode);
            }

            UpdateTimeAndScheduleReadyAfterTimePips(0);

            while (m_readyWithPriorityPips.Count != 0)
            {
                NodeId maxPriorityPip = TakeMax(m_readyWithPriorityPips).Node;

                var minimumStartTime = MinimumStartTimes[maxPriorityPip];
                UpdateTimeAndScheduleReadyAfterTimePips(minimumStartTime);

                // get the thread which will complete soonest
                // available threads is sorted based on the completion time of the thread (i.e
                // the time when the current process on that thread completes)
                ExecutionThread thread = availableThreadsSortedByEndTime.Min;

                // remove therad and add again to update its order in the sorted list (available threads) now that thread has new end time
                availableThreadsSortedByEndTime.Remove(thread);
                thread.AddExecution(maxPriorityPip, this);
                availableThreadsSortedByEndTime.Add(thread);

                // The current time of the build is now that of the thread with earliest end time
                // so update it to add pips are now ready
                UpdateTimeAndScheduleReadyAfterTimePips(availableThreadsSortedByEndTime.Min.EndTime);

                // Still no ready pips,
                // Go through all the threads sorted by end time in ascending order (ie earliest first)
                // to update current simulation to the threads end time
                // until more pips are ready.
                if (m_readyWithPriorityPips.Count == 0)
                {
                    foreach (var currentThread in availableThreadsSortedByEndTime)
                    {
                        UpdateTimeAndScheduleReadyAfterTimePips(currentThread.EndTime);

                        if (m_readyWithPriorityPips.Count != 0)
                        {
                            break;
                        }
                    }
                }
            }

            foreach (var thread in Threads)
            {
                if (thread.EndTime > TotalTime)
                {
                    TotalTime = thread.EndTime;
                    ThreadWithLatestEndTime = thread;
                }

                TotalActiveTime += thread.ActiveTime;
            }

            var graph = ExecutionData.DataflowGraph;
            foreach (var thread in Threads)
            {
                ulong minStart = 0;
                foreach (var execution in thread.Executions)
                {
                    var node = execution.Id;
                    Assert(execution.StartTime >= minStart);

                    Assert(execution.EndTime == (execution.StartTime + execution.Duration));

                    Assert(execution.StartTime == StartTimes[node]);

                    Assert(execution.EndTime == EndTimes[node]);

                    ulong maxDuration = 0;

                    foreach (var incoming in graph.GetIncomingEdges(node))
                    {
                        var longestRunningDepCandidate = incoming.OtherNode;
                        var pipType = ExecutionData.GetPipType(longestRunningDepCandidate);

                        if (pipType == Pips.Operations.PipType.SealDirectory)
                        {
                            var dep = LongestRunningDependency[longestRunningDepCandidate];
                            if (dep.IsValid)
                            {
                                longestRunningDepCandidate = dep;
                            }
                        }

                        var duration = ExecutionData.Durations[longestRunningDepCandidate];
                        if (duration > maxDuration)
                        {
                            LongestRunningDependency[node] = longestRunningDepCandidate;
                            maxDuration = duration;
                        }

                        var incomingEndTime = EndTimes[incoming.OtherNode];

                        Assert(incomingEndTime <= execution.StartTime);
                    }

                    foreach (var outgoing in graph.GetOutgoingEdges(node))
                    {
                        var otherStartTime = StartTimes[outgoing.OtherNode];
                        Assert(otherStartTime >= execution.EndTime);
                    }

                    minStart = execution.EndTime;
                }
            }
        }

        public void Assert(bool condition)
        {
            if (!condition)
            {
                Debugger.Launch();
            }
        }

        public void WriteSimulation(string path)
        {
            List<string> incoming = new List<string>();
            using (var writer = new StreamWriter(path))
            {
                foreach (var execution in Threads.SelectMany(t => t.Executions).OrderBy(p => p.StartTime).ThenBy(p => p.Duration).ThenBy(p => p.Id.Value))
                {
                    foreach (var node in ExecutionData.DataflowGraph.GetIncomingEdges(execution.Id))
                    {
                        incoming.Add(ExecutionData.PipIds[execution.Id].ToString());
                    }

                    writer.WriteLine(
                        "{2}: {0} [{3}] {1} min",
                        ExecutionData.GetName(execution.Id).PadLeft(100),
                        execution.Duration.ToMinutes(),
                        ExecutionData.PipIds[execution.Id],
                        string.Join(",", incoming));
                }
            }
        }

        private ExecutionThread AddThread(ulong initialIdleTime)
        {
            var thread = new ExecutionThread(Threads.Count);
            Threads.Add(thread);

            thread.IdleTime = initialIdleTime;
            thread.EndTime = initialIdleTime;
            return thread;
        }

        private void CompletePip(ulong startTime, NodeId pip)
        {
            var endTime = startTime + ExecutionData.Durations[pip];
            CompletedPipCount++;

            StartTimes[pip] = startTime;
            EndTimes[pip] = endTime;
            foreach (var outgoingEdge in ExecutionData.DataflowGraph.GetOutgoingEdges(pip))
            {
                var consumer = outgoingEdge.OtherNode;
                var consumerRefCount = RefCounts[consumer] - 1;

                RefCounts[consumer] = consumerRefCount;
                var minimumStartTime = Math.Max(endTime, MinimumStartTimes[consumer]);
                MinimumStartTimes[consumer] = minimumStartTime;
                if (minimumStartTime == endTime)
                {
                    LastRunningDependency[consumer] = pip;
                }

                // all dependencies have been processed, so the node itself can be processed.
                if (consumerRefCount == 0)
                {
                    AddOrComplete(minimumStartTime, consumer);
                }
            }
        }

        private void AddOrComplete(ulong time, NodeId consumer)
        {
            if (ExecutionData.Durations[consumer] == 0)
            {
                CompletePip(time, consumer);
            }
            else
            {
                m_readyAtMinimumStartTimePips.Add(GetPipAndMinimumStartTime(consumer));
            }
        }

        public T TakeMin<T>(SortedSet<T> set)
        {
            var minimum = set.Min;
            set.Remove(minimum);
            return minimum;
        }

        public T TakeMax<T>(SortedSet<T> set)
        {
            var maximum = set.Max;
            set.Remove(maximum);
            return maximum;
        }

        public PipAndPriority GetPipAndPriority(NodeId pip)
        {
            return new PipAndPriority()
            {
                Node = pip,
                Priority = Priorities[pip],
            };
        }

        public PipAndPriority GetPipAndMinimumStartTime(NodeId pip)
        {
            return new PipAndPriority()
            {
                Node = pip,
                Priority = MinimumStartTimes[pip],
            };
        }

        public class ExecutionThread : IComparable<ExecutionThread>
        {
            public int Id;

            public ulong ActiveTime { get; set; }

            public ulong IdleTime { get; set; }

            public ulong EndTime { get; set; }

            public List<PipSpan> Executions { get; set; }

            public ulong StartTime = 0;

            public ExecutionThread(int id)
            {
                Id = id;
                Executions = new List<PipSpan>();
            }

            public void AddExecution(NodeId pip, SimulationResult result)
            {
                var execution = new PipSpan()
                {
                    Id = pip,
                    StartTime = Math.Max(EndTime, result.m_simulationCurrentTime),
                    Duration = result.ExecutionData.Durations[pip],
                    Thread = Id,
                };

                if (Executions.Count == 0)
                {
                    StartTime = execution.StartTime;
                }

                Executions.Add(execution);
                EndTime = execution.EndTime;
                ActiveTime += execution.Duration;

                result.CompletePip(execution.StartTime, pip);
            }

            public override string ToString()
            {
                return $"Thread (Start: {StartTime.ToMinutes()}, End: {EndTime.ToMinutes()}, Active: {ActiveTime.ToMinutes()})";
            }

            #region IComparable<ExecutionThread> Members

            public int CompareTo(ExecutionThread other)
            {
                int result = EndTime.CompareTo(other.EndTime);
                if (result != 0)
                {
                    return result;
                }

                return Id.CompareTo(other.Id);
            }

            #endregion
        }
    }
}
