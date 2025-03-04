// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Execution state for processes, which is carried between pip execution steps.
    /// </summary>
    public sealed class ProcessRunnablePip : RunnablePip
    {
        /// <nodoc/>
        public Process Process => (Process)Pip;

        /// <summary>
        /// Process weight
        /// </summary>
        /// <remarks>
        /// If process weight is defined as greater than the minimum weight in the specs, use it. 
        /// Otherwise, use the weight based on historic cpu usage.
        /// </remarks>
        public override int Weight => Math.Max(Process.Weight, m_weightBasedOnHistoricCpuUsage);

        /// <inheritdoc/>
        public override bool IsLight => Process.IsLight;

        /// <nodoc/>
        public RunnableFromCacheResult CacheResult { get; private set; }

        /// <nodoc/>
        public PipCachePerfInfo CacheLookupPerfInfo => Performance.CacheLookupPerfInfo;

        /// <summary>
        /// Gets whether the process was executed (i.e. process was cache miss and ran ExecuteProcess step)
        /// </summary>
        public bool Executed { get; set; }

        /// <summary>
        /// The expected memory usage for the pip
        /// </summary>
        public ProcessMemoryCounters? ExpectedMemoryCounters;

        /// <summary>
        /// Historic perf data
        /// </summary>
        public ProcessPipHistoricPerfData? HistoricPerfData;

        /// <summary>
        /// SemaphoreResources
        /// </summary>
        public ItemResources? Resources { get; private set; }

        /// <summary>
        /// Cacheable process to avoid re-creation.
        /// </summary>
        public CacheableProcess CacheableProcess { get; private set; }

        /// <summary>
        /// Source change affected input of the pip
        /// </summary>
        public IReadOnlyCollection<AbsolutePath> ChangeAffectedInputs { get; set; }

        /// <summary>
        /// Run location of the process.
        /// </summary>
        public ProcessRunLocation RunLocation { get; set; } = ProcessRunLocation.Default;

        /// <summary>
        /// Number of path sets to check on cache lookup.
        /// </summary>
        public int NumberOfUniquePathSetsToCheck => Environment.Configuration.Cache.MaxPathSetsOnCacheLookup > 0
            ? Environment.Configuration.Cache.MaxPathSetsOnCacheLookup
            : int.MaxValue;

        private readonly int m_weightBasedOnHistoricCpuUsage;

        /// <summary>
        /// Maintain a list of all execution results from all retries, including retries that happened on different workers
        /// </summary>
        private readonly List<ExecutionResult> m_allExecutionResults = new();

        /// <nodoc/>
        public IEnumerable<ExecutionResult> AllExecutionResults => m_allExecutionResults;

        /// <nodoc/>
        public bool MustRunOnOrchestrator { get; set; }

        internal ProcessRunnablePip(
            LoggingContext phaseLoggingContext,
            PipId pipId,
            int priority,
            Func<RunnablePip, Task> executionFunc,
            IPipExecutionEnvironment environment,
            ushort cpuUsageInPercents = 0,
            Pip pip = null)
            : base(phaseLoggingContext, pipId, PipType.Process, priority, executionFunc, environment, pip)
        {
            if (cpuUsageInPercents > 100)
            {
                m_weightBasedOnHistoricCpuUsage = (int)Math.Round(cpuUsageInPercents / 100.0);
            }
            else
            {
                // If cpu usage is less than 100%, just use the lowest possible weight.
                m_weightBasedOnHistoricCpuUsage = Process.MinWeight;
            }
        }

        /// <summary>
        /// Set the executionResult object and the list of executionResult objects.
        /// </summary>
        public override void SetExecutionResult(ExecutionResult executionResult)
        {
            base.SetExecutionResult(executionResult);
            m_allExecutionResults.Add(executionResult);
        }

        /// <nodoc/>
        protected override void OperationCompleted(OperationKind kind, TimeSpan duration)
        {
            if (kind.CacheLookupCounterId >= 0)
            {
                CacheLookupPerfInfo.LogCacheLookupStep(Step, kind, duration);
            }
        }

        /// <nodoc/>
        public bool TryAcquireResources(SemaphoreSet<StringId> machineSemaphores, IReadOnlyList<ProcessSemaphoreInfo> additionalResources, bool force, out StringId limitingResourceName)
        {
            // TryAcquireResources might be called more than once.
            // Once we cannot acquire resources and try again,
            // we need to call again because custom resource counts may be used per machine
            Resources = Process.GetSemaphoreResources(machineSemaphores, customSemaphores: additionalResources);

            int? limitingResourceIndex;
            var result = machineSemaphores.TryAcquireResources(Resources.Value, out limitingResourceIndex, force);
            if (!result)
            {
                limitingResourceName = machineSemaphores.GetKey(limitingResourceIndex.Value);
            }
            else
            {
                limitingResourceName = StringId.Invalid;
            }

            return result;
        }

        /// <nodoc/>
        public void SetCacheResult(RunnableFromCacheResult cacheResult)
        {
            CacheResult = cacheResult;
        }

        /// <nodoc/>
        public void SetCacheableProcess(CacheableProcess cacheableProcess)
        {
            CacheableProcess = cacheableProcess;
        }
    }
}
