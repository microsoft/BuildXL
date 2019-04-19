// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BuildXL.Pips;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Threading;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Manages states for an identifier
    /// </summary>
    /// <typeparam name="T">the state type</typeparam>
    public abstract class PipStateManager<T>
    {
        private readonly ConcurrentBigSet<Entry> m_stateMap = new ConcurrentBigSet<Entry>();
        private readonly StateCount[] m_stateCounts;
        private readonly ReadWriteLock m_stateCountsLock = ReadWriteLock.Create();

        /// <nodoc />
        protected PipStateManager(int numberOfStates)
        {
            m_stateCounts = new StateCount[numberOfStates];
        }

        /// <summary>
        /// Transitions the state of the pip
        /// </summary>
        public virtual void Transition(PipId pipId, T toState)
        {
            // TODO: Maybe remove on transition to terminal state?
            using (m_stateCountsLock.AcquireReadLock())
            {
                int toStateValue = Convert(toState);
                Interlocked.Increment(ref m_stateCounts[toStateValue].CumulativeCount);
                var result = m_stateMap.GetOrAdd(new Entry(pipId, toStateValue));
                if (!result.IsFound)
                {
                    Interlocked.Increment(ref m_stateCounts[toStateValue].ActiveCount);
                    return;
                }

                var bufferPointer = m_stateMap.GetItemsUnsafe().GetBufferPointer(result.Index);
                while (true)
                {
                    var fromStateValue = Volatile.Read(ref bufferPointer.Buffer[bufferPointer.Index].State);
                    var fromState = Convert(fromStateValue);
                    if (!IsValidTransition(fromState, toState))
                    {
                        return;
                    }

                    if (Interlocked.CompareExchange(ref bufferPointer.Buffer[bufferPointer.Index].State, toStateValue, fromStateValue) == fromStateValue)
                    {
                        Interlocked.Decrement(ref m_stateCounts[fromStateValue].ActiveCount);
                        Interlocked.Increment(ref m_stateCounts[toStateValue].ActiveCount);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Get a snapshot of the counts of each of the states
        /// </summary>
        public Snapshot GetSnapshot()
        {
            return new Snapshot(this);
        }

        /// <summary>
        /// Convert the state to an integer
        /// </summary>
        protected abstract int Convert(T state);

        /// <summary>
        /// Convert the integer to a state
        /// </summary>
        protected abstract T Convert(int state);

        /// <summary>
        /// Gets whether the state transition is valid
        /// </summary>
        protected virtual bool IsValidTransition(T fromState, T toState)
        {
            return true;
        }

        /// <summary>
        /// Update the state counts in the given array
        /// </summary>
        private void Update(StateCount[] stateCounts)
        {
            using (m_stateCountsLock.AcquireWriteLock())
            {
                for (int i = 0; i < m_stateCounts.Length; i++)
                {
                    stateCounts[i] = m_stateCounts[i];
                }
            }
        }

        private struct Entry : IEquatable<Entry>
        {
            public PipId PipId { get; }

#pragma warning disable CA1051 // Do not declare visible instance fields

            // The field is modified by interlocked operations and should be public.
            public int State;
#pragma warning restore CA1051 // Do not declare visible instance fields

            public Entry(PipId pipId, int state = 0)
            {
                PipId = pipId;
                State = state;
            }

            public bool Equals(Entry other)
            {
                return PipId.Equals(other.PipId);
            }

            public override int GetHashCode()
            {
                return PipId.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return obj is Entry && Equals((Entry)obj);
            }

            public static bool operator ==(Entry left, Entry right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(Entry left, Entry right)
            {
                return !(left == right);
            }
        }

        /// <summary>
        /// Counts for pips in states
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
        public struct StateCount
        {
            /// <summary>
            /// The current count of pips in this state
            /// </summary>
            public int ActiveCount;

            /// <summary>
            /// The cumulative count of pips which have entered
            /// </summary>
            public int CumulativeCount;
        }

        /// <summary>
        /// A snapshot of the state counts at a particular point in time
        /// </summary>
        public sealed class Snapshot
        {
            private readonly PipStateManager<T> m_manager;
            private readonly StateCount[] m_stateCounts;

            /// <nodoc />
            public Snapshot(PipStateManager<T> manager)
            {
                m_manager = manager;
                m_stateCounts = new StateCount[manager.m_stateCounts.Length];
            }

            /// <summary>
            /// Updates the snapshot with the latest values
            /// </summary>
            public void Update()
            {
                m_manager.Update(m_stateCounts);
            }

            /// <summary>
            /// Gets the count for the state from the snapshot
            /// </summary>
            public int this[T state] => m_stateCounts[m_manager.Convert(state)].ActiveCount;

            /// <summary>
            /// Gets the cumulative count for the given state
            /// </summary>
            public int GetCumulativeCount(T state)
            {
                return m_stateCounts[m_manager.Convert(state)].CumulativeCount;
            }
        }
    }
}
