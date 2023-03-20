// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Error result for process remoting.
    /// </summary>
    internal class ErrorRemoteProcessPipResult : IRemoteProcessPipResult
    {
        /// <inheritdoc/>
        public bool ShouldRunLocally => true;

        /// <inheritdoc/>
        public int? ExitCode => default;

        /// <inheritdoc/>
        public string StdOut => string.Empty;

        /// <inheritdoc/>
        public string StdErr { init; get; }

        /// <inheritdoc/>
        public RemoteResultDisposition Disposition => RemoteResultDisposition.None;

        /// <summary>
        /// Creates an instance of <see cref="ErrorRemoteProcessPipResult"/>.
        /// </summary>
        public ErrorRemoteProcessPipResult(string error) => StdErr = error;
    }
}
