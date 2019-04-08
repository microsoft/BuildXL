// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.Pips.Operations;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Maintains counters for the number of pips in each <see cref="PipState" />.
    /// </summary>
    /// <remarks>
    /// Instance methods are thread-safe.
    /// This implementation is lock-free for counter updates while still providing consistent snapshots:
    /// - Each state counter is always (authentically; without simply clipping to zero).
    /// - If some pip P transitions from state S -> S' and then S' -> S'', the first transition is reflected
    /// first (i.e., transitions are seen in order; otherwise we could not provide the prior property).
    /// The approach has three key parts:
    /// - Allowing atomic recording of a transition. We keep a matrix indexed as [PipState, PipState] where
    /// each cell [F, T] is a counter for transitions from F -> T. Were we to instead keep an array of [PipState],
    /// we would need a DCAS (discontiguous compare and swap) operation to atomically update both the 'from' and 'to' states' counters.
    /// - Ensuring that a counter update for F -> T is made before the actual transition of F -> T is visible.
    /// This is made possible via the <c>onCommit</c> callback of <see cref="PipRuntimeInfo.TryTransition" />.
    /// - Taking an atomic snapshot of the counters matrix before deriving sums.
    /// Were we to simply iterate over the matrix, there may be some transition S -> S', S' -> S after we've
    /// visited matrix[S, S'] but before we've visited matrix[S', S] (thus violating the ordering property).
    /// </remarks>
    public sealed class PipStateCounters
    {
        /// <summary>
        /// Counters matrix used by <see cref="AccumulateTransition" />. Not protected by any locks.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1814:PreferJaggedArraysOverMultidimensional", Justification = "Dense.")]
        private readonly long[,,] m_countersMatrix = new long[PipStateRange.Range, PipStateRange.Range, (int) PipType.Max];

        /// <summary>
        /// Matrix used as a snapshot of <see cref="m_countersMatrix" />. This matrix must be locked while in use.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1814:PreferJaggedArraysOverMultidimensional", Justification = "Dense.")]
        private readonly long[,,] m_countersMatrixSnapshot = new long[PipStateRange.Range, PipStateRange.Range, (int) PipType.Max];

        /// <summary>
        /// Lock for collecting snapshots.
        /// </summary>
        private readonly object m_snapshotLock = new object();

        /// <summary>
        /// Buffer used to store the per-state counters derived from a matrix snapshot.
        /// </summary>
        /// <remarks>
        /// This field can't be purposefully read; its only purpose is to avoid allocations when replacing the contents
        /// of a <see cref="PipStateCountersSnapshot" />. We calculate the sums before modifying the snapshot so that they
        /// can be first validated (resulting in a strong invariant for the snapshot type).
        /// </remarks>
        private long[] m_snapshotSums = new long[PipStateRange.Range];

        /// <summary>
        /// Cached delegate for the <c>onCommit</c> callback of <see cref="PipRuntimeInfo.TryTransition" />.
        /// </summary>
        public readonly Action<PipState, PipState, PipType> OnPipStateTransitionCommit;

        /// <summary>
        /// Creates a set of zeroed counters for pip states.
        /// </summary>
        public PipStateCounters()
        {
            OnPipStateTransitionCommit = AccumulateTransition;
        }

        /// <summary>
        /// Records that a pip transitioned between two states.
        /// </summary>
        /// <remarks>
        /// Note that for full consistency guarantees, this must be called as the <c>onCommit</c> callback of
        /// <see cref="PipRuntimeInfo.TryTransition" /> / <see cref="PipRuntimeInfo.Transition" />.
        /// </remarks>
        public void AccumulateTransition(PipState from, PipState to, PipType pipType)
        {
            Contract.Requires(from != to);

            AccumulateTransitionInternal(from, to, pipType);
        }

        /// <summary>
        /// Records that a pip is currently in <paramref name="state"/> and will have future transitions to count
        /// (but no prior transitions have been counted).
        /// </summary>
        public void AccumulateInitialState(PipState state, PipType pipType)
        {
            AccumulateTransitionInternal(state, state, pipType);
        }

        /// <summary>
        /// Records that a pip is currently in <paramref name="state"/> and will have future transitions to count
        /// (but no prior transitions have been counted).
        /// </summary>
        public void AccumulateInitialStateBulk(PipState state, PipType pipType, int count)
        {
            long prior = Interlocked.CompareExchange(
                ref m_countersMatrix[(int) state - PipStateRange.MinValue, (int) state - PipStateRange.MinValue, (int) pipType],
                count,
                0);
            Contract.Assume(prior == 0);
        }

        private void AccumulateTransitionInternal(PipState from, PipState to, PipType pipType)
        {
            // TODO: not needed? Contract.Requires((int) from <= PipStateRange.MaxValue && (int) from >= PipStateRange.MinValue);
            // TODO: not needed? Contract.Requires((int) to <= PipStateRange.MaxValue && (int) to >= PipStateRange.MinValue);
            Interlocked.Increment(ref m_countersMatrix[(int)from - PipStateRange.MinValue, (int)to - PipStateRange.MinValue, (int)pipType]);
        }

        /// <summary>
        /// Collects a snapshot of current state counts into the provided instance.
        /// </summary>
        /// <remarks>
        /// Calls to this method are serialized.
        /// </remarks>
        public void CollectSnapshot(PipType[] pipTypes, PipStateCountersSnapshot target)
        {
            lock (m_snapshotLock)
            {
                // Populate m_countersMatrixSnapshot as a snapshot of m_countersMatrix.
                while (!TrySnapshotMatrix())
                {
                }

                target.Clear();

                // Note that from now on we should be using m_countersMatrixSnapshot. No access to m_countersMatrix
                // would be correct, since m_snapshotLock does not block writers to m_countersMatrix.

                // For each state, derive the number of pips in that state by accumulating the transitions into and out of it.
                for (int fromIdx = 0; fromIdx < m_countersMatrixSnapshot.GetLength(0); fromIdx++)
                {
                    for (int toIdx = 0; toIdx < m_countersMatrixSnapshot.GetLength(1); toIdx++)
                    {
                        for (int pipTypeIdx = 0; pipTypeIdx < pipTypes.Length; pipTypeIdx++)
                        {
                            var pipType = pipTypes[pipTypeIdx];

                            long transitionCount = m_countersMatrixSnapshot[fromIdx, toIdx, (int)pipType];

                            m_snapshotSums[toIdx] += transitionCount;

                            // The reflexive 'transitions' are for counting initial states.
                            if (toIdx != fromIdx)
                            {
                                m_snapshotSums[fromIdx] -= transitionCount;
                            }
                        }
                    }
                }

                for (int i = 0; i < m_snapshotSums.Length; i++)
                {
                    if (m_snapshotSums[i] < 0)
                    {
                        Contract.Assume(
                            false,
                            "PipStateCounters dropped below 0 for a state, suggesting PipStateCounters was not notified of a transition: " +
                            (PipState)(i + PipStateRange.MinValue));
                    }
                }

                m_snapshotSums = target.Swap(m_snapshotSums);
            }
        }

        /// <summary>
        /// Attempts to populate <see cref="m_countersMatrixSnapshot"/> from <see cref="m_countersMatrix"/> in
        /// a consistent fashion (linearizable with all of the transitions).
        /// </summary>
        /// <remarks>
        /// <see cref="m_snapshotLock"/> must be held to protect <see cref="m_countersMatrixSnapshot"/>.
        /// </remarks>
        private bool TrySnapshotMatrix()
        {
            Contract.Requires(Monitor.IsEntered(m_snapshotLock));

            // TODO: This deserves perf instrumentation. If this simple implementation becomes a problem,
            //       we can move to an implementation that co-operates with concurrent writers to complete.
            //       For example see:
            //         Maurice Herlihy and Nir Shavit. 2008. The Art of Multiprocessor Programming. Morgan Kaufmann Publishers Inc., San Francisco, CA, USA.
            //       (section 4.3 - 'Atomic Snapshots')
            //       This implementation (like the example in 4.3.1) is merely 'obstruction-free' since it is only guaranteed to complete in bounded steps
            //       when running in isolation.
            Array.Copy(m_countersMatrix, m_countersMatrixSnapshot, m_countersMatrix.Length);

            for (int i = 0; i < m_countersMatrix.GetLength(0); i++)
            {
                for (int j = 0; j < m_countersMatrix.GetLength(1); j++)
                {
                    for (int pipTypeIdx = 0; pipTypeIdx < (int) PipType.Max; pipTypeIdx++)
                    {
                        if (m_countersMatrix[i, j, pipTypeIdx] != m_countersMatrixSnapshot[i, j, pipTypeIdx])
                        {
                            // Snapshot violated.
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Collects a snapshot of current state counts for each pip type
        /// </summary>
        public void CollectSnapshotsForEachType(PipStateCountersSnapshot[] target)
        {
            for (int i = 0; i < (int) PipType.Max; i++)
            {
                CollectSnapshot(new[] { (PipType) i }, target[i]);
            }
        }
    }

    /// <summary>
    /// Consistent snapshot of counters for the number of pips in each <see cref="PipState" />.
    /// </summary>
    /// <remarks>
    /// Retrieving counter values is thread-safe. However, note that collecting a snapshot into this instance
    /// with <see cref="PipStateCounters.CollectSnapshot" /> modifies this instance, and so violates consistency guarantees
    /// if it runs concurrently with counter queries.
    /// </remarks>
    public sealed class PipStateCountersSnapshot
    {
        private long[] m_counters = new long[PipStateRange.Range];

        /// <summary>
        /// Returns the number of pips in the given state (as of this snapshot).
        /// </summary>
        public long this[PipState state]
        {
            get
            {
                // TODO: not needed? Contract.Requires((int) state <= PipStateRange.MaxValue && (int) state >= PipStateRange.MinValue);
                Contract.Ensures(Contract.Result<long>() >= 0);
                return m_counters[(int) state - PipStateRange.MinValue];
            }
        }

        /// <summary>
        /// Returns the number of pips on any state (as of this snapshot).
        /// It is useful to see the number of scheduled pips when you build with no execution phase.
        /// </summary>
        public long Total => m_counters.Sum();

        /// <summary>
        /// Returns the number of pips on the states that indicate success (as of this snapshot).
        /// </summary>
        public long DoneCount => this[PipState.Done];

        /// <summary>
        /// Returns the number of pips on the states that indicate they are running (as of this snapshot).
        /// </summary>
        public long RunningCount => this[PipState.Running];

        /// <summary>
        /// Returns the number of pips on the states that indicate they are ignored (as of this snapshot).
        /// </summary>
        public long IgnoredCount => this[PipState.Ignored];

        /// <summary>
        /// Returns the number of pips on the states that indicate they are skipped due to failed dependencies (as of this snapshot).
        /// </summary>
        public long SkippedDueToFailedDependenciesCount => this[PipState.Skipped] + this[PipState.Canceled];

        internal long[] Swap(long[] counters)
        {
            Contract.Requires(counters.Length == PipStateRange.Range);
            Contract.RequiresForAll(counters, i => i >= 0);
            Contract.Ensures(Contract.Result<long[]>().Length == PipStateRange.Range);

            long[] swap = m_counters;
            m_counters = counters;
            return swap;
        }

        internal void Clear()
        {
            Array.Clear(m_counters, 0, m_counters.Length);
        }

        /// <summary>
        /// Aggregate values for each pip state from the collection of snapshots of each pip type
        /// </summary>
        public void AggregateByPipTypes(PipStateCountersSnapshot[] snapshots, PipType[] pipTypes)
        {
            Clear();
            long sum;
            for (int pipState = 0; pipState < m_counters.Length; pipState++)
            {
                sum = 0;
                for (int i = 0; i < pipTypes.Length; i++)
                {
                    var pipType = pipTypes[i];
                    sum += snapshots[(int) pipType][(PipState) pipState];
                }

                m_counters[pipState] = sum;
            }
        }
    }

    /// <summary>
    /// Extension methods for transitioning pips' states while simultaneously updating counters.
    /// </summary>
    internal static class PipRuntimeInfoCounterExtensions
    {
        /// <summary>
        /// See <see cref="PipRuntimeInfo.Transition" /> ; additionally accumulates counters for pips in the source and target
        /// states.
        /// </summary>
        public static PipState Transition(this PipRuntimeInfo pipRuntimeInfo, PipStateCounters counters, PipType pipType, PipState targetState)
        {
            Contract.Requires(pipRuntimeInfo != null);
            Contract.Requires(counters != null);

            return pipRuntimeInfo.Transition(targetState, pipType, counters.OnPipStateTransitionCommit);
        }

        /// <summary>
        /// See <see cref="PipRuntimeInfo.TryTransition" /> ; additionally accumulates counters for pips in the source and target
        /// states.
        /// </summary>
        public static bool TryTransition(
            this PipRuntimeInfo pipRuntimeInfo,
            PipStateCounters counters,
            PipType pipType,
            PipState assumedPresentState,
            PipState targetState)
        {
            Contract.Requires(pipRuntimeInfo != null);
            Contract.Requires(counters != null);

            return pipRuntimeInfo.TryTransition(assumedPresentState, targetState, pipType, counters.OnPipStateTransitionCommit);
        }
    }
}
