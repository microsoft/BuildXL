// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Pips.Builders
{
    /// <summary>
    /// Counters for CredScan
    /// </summary>
    public enum CredScanCounter
    {
        /// <summary>
        /// The amount of time spent scanning the environment variables using the CredScan library for credentials.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ScanDuration,

        /// <summary>
        /// The amount of time spent scanning the environment variables using the CredScan library for credentials.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PostDuration,

        /// <summary>
        /// The amount of time spent scanning the environment variables using the CredScan library for credentials.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CompleteDuration,

        /// <nodoc/>
        NumSkipped,

        /// <nodoc/>
        NumScanCalls,

        /// <nodoc/>
        NumProcessed,
    }
}
