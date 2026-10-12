// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core;

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
        /// Whether a node matched the build filter.
        /// </summary>
        private readonly Func<NodeId, bool> m_isExplicitlyScheduled;

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
            : this(loggingContext, GetMembershipCheck(explicitlyScheduledProcessNodes))
        {
        }

        /// <summary>
        /// Creates an instance of <see cref="PipCountersByFilter"/> for a set that can grow while the counters are in use.
        /// </summary>
        public PipCountersByFilter(LoggingContext loggingContext, ConcurrentBigSet<NodeId> explicitlyScheduledProcessNodes)
            : this(loggingContext, GetMembershipCheck(explicitlyScheduledProcessNodes))
        {
        }

        private PipCountersByFilter(LoggingContext loggingContext, Func<NodeId, bool> isExplicitlyScheduled)
            : base(loggingContext)
        {
            Contract.Requires(loggingContext != null);

            m_isExplicitlyScheduled = isExplicitlyScheduled;
            CountersByGroup.TryAdd(Explicit, new CounterCollection<PipCountersByGroup>());
            CountersByGroup.TryAdd(Implicit, new CounterCollection<PipCountersByGroup>());
        }

        private static Func<NodeId, bool> GetMembershipCheck(HashSet<NodeId> nodes)
        {
            Contract.Requires(nodes != null);
            return node => nodes.Contains(node);
        }

        private static Func<NodeId, bool> GetMembershipCheck(ConcurrentBigSet<NodeId> nodes)
        {
            Contract.Requires(nodes != null);
            return node => nodes.Contains(node);
        }

        /// <inheritdoc />
        protected override IEnumerable<bool> GetCategories(Process process)
        {
            yield return m_isExplicitlyScheduled(process.PipId.ToNodeId());
        }

        /// <inheritdoc />
        protected override string CategoryToString(bool category) => category == Explicit ? nameof(ExplicitlyScheduledProcesses) : nameof(ImplicitlyScheduledProcesses);
    }
}
