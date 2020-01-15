// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Tracks <see cref="PipExecutionStep"/> of pips
    /// </summary>
    public class PipExecutionStepTracker : PipStateManager<PipExecutionStep>
    {
        /// <summary>
        /// Snapshot of pip execution steps
        /// </summary>
        public Snapshot CurrentSnapshot { get; }

        /// <nodoc />
        public PipExecutionStepTracker()
            : base(EnumTraits<PipExecutionStep>.ValueCount)
        {
            CurrentSnapshot = GetSnapshot();
        }

        /// <inheritdoc />
        protected override int Convert(PipExecutionStep state)
        {
            return (int) state;
        }

        /// <inheritdoc />
        protected override PipExecutionStep Convert(int state)
        {
            return (PipExecutionStep) state;
        }
    }
}
