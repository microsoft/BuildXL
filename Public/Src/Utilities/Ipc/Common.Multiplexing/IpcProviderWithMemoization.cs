// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Ipc.Common.Multiplexing
{
    /// <summary>
    /// IpcProvider with memoization for monikers, clients, and servers.
    ///
    /// It takes another <see cref="IIpcProvider"/> to which it delegates the tasks of
    /// (1) getting a client for a given moniker, and
    /// (2) getting a server for a given moniker.
    ///
    /// Only one underying connection string is rendered per moniker.
    /// The connection string is remembered and returned for all subsequent
    /// requests to render a moniker that has the same ID.
    ///
    /// All calls to <see cref="GetClient(string, IClientConfig)"/> when the same
    /// connection string is supplied return the same <see cref="IClient"/> object.
    ///
    /// All calls to <see cref="GetServer(string, IServerConfig)"/> when the same
    /// connection string is supplied return the same <see cref="IServer"/> object.
    /// </summary>
    /// <remarks>
    /// Thread safe.
    /// </remarks>
    public sealed class IpcProviderWithMemoization : IIpcProvider, IDisposable
    {
        private readonly IIpcProvider m_provider;
        private readonly IIpcLogger m_defaultClientLogger;

        private readonly ConcurrentDictionary<string, IClient> m_connectionString2Client = new ConcurrentDictionary<string, IClient>();
        private readonly ConcurrentDictionary<string, IServer> m_connectionString2Server = new ConcurrentDictionary<string, IServer>();
        private readonly ConcurrentDictionary<string, string> m_moniker2connectionString = new ConcurrentDictionary<string, string>();

        /// <nodoc />
        public IpcProviderWithMemoization(IIpcProvider provider, IIpcLogger defaultClientLogger = null)
        {
            m_provider = provider;
            m_defaultClientLogger = defaultClientLogger;
        }

        /// <inheritdoc />
        public string RenderConnectionString(IpcMoniker moniker) => GetOrAddConnectionString(moniker.Id);

        /// <summary>
        /// If no client has been created for a given moniker, creates and returns a new client;
        /// otherwise, returns the client previously created for the same moniker.
        /// </summary>
        /// <inheritdoc />
        public IClient GetClient(string connectionString, IClientConfig config)
        {
            lock (m_connectionString2Client)
            {
                return m_connectionString2Client.GetOrAdd(
                    key: connectionString,
                    valueFactory: (s) => m_provider.GetClient(s, OverrideLogger(config)));
            }
        }

        /// <summary>
        /// If no server has been created for a given moniker, creates and returns a new server;
        /// otherwise, returns the server previously created for the same moniker.
        /// </summary>
        /// <inheritdoc />
        public IServer GetServer(string connectionString, IServerConfig config)
        {
            lock (m_connectionString2Server)
            {
                return m_connectionString2Server.GetOrAdd(
                    key: connectionString,
                    valueFactory: (s) => m_provider.GetServer(s, config));
            }
        }

        /// <summary>
        /// Requests stop on each memoized client and server and returns a task that waits until all of them have completed.
        /// </summary>
        public Task Stop()
        {
            var tasks = new List<Task>(m_connectionString2Client.Count + m_connectionString2Server.Count);

            foreach (var value in m_connectionString2Server.Values)
            {
                value.RequestStop();
                tasks.Add(value.Completion);
            }

            foreach (var value in m_connectionString2Client.Values)
            {
                value.RequestStop();
                tasks.Add(value.Completion);
            }

            return TaskUtilities.SafeWhenAll(tasks);
        }

        /// <summary>
        /// Disposes concurrent maps used for remembering created clients/servers.
        /// </summary>
        public void Dispose()
        {
            DisposeConcurrentMap(m_connectionString2Client);
            DisposeConcurrentMap(m_connectionString2Server);
            m_moniker2connectionString.Clear();
        }

        private string GetOrAddConnectionString(string monikerId)
        {
            return m_moniker2connectionString.GetOrAdd(monikerId, (mId) => m_provider.CreateNewConnectionString());
        }

        private IClientConfig OverrideLogger(IClientConfig config)
        {
            return m_defaultClientLogger == null || config.Logger != null
                ? config
                : new ClientConfig
                {
                    MaxConnectRetries = config.MaxConnectRetries,
                    ConnectRetryDelay = config.ConnectRetryDelay,
                    Logger = m_defaultClientLogger,
                };
        }

        private static void DisposeConcurrentMap<TKey, TValue>(ConcurrentDictionary<TKey, TValue> map) where TValue : IDisposable
        {
            lock (map)
            {
                foreach (var value in map.Values)
                {
                    value.Dispose();
                }

                map.Clear();
            }
        }
    }
}
