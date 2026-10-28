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

        /// <summary>
        /// Whether to report the daemon's counters and configuration to BuildXL as a DaemonTelemetry event.
        /// </summary>
        public bool ReportTelemetry { get; }

        /// <nodoc />
        public static int DefaultMaxDegreeOfParallelism { get; } = 10;

        /// <nodoc />
        public static bool DefaultReportTelemetry { get; } = true;

        /// <nodoc />
        public BlobDaemonConfig(
            int maxDegreeOfParallelism,
            string logDir = null,
            bool reportTelemetry = true)
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism;
            LogDir = logDir;
            ReportTelemetry = reportTelemetry;
        }
    }
}