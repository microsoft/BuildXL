// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Graph
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
