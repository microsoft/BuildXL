// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

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

        /// <summary>
        /// Max pending events in the binary logger.
        /// </summary>
        MaxPendingEvents,

        /// <summary>
        /// Number of event writer creations.
        /// </summary>
        EventWriterFactoryCalls,
    }
}
