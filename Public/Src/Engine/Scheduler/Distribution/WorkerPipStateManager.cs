// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Tracks <see cref="WorkerPipState"/> of pips on workers
    /// </summary>
    public class WorkerPipStateManager : PipStateManager<WorkerPipState>
    {
        /// <nodoc />
        public WorkerPipStateManager()
            : base(EnumTraits<WorkerPipState>.ValueCount)
        {
        }

        /// <inheritdoc />
        protected override int Convert(WorkerPipState state)
        {
            return (int)state;
        }

        /// <inheritdoc />
        protected override WorkerPipState Convert(int state)
        {
            return (WorkerPipState)state;
        }

        /// <inheritdoc />
        protected override bool IsValidTransition(WorkerPipState fromState, WorkerPipState toState)
        {
            return toState > fromState;
        }
    }
}
