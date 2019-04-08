// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Pip running state
    /// </summary>
    /// <remarks>
    /// The underlying type is int. Do not change it! The field storing the state uses Interlocked and cannot be declared as enum.
    /// </remarks>
    public enum PipState : int
    {
        /// <summary>
        /// Pip doesn't apply to current build due to being filtered out
        /// </summary>
        Ignored,

        /// <summary>
        /// Pip is pending some dependencies to be fulfilled
        /// before it is queued to run
        /// </summary>
        Waiting,

        /// <summary>
        /// Pip is queued and ready for executing
        /// </summary>
        Ready,

        /// <summary>
        /// Pip is currently running
        /// </summary>
        Running,

        /// <summary>
        /// Pip completed successfully
        /// </summary>
        Done,

        /// <summary>
        /// Pip has failed
        /// </summary>
        Failed,

        /// <summary>
        /// Pip execution was skipped
        /// </summary>
        Skipped,

        /// <summary>
        /// Pip was canceled despite being eligible to run (e.g. early schedule termination).
        /// </summary>
        Canceled,
    }

    /// <summary>
    /// Numeric range of <see cref="PipState"/>.
    /// </summary>
    public static class PipStateRange
    {
        /// <summary>
        /// Max integer value of a valid <see cref="PipState"/>.
        /// </summary>
        public const int MaxValue = (int)PipState.Canceled;

        /// <summary>
        /// Min integer value of a valid <see cref="PipState"/>.
        /// </summary>
        public const int MinValue = (int)PipState.Ignored;

        /// <summary>
        /// Size of the inclusive integer range of <see cref="PipState"/> (<see cref="MaxValue"/> - <see cref="MinValue"/> + 1)
        /// </summary>
        public const int Range = MaxValue - MinValue + 1;
    }

    /// <summary>
    /// Extensions for modelling <see cref="PipState"/> attributes and the pip state machine.
    /// </summary>
    public static class PipStateExtensions
    {
        /// <summary>
        /// Indicates if there is a valid state transition from <paramref name="current"/> to <paramref name="target"/>.
        /// </summary>
        [Pure]
        public static bool CanTransitionTo(this PipState current, PipState target)
        {
            switch (target)
            {
                case PipState.Ignored:
                    return false; // Entry state.
                case PipState.Waiting:
                    // Initial state on pip addition / explicit scheduling.
                    return current == PipState.Ignored ||
                           /* fail establish pip fingerprint */ current == PipState.Running;
                case PipState.Ready:
                    // Pending pips are the staging area on the way to being queued - they may have unsatisfied dependencies
                    return current == PipState.Waiting;
                case PipState.Running:
                    // Queued pips can be run when they reach the queue head.
                    return current == PipState.Ready;
                case PipState.Skipped:
                    // Skipping (due to failure of pre-requirements) can occur so long as the pip still has pre-requirements (i.e., Pending),
                    // or if we tentatively skipped a filter-failing pip (that was later scheduled due to a filter change).
                    return current == PipState.Waiting;
                case PipState.Canceled:
                    // Completion / failure is the consequence of actual execution.
                    return current == PipState.Running;
                case PipState.Failed:
                case PipState.Done:
                    // Completion.
                    return current == PipState.Running;
                default:
                    throw Contract.AssertFailure("Unhandled Pip State");
            }
        }

        /// <summary>
        /// Indicates if <paramref name="current"/> is a terminal state (there are no valid transitions out of it).
        /// </summary>
        [Pure]
        public static bool IsTerminal(this PipState current)
        {
            switch (current)
            {
                case PipState.Ignored:
                case PipState.Waiting:
                case PipState.Ready:
                case PipState.Running:
                    return false;
                case PipState.Skipped:
                case PipState.Failed:
                case PipState.Done:
                case PipState.Canceled:
                    return true;
                default:
                    throw Contract.AssertFailure("Unhandled Pip State");
            }
        }

        /// <summary>
        /// Indicates if <paramref name="current"/> is a terminal state which indicates that a pip has failed.
        /// </summary>
        [Pure]
        public static bool IndicatesFailure(this PipState current)
        {
            Contract.Ensures(ContractUtilities.Static(IsTerminal(current)));

            switch (current)
            {
                case PipState.Ignored:
                case PipState.Waiting:
                case PipState.Ready:
                case PipState.Running:
                    return false; // Non-terminal
                case PipState.Done:
                    return false; // Terminal but successful
                case PipState.Skipped:
                case PipState.Failed:
                case PipState.Canceled:
                    return true; // Oh no!
                default:
                    throw Contract.AssertFailure("Unhandled Pip State");
            }
        }
    }
}
