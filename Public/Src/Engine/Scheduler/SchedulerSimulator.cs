// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Simulates the BuildXL scheduling process by assigning pips
    /// to workers while considering dependencies and resource constraints (CPU and memory).
    /// It supports evaluating multiple configurations—such as different SKUs and varying worker counts—by
    /// running simulations that compute key metrics including overall build duration, cost (expressed in core-minutes),
    /// and core utilization. This enables comprehensive evaluation of build efficiency and resource
    /// utilization across different hardware configurations.
    /// </summary>
    internal sealed class SchedulerSimulator
    {
        private const string CurrentSkuName = "Current_SKU";

        private readonly LoggingContext m_loggingContext;
        private readonly IConfiguration m_config;
        private readonly PipGraph m_pipGraph;
        private readonly IReadonlyDirectedGraph m_scheduledGraph;
        private readonly Func<PipId, int> m_getPipPriorityFunc;

        private readonly PipInfo[] m_pips;
        private readonly ObjectPool<PipInfo[]> m_pipInfoArrayPool;
        private readonly ConcurrentDictionary<PipId, RunnablePipPerformanceInfo> m_runnablePipPerformance;
        private readonly int m_initialAvailableRamMb;
        private readonly List<int> m_initialReadyPipIndexes = new List<int>();

        /// <summary>
        /// Maximum number of workers to be used for simulation
        /// </summary>
        private static readonly int s_maxWorkers = EngineEnvironmentSettings.SchedulerSimulatorMaxWorkers.Value;

        private readonly List<Sku> m_skus = new List<Sku>()
        {
            { new Sku() { Name = "Standard_D8ds_v5", Cpus = 8, RamMb = 32768 } },
            { new Sku() { Name = "Standard_D16ds_v5", Cpus = 16, RamMb = 65536 } },
            { new Sku() { Name = "Standard_D32ds_v5", Cpus = 32, RamMb = 131072 } },
            { new Sku() { Name = "Standard_D48ds_v5", Cpus = 48, RamMb = 196608 } },
            { new Sku() { Name = "Standard_D64ds_v5", Cpus = 64, RamMb = 262144 } },
        };
        
        private class Sku
        {
            public string Name;
            public int Cpus;
            public int RamMb; 
        }

        private class SimulationResult
        {
            public int MemoryLimitMb;
            public double DurationMin;
            public int CoreMinutes;
        }

        private class PipInfo
        {
            public int NumIncomingEdges;
            public int RefCount;
            public int StartTime;
            public int EndTime;
            public int WorkerId;
            public int Priority;
            public NodeId NodeId;
        }

        private class ProcessPipInfo : PipInfo
        {
            public int ExeDurationMs;
            public int MemoryUsageMb;
        }

        /// <summary>
        /// A helper class representing an interval of time with available resources.
        /// </summary>
        private class ResourceInterval
        {
            public int Start;
            public int End;
            public int FreeCpu;      // available CPU slots during this interval
            public int FreeMemory;   // available memory (in Mb) during this interval
        }

        private class Worker
        {
            public List<ProcessPipInfo> ScheduledPips = new List<ProcessPipInfo>();
            public int Id;
            public int MemoryCapacityMb;
            public int Cpus;

            // Cached list of intervals (with constant available capacity) for scheduling new pips.
            public List<ResourceInterval> AvailableIntervals;

            public Worker(int id, int memoryCapacityMb, int cpus)
            {
                Id = id;
                MemoryCapacityMb = memoryCapacityMb;
                Cpus = cpus;

                // Initially, the entire timeline [0, ∞) is available with full capacity.
                AvailableIntervals = new List<ResourceInterval>
                {
                    new ResourceInterval { Start = 0, End = int.MaxValue, FreeCpu = cpus, FreeMemory = memoryCapacityMb }
                };
            }
        }

        /// <nodoc/>
        public SchedulerSimulator(
            LoggingContext loggingContext,
            IConfiguration config,
            PipGraph pipGraph,
            ConcurrentDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance,
            IReadonlyDirectedGraph scheduledGraph,
            Func<PipId, int> getPipPriorityFunc,
            int initialAvailableRamMb)
        {
            m_loggingContext = loggingContext;
            m_config = config;
            m_pipGraph = pipGraph;
            m_runnablePipPerformance = runnablePipPerformance;
            m_initialAvailableRamMb = initialAvailableRamMb;
            m_scheduledGraph = scheduledGraph;
            m_getPipPriorityFunc = getPipPriorityFunc;

            // Adding a current SKU representing the system configuration
            m_skus.Add(new Sku() { Name = CurrentSkuName, Cpus = config.Schedule.MaxProcesses, RamMb = m_initialAvailableRamMb == 0 ? int.MaxValue : m_initialAvailableRamMb });

            m_pips = new PipInfo[(int)m_pipGraph.NodeRange.ToInclusive.Value + 1];
            m_pipInfoArrayPool = new ObjectPool<PipInfo[]>(
                () => ClonePips(m_pips),
                ResetPipStates);
        }

        /// <summary>
        /// Creates a deep clone of the m_pips array so that each simulation run can work independently.
        /// </summary>
        private PipInfo[] ClonePips(PipInfo[] source)
        {
            PipInfo[] clone = new PipInfo[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                {
                    continue;
                }
                if (source[i] is ProcessPipInfo processPip)
                {
                    clone[i] = new ProcessPipInfo
                    {
                        NumIncomingEdges = processPip.NumIncomingEdges,
                        Priority = processPip.Priority,
                        NodeId = processPip.NodeId,
                        ExeDurationMs = processPip.ExeDurationMs,
                        MemoryUsageMb = processPip.MemoryUsageMb,
                    };
                }
                else
                {
                    clone[i] = new PipInfo
                    {
                        NumIncomingEdges = source[i].NumIncomingEdges,
                        Priority = source[i].Priority,
                        NodeId = source[i].NodeId,
                    };
                }
            }
            return clone;
        }

        public async Task StartAsync()
        {
            // Unblock caller
            await Task.Yield();

            var stopwatch = StopwatchSlim.Start();
            long totalExeDurationMs = 0;

            try
            {
                // Initialize the pips
                await Enumerable.Range(1, m_pips.Length - 1).ParallelForEachAsync((i) =>
                {
                    var nodeId = new NodeId((uint)i);
                    var pipId = nodeId.ToPipId();

                    // Skip nodes that are not part of the scheduled graph
                    if (!m_scheduledGraph.ContainsNode(nodeId))
                    {
                        return;
                    }

                    var priority = m_getPipPriorityFunc(pipId);

                    if (m_pipGraph.PipTable.GetPipType(pipId) == PipType.Process)
                    {
                        RunnablePipPerformanceInfo perfInfo = m_runnablePipPerformance[pipId];
                        var exeDurationMs = (int)perfInfo.ExeDuration.TotalMilliseconds;
                        totalExeDurationMs += exeDurationMs;

                        m_pips[i] = new ProcessPipInfo
                        {
                            ExeDurationMs = exeDurationMs,
                            MemoryUsageMb = perfInfo.ActualAverageWorkingSetMb,
                            NumIncomingEdges = m_scheduledGraph.CountIncomingHeavyEdges(nodeId),
                            NodeId = nodeId,
                            Priority = priority
                        };
                    }
                    else
                    {
                        m_pips[i] = new PipInfo
                        {
                            NumIncomingEdges = m_scheduledGraph.CountIncomingHeavyEdges(nodeId),
                            NodeId = nodeId,
                            Priority = priority
                        };
                    }

                    // Identify and store ready nodes for scheduling
                    if (m_scheduledGraph.IsSourceNode(nodeId) || m_pips[i].NumIncomingEdges == 0)
                    {
                        lock (m_initialReadyPipIndexes)
                        {
                            m_initialReadyPipIndexes.Add(i);
                        }
                    }
                });

                int initializeDurationMs = (int)stopwatch.ElapsedAndReset().TotalMilliseconds;

                // Use a ConcurrentDictionary to store simulation results in a thread-safe way.
                var simulationResults = new ConcurrentDictionary<(Sku, int), SimulationResult>();
                var simulationConfigs = new List<(Sku, int)>();

                foreach (var sku in m_skus)
                {
                    for (int numWorkers = 1; numWorkers <= s_maxWorkers; numWorkers++)
                    {
                        simulationConfigs.Add((sku, numWorkers));
                    }
                }

                await simulationConfigs.ParallelAddToConcurrentDictionaryAsync(
                    simulationResults, config => config, config =>
                    {
                        var sku = config.Item1;
                        var numWorkers = config.Item2;

                        int cpus = sku.Cpus;
                        int memoryLimitMb = (int)(sku.RamMb * m_config.Schedule.RamSemaphoreMultiplier);

                        using (var pipsInstance = m_pipInfoArrayPool.GetInstance())
                        {
                            var pips = pipsInstance.Instance;

                            // Calculate build duration for the current configuration
                            double durationMin = RunSimulation(pips, numWorkers, cpus, memoryLimitMb);

                            return new SimulationResult()
                            {
                                CoreMinutes = (int)Math.Round(durationMin * numWorkers * sku.Cpus),
                                DurationMin = durationMin,
                                MemoryLimitMb = memoryLimitMb
                            };
                        }
                    });

                int simulationDurationMs = (int)stopwatch.ElapsedAndReset().TotalMilliseconds;

                var skuResults = simulationResults.Where(a => a.Key.Item1.Name != CurrentSkuName).ToList();
                double minDuration = skuResults.Select(a => a.Value.DurationMin).Min();
                double maxDuration = skuResults.Select(a => a.Value.DurationMin).Max();
                double minCoreMinutes = skuResults.Select(a => a.Value.CoreMinutes).Min();
                double maxCoreMinutes = skuResults.Select(a => a.Value.CoreMinutes).Max();

                var orderedResults = simulationResults
                    .OrderBy(kvp => kvp.Key.Item1.Cpus)
                    .ThenBy(kvp => kvp.Key.Item2);

                // Weight for duration in [0..1]. (0 = only cost matters, 1 = only duration matters)
                double[] weights = { 0.5, 0.7, 0.8, 0.9 };

                foreach (var kvp in orderedResults)
                {
                    var sku = kvp.Key.Item1;
                    int numWorkers = kvp.Key.Item2;
                    var result = kvp.Value;

                    string[] scores = weights.Select(weight => CalculateBuildEfficiencyScore(weight, result.DurationMin, result.CoreMinutes, minDuration, maxDuration, minCoreMinutes, maxCoreMinutes).ToString("0.0")).ToArray();
                    string durationMin = result.DurationMin.ToString("0.0");

                    // Calculate the core utilization percentage, which measures the fraction of total available core–time that was actively used.
                    // - totalExeDurationMs / 60000: the sum of execution times (in minutes) for all process pips (i.e. the total busy core time).
                    // - result.DurationMin: the overall build duration in minutes.
                    // - numWorkers * sku.Cpus: the total number of cores available across all workers.
                    // Therefore, (result.DurationMin * numWorkers * sku.Cpus) represents the maximum available core–minutes.
                    // The ratio (total busy core–minutes) / (total available core–minutes), multiplied by 100 and rounded, yields the core utilization percentage.
                    var coreUtilization = result.DurationMin > 0 ? (int)Math.Round(100 * (totalExeDurationMs / 60000.00 / (result.DurationMin * numWorkers * sku.Cpus))) : 0;

                    var coreHours = (int)Math.Round(result.CoreMinutes / 60.0);

                    string message = string.Format(
                        "{0,-20} Workers: {1,2} Cpus: {2,2} TotalRamMb: {3,6} RamLimitMb: {4,6} DurationMin: {5,6}, CoreHours: {6,5}, CoreUtilization: {7,5}, Score50: {8,5}, Score70: {9,5}, Score80: {10,5}, Score90: {11,5}",
                        sku.Name,
                        numWorkers,
                        sku.Cpus,
                        sku.RamMb,
                        result.MemoryLimitMb,
                        durationMin,
                        coreHours,
                        coreUtilization,
                        scores[0],
                        scores[1],
                        scores[2],
                        scores[3]
                    );
                    Tracing.Logger.Log.SchedulerSimulatorResult(m_loggingContext, message);

                    // Sending less data to the structured telemetry. 
                    Tracing.Logger.Log.SchedulerSimulator(m_loggingContext, sku.Name, kvp.Key.Item2, durationMin, coreHours, coreUtilization, scores[0], scores[1], scores[2], scores[3]);
                }

                Tracing.Logger.Log.SchedulerSimulatorCompleted(m_loggingContext, initializeDurationMs, simulationDurationMs);
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.SchedulerSimulatorFailed(m_loggingContext, ex.ToStringDemystified());
            }
        }

        /// <summary>
        /// Calculates the efficiency score for a build based on its duration and cost (coreHours).
        /// The score is computed by normalizing the duration and cost values relative to their minimum
        /// and maximum values across the dataset and applying weighted contributions of each factor.
        /// The score ranges between 0 and 100, where higher scores indicate better efficiency.
        /// </summary>
        private static double CalculateBuildEfficiencyScore(double weightForDuration, double duration, double coreMinutes, double minDuration, double maxDuration, double minCoreMinutes, double maxCoreMinutes)
        {
            if (coreMinutes == 0 || duration == 0)
            {
                return 0;
            }

            // Normalize the duration and cost
            double rescaledDuration = (minDuration == maxDuration) ? 0 : (duration - minDuration) / (maxDuration - minDuration);
            double rescaledCost = (minCoreMinutes == maxCoreMinutes) ? 0 : (coreMinutes - minCoreMinutes) / (maxCoreMinutes - minCoreMinutes);

            // Calculate the score
            return 100 - ((weightForDuration * rescaledDuration) + ((1 - weightForDuration) * rescaledCost)) * 100;
        }

        /// <summary>
        /// Finds the earliest feasible start time for a given process on a specific worker using the worker's cached intervals.
        /// Considers the worker's currently scheduled tasks and ensures that CPU and memory constraints are not violated.
        /// </summary>
        private int FindEarliestFeasibleStartTime(int currentTime, Worker worker, ProcessPipInfo process)
        {
            foreach (var interval in worker.AvailableIntervals)
            {
                // Skip intervals that end before the current time.
                if (interval.End <= currentTime)
                {
                    continue;
                }

                // Determine when we could start in this interval.
                int feasibleStart = Math.Max(currentTime, interval.Start);

                // Check if both CPU and memory capacity are sufficient.
                if (interval.FreeCpu > 0 && interval.FreeMemory >= process.MemoryUsageMb &&
                    (interval.End - feasibleStart) >= process.ExeDurationMs)
                {
                    return feasibleStart;
                }
            }

            // No suitable interval was found.
            return int.MaxValue;
        }

        /// <summary>
        /// Incrementally updates the worker's cached available intervals when a process is scheduled.
        /// The process consumes one CPU and a given amount of memory over the interval [start, end).
        /// </summary>
        private void UpdateWorkerAvailableIntervals(Worker worker, ProcessPipInfo process, int start, int end)
        {
            var newIntervals = new List<ResourceInterval>();

            // Process each cached interval.
            foreach (var interval in worker.AvailableIntervals)
            {
                // If there is no overlap with [start, end), then just keep the interval.
                if (interval.End <= start || interval.Start >= end)
                {
                    newIntervals.Add(interval);
                }
                else
                {
                    // If part of the interval occurs before 'start', keep that part unchanged.
                    if (interval.Start < start)
                    {
                        newIntervals.Add(new ResourceInterval
                        {
                            Start = interval.Start,
                            End = start,
                            FreeCpu = interval.FreeCpu,
                            FreeMemory = interval.FreeMemory
                        });
                    }

                    // Compute the overlapping portion.
                    int overlapStart = Math.Max(interval.Start, start);
                    int overlapEnd = Math.Min(interval.End, end);
                    newIntervals.Add(new ResourceInterval
                    {
                        Start = overlapStart,
                        End = overlapEnd,
                        // Decrease available CPU by 1 (since the process uses one CPU)
                        FreeCpu = interval.FreeCpu - 1,
                        // Decrease available memory by the process's memory usage.
                        FreeMemory = interval.FreeMemory - process.MemoryUsageMb
                    });

                    // If part of the interval occurs after 'end', keep that part unchanged.
                    if (interval.End > end)
                    {
                        newIntervals.Add(new ResourceInterval
                        {
                            Start = end,
                            End = interval.End,
                            FreeCpu = interval.FreeCpu,
                            FreeMemory = interval.FreeMemory
                        });
                    }
                }
            }

            // Merge any adjacent intervals that now have identical resource availability.
            worker.AvailableIntervals = MergeIntervals(newIntervals);
        }

        /// <summary>
        /// Merges adjacent intervals with the same free CPU and free memory.
        /// </summary>
        private List<ResourceInterval> MergeIntervals(List<ResourceInterval> intervals)
        {
            if (intervals.Count == 0)
            {
                return intervals;
            }

            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<ResourceInterval>();
            var current = intervals[0];

            for (int i = 1; i < intervals.Count; i++)
            {
                var next = intervals[i];
                // If intervals are contiguous and have the same resource availability, merge them.
                if (current.End == next.Start && current.FreeCpu == next.FreeCpu && current.FreeMemory == next.FreeMemory)
                {
                    current.End = next.End;
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            return merged;
        }

        /// <summary>
        /// Runs a simulation of the build scheduling using the specified worker configuration. 
        /// Returns the total build duration in minutes.
        /// 
        /// The simulation works as follows:
        /// 1. Initialize a set of workers, each with its own cached available intervals representing CPU and memory capacity.
        /// 2. Create a sorted set of pip completion events to know when a running pip finishes.
        /// 3. Maintain a global simulation time (currentTime) starting at zero.
        /// 4. Process pips that are ready to run (i.e., those with all dependencies satisfied) in a loop:
        ///    a. For non-process pips, schedule them immediately (they have zero duration).
        ///    b. For process pips, scan through all workers to determine the earliest feasible start time 
        ///       (based on resource availability cached on the worker).
        ///    c. If a worker is available, schedule the pip on that worker by setting its start and end times,
        ///       update the worker’s available intervals, and add a completion event.
        /// 5. If no new pips can be scheduled at the current simulation time, advance time to the next pip completion event,
        ///    then process that event’s dependents (which may now become ready).
        /// 6. The loop continues until all pips have been scheduled and executed.
        /// 7. Finally, the simulation returns the overall build duration based on the maximum end time of any pip.
        /// </summary>
        private double RunSimulation(PipInfo[] pips, int workerCount, int cpus, int memoryLimitMb)
        {
            // Initialize workers with the given configuration.
            // Each worker maintains its own resource availability via a cached list of available intervals.
            List<Worker> workers = new List<Worker>();
            for (int i = 0; i < workerCount; i++)
            {
                workers.Add(new Worker(i, memoryLimitMb, cpus));
            }

            // Prepare the set of pips that are ready to execute.
            var readyPips = new SortedSet<PipInfo>(
                Comparer<PipInfo>.Create((a, b) =>
                {
                    // Descending sort by Priority.
                    int cmp = b.Priority.CompareTo(a.Priority);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    // Tie-breaker: sort by NodeId.
                    return a.NodeId.Value.CompareTo(b.NodeId.Value);
                }));

            foreach (var pipIndex in m_initialReadyPipIndexes)
            {
                readyPips.Add(pips[pipIndex]);
            }

            // Create a sorted set for pip completion events.
            // Each event is a tuple containing the finish time and the corresponding pip.
            // Sorting is done by time (earliest first), then by pip priority and NodeId as tie-breakers.
            var pipCompletionEvents = new SortedSet<(int Time, PipInfo Pip)>(Comparer<(int Time, PipInfo Pip)>.Create((a, b) =>
            {
                // Ascending order by Time.
                int cmp = a.Time.CompareTo(b.Time);
                if (cmp != 0)
                {
                    return cmp;
                }

                // Descending order by Priority.
                cmp = b.Pip.Priority.CompareTo(a.Pip.Priority);
                if (cmp != 0)
                {
                    return cmp;
                }

                return a.Pip.NodeId.Value.CompareTo(b.Pip.NodeId.Value);
            }));

            int currentTime = 0;

            // Main scheduling loop:
            // Continue as long as there are pips ready to run or running pips that will complete in the future.
            while (readyPips.Count > 0 || pipCompletionEvents.Count > 0)
            {
                // Flag to indicate whether any pip was scheduled in this iteration.
                bool scheduledInThisIteration = false;

                // Process all ready pips.
                // The readyPips collection is ordered by priority
                while (readyPips.Count > 0)
                {
                    var pip = readyPips.First();
                    readyPips.Remove(pip);
                    var process = pip as ProcessPipInfo;

                    Worker selectedWorker = null;
                    int earliestStartTime = int.MaxValue;

                    // For non-process pips (e.g., metadata pips) that have no execution time,
                    // we simply schedule them at the current time.
                    if (process == null)
                    {
                        pip.StartTime = currentTime;
                        pip.EndTime = currentTime;
                        // Process dependents immediately since the pip executes instantly.
                        iterateDependents(pip);
                        scheduledInThisIteration = true;
                        continue;
                    }

                    // For process pips, we need to check each worker to see when the pip can start.
                    // The method FindEarliestFeasibleStartTime checks the worker's cached available intervals
                    // and returns the earliest time when the pip can run without exceeding CPU or memory constraints.
                    foreach (var worker in workers)
                    {
                        int startTime = FindEarliestFeasibleStartTime(currentTime, worker, process);
                        if (startTime < earliestStartTime)
                        {
                            selectedWorker = worker;
                            earliestStartTime = startTime;
                        }
                    }

                    // If a worker is found that can schedule this pip, then assign the pip.
                    if (selectedWorker != null && earliestStartTime != int.MaxValue)
                    {
                        pip.StartTime = earliestStartTime;
                        pip.EndTime = earliestStartTime + process.ExeDurationMs;
                        pip.WorkerId = selectedWorker.Id;

                        // Add the process to the worker's scheduled tasks.
                        selectedWorker.ScheduledPips.Add(process);

                        // Update the worker's cached available intervals to account for the resource usage
                        // during the time [StartTime, EndTime) by this pip.
                        UpdateWorkerAvailableIntervals(selectedWorker, process, pip.StartTime, pip.EndTime);

                        // Add a completion event so that once this pip finishes, its dependents can be processed.
                        pipCompletionEvents.Add((pip.EndTime, pip));
                        scheduledInThisIteration = true;
                    }
                    else
                    {
                        // If no worker is available to run the pip at this time,
                        // re-add the pip to the ready set and break out to advance simulation time.
                        readyPips.Add(pip);
                        break;
                    }
                }

                // If no pip was scheduled during this iteration, then we must advance time.
                // This occurs when no ready pip can be scheduled due to resource constraints.
                if (!scheduledInThisIteration)
                {
                    if (pipCompletionEvents.Count > 0)
                    {
                        // Advance time to the next completion event.
                        var nextEvent = pipCompletionEvents.Min;
                        pipCompletionEvents.Remove(nextEvent);
                        currentTime = nextEvent.Time;

                        // Process the pip that has completed.
                        var completedPip = nextEvent.Pip;

                        // Updating its dependents may add new pips to the ready set.
                        iterateDependents(completedPip);
                    }
                    else
                    {
                        // If there are no pips ready or running, then a deadlock has occurred.
                        // We do not continue and return 0 as the total build duration.
                        return -1;
                    }
                }
            }

            // After all pips have been scheduled and processed,
            // the total execution time is the maximum end time across all pips.
            int totalExecutionTimeMs = pips.Max(t => t?.EndTime ?? 0);

            // Verify that all pips have completed (dependency counters match).
            Contract.Assert(pips.Where(a => a != null && a.NumIncomingEdges != a.RefCount).Count() == 0, "There are unfinished pips");

            return totalExecutionTimeMs.MillisecondsToTimeSpan().TotalMinutes;

            // Local helper method to update the dependency counters of dependent pips.
            // For each outgoing heavy edge from the completed pip, increase the dependency counter.
            // When a pip's RefCount equals its required incoming edges, add it to the ready set.
            void iterateDependents(PipInfo pip)
            {
                foreach (var edge in m_scheduledGraph.GetOutgoingEdges(pip.NodeId))
                {
                    if (edge.IsLight)
                    {
                        continue;
                    }

                    var dependentPip = pips[edge.OtherNode.Value];
                    dependentPip.RefCount++;
                    if (dependentPip.RefCount == dependentPip.NumIncomingEdges)
                    {
                        readyPips.Add(dependentPip);
                    }
                }
            }
        }

        private void ResetPipStates(PipInfo[] pips)
        {
            // Clearing the states.
            foreach (var pip in pips)
            {
                if (pip == null)
                {
                    continue;
                }

                pip.RefCount = 0;
                pip.StartTime = 0;
                pip.EndTime = 0;
                pip.WorkerId = 0;
            }
        }
    }
}
