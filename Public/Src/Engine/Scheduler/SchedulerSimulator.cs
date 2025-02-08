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
using BuildXL.Utilities.Configuration.Mutable;
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

        private enum PipPhase { CacheLookup, Executable, Completed }

        private class PipInfo
        {
            public int NumIncomingEdges;
            public int RefCount;
            public int StartTime;
            public int EndTime;
            public Worker Worker;
            public int Priority;
            public NodeId NodeId;
        }

        private class ProcessPipInfo : PipInfo
        {
            public int ExecuteDurationMs;
            public int MemoryUsageMb;
            public int CacheLookupDurationMs;
            public bool IsCacheHit;
            public PipPhase Phase = PipPhase.CacheLookup;
        }

        private class Worker
        {
            public int TotalExecuteSlots;
            public int TotalMemory;
            public int AvailableExecuteSlots;
            public int AvailableMemory;
            public int TotalCacheLookupSlots;
            public int AvailableCacheLookupSlots;

            public Worker(int memoryLimitMb, int executeSlots, int cacheLookupSlots)
            {
                TotalExecuteSlots = executeSlots;
                TotalMemory = memoryLimitMb;
                AvailableExecuteSlots = executeSlots;
                AvailableMemory = memoryLimitMb;
                TotalCacheLookupSlots = cacheLookupSlots;
                AvailableCacheLookupSlots = cacheLookupSlots;
            }

            internal bool TryAcquireWorker(ProcessPipInfo process)
            {
                if (process.Phase == PipPhase.CacheLookup)
                {
                    if (AvailableCacheLookupSlots > 0)
                    {
                        process.EndTime = process.StartTime + process.CacheLookupDurationMs;
                        process.Worker = this;
                        AvailableCacheLookupSlots--;
                        return true;
                    }
                }
                else if (process.Phase == PipPhase.Executable)
                {
                    if (AvailableExecuteSlots > 0 && AvailableMemory >= process.MemoryUsageMb)
                    {
                        process.EndTime = process.StartTime + process.ExecuteDurationMs;
                        process.Worker = this;
                        AvailableExecuteSlots--;
                        AvailableMemory -= process.MemoryUsageMb;
                        return true;
                    }
                }

                return false;
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
            m_skus.Add(new Sku() { Name = CurrentSkuName, Cpus = Environment.ProcessorCount, RamMb = m_initialAvailableRamMb == 0 ? int.MaxValue : m_initialAvailableRamMb });

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
                        ExecuteDurationMs = processPip.ExecuteDurationMs,
                        MemoryUsageMb = processPip.MemoryUsageMb,
                        CacheLookupDurationMs = processPip.CacheLookupDurationMs,
                        IsCacheHit = processPip.IsCacheHit,
                        Phase = processPip.Phase
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
            long totalExecuteDurationMs = 0;

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

                        bool isCacheHit = perfInfo.CacheLookupPerfInfo.CacheMissType == PipCacheMissType.Hit;

                        var cacheLookupDurationMs = getStepDuration(pipId, PipExecutionStep.CacheLookup);

                        var exeDurationMs = getStepDuration(pipId, PipExecutionStep.ExecuteProcess);
                        // Total execute duration after cachelookup includes materialization and postprocess steps as well. 
                        var executeDurationMs = getStepDuration(pipId, PipExecutionStep.MaterializeInputs) + exeDurationMs + getStepDuration(pipId, PipExecutionStep.PostProcess);

                        totalExecuteDurationMs += exeDurationMs;

                        m_pips[i] = new ProcessPipInfo
                        {
                            ExecuteDurationMs = executeDurationMs,
                            CacheLookupDurationMs = cacheLookupDurationMs,
                            MemoryUsageMb = perfInfo.ActualAverageWorkingSetMb,
                            NumIncomingEdges = m_scheduledGraph.CountIncomingHeavyEdges(nodeId),
                            NodeId = nodeId,
                            Priority = priority,
                            IsCacheHit = isCacheHit
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

                int maxWorkers = Math.Max(s_maxWorkers, m_config.Distribution.RemoteWorkerCount + 1);
                foreach (var sku in m_skus)
                {
                    for (int numWorkers = 1; numWorkers <= maxWorkers; numWorkers++)
                    {
                        simulationConfigs.Add((sku, numWorkers));
                    }
                }

                await simulationConfigs.ParallelAddToConcurrentDictionaryAsync(
                    simulationResults, config => config, config =>
                    {
                        var sku = config.Item1;
                        var numWorkers = config.Item2;

                        int memoryLimitMb = (int)(sku.RamMb * m_config.Schedule.RamSemaphoreMultiplier);
                        int executeSlots = (int)Math.Ceiling(ScheduleConfiguration.DefaultProcessorCountMultiplier * sku.Cpus);
                        int cacheLookupSlots = sku.Cpus;

                        using (var pipsInstance = m_pipInfoArrayPool.GetInstance())
                        {
                            var pips = pipsInstance.Instance;

                            // Calculate build duration for the current configuration
                            double durationMin = RunSimulation(pips, numWorkers, memoryLimitMb, executeSlots, cacheLookupSlots);

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
                    var coreUtilization = result.DurationMin > 0 ? (int)Math.Round(100 * (totalExecuteDurationMs / 60000.00 / (result.DurationMin * numWorkers * sku.Cpus))) : 0;

                    var coreHours = (int)Math.Round(result.CoreMinutes / 60.0);

                    string message = string.Format(
                        "{0,-20} Workers: {1,2} Cpus: {2,3} TotalRamMb: {3,6} RamLimitMb: {4,6} DurationMin: {5,6}, CoreHours: {6,5}, CoreUtilization: {7,5}, Score50: {8,5}, Score70: {9,5}, Score80: {10,5}, Score90: {11,5}",
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

            int getStepDuration(PipId pipId, PipExecutionStep step)
            {
                RunnablePipPerformanceInfo perfInfo = m_runnablePipPerformance[pipId];

                // To get the actual duration of the step, we first check the RemoteStepDurations to avoid including the queue duration for remote pips.
                var duration = perfInfo.RemoteStepDurations.GetOrDefault(step, TimeSpan.Zero);
               
                // If remoteStep duration is zero, it means that the step was not executed remotely, so we check the StepDurations.
                return (int)(duration == TimeSpan.Zero ? perfInfo.StepDurations.GetOrDefault(step, TimeSpan.Zero) : duration).TotalMilliseconds;
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
        /// Runs a simulation of the build scheduling using the specified worker configuration. 
        /// Returns the total build duration in minutes.
        /// </summary>
        private double RunSimulation(PipInfo[] pips, int workerCount, int memoryLimitMb, int executeSlots, int cacheLookupSlots)
        {
            // Initialize workers with the given configuration.
            // Each worker maintains its own resource availability.
            Worker[] workers = new Worker[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                workers[i] = new Worker(memoryLimitMb, executeSlots, cacheLookupSlots);
            }

            // Prepare the set of pips that are ready to execute (those with no dependencies).
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

            // Global simulation clock in milliseconds.
            int currentTime = 0;

            // Global event queue
            var pipCompletionSet = new SortedSet<PipInfo>(
                Comparer<PipInfo>.Create((a, b) =>
                {
                    // Ascending sort by EndTime
                    int cmp = a.EndTime.CompareTo(b.EndTime);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    // Tie-breaker: sort by NodeId.
                    return a.NodeId.Value.CompareTo(b.NodeId.Value);
                }));

            // Main scheduling loop:
            // Continue as long as there are pips ready to run or running pips that will complete in the future.
            while (readyPips.Count > 0 || pipCompletionSet.Count > 0)
            {
                // Flag to indicate whether any pip was scheduled in this iteration.
                bool scheduledInThisIteration = false;

                // Try to schedule all ready pips.
                // The readyPips collection is ordered by priority
                while (readyPips.Count > 0)
                {
                    var pip = readyPips.First();
                    readyPips.Remove(pip);
                    var process = pip as ProcessPipInfo;
                    pip.StartTime = currentTime;

                    // For non-process pips (e.g., metadata pips) that have no execution time,
                    // we simply schedule them at the current time.
                    if (process == null)
                    {
                        pip.EndTime = currentTime;
                        // Iterate dependents immediately since the pip executes instantly.
                        iterateDependents(pip);
                        scheduledInThisIteration = true;
                        continue;
                    }

                    Worker selectedWorker = null;
                    foreach (var worker in workers)
                    {
                        if (worker.TryAcquireWorker(process))
                        {
                            selectedWorker = worker;
                            break;
                        }
                    }

                    // If a worker is found that can schedule this pip, then assign the pip.
                    if (selectedWorker != null)
                    {
                        // Add a completion event so that once this pip finishes, its dependents can be processed.
                        pipCompletionSet.Add(process);
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
                    if (pipCompletionSet.Count > 0)
                    {
                        // Advance time to the next completion event.
                        var completedPip = pipCompletionSet.Min;
                        pipCompletionSet.Remove(completedPip);
                        currentTime = completedPip.EndTime;

                        processCompletionEvent(completedPip);
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

            // Local helper method to process a completion event.
            void processCompletionEvent(PipInfo pipInfo)
            {
                var process = pipInfo as ProcessPipInfo;
                if (process == null)
                {
                    return;
                }

                var worker = process.Worker;
                if (process.Phase == PipPhase.CacheLookup)
                {
                    // Free the cache lookup slot.
                    worker.AvailableCacheLookupSlots++;
                    // If this pip is a cache hit, mark it complete.
                    if (process.IsCacheHit)
                    {
                        process.Phase = PipPhase.Completed;
                    }
                    else
                    {
                        // Cache miss: transition to Executable phase.
                        process.Phase = PipPhase.Executable;
                        // Add the pip back to the ready set for scheduling its executable stage.
                        readyPips.Add(process);
                    }
                }
                else if (process.Phase == PipPhase.Executable)
                {
                    // Free CPU and memory on the worker.
                    worker.AvailableExecuteSlots++;
                    worker.AvailableMemory += process.MemoryUsageMb;
                    process.Phase = PipPhase.Completed;
                }

                if (process.Phase == PipPhase.Completed)
                {
                    // Process dependents of the completed pip.
                    iterateDependents(process);
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
                pip.Worker = null;

                var process = pip as ProcessPipInfo;
                if (process == null)
                {
                    continue;
                }

                process.Phase = PipPhase.CacheLookup;
            }
        }
    }
}
