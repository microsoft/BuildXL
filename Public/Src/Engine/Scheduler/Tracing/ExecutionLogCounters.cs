// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Counters for <see cref="ExecutionLogFileTarget"/>.
    /// </summary>
    public enum ExecutionLogCounters
    {
        /// <summary>
        /// The number of ms spent logging data to the execution log file.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecutionLogFileLoggingTime,
    }
}
