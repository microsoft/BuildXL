// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
