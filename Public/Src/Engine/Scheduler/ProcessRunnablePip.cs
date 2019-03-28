// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
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

        /// <nodoc/>
        public RunnableFromCacheResult CacheResult { get; private set; }

        /// <nodoc/>
        public CacheLookupPerfInfo CacheLookupPerfInfo => Performance.CacheLookupPerfInfo;

        /// <summary>
        /// Gets whether the process was executed (i.e. process was cache miss and ran ExecuteProcess step)
        /// </summary>
        public bool Executed { get; set; }

        /// <summary>
        /// The expected RAM utilization of the pip
        /// </summary>
        public int? ExpectedRamUsageMb;

        /// <summary>
        /// SemaphoreResources
        /// </summary>
        public ItemResources? Resources { get; private set; }

        /// <summary>
        /// Cacheable process to avoid re-creation.
        /// </summary>
        public CacheableProcess CacheableProcess { get; private set; }

        internal ProcessRunnablePip(
            LoggingContext phaseLoggingContext,
            PipId pipId,
            int priority,
            Func<RunnablePip, Task> executionFunc,
            IPipExecutionEnvironment environment,
            Pip pip = null)
            : base(phaseLoggingContext, pipId, PipType.Process, priority, executionFunc, environment, pip)
        {
        }

        /// <nodoc/>
        protected override void OperationCompleted(OperationKind kind, TimeSpan duration)
        {
            if (kind.CacheLookupCounterId >= 0)
            {
                CacheLookupPerfInfo.LogCacheLookupStep(kind, duration);
            }
        }

        /// <nodoc/>
        public bool TryAcquireResources(SemaphoreSet<StringId> machineSemaphores, IReadOnlyList<ProcessSemaphoreInfo> additionalResources, out StringId limitingResourceName)
        {
            // TryAcquireResources might be called more than once.
            // Once we cannot acquire resources and try again,
            // we need to call again because custom resource counts may be used per machine
            Resources = Process.GetSemaphoreResources(machineSemaphores, customSemaphores: additionalResources);

            int? limitingResourceIndex;
            var result = machineSemaphores.TryAcquireResources(Resources.Value, out limitingResourceIndex);
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
