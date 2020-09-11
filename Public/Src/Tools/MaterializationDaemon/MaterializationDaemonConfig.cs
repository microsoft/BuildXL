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

        /// <summary>
        /// Path the manifest parser executable
        /// </summary>
        public string ParserExeLocation { get; }

        /// <summary>
        /// Additional command line arguments to include when launching a parser
        /// </summary>
        public string ParserAdditionalCommandLineArguments { get; }

        /// <nodoc />
        public static int DefaultMaxDegreeOfParallelism { get; } = 10;

        /// <nodoc />
        public MaterializationDaemonConfig(
            int maxDegreeOfParallelism,
            string parserExe,
            string parserArgs,
            string logDir = null)
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism;
            ParserExeLocation = parserExe;
            ParserAdditionalCommandLineArguments = parserArgs;
            LogDir = logDir;
        }
    }
}