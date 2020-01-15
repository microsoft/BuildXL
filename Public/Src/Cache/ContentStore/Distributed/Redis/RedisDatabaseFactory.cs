// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Creates and returns a Redis Database
    /// </summary>
    public class RedisDatabaseFactory
    {
        private readonly SemaphoreSlim _creationSemaphore = new SemaphoreSlim(1, 1);

        private readonly Func<Task<IConnectionMultiplexer>> _connectionMultiplexerFactory;
        private readonly Func<IConnectionMultiplexer, Task> _connectionMultiplexerShutdownFunc;
        private IConnectionMultiplexer _connectionMultiplexer;

        private volatile bool _resetConnectionMultiplexer = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisDatabaseFactory"/> class.
        /// </summary>
        private RedisDatabaseFactory(IConnectionMultiplexer connectionMultiplexer, Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory, Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc)
        {
            _connectionMultiplexerFactory = connectionMultiplexerFactory;
            _connectionMultiplexerShutdownFunc = connectionMultiplexerShutdownFunc;
            _connectionMultiplexer = connectionMultiplexer;
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        public static async Task<RedisDatabaseFactory> CreateAsync(Context context, IConnectionStringProvider provider)
        {
            Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory = () => RedisConnectionMultiplexer.CreateAsync(context, provider);

            Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc = async m =>
            {
                ConfigurationOptions options = ConfigurationOptions.Parse(m.Configuration);
                await RedisConnectionMultiplexer.ForgetAsync(options);
            };

            var multiplexer = await RedisConnectionMultiplexer.CreateAsync(context, provider);
            var databaseFactory = new RedisDatabaseFactory(multiplexer, connectionMultiplexerFactory, connectionMultiplexerShutdownFunc);
            return databaseFactory;
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        /// <remarks>
        /// Used by tests only.
        /// </remarks>
        public static Task<RedisDatabaseFactory> CreateAsync(IConnectionStringProvider provider, IConnectionMultiplexer connectionMultiplexer)
        {
            Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory = () => Task.FromResult(connectionMultiplexer);

            Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc = m => BoolResult.SuccessTask;

            var databaseFactory = new RedisDatabaseFactory(connectionMultiplexer, connectionMultiplexerFactory, connectionMultiplexerShutdownFunc);
            return Task.FromResult(databaseFactory);
        }

        /// <summary>
        /// Factory method for a database factory used by tests only.
        /// </summary>
        /// <remarks>
        /// Used by tests only.
        /// </remarks>
        public static Task<RedisDatabaseFactory> CreateAsync(Func<IConnectionMultiplexer> connectionMultiplexerFactory, Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc)
        {
            var connectionMultiplexer = connectionMultiplexerFactory();
            var databaseFactory = new RedisDatabaseFactory(connectionMultiplexer, () => Task.FromResult(connectionMultiplexerFactory()), connectionMultiplexerShutdownFunc);
            return Task.FromResult(databaseFactory);
        }

        /// <nodoc />
        public EndPoint[] GetEndPoints() => _connectionMultiplexer.GetEndPoints();

        /// <nodoc />
        public IServer GetServer(EndPoint endpoint) => _connectionMultiplexer.GetServer(endpoint);

        /// <summary>
        /// Gets a Redis Database object with a specified key prefix.
        /// </summary>
        public async Task<IDatabase> GetDatabaseWithKeyPrefix(Context context, string keySpace)
        {
            if (_resetConnectionMultiplexer)
            {
                using (await SemaphoreSlimToken.WaitAsync(_creationSemaphore))
                {
                    if (_resetConnectionMultiplexer)
                    {
                        context.Debug("Shutting down current connection multiplexer.");
                        await _connectionMultiplexerShutdownFunc(_connectionMultiplexer);

                        context.Debug("Creating new multiplexer instance.");
                        _connectionMultiplexer = await _connectionMultiplexerFactory();

                        _resetConnectionMultiplexer = false;
                    }
                }
            }

            return _connectionMultiplexer.GetDatabase().WithKeyPrefix(keySpace);
        }

        /// <summary>
        /// Informs the database factory to reset and reconnect its connection multiplexer.
        /// </summary>
        public void ResetConnectionMultiplexer()
        {
            _resetConnectionMultiplexer = true;
        }
    }
}
