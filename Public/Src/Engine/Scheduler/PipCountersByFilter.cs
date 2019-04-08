// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Counter collections for process pips aggregated by explicitly scheduled processes as requested
    /// by the user versus implicitly scheduled (dependency) processes as required for build correctness.
    /// </summary>
    public sealed class PipCountersByFilter : PipCountersByGroupCategoryBase<bool>
    {
        private const bool Explicit = true;
        private const bool Implicit = false;

        /// <summary>
        /// Nodes that matched the build filter.
        /// </summary>
        private HashSet<NodeId> m_explicitlyScheduledProcessNodes;

        /// <summary>
        /// Counters for process pips that match the build filter, defined as explicitly scheduled.
        /// </summary>
        public CounterCollection<PipCountersByGroup> ExplicitlyScheduledProcesses => CountersByGroup[Explicit];

        /// <summary>
        /// Counters for process pips that are dependencies of explicitly scheduled pips, defined as implicitly scheduled.
        /// Note, if there is no filter, then all nodes are considered implicitly scheduled.
        /// </summary>
        public CounterCollection<PipCountersByGroup> ImplicitlyScheduledProcesses => CountersByGroup[Implicit];

        /// <summary>
        /// Creates an instance of <see cref="PipCountersByFilter"/>.
        /// </summary>
        public PipCountersByFilter(LoggingContext loggingContext, HashSet<NodeId> explicitlyScheduledProcessNodes)
            : base(loggingContext)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(explicitlyScheduledProcessNodes != null);

            m_explicitlyScheduledProcessNodes = explicitlyScheduledProcessNodes;
            CountersByGroup.TryAdd(Explicit, new CounterCollection<PipCountersByGroup>());
            CountersByGroup.TryAdd(Implicit, new CounterCollection<PipCountersByGroup>());
        }

        /// <inheritdoc />
        protected override IEnumerable<bool> GetCategories(Process process)
        {
            yield return m_explicitlyScheduledProcessNodes.Contains(process.PipId.ToNodeId());
        }

        /// <inheritdoc />
        protected override string CategoryToString(bool category) => category == Explicit ? nameof(ExplicitlyScheduledProcesses) : nameof(ImplicitlyScheduledProcesses);
    }
}
