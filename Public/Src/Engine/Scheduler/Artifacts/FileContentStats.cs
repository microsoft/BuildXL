// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Statistics for the file content management.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct FileContentStats
    {
        /// <summary>
        /// Number of produced outputs.
        /// </summary>
        public long OutputsProduced;

        /// <summary>
        /// Number of deployed outputs.
        /// </summary>
        public long OutputsDeployed;

        /// <summary>
        /// Number of up-to-date outputs.
        /// </summary>
        public long OutputsUpToDate;

        /// <summary>
        /// Number of source files referenced that did not exist on disk.
        /// </summary>
        public int SourceFilesAbsent;

        /// <summary>
        /// Number of source files 'untracked' (unhashed) due to being outside of a defined mount, or in a mount with hashing
        /// disabled.
        /// </summary>
        public long SourceFilesUntracked;

        /// <summary>
        /// Number of source files which were hashed (not seen before).
        /// </summary>
        public long SourceFilesHashed;

        /// <summary>
        /// Number of source files which were unchanged from a prior build (seen before, so not hashed).
        /// </summary>
        public long SourceFilesUnchanged;

        /// <summary>
        /// Number of attempts at bringing content to local cache.
        /// </summary>
        public long TryBringContentToLocalCache;

        /// <summary>
        /// Number of artifacts brought to local cache.
        /// </summary>
        public long ArtifactsBroughtToLocalCache;

        /// <summary>
        /// Total size of artifacts brought to local cache by bytes.
        /// </summary>
        public long TotalSizeArtifactsBroughtToLocalCache;

        /// <summary>
        /// The total size of deduplicated cached content used/stored by build
        /// </summary>
        public long TotalCacheSizeNeeded;

        /// <summary>
        /// Number of output files which were hashed (not seen before).
        /// </summary>
        public long OutputFilesHashed;

        /// <summary>
        /// Number of output files which were unchanged from a prior build (seen before, so not hashed).
        /// </summary>
        public long OutputFilesUnchanged;

        /// <summary>
        /// Number of times we attempted to recover a file that was not in the cache when requested.
        /// </summary>
        public long FileRecoveryAttempts;

        /// <summary>
        /// Number of times we successfully recovered a file that was not in the cache when requested. 
        /// </summary>
        public long FileRecoverySuccesses;

        /// <summary>
        /// The total size of materialized inputs.
        /// </summary>
        public long TotalMaterializedInputsSize;

        /// <summary>
        /// The total size of materialized outputs.
        /// </summary>
        public long TotalMaterializedOutputsSize;

        /// <summary>
        /// The total count of materialized inputs.
        /// </summary>
        public long TotalMaterializedInputsCount;

        /// <summary>
        /// The total count of materialized outputs.
        /// </summary>
        public long TotalMaterializedOutputsCount;

        /// <summary>
        /// The total count of expensive materialized input operations.
        /// </summary>
        public long TotalMaterializedInputsExpensiveCount;

        /// <summary>
        /// The total count of expensive materialized output operations.
        /// </summary>
        public long TotalMaterializedOutputsExpensiveCount;
        


    }
}
