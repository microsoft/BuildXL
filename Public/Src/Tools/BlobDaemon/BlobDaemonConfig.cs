// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Tool.BlobDaemon
{
    /// <nodoc />
    public sealed class BlobDaemonConfig
    {
        /// <summary>
        /// Maximum number of files to materialize concurrently
        /// </summary>
        public int MaxDegreeOfParallelism { get; }

        /// <summary>
        /// Log directory
        /// </summary>
        public string LogDir { get; }

        /// <nodoc />
        public static int DefaultMaxDegreeOfParallelism { get; } = 10;

        /// <nodoc />
        public BlobDaemonConfig(
            int maxDegreeOfParallelism,
            string logDir = null)
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism;
            LogDir = logDir;
        }
    }
}