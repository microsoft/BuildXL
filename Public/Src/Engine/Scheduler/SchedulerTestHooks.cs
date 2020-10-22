// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Processes;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Test hooks for BuildXL scheduler.
    /// </summary>
    /// <remarks>
    /// These hooks are used to query private information about the state of the scheduler.
    /// </remarks>
    public class SchedulerTestHooks
    {
        /// <summary>
        /// Incremental scheduling state owned by the scheduler.
        /// </summary>
        public IIncrementalSchedulingState IncrementalSchedulingState { get; set; }

        /// <summary>
        /// Action to validate incremental scheduling state after journal scan.
        /// </summary>
        public Action<IIncrementalSchedulingState> IncrementalSchedulingStateAfterJournalScanAction { get; set; }

        /// <summary>
        /// Validates incremental scheduling state after journal scan.
        /// </summary>
        internal void ValidateIncrementalSchedulingStateAfterJournalScan(IIncrementalSchedulingState incrementalSchedulingState)
        {
            Contract.Requires(incrementalSchedulingState != null);
            IncrementalSchedulingStateAfterJournalScanAction?.Invoke(incrementalSchedulingState);
        }

        /// <summary>
        /// Test hooks for the <see cref="FingerprintStore"/>.
        /// </summary>
        public FingerprintStoreTestHooks FingerprintStoreTestHooks { get; set; }

        /// <summary>
        /// Listener to collect detours reported accesses
        /// </summary>
        public IDetoursEventListener DetoursListener { get; set; }

        /// <summary>
        /// A function to generate synthetic machine perf info
        /// </summary>
        public Func<LoggingContext, Scheduler, PerformanceCollector.MachinePerfInfo> GenerateSyntheticMachinePerfInfo { get; set; }

        /// <summary>
        /// Shortcut to get <see cref="CounterCollection{FingerprintStoreCounters}"/>.
        /// </summary>
        public CounterCollection<FingerprintStoreCounters> FingerprintStoreCounters => FingerprintStoreTestHooks?.Counters;

        /// <summary>
        /// Flag used to enable Pip Cancellation due to high memory on Mac
        /// </summary>
        public bool SimulateHighMemoryPressure { get; set; }

        /// <summary>
        /// Scanning journal result.
        /// </summary>
        public ScanningJournalResult ScanningJournalResult { get; set; }
    }
}
