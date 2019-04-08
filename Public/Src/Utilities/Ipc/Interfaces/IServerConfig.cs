// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
