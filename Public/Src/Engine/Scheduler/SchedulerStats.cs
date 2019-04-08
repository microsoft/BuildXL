// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Statistics for the scheduler.
    /// </summary>
    /// <remarks>
    /// TODO: In the future this can be used by the scheduler itself to keep track of its statistics.
    /// </remarks>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct SchedulerStats
    {
        /// <summary>
        /// Number of completed process pips.
        /// </summary>
        public long ProcessPipsCompleted;

        /// <summary>
        /// Number of completed IPC pips.
        /// </summary>
        public long IpcPipsCompleted;

        /// <summary>
        /// Number of pips satisfied from cache.
        /// </summary>
        public long ProcessPipsSatisfiedFromCache;

        /// <summary>
        /// Number of pips not satisfied from cache (executed)
        /// </summary>
        public long ProcessPipsUnsatisfiedFromCache;

        /// <summary>
        /// Number of pips with warning.
        /// </summary>
        public int PipsWithWarnings;

        /// <summary>
        /// Number of pips with warning from cache.
        /// </summary>
        public int PipsWithWarningsFromCache;

        /// <summary>
        /// Number of attempts at bringing content to local cache.
        /// </summary>
        public long TryBringContentToLocalCache => FileContentStats.TryBringContentToLocalCache;

        /// <summary>
        /// Number of artifacts brought to local cache.
        /// </summary>
        public long ArtifactsBroughtToLocalCache => FileContentStats.ArtifactsBroughtToLocalCache;

        /// <summary>
        /// Total size of artifacts brought to local cache by bytes.
        /// </summary>
        public long TotalSizeArtifactsBroughtToLocalCache => FileContentStats.TotalSizeArtifactsBroughtToLocalCache;

        /// <summary>
        /// Number of completed service pips.
        /// </summary>
        public long ServicePipsCompleted;

        /// <summary>
        /// Number of completed service shutdown pips.
        /// </summary>
        public long ServiceShutdownPipsCompleted;

        /// <nodoc/>
        public FileContentStats FileContentStats;

        /// <summary>
        /// Number of produced outputs.
        /// </summary>
        public long OutputsProduced => FileContentStats.OutputsProduced;

        /// <summary>
        /// Number of deployed outputs.
        /// </summary>
        public long OutputsDeployed => FileContentStats.OutputsDeployed;

        /// <summary>
        /// Number of up-to-date outputs.
        /// </summary>
        public long OutputsUpToDate => FileContentStats.OutputsUpToDate;
    }
}
