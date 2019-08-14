// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Creates and returns a Redis Database
    /// </summary>
    public class RedisDatabaseFactory
    {
        private readonly IConnectionStringProvider _connectionStringProvider;
        private readonly SemaphoreSlim _creationSemaphore = new SemaphoreSlim(1, 1);
        private IConnectionMultiplexer _connectionMultiplexer;
        private volatile bool _resetConnectionMultiplexer = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisDatabaseFactory"/> class.
        /// </summary>
        private RedisDatabaseFactory(IConnectionStringProvider connectionStringProvider)
        {
            _connectionStringProvider = connectionStringProvider;
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        public static async Task<RedisDatabaseFactory> CreateAsync(Context context, IConnectionStringProvider provider)
        {
            var databaseFactory = new RedisDatabaseFactory(provider);
            await databaseFactory.StartupAsync(context);
            return databaseFactory;
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        public static Task<RedisDatabaseFactory> CreateAsync(IConnectionStringProvider provider, IConnectionMultiplexer connectionMultiplexer)
        {
            var databaseFactory = new RedisDatabaseFactory(provider);
            databaseFactory._connectionMultiplexer = connectionMultiplexer;
            return Task.FromResult(databaseFactory);
        }

        /// <summary>
        /// Starts up the database factory.
        /// </summary>
        private async Task StartupAsync(Context context)
        {
            _connectionMultiplexer = await RedisConnectionMultiplexer.CreateAsync(context, _connectionStringProvider);
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
                        ConfigurationOptions options = ConfigurationOptions.Parse(_connectionMultiplexer.Configuration);
                        await RedisConnectionMultiplexer.ForgetAsync(options);
                        _connectionMultiplexer = await RedisConnectionMultiplexer.CreateAsync(context, _connectionStringProvider);
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
