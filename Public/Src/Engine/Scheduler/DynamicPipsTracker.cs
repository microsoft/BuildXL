// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Tracks the pips that the dynamic scheduler has picked up for scheduling: a pip is visited as soon as it leaves the
    /// <see cref="PipState.Ignored"/> state.
    /// </summary>
    /// <remarks>
    /// Visiting a pip is what transitions it from <see cref="PipState.Ignored"/> to <see cref="PipState.Waiting"/>, so this is
    /// the single place where pips are selected. It must only be used from the admission consumer, which is the only thread
    /// that moves pips out of the <see cref="PipState.Ignored"/> state.
    /// </remarks>
    internal sealed class DynamicPipsTracker : IVisitationTracker
    {
        private readonly IPipTable m_pipTable;
        private readonly PipStateCounters m_pipStateCounters;
        private readonly Func<PipId, PipRuntimeInfo> m_getPipRuntimeInfo;
        private readonly int m_initialPipCount;

        /// <nodoc />
        /// <param name="pipTable">The pip table of the dynamic graph.</param>
        /// <param name="pipStateCounters">The counters to keep in sync with state transitions.</param>
        /// <param name="getPipRuntimeInfo">Gets the runtime info of a pip.</param>
        /// <param name="initialPipCount">
        /// The number of pips in the pip table when the pip states were initialized. Their initial state is already
        /// accounted for in <paramref name="pipStateCounters"/>.
        /// </param>
        public DynamicPipsTracker(
            IPipTable pipTable,
            PipStateCounters pipStateCounters,
            Func<PipId, PipRuntimeInfo> getPipRuntimeInfo,
            int initialPipCount)
        {
            Contract.Requires(pipTable != null);
            Contract.Requires(pipStateCounters != null);
            Contract.Requires(getPipRuntimeInfo != null);

            m_pipTable = pipTable;
            m_pipStateCounters = pipStateCounters;
            m_getPipRuntimeInfo = getPipRuntimeInfo;
            m_initialPipCount = initialPipCount;
        }

        /// <inheritdoc />
        public bool MarkVisited(NodeId node)
        {
            var pipId = node.ToPipId();
            var pipType = m_pipTable.GetPipType(pipId);

            // Only pips that produce outputs are scheduled. Others, like hash source file pips, are never selected.
            if (!PipArtifacts.CanProduceOutputs(pipType))
            {
                return false;
            }

            var pipRuntimeInfo = m_getPipRuntimeInfo(pipId);
            if (pipRuntimeInfo.State != PipState.Ignored)
            {
                return false;
            }

            if (pipId.Value > m_initialPipCount)
            {
                // The initial bulk count only included the pip-table snapshot taken during scheduler initialization.
                // Register the initial state before transitioning a pip admitted after that snapshot.
                m_pipStateCounters.AccumulateInitialState(PipState.Ignored, pipType);
            }

            pipRuntimeInfo.Transition(m_pipStateCounters, pipType, PipState.Waiting);
            return true;
        }
    }
}
