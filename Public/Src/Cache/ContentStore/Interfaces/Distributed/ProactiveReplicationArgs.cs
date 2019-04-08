// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// Settings for Proactive Replication.
    /// </summary>
    public class ProactiveReplicationArgs
    {
        /// <summary>
        /// A proactive replication session is triggered at this interval.
        /// </summary>
        public readonly int ReplicationIntervalMinutes;

        /// <summary>
        /// Number of files to take off replica queue each replication session.
        /// </summary>
        public readonly int FilesToReplicate;

        /// <summary>
        /// Minimum number of replicas required for file to be removed from replica queue.
        /// </summary>
        public readonly int MinReplicationThreshold;

        /// <summary>
        /// Optional file copy parallelism
        /// </summary>
        public readonly int? CopyParallelism;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProactiveReplicationArgs"/> class.
        /// </summary>
        public ProactiveReplicationArgs(int replicationIntervalMinutes, int filesToReplicate, int minReplicationThreshold, int? copyParallelism = null)
        {
            Contract.Requires(filesToReplicate >= 0);
            Contract.Requires(minReplicationThreshold > 0);

            ReplicationIntervalMinutes = replicationIntervalMinutes;
            FilesToReplicate = filesToReplicate;
            MinReplicationThreshold = minReplicationThreshold;
            CopyParallelism = copyParallelism;
        }
    }
}
