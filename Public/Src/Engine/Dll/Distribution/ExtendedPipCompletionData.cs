// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
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
        /// For tracing purposes only. Not transferred as a part of gRPC call.
        /// </summary>
        internal long SemiStableHash { get; set; }

        /// <summary>
        /// For telemetry purposes only. Not transferred as a part of gRPC call.
        /// </summary>
        internal PipType PipType { get; set; }

        internal ExecutionResult ExecutionResult { get; set; }

        internal bool PreservePathSetCasing { get; set; }
    }
}
