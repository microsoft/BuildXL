// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Result disposition.
    /// </summary>
    public enum RemoteResultDisposition
    {
        /// <summary>
        /// Process completed with cache hit.
        /// </summary>
        CacheHit,

        /// <summary>
        /// The process was remoted.
        /// </summary>
        Remoted,

        /// <summary>
        /// The process was run locally by the remoting engine and no fallback execution is needed.
        /// </summary>
        RanLocally,
    }
}
