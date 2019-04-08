// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// A factory for creating <see cref="IServer"/> and <see cref="IClient"/> instances.
    /// </summary>
    public interface IIpcProvider
    {
        /// <summary>
        /// Returns a new moniker.
        /// </summary>
        IIpcMoniker CreateNewMoniker();

        /// <summary>
        /// Loads or creates a new moniker with a given id.
        /// </summary>
        IIpcMoniker LoadOrCreateMoniker(string monikerId);

        /// <summary>
        /// Renders given moniker to a connection string (that can be passed to <see cref="GetClient"/> and <see cref="GetServer"/>).
        /// </summary>
        /// <remarks>
        /// This method MUST be <strong>stable</strong>, i.e., over time, it must
        /// always return the same connetion string for all monikers with the same ID.
        /// </remarks>
        string RenderConnectionString(IIpcMoniker moniker);

        /// <summary>
        /// Creates an <see cref="IServer"/> instance given
        /// a moniker and a server configuration.
        /// </summary>
        /// <remarks>
        /// The <paramref name="connectionString"/> must be a string obtained by calling
        /// <see cref="RenderConnectionString"/> on a moniker previously returned by this provider.
        /// </remarks>
        IServer GetServer(string connectionString, IServerConfig config);

        /// <summary>
        /// Creates an <see cref="IClient"/> instance given
        /// a moniker and a client configuration.
        /// </summary>
        /// <remarks>
        /// The <paramref name="connectionString"/> must be a string obtained by calling
        /// <see cref="RenderConnectionString"/> on a moniker previously returned by this provider.
        /// </remarks>
        IClient GetClient(string connectionString, IClientConfig config);
    }
}
