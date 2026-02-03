// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Counters for <see cref="FileMonitoringViolationAnalyzer"/>.
    /// </summary>
    public enum FileMonitoringViolationAnalysisCounter
    {
        /// <summary>
        /// Elapsed time for analyzing pip violation.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        AnalyzePipViolationsDuration,

        /// <summary>
        /// Elapsed time analyzing dynamic violations for a pip.
        /// </summary>
        /// <remarks>
        /// This is a subset of <see cref="AnalyzePipViolationsDuration"/> since dynamic violations are analyzed
        /// as part of the overall pip violation analysis.
        /// </remarks>
        [CounterType(CounterType.Stopwatch)]
        AnalyzeDynamicViolationsDuration,

        /// <summary>
        /// Number of violations by processes for which classification was attempted (but not all were necessarily classified).
        /// </summary>
        NumbersOfViolationClassificationAttempts,
    }
}
