// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage.ChangeTracking;

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
    }
}
