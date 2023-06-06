// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ipc.Common;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// A factory for creating <see cref="IServer"/> and <see cref="IClient"/> instances.
    /// </summary>
    public interface IIpcProvider
    {
        /// <summary>
        /// Renders given moniker to a connection string (that can be passed to <see cref="GetClient"/> and <see cref="GetServer(string, IServerConfig)"/>).
        /// </summary>
        /// <remarks>
        /// This method MUST be <strong>stable</strong>, i.e., over time, it must
        /// always return the same connection string for all monikers with the same ID.
        /// </remarks>
        string RenderConnectionString(IpcMoniker moniker);

        /// <summary>
        /// Creates an <see cref="IServer"/> instance given
        /// a moniker and a server configuration.
        /// </summary>
        /// <remarks>
        /// The <paramref name="connectionString"/> must be a string obtained by calling
        /// <see cref="RenderConnectionString"/> on an IPC moniker.
        /// </remarks>
        IServer GetServer(string connectionString, IServerConfig config);

        /// <summary>
        /// Creates an <see cref="IServer"/> instance given a server configuration. 
        /// This is an optional interface, a given provider must throw <see cref="System.NotSupportedException"/>
        /// if it cannot create servers without a provided connection string.
        /// </summary>
        /// <remarks>
        /// The connection string used by the server (<see cref="IServer.ConnectionString"/>) must be used
        /// to create clients via <see cref="GetClient"/>.
        /// </remarks>
        IServer GetServer(IServerConfig config);

        /// <summary>
        /// Creates an <see cref="IClient"/> instance given
        /// a moniker and a client configuration.
        /// </summary>
        /// <remarks>
        /// The <paramref name="connectionString"/> must be a string obtained by calling
        /// <see cref="RenderConnectionString"/> on a moniker previously returned by this provider.
        /// </remarks>
        IClient GetClient(string connectionString, IClientConfig config);

        /// <summary>
        /// Associates a connection string with a moniker. This string will be returned by <see cref="RenderConnectionString"/>
        /// when it is called for this moniker.
        /// This method should NOT be called if there are server / clients that are already using a connection string previously 
        /// created for this moniker.
        /// </summary>
        void UnsafeSetConnectionStringForMoniker(IpcMoniker ipcMoniker, string connectionString);
    }
}
