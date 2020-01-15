// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// Common <see cref="IClient"/> configuration parameters.
    /// </summary>
    public interface IServerConfig
    {
        /// <summary>
        /// Logger to use.  May be null to indicate no logging.
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Maximum number of clients to serve concurrently.
        /// </summary>
        int MaxConcurrentClients { get; }

        /// <summary>
        /// Terminate the daemon process after first failed operation (e.g., 'drop create' fails because the drop already exists).
        /// </summary>
        bool StopOnFirstFailure { get; }
    }
}
