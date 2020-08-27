// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Tool.MaterializationDaemon
{
    /// <nodoc />
    public sealed class MaterializationDaemonConfig
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
        public MaterializationDaemonConfig(
            int maxDegreeOfParallelism,
            string logDir = null)
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism;
            LogDir = logDir;
        }
    }
}