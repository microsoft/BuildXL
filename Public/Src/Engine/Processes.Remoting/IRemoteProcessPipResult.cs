// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Results of remoting process pip.
    /// </summary>
    public interface IRemoteProcessPipResult
    {
        /// <summary>
        /// Whether the process should be run locally because of some failure remoting.
        /// </summary>
        public bool ShouldRunLocally { get; }

        /// <summary>
        /// Gets the process exit code, or null if the process was not remotable.
        /// </summary>
        int? ExitCode { get; }

        /// <summary>
        /// Gets the stdout contents of the process, or null if the process was not remotable.
        /// </summary>
        string? StdOut { get; }

        /// <summary>
        /// Gets the stderr contents of the process, or null if the process was not remotable.
        /// </summary>
        string? StdErr { get; }

        /// <summary>
        /// Gets whether the process was a cache hit or was remoted.
        /// </summary>
        RemoteResultDisposition Disposition { get; }
    }
}
