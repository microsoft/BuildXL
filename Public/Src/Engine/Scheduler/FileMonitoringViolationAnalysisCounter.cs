// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Counters for <see cref="FileMonitoringViolationAnalyzer"/>.
    /// </summary>
    public enum FileMonitoringViolationAnalysisCounter
    {
        /// <summary>
        /// Elapsed time querying the pip graph (reachability checks, etc.) to classify violations.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ViolationClassificationGraphQueryDuration,

        /// <summary>
        /// Elapsed time for analyzing pip violation. This duration subsumes <see cref="ViolationClassificationGraphQueryDuration" />.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        AnalyzePipViolationsDuration,

        /// <summary>
        /// Elapsed time for analyzing dynamic violation.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        AnalyzeDynamicViolationsDuration,

        /// <summary>
        /// Number of violations by processes for which classification was attempted (but not all were necessarily classified).
        /// </summary>
        NumbersOfViolationClassificationAttempts,
    }
}
