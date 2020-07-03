// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Counters for pip graph building.
    /// </summary>
    public enum PipGraphCounter
    {
        /// <summary>
        /// The amount of time for pip graph post validation.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        GraphPostValidation,
    }
}
