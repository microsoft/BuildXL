// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.IncrementalScheduling;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// This immutable object carries the state of the scheduler between BuildXL client sessions
    /// </summary>
    public sealed class SchedulerState : IDisposable
    {
        /// <summary>
        /// Root filter
        /// </summary>
        public RootFilter RootFilter
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_rootFilter;
            }
        }

        private readonly RootFilter m_rootFilter;

        /// <summary>
        /// Filter passing nodes
        /// </summary>
        public RangedNodeSet FilterPassingNodes
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_filterPassingNodes;
            }
        }

        private readonly RangedNodeSet m_filterPassingNodes;

        /// <summary>
        /// Incremental scheduling state.
        /// </summary>
        public IIncrementalSchedulingState IncrementalSchedulingState
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_incrementalSchedulingState;
            }
        }

        private readonly IIncrementalSchedulingState m_incrementalSchedulingState;

        /// <summary>
        /// Whether this instance got disposed.
        /// </summary>
        [Pure]
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Creates the scheduler state
        /// </summary>
        public SchedulerState(Scheduler scheduler)
        {
            // If the filter is not applied, rootFilter and filterPassingNodes are null.
            m_rootFilter = scheduler.RootFilter;
            m_filterPassingNodes = scheduler.FilterPassingNodes;
            m_incrementalSchedulingState = scheduler.IncrementalSchedulingState;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        /// <remarks>If the state from previous build is not usable, this object must be disposed so that developers cannot access old state.</remarks>
        public void Dispose() => IsDisposed = true;
    }
}
