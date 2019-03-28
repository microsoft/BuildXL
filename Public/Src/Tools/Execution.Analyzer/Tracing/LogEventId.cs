// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Execution.Analyzer.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
    #pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />.
    /// </summary>
    public enum LogEventId : ushort
    {
        // RESERVED TO [8900, 8999] (BuildXL.Execution.Analyzer)
        ExecutionAnalyzerInvoked = 8900,
        ExecutionAnalyzerCatastrophicFailure = 8901,
        ExecutionAnalyzerEventCount = 8902,

        // RESERVED To [8910, 8919] FingerprintStoreAnalyzer
        FingerprintStorePipMissingFromOldBuild = 8910,
        FingerprintStorePipMissingFromNewBuild = 8911,
        FingerprintStoreUncacheablePip = 8912,
    }
}
