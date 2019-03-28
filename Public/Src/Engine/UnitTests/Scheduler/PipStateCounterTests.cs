// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class PipStateCounterTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private static readonly PipState[] s_pipStates = Enum.GetValues(typeof(PipState)).Cast<PipState>().ToArray();
        private static readonly PipType[] s_pipTypes = Enum.GetValues(typeof(PipType)).Cast<PipType>().Where(type => type != PipType.Max).ToArray();

        public PipStateCounterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void Empty()
        {
            var counters = new PipStateCounters();
            var snapshot = new PipStateCountersSnapshot();
            counters.CollectSnapshot(s_pipTypes, snapshot);

            foreach (PipState state in s_pipStates)
            {
                XAssert.AreEqual(0, snapshot[state]);
            }
        }

        [Fact]
        public void InitialStates()
        {
            var counters = new PipStateCounters();

            int numInitial = 0;
            foreach (PipState state in s_pipStates)
            {
                numInitial++;
                for (int i = 0; i < numInitial; i++)
                {
                    counters.AccumulateInitialState(state, PipType.Process);
                }
            }

            var snapshot = new PipStateCountersSnapshot();
            counters.CollectSnapshot(s_pipTypes, snapshot);

            int numExpected = 0;
            foreach (PipState state in s_pipStates)
            {
                numExpected++;
                XAssert.AreEqual(numExpected, snapshot[state]);
            }
        }

        [Fact]
        public void Transitions()
        {
            var counters = new PipStateCounters();

            PipState? previousState = null;
            foreach (PipState state in s_pipStates)
            {
                if (previousState.HasValue)
                {
                    counters.AccumulateTransition(previousState.Value, state, PipType.Process);
                }
                else
                {
                    counters.AccumulateInitialState(state, PipType.Process);
                }

                previousState = state;
            }

            var snapshot = new PipStateCountersSnapshot();
            counters.CollectSnapshot(s_pipTypes, snapshot);

            int numExpected = 1;
            foreach (PipState state in s_pipStates.Reverse())
            {
                XAssert.AreEqual(numExpected, snapshot[state]);

                // Only the last state should a count. We did (initial A) A -> B -> C, expecting one unit on C.
                numExpected = 0;
            }
        }

        [Fact]
        public void TransitionsCancellation()
        {
            var counters = new PipStateCounters();

            // Here for various pairs we transition A -> B then B -> A.
            // This cancels out to 'one pips in A and B' since each has one initially.
            PipState? previousState = null;
            foreach (PipState state in s_pipStates)
            {
                counters.AccumulateInitialState(state, PipType.Process);

                if (previousState.HasValue)
                {
                    counters.AccumulateTransition(previousState.Value, state, PipType.Process);
                    counters.AccumulateTransition(state, previousState.Value, PipType.Process);
                }

                previousState = state;
            }

            var snapshot = new PipStateCountersSnapshot();
            counters.CollectSnapshot(s_pipTypes, snapshot);

            foreach (PipState state in s_pipStates)
            {
                XAssert.AreEqual(1, snapshot[state]);
            }
        }

        [Fact]
        public void SnapshotsAreIsolated()
        {
            var counters = new PipStateCounters();
            var snapshot1 = new PipStateCountersSnapshot();
            var snapshot2 = new PipStateCountersSnapshot();

            counters.CollectSnapshot(s_pipTypes, snapshot1);
            XAssert.AreEqual(0, snapshot1[PipState.Running]);

            counters.AccumulateInitialState(PipState.Running, PipType.Process);
            counters.CollectSnapshot(s_pipTypes, snapshot2);
            XAssert.AreEqual(0, snapshot1[PipState.Running]);
            XAssert.AreEqual(1, snapshot2[PipState.Running]);

            counters.AccumulateInitialState(PipState.Running, PipType.Process);
            XAssert.AreEqual(0, snapshot1[PipState.Running]);
            XAssert.AreEqual(1, snapshot2[PipState.Running]);
        }

        /// <summary>
        /// Performs concurrent random transitions of imaginary pips while repeatedly taking snapshots
        /// (until the total snapshotted transition count reaches some threshold).
        /// </summary>
        [Fact]
        public void SnapshotStress()
        {
            var counters = new PipStateCounters();
            bool[] exit = {false};

            var transitionThreads = new Thread[4];

            for (int i = 0; i < transitionThreads.Length; i++)
            {
                int threadId = i;
                transitionThreads[i] = new Thread(
                    () =>
                    {
                        while (!Volatile.Read(ref exit[0]))
                        {
                            PipState[] orderedStates = s_pipStates.ToArray();
                            Shuffle(new Random(threadId), orderedStates);

                            // Create a new imaginary pip.
                            counters.AccumulateInitialState(PipState.Ignored, PipType.Process);

                            // Transition randomly until it is terminal.
                            var currentState = PipState.Ignored;
                            var priorStates = new HashSet<PipState> {currentState};
                            int nextCandidateStateIndex = 0;

                            while (!currentState.IsTerminal())
                            {
                                nextCandidateStateIndex = (nextCandidateStateIndex + 1) % orderedStates.Length;
                                PipState possibleNextState = orderedStates[nextCandidateStateIndex];

                                if (priorStates.Contains(possibleNextState))
                                {
                                    // Cycle is possible.
                                    break;
                                }

                                if (possibleNextState != currentState && currentState.CanTransitionTo(possibleNextState))
                                {
                                    priorStates.Add(possibleNextState);
                                    counters.AccumulateTransition(currentState, possibleNextState, PipType.Process);
                                    currentState = possibleNextState;
                                }
                            }
                        }
                    });
            }

            try
            {
                foreach (Thread t in transitionThreads)
                {
                    t.Start();
                }

                var snapshot = new PipStateCountersSnapshot();

                long sum = 0;
                while (sum < 100 * 1000)
                {
                    counters.CollectSnapshot(s_pipTypes, snapshot);

                    foreach (PipState state in s_pipStates)
                    {
                        XAssert.IsTrue(snapshot[state] >= 0);
                    }

                    long newSum = s_pipStates.Sum(s => snapshot[s]);
                    XAssert.IsTrue(newSum >= sum, "Counters must be (probably non-strictly) monotonic");
                    sum = newSum;
                }
            }
            finally
            {
                Volatile.Write(ref exit[0], true);
            }

            foreach (Thread t in transitionThreads)
            {
                t.Join();
            }
        }
        
        private void Shuffle<T>(Random rng, T[] array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                int j = rng.Next(i, array.Length);
                T swap = array[j];
                array[j] = array[i];
                array[i] = swap;
            }
        }
    }
}
