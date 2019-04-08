// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Wrapper for PipCompletionData with extra members which are not meant to be serialized.
    /// </summary>
    public sealed class ExtendedPipCompletionData
    {
        internal readonly PipCompletionData SerializedData;

        /// <nodoc/>
        public ExtendedPipCompletionData(PipCompletionData pipCompletionData)
        {
            SerializedData = pipCompletionData;
        }

        /// <summary>
        /// Gets or sets the pip id
        /// </summary>
        internal PipId PipId => new PipId(SerializedData.PipIdValue);

        /// <summary>
        /// For tracing purposes only. Not transferred as a part of Bond call.
        /// </summary>
        internal long SemiStableHash { get; set; }

        /// <summary>
        /// Signal for start of step execution
        /// </summary>
        internal TaskSourceSlim<bool> StepExecutionStarted { get; set; } = TaskSourceSlim.Create<bool>();

        /// <summary>
        /// Signal for end of step execution
        /// </summary>
        internal TaskSourceSlim<ExecutionResult> StepExecutionCompleted { get; set; } = TaskSourceSlim.Create<ExecutionResult>();

        internal ExecutionResult ExecutionResult { get; set; }
    }
}
