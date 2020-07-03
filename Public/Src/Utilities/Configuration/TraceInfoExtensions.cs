// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The case insensitive names of various /traceInfo arguments that are passed to BuildXL
    /// </summary>
    public static class TraceInfoExtensions
    {
        /// <summary>
        /// The argument to specify the queue name in CB.
        /// </summary>
        public const string CloudBuildQueue = "CloudBuildQueue";

        /// <summary>
        /// The argument to specify a custom key that contributes to fingerprinting of HistoricMetadataCache, HistoricRunningTimeTable, and FingerprintStore.
        /// </summary>
        public const string CustomFingerprint = "CustomFingerprint";

        /// <summary>
        /// The argument to specify the AB Testing.
        /// </summary>
        public const string ABTesting = "ABTesting";
    }
}
