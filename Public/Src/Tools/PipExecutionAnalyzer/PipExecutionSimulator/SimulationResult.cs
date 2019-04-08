// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Scheduler.Graph;

namespace PipExecutionSimulator
{


    public class SimulationResult
    {
        public PipExecutionData ExecutionData;
        public SortedSet<ExecutionThread> Threads = new SortedSet<ExecutionThread>();
        public ConcurrentNodeDictionary<int> RefCounts = new ConcurrentNodeDictionary<int>(false);
        public ConcurrentNodeDictionary<ulong> EndTimes = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<ulong> MinimumStartTimes = new ConcurrentNodeDictionary<ulong>(false);
        public ConcurrentNodeDictionary<ulong> Priorities;
        public ConcurrentNodeDictionary<ulong> StartTimes = new ConcurrentNodeDictionary<ulong>(false);

        public ulong TotalTime = 0;
        public ExecutionThread CriticalPath;
        public ulong TotalActiveTime = 0;
        public HashSet<NodeId> CriticalPathSet = new HashSet<NodeId>();
        public PriorityMode PriorityMode;

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
            CriticalPathSet.UnionWith(executionData.CriticalPath);
            Priorities = priorities;
        }

        public ConcurrentNodeDictionary<ulong> GetAdjustedDurations(ulong threshold)
        {
            ConcurrentNodeDictionary<ulong> durations = new ConcurrentNodeDictionary<ulong>(true);

            foreach (var node in ExecutionData.DataflowGraph.Nodes)
            {
                if (EndTimes[node] > threshold)
                {
                    //Debugger.Launch();
                }

                durations[node] = ExecutionData.Durations[node] + Math.Max(0, EndTimes[node] - threshold);
            }

            return durations;
        }

        public ulong GetAdjustedStartTime(NodeId pip)
        {
            return ExecutionData.StartTimes[pip] - ExecutionData.MinStartTime;
        }

        public void Simulate(uint maxThreadCount)
        {
            foreach (var node in ExecutionData.DataflowGraph.Nodes)
            {
                RefCounts[node] = ExecutionData.DataflowGraph.GetIncomingEdgesCount(node);
            }

            Contract.Assert(maxThreadCount > 0);
           
            // The set of pips which are free to execute at the given time (ie all dependencies have finished executing)
            SortedSet<PipAndPriority> readyPipsWithPriority = new SortedSet<PipAndPriority>();

            // The set of pips which will be freed after the execution of some set of the currently executing pips
            // The priority used here is the minimum start time. We pip the pip which will be freed first in the event
            // that they are no free pips.
            SortedSet<PipAndPriority> pendingPipsWithMinimumStartTime = new SortedSet<PipAndPriority>();
            Queue<PipAndPriority> pipBuffer = new Queue<PipAndPriority>();

            SortedSet<ExecutionThread> availableThreads = new SortedSet<ExecutionThread>();
            List<ExecutionThread> executingThreads = new List<ExecutionThread>();
            for (int i = 0; i < maxThreadCount; i++)
            {
                availableThreads.Add(AddThread(0));
            }

            foreach (var sourceNode in ExecutionData.DataflowGraph.GetSourceNodes())
            {
                AddOrComplete(0, readyPipsWithPriority, sourceNode);
            }

            while (readyPipsWithPriority.Count != 0 || pendingPipsWithMinimumStartTime.Count != 0)
            {
                NodeId maxPriorityPip;
                if (readyPipsWithPriority.Count == 0)
                {
                    // No pips are ready. Pick the pip that would be freed first based on the
                    // currently executing pips.
                    maxPriorityPip = TakeMin(pendingPipsWithMinimumStartTime).Node;
                }
                else
                {
                    // Pick the highest priority pip of the pips that are free to execute.
                    maxPriorityPip = TakeMax(readyPipsWithPriority).Node;
                }


                var minimumStartTime = MinimumStartTimes[maxPriorityPip];

                // Get the thread which finishes its current work first.
                ExecutionThread thread = availableThreads.Min;

                availableThreads.Remove(thread);

                // Add the execution of the pip and add any pips which are freed to run after the pips execution to  pending pips
                thread.AddExecution(maxPriorityPip, minimumStartTime, ExecutionData.Durations[maxPriorityPip], this, pendingPipsWithMinimumStartTime);
                availableThreads.Add(thread);

                var minimum = availableThreads.Min;

                // Go through the pending pips and remove the ones which are ready (ie
                // there is a thread available which can execute the pip).
                foreach (var pendingPip in pendingPipsWithMinimumStartTime)
                {
                    minimumStartTime = pendingPip.Priority;
                    if (minimum.EndTime <= minimumStartTime)
                    {
                        // This thread ended its last execution in time to execute this pip.
                        // Add it to the buffer to be added to the readyPips
                        pipBuffer.Enqueue(pendingPip);
                    }
                    else
                    {
                        break;
                    }
                }

                while (pipBuffer.Count != 0)
                {
                    var pip = pipBuffer.Dequeue();
                    pendingPipsWithMinimumStartTime.Remove(pip);
                    readyPipsWithPriority.Add(GetPipAndPriority(pip.Node));
                }
            }

            foreach (var thread in Threads)
            {
                if (thread.EndTime > TotalTime)
                {
                    TotalTime = thread.EndTime;
                    CriticalPath = thread;
                }

                TotalActiveTime += thread.ActiveTime;
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

        private void CompletePip(ulong time, NodeId pip, SortedSet<PipAndPriority> pips)
        {
            var endTime = time + ExecutionData.Durations[pip];
            foreach (var outgoingEdge in ExecutionData.DataflowGraph.GetOutgoingEdges(pip))
            {
                var consumer = outgoingEdge.OtherNode;
                var consumerRefCount = RefCounts[consumer] - 1;
                RefCounts[consumer] = consumerRefCount;
                var minimumStartTime = Math.Max(endTime, MinimumStartTimes[consumer]);
                MinimumStartTimes[consumer] = minimumStartTime;

                if (consumerRefCount == 0)
                {
                    AddOrComplete(minimumStartTime, pips, consumer);
                }
            }
        }

        private void AddOrComplete(ulong time, SortedSet<PipAndPriority> pips, NodeId consumer)
        {
            if (ExecutionData.Durations[consumer] == 0)
            {
                CompletePip(time, consumer, pips);
            }
            else
            {
                pips.Add(GetPipAndMinimumStartTime(consumer));
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
                Priority = Priorities[pip]
            };
        }
        public PipAndPriority GetPipAndMinimumStartTime(NodeId pip)
        {
            return new PipAndPriority()
            {
                Node = pip,
                Priority = MinimumStartTimes[pip]
            };
        }

        public class ExecutionThread : IComparable<ExecutionThread>
        {
            private static int LastId = 0;
            public int Id;
            public ulong ActiveTime { get; set; }
            public ulong IdleTime { get; set; }
            public ulong EndTime { get; set; }
            public List<PipSpan> Executions { get; set; }

            public ExecutionThread(int id)
            {
                Id = id;
                Executions = new List<PipSpan>();
            }

            public void AddExecution(NodeId pip, ulong minimumStartTime, ulong duration, SimulationResult result, SortedSet<PipAndPriority> pips)
            {
                var execution = new PipSpan()
                {
                    Id = pip,
                    StartTime = Math.Max(EndTime, minimumStartTime),
                    Duration = duration,
                    Thread = Id,
                };

                Executions.Add(execution);
                EndTime = execution.StartTime + duration;
                ActiveTime += duration;
                result.EndTimes[pip] = EndTime;

                result.CompletePip(execution.StartTime, pip, pips);
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

    public struct PipAndPriority : IComparable<PipAndPriority>
    {
        public NodeId Node;
        public ulong Priority;

        public int CompareTo(PipAndPriority other)
        {
            int result = Priority.CompareTo(other.Priority);
            if (result != 0)
            {
                return result;
            }

            return Node.Value.CompareTo(other.Node.Value);
        }
    }

}
