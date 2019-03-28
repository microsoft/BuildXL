// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Contains the runtime-specific pip data, like the current state, ref count and assigned priority
    /// </summary>
    internal sealed class PipRuntimeInfo
    {
        /// <summary>
        /// We use the top bit of <see cref="m_state"/> to 'lock' states before transitioning them.
        /// </summary>
        private const int PreCommitStateBit = 1 << 31;

        /// <summary>
        /// Mask which turns <see cref="m_state"/> into a value valid for <see cref="PipState"/>
        /// (by masking off <see cref="PreCommitStateBit"/>)
        /// </summary>
        private const int CommittedStateMask = ~PreCommitStateBit;

        #region Fields

        /// <summary>
        /// Integer representation of the <see cref="PipState" /> (suitable for
        /// <see cref="Interlocked.CompareExchange(ref int,int,int)" />).
        /// </summary>
        private int m_state = (int)PipState.Ignored;

        /// <summary>
        /// The pip reference count.
        /// </summary>
        /// <remarks>
        /// When reference count gets 0, then the pip is ready for
        /// execution. While pip is executed its ref count is set to -1 (queued).
        /// Once the pip is executed, its ref count is set to -2 (succeeded) or -3 (failed).
        /// </remarks>
        private int m_refCount;

        /// <summary>
        /// The pip priority - currently based on the critical path.
        /// </summary>
        private int m_priority;

        /// <summary>
        /// The critical path duration in milliseconds
        /// </summary>
        internal int CriticalPathDurationMs { get; set; }

        /// <summary>
        /// The execution time of the external process. This will be lower than the e2e time of the pip itself. It should be 0 for a cache hit
        /// </summary>
        internal int ProcessExecuteTimeMs { get; set; }

        /// <summary>
        /// The pip result
        /// </summary>
        internal BuildXL.Pips.PipExecutionLevel Result { get; set; }

        internal TimeSpan CriticalPath => TimeSpan.FromMilliseconds(CriticalPathDurationMs);

        #endregion

        #region State

        /// <summary>
        /// The Pip's running state
        /// </summary>
        /// <remarks>
        /// This property is changed by the scheduler to reflect the pip's current state. The initial
        /// state is <see cref="PipState.Ignored" /> (until the pip is chosen for execution).
        /// </remarks>
        [ContractVerification(false)]
        public PipState State
        {
            get
            {
                Contract.Ensures(ContractUtilities.Static(((int)Contract.Result<PipState>() & PreCommitStateBit) == 0));
                return (PipState)(Volatile.Read(ref m_state) & CommittedStateMask);
            }
        }

        /// <summary>
        /// Atomically transitions <see cref="State" /> from <paramref name="assumedPresentState" /> to
        /// <paramref name="targetState" />.
        /// Returns a bool indicating if the transition succeeded (if false, the <paramref name="assumedPresentState" /> was
        /// mismatched with the current state).
        /// If provided, <paramref name="onCommit"/> is called upon committing to transition to the next state but
        /// before the state could possibly transition further.
        /// </summary>
        /// <remarks>
        /// This method is thread safe.
        /// </remarks>
        public bool TryTransition(PipState assumedPresentState, PipState targetState, PipType pipType = PipType.Max, Action<PipState, PipState, PipType> onCommit = null)
        {
            Contract.Requires(assumedPresentState.CanTransitionTo(targetState));
            Contract.Requires(((int)assumedPresentState & PreCommitStateBit) == 0, "PipState values should have the high bit cleared");
            Contract.Requires(pipType < PipType.Max || onCommit == null);

            return TryTransitionInternal(assumedPresentState, targetState, pipType, onCommit);
        }

        /// <summary>
        /// Atomically transitions <see cref="State" /> to <paramref name="targetState" />.
        /// The current state must not change during the attempted transition (i.e., synchronization may be required).
        /// If provided, <paramref name="onCommit"/> is called upon committing to transition to the next state but
        /// before the state could possibly transition further.
        /// </summary>
        /// <remarks>
        /// This method is effectively not thread safe, since one must somehow know that a transition to
        /// <paramref name="targetState" /> is valid
        /// and that the current state will not change during this call (e.g. via some collaborating lock).
        /// </remarks>
        /// <returns>The prior pip state</returns>
        public PipState Transition(PipState targetState, PipType pipType, Action<PipState, PipState, PipType> onCommit = null)
        {
            // This Requires is Static (not at runtime) so we avoid loading State twice (it can change behind our backs).
            // Below we do a final (authoritative) load of State and then runtime-verify a valid transition.
            Contract.RequiresDebug(State.CanTransitionTo(targetState));
            Contract.Requires(pipType < PipType.Max || onCommit == null);

            PipState presentState = State;
            if (!State.CanTransitionTo(targetState))
            {
                Contract.Assume(false, I($"Transition failure (not a valid transition): {presentState:G} -> {targetState:G}"));
            }

            Contract.Assume(((int)presentState & PreCommitStateBit) == 0);
            bool transitioned = TryTransitionInternal(presentState, targetState, pipType, onCommit);

            if (!transitioned)
            {
                Contract.Assume(
                    false,
                    I($"Failed to transition a pip from {presentState:G} to {targetState:G} due to an unexpected intervening state change (current state is now {State:G})"));
            }

            return presentState;
        }

        private bool TryTransitionInternal(PipState assumedPresentState, PipState targetState, PipType pipType, Action<PipState, PipState, PipType> onCommit)
        {
            Contract.Requires(((int)assumedPresentState & PreCommitStateBit) == 0, "PipState values should have the high bit cleared");

            // First we see if we can transition from assumedPresentState to a pre-commit version of it (note return value of State does
            // not change as a result of this). If we win, then we have rights to actually commit and visibly transition thereafter.
            // The end result is that {pre commit, onCommit(), actually transition} is atomic with respect to other concurrent calls.
            int preCommitStateValue = ((int)assumedPresentState) | PreCommitStateBit;
            int observedPresentStateValue = Interlocked.CompareExchange(ref m_state, preCommitStateValue, (int)assumedPresentState);

            if (observedPresentStateValue == (int)assumedPresentState)
            {
                if (onCommit != null)
                {
                    onCommit(assumedPresentState, targetState, pipType);
                }

                Volatile.Write(ref m_state, (int)targetState);
                return true;
            }
            else
            {
                return false;
            }
        }

        #endregion

        #region RefCount

        /// <summary>
        /// Get/set the pip ref count. The setter is valid only for the initial or final change.
        /// </summary>
        public int RefCount
        {
            get
            {
                return m_refCount;
            }

            set
            {
                Contract.Requires(RefCount == 0, "Setting refcount multiple times is not allowed.");
                m_refCount = value;
            }
        }

        /// <summary>
        /// Decrement the reference count.
        /// </summary>
        /// <returns>True if the pip is now unlocked; false if it is still locked.</returns>
        public bool DecrementRefCount()
        {
            Contract.Requires(RefCount > 0, "Too many DecrementRefCount calls");
            int newCount = Interlocked.Decrement(ref m_refCount);
            Contract.Assert(newCount >= 0, "Too many DecrementRefCount calls");
            return newCount == 0;
        }

        #endregion

        #region Priority

        /// <summary>
        /// Get/set the pip priority.
        /// </summary>
        public int Priority
        {
            get
            {
                return m_priority;
            }

            set
            {
                Contract.Requires(Priority == 0 || State.IsTerminal(), "Setting priority to new value is only allowed after pip completion for critical path computation.");
                m_priority = value;
            }
        }

        #endregion

        /// <summary>
        /// Whether the pip is impacted by uncacheability
        /// </summary>
        public bool IsUncacheableImpacted { get; set; }
    }
}
