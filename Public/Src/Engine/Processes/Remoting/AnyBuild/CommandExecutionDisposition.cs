// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Various execution sub-results provided from AnyBuild process execution.
    /// </summary>
    public enum CommandExecutionDisposition
    {
        /// <summary>
        /// Default zero value.
        /// </summary>
        Unknown,

        /// <summary>
        /// Process completed with cache hit.
        /// </summary>
        CacheHit,

        /// <summary>
        /// The process was remoted to an AnyBuild cluster.
        /// </summary>
        Remoted,

        /// <summary>
        /// The process was run locally by AnyBuild.exe and no fallback execution is needed.
        /// </summary>
        RanLocally,
    }
}
