// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.Workspaces;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// Statistics for the Download frontend 
    /// </summary>
    public class Statistics
    {
        /// <summary>
        /// Download statistics
        /// </summary>
        public OperationStatistics Downloads { get; } = new OperationStatistics();

        /// <summary>
        /// Extract statistics
        /// </summary>
        public OperationStatistics Extractions { get; } = new OperationStatistics();
    }

    /// <summary>
    /// Statistics for an operational aspect of the download frontend statistics
    /// </summary>
    public class OperationStatistics
    {
        /// <summary>
        /// Number of total operations performed
        /// </summary>
        public Counter Total { get; } = new Counter();

        /// <summary>
        /// Number of failed operations
        /// </summary>
        public Counter Failures { get; } = new Counter();

        /// <summary>
        /// Aggregate duration in ms of operation
        /// </summary>
        public Counter Duration { get; } = new Counter();

        /// <summary>
        /// Aggregate duration in ms of operation
        /// </summary>
        public Counter UpToDateCheckDuration { get; } = new Counter();

        /// <summary>
        /// Number of operations skipped because the manifest deemed succsfull
        /// </summary>
        public Counter SkippedDueToManifest { get; } = new Counter();

        /// <nodoc />
        public void LogStatistics(string prefix, Dictionary<string, long> statistics)
        {
            statistics.Add(prefix + ".Total", Total.Count);
            statistics.Add(prefix + ".SkippedDueToManifest", SkippedDueToManifest.Count);
            statistics.Add(prefix + ".Failures", Failures.Count);
            statistics.Add(prefix + ".Duration", (long)Duration.AggregateDuration.TotalMilliseconds);
            statistics.Add(prefix + ".UpToDateCheckDuration", (long)Duration.AggregateDuration.TotalMilliseconds);
        }
    }
}
